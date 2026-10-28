using System.Security.Cryptography;
using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.OnCurve;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Works out <c>stats_simulate</c>: reads a local deck, turns the request (or the on-curve preset) into engine rules,
/// runs the seeded samples, and cuts the result to the size limit. It judges nothing.
/// </summary>
internal sealed class StatisticsSimulateCoordinator
{
    /// <summary>
    /// Gives the most skipped cards one result lists.
    /// </summary>
    private const int MaximumSkippedListed = 50;

    /// <summary>
    /// Gives the most bytes the result data may take, which keeps the whole tool result under the size limit.
    /// </summary>
    private const int MaximumResultBytes = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes;

    /// <summary>
    /// Gives how many candidate lands each step of a cut removes.
    /// </summary>
    private const int LandCutStep = 10;

    /// <summary>
    /// Says what to change when sample hands were cut.
    /// </summary>
    private const string SampleNarrow =
        "Some sample hands were left out to stay under the size limit. Ask for fewer sampleHands or fewer turns. ";

    /// <summary>
    /// Says what to change when the preset's land list was cut.
    /// </summary>
    private const string LandNarrow =
        "The list of candidate lands was cut to stay under the size limit; local_deck_get lists every entry.";

    /// <summary>
    /// Reads the saved deck and the installed card data.
    /// </summary>
    private readonly CardListResolver resolver;

    /// <summary>
    /// Creates the coordinator.
    /// </summary>
    internal StatisticsSimulateCoordinator(CardListResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.resolver = resolver;
    }

    /// <summary>
    /// Runs one simulation request.
    /// </summary>
    internal async Task<OperationResult<SimulateToolResult>> SimulateAsync(
        SimulationDeckInput deck,
        IReadOnlyList<SimulationGroupInput>? groups,
        SimulationOpeningHandInput? openingHand,
        IReadOnlyList<SimulationRuleInput>? eachTurn,
        int? turns,
        IReadOnlyList<SimulationConditionInput>? conditions,
        SimulationPresetInput? preset,
        int samples,
        int sampleHands,
        string? seed,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? problem = CheckShape(deck, groups, openingHand, eachTurn, turns, conditions, preset);
        if (problem is not null)
        {
            return problem;
        }

        if (!TakeSeed(seed, out ulong seedValue))
        {
            return new OperationInvalidInput(
                "invalid-seed",
                "seed must be exactly 16 hexadecimal characters.",
                "seed");
        }

        OperationResult<ResolvedDeckDocument> read = await resolver
            .ReadDeckDocumentAsync(
                new DeckSourceInput(deck.DeckId, deck.ExpectedRevision, [SimulationRequestMapper.MainZone]),
                "deck",
                cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<ResolvedDeckDocument> document)
        {
            return OperationFailure.Forward<ResolvedDeckDocument, SimulateToolResult>(read);
        }

        DeckDocument saved = document.Data.Deck;
        List<DeckEntry> library = [.. saved.Entries.Where(entry =>
            string.Equals(entry.Zone, SimulationRequestMapper.MainZone, StringComparison.OrdinalIgnoreCase))];
        if (library.Count == 0)
        {
            return new OperationInvalidInput(
                "empty-main-zone",
                "The deck has no entries in the main zone, which is the only zone the simulation shuffles.",
                "deck");
        }

        OperationResult<Prepared> prepared = await PrepareAsync(
            preset,
            groups,
            openingHand,
            eachTurn,
            turns,
            conditions,
            library,
            saved,
            cancellationToken).ConfigureAwait(false);
        if (prepared is not OperationSuccess<Prepared> ready)
        {
            return OperationFailure.Forward<Prepared, SimulateToolResult>(prepared);
        }

        OperationResult<SimulationMapping> mapped = SimulationRequestMapper.Map(
            ready.Data.Scenario,
            saved,
            library,
            samples,
            sampleHands,
            seedValue,
            limitIds: preset is null);
        if (mapped is not OperationSuccess<SimulationMapping> mapping)
        {
            return OperationFailure.Forward<SimulationMapping, SimulateToolResult>(mapped);
        }

        int echoed = ResultSize.Measure(new SimulationAppliedRules(
                ready.Data.Scenario.Turns,
                ready.Data.Scenario.OpeningHand,
                ready.Data.Scenario.Groups,
                ready.Data.Scenario.EachTurn,
                ready.Data.Scenario.Conditions));
        if (echoed > MaximumResultBytes)
        {
            return new OperationInvalidInput(
                "result-too-large",
                "The echoed rules alone are over the size limit. Use fewer groups, rules, and conditions.",
                "groups");
        }

        OperationResult<SimulationReport> run = SimulationEngine.Run(mapping.Data.Request, cancellationToken);
        if (run is not OperationSuccess<SimulationReport> report)
        {
            return OperationFailure.Forward<SimulationReport, SimulateToolResult>(run);
        }

        return Present(document.Data.Info, ready.Data, mapping.Data, report.Data, library);
    }

    /// <summary>
    /// Holds the scenario to run and, for the preset, the facts it read.
    /// </summary>
    private sealed record Prepared(SimulationScenario Scenario, SimulationPresetFacts? Facts);

    /// <summary>
    /// Checks that the request gives either a preset or all rule parts, and never both.
    /// </summary>
    private static OperationInvalidInput? CheckShape(
        SimulationDeckInput? deck,
        IReadOnlyList<SimulationGroupInput>? groups,
        SimulationOpeningHandInput? openingHand,
        IReadOnlyList<SimulationRuleInput>? eachTurn,
        int? turns,
        IReadOnlyList<SimulationConditionInput>? conditions,
        SimulationPresetInput? preset)
    {
        if (deck is null)
        {
            return new OperationInvalidInput(KindFields.MissingField, "deck is required.", "deck");
        }

        if (preset is not null)
        {
            (string Name, bool Given)[] extras =
            [
                ("groups", groups is not null),
                ("openingHand", openingHand is not null),
                ("eachTurn", eachTurn is not null),
                ("turns", turns is not null),
                ("conditions", conditions is not null),
            ];
            foreach ((string name, bool given) in extras)
            {
                if (given)
                {
                    return new OperationInvalidInput(
                        KindFields.FieldNotAllowed,
                        $"{name} is not allowed with a preset, because the preset states its own rules.",
                        name);
                }
            }

            return preset is SimulationOnCurvePresetInput { LandRules: not null }
                ? null
                : new OperationInvalidInput(
                    KindFields.MissingField,
                    "preset.landRules is required.",
                    "preset.landRules");
        }

        (string Name, bool Given)[] required =
        [
            ("openingHand", openingHand is not null),
            ("eachTurn", eachTurn is not null),
            ("turns", turns is not null),
            ("conditions", conditions is not null),
        ];
        foreach ((string name, bool given) in required)
        {
            if (!given)
            {
                return new OperationInvalidInput(
                    KindFields.MissingField,
                    $"{name} is required unless a preset is given.",
                    name);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the replay seed, or draws a new one when the caller gave none.
    /// </summary>
    private static bool TakeSeed(string? text, out ulong seed)
    {
        if (text is null)
        {
            seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
            return true;
        }

        return SimulationEngine.TryParseSeed(text, out seed);
    }

    /// <summary>
    /// Chooses the scenario: the caller's rules, or what the preset expands to.
    /// </summary>
    private async Task<OperationResult<Prepared>> PrepareAsync(
        SimulationPresetInput? preset,
        IReadOnlyList<SimulationGroupInput>? groups,
        SimulationOpeningHandInput? openingHand,
        IReadOnlyList<SimulationRuleInput>? eachTurn,
        int? turns,
        IReadOnlyList<SimulationConditionInput>? conditions,
        IReadOnlyList<DeckEntry> library,
        DeckDocument deck,
        CancellationToken cancellationToken)
    {
        if (preset is not SimulationOnCurvePresetInput onCurve)
        {
            return new OperationSuccess<Prepared>(new Prepared(
                new SimulationScenario(groups ?? [], openingHand!, eachTurn!, turns!.Value, conditions!),
                null));
        }

        OperationResult<ExpandedPreset> expanded = await SimulationPresetExpander
            .ExpandAsync(resolver, onCurve, library, deck, cancellationToken)
            .ConfigureAwait(false);
        return expanded is OperationSuccess<ExpandedPreset> success
            ? new OperationSuccess<Prepared>(new Prepared(success.Data.Scenario, success.Data.Facts))
            : OperationFailure.Forward<ExpandedPreset, Prepared>(expanded);
    }

    /// <summary>
    /// Builds the tool result and cuts it until the whole result fits the size limit: first the returned sample
    /// hands, then the preset's list of candidate lands. A cut is always stated in the result.
    /// </summary>
    private static OperationResult<SimulateToolResult> Present(
        CardListSourceInfo info,
        Prepared prepared,
        SimulationMapping mapping,
        SimulationReport report,
        List<DeckEntry> library)
    {
        List<SimulationSkipped> listed = [.. mapping.Skipped.Take(MaximumSkippedListed)];
        List<string> limits = [.. report.Limits];
        if (prepared.Facts is not null)
        {
            limits.Add(
                "The on-curve preset plays at most one land each turn, keeps the opening seven cards, and reads the " +
                "lands and the target from installed Scryfall card data; a land that enters tapped is modeled only " +
                "by its one-mana-next-turn rule.");
        }

        ResultView view = new(info, prepared, mapping, report, library, listed, limits);
        int lands = prepared.Facts?.CandidateLands.Count ?? 0;
        for (int keepSamples = report.SampleHands.Count; keepSamples >= 0; keepSamples--)
        {
            SimulateToolResult candidate = view.Build(keepSamples, lands);
            if (Fits(candidate))
            {
                return new OperationSuccess<SimulateToolResult>(candidate);
            }
        }

        for (int keepLands = lands - LandCutStep; keepLands >= 0; keepLands -= LandCutStep)
        {
            SimulateToolResult candidate = view.Build(0, keepLands);
            if (Fits(candidate))
            {
                return new OperationSuccess<SimulateToolResult>(candidate);
            }
        }

        SimulateToolResult last = view.Build(0, 0);
        return Fits(last)
            ? new OperationSuccess<SimulateToolResult>(last)
            : new OperationInvalidInput(
                "result-too-large",
                "The result is over the size limit even without sample hands. Use fewer groups, rules, and conditions.",
                "groups");
    }

    /// <summary>
    /// Reports whether a result is within the size limit as the server writes it.
    /// </summary>
    private static bool Fits(SimulateToolResult candidate)
    {
        return ResultSize.Measure(new OperationSuccess<SimulateToolResult>(candidate)) <= MaximumResultBytes;
    }

    /// <summary>
    /// Holds everything a result is built from, so the cuts differ only in how much of the two long lists they keep.
    /// </summary>
    private sealed record ResultView(
        CardListSourceInfo Info,
        Prepared Prepared,
        SimulationMapping Mapping,
        SimulationReport Report,
        List<DeckEntry> Library,
        List<SimulationSkipped> Listed,
        List<string> Limits)
    {
        /// <summary>
        /// Builds the result with the first returned samples and candidate lands.
        /// </summary>
        internal SimulateToolResult Build(int keepSamples, int keepLands)
        {
            SimulationScenario scenario = Prepared.Scenario;
            List<SimulationSampleRecord> samples = [.. Report.SampleHands.Take(keepSamples)];
            bool cutSamples = keepSamples < Report.SampleHands.Count;
            SimulationPresetFacts? facts = Prepared.Facts;
            bool cutLands = facts is not null && keepLands < facts.CandidateLands.Count;
            if (cutLands)
            {
                facts = facts! with
                {
                    CandidateLands = [.. facts.CandidateLands.Take(keepLands)],
                    CandidateLandsOmitted = facts.CandidateLands.Count - keepLands,
                };
            }

            return new SimulateToolResult(
                [Info],
                Report.Seed,
                Report.RandomVersion,
                Report.ModelVersion,
                Report.Samples,
                Report.Fingerprint,
                new SimulationAppliedRules(
                    scenario.Turns,
                    scenario.OpeningHand,
                    scenario.Groups,
                    scenario.EachTurn,
                    scenario.Conditions),
                Mapping.Groups,
                Listed,
                [.. Report.Results.Select(result => new SimulationConditionView(
                    result.Id,
                    result.Hits,
                    result.Rate,
                    [result.WilsonLower, result.WilsonUpper]))],
                Report.Mulligans,
                Report.LibraryEmpty,
                samples,
                NameCards(samples, Library),
                Limits)
            {
                Preset = facts,
                SkippedOmitted = Mapping.Skipped.Count > Listed.Count ? Mapping.Skipped.Count - Listed.Count : null,
                SampleHandsOmitted = cutSamples ? Report.SampleHands.Count - keepSamples : null,
                Truncated = cutSamples || cutLands ? true : null,
                Narrow = cutSamples || cutLands
                    ? (cutSamples ? SampleNarrow : string.Empty) + (cutLands ? LandNarrow : string.Empty)
                    : null,
            };
        }
    }

    /// <summary>
    /// Names each card that a returned sample mentions, once, so entry IDs there can be read.
    /// </summary>
    private static List<SimulationCardName> NameCards(List<SimulationSampleRecord> samples, List<DeckEntry> library)
    {
        Dictionary<Guid, string> names = library.ToDictionary(entry => entry.EntryId, entry => entry.CardName);
        List<Guid> ordered = [];
        HashSet<Guid> seen = [];
        foreach (SimulationSampleRecord sample in samples)
        {
            foreach (Guid id in SampleEntryIds(sample))
            {
                if (seen.Add(id))
                {
                    ordered.Add(id);
                }
            }
        }

        return [.. ordered.Select(id => new SimulationCardName(id, names[id]))];
    }

    /// <summary>
    /// Lists every entry ID a sample mentions, in the order it appears.
    /// </summary>
    private static IEnumerable<Guid> SampleEntryIds(SimulationSampleRecord sample)
    {
        foreach (SimulationHandDraw hand in sample.Hands)
        {
            foreach (Guid id in hand.EntryIds)
            {
                yield return id;
            }
        }

        foreach (Guid id in sample.Kept.Concat(sample.Bottomed))
        {
            yield return id;
        }

        foreach (SimulationTurnRecord turn in sample.Turns)
        {
            foreach (Guid id in turn.Drawn.Concat(turn.Moved.Select(move => move.EntryId)))
            {
                yield return id;
            }
        }
    }
}
