using System.Buffers.Binary;
using System.Security.Cryptography;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.OnCurve;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Turns saved-deck entries and direct installed card facts into one pure estimate request.
/// </summary>
internal static class DeckOnCurveEstimatePreparer
{
    /// <summary>
    /// Builds the pure request and all source-facing report details without inspecting card text.
    /// </summary>
    internal static OperationResult<DeckOnCurvePreparedEstimate> Prepare(
        DeckDocument deck,
        DeckOnCurveDeckSelection selection,
        DeckOnCurveResolvedCards source,
        DeckOnCurveEstimateToolRequest request)
    {
        DeckOnCurveResolvedCard? target = null;
        foreach (DeckOnCurveResolvedCard resolved in source.Cards)
        {
            if (resolved.Entry.EntryId == selection.Target.EntryId)
            {
                target = resolved;
                break;
            }
        }

        if (target is null)
        {
            return new OperationUnavailable(
                "on-curve-target-fact-unavailable",
                "The selected target's exact installed Scryfall fact is unavailable.");
        }

        if (target.Card.Faces.Count > 0)
        {
            return new OperationUnsupported(
                "unsupported-on-curve-multiface-target",
                "The version-one on-curve estimate does not model a multi-face target card.");
        }

        if (source.Binding.CorpusGenerationId is not Guid generationId)
        {
            return new OperationUnavailable(
                "on-curve-card-fact-unavailable",
                "The exact installed Scryfall fact required for this estimate is unavailable.");
        }

        List<OnCurveDeckEntry> mainboard = [];
        List<DeckOnCurveResolvedCard> candidates = [];
        List<DeckOnCurveKnownEmptyLand> knownEmptyLands = [];
        List<DeckOnCurveMultiFaceCard> multiFaceCards = [];
        List<DeckOnCurveSourceFact> sourceFacts = [];
        foreach (DeckOnCurveResolvedCard resolved in source.Cards)
        {
            ScryfallCard card = resolved.Card;
            mainboard.Add(new OnCurveDeckEntry(
                resolved.Entry.EntryId,
                card.Id,
                resolved.Entry.Quantity,
                MapProducedMana(card.ProducedMana)));
            sourceFacts.Add(MapSourceFact(resolved));
            if (card.Faces.Count > 0)
            {
                multiFaceCards.Add(new DeckOnCurveMultiFaceCard(
                    resolved.Entry.EntryId,
                    card.Id,
                    card.Name));
                continue;
            }

            if (!IsLand(card.TypeLine))
            {
                continue;
            }

            switch (card.ProducedMana.Value)
            {
                case ScryfallProducedManaMissing:
                case ScryfallProducedManaNull:
                    return new OperationUnavailable(
                        "on-curve-land-produced-mana-unavailable",
                        "A single-faced land is missing its direct installed Scryfall produced-mana fact.");
                case ScryfallProducedManaValues values when values.Colors.Count == 0:
                    knownEmptyLands.Add(new DeckOnCurveKnownEmptyLand(
                        resolved.Entry.EntryId,
                        card.Id,
                        card.Name,
                        card.TypeLine,
                        MapProducedManaFact(card.ProducedMana)));
                    break;
                case ScryfallProducedManaValues:
                    candidates.Add(resolved);
                    break;
                default:
                    return new OperationUnavailable(
                        "on-curve-land-produced-mana-unavailable",
                        "A single-faced land has an unusable installed Scryfall produced-mana fact.");
            }
        }

        OperationResult<IReadOnlyList<OnCurveLandRuleInput>> rulesResult = MapLandRules(
            request.LandRules,
            candidates);
        if (rulesResult is not OperationSuccess<IReadOnlyList<OnCurveLandRuleInput>> rules)
        {
            return ForwardFailure<IReadOnlyList<OnCurveLandRuleInput>, DeckOnCurvePreparedEstimate>(rulesResult);
        }

        List<Guid> candidateIds = [];
        foreach (DeckOnCurveResolvedCard candidate in candidates)
        {
            candidateIds.Add(candidate.Entry.EntryId);
        }

        OnCurveRequest onCurveRequest = new(
            deck.DeckId,
            deck.Revision,
            new OnCurveTarget(selection.Target.EntryId, target.Card.Id, target.Card.ManaCost),
            mainboard,
            candidateIds,
            rules.Data,
            request.TurnLimit,
            request.SampleCount,
            request.OnThePlay,
            request.Seed ?? CreateSeed());
        DeckOnCurveLandRuleCoverage coverage = BuildLandRuleCoverage(
            candidates,
            rules.Data,
            knownEmptyLands,
            multiFaceCards);
        DeckOnCurveSourceEvidence sourceEvidence = new(
            "installed-scryfall-card-data",
            generationId,
            source.Binding.EvidenceChecksum);
        return new OperationSuccess<DeckOnCurvePreparedEstimate>(new DeckOnCurvePreparedEstimate(
            onCurveRequest,
            new DeckOnCurveTargetResult(
                selection.Target.EntryId,
                target.Card.Id,
                target.Card.Name,
                target.Card.ManaCost),
            sourceFacts,
            sourceEvidence,
            coverage));
    }

    /// <summary>
    /// Maps all caller land rules only after the exact candidate set is known.
    /// </summary>
    private static OperationResult<IReadOnlyList<OnCurveLandRuleInput>> MapLandRules(
        IReadOnlyList<DeckOnCurveLandRuleToolInput>? inputs,
        IReadOnlyList<DeckOnCurveResolvedCard> candidates)
    {
        if (inputs is null)
        {
            return Invalid<IReadOnlyList<OnCurveLandRuleInput>>(
                "The request must include land rules for every eligible land.");
        }

        HashSet<Guid> candidateIds = [];
        foreach (DeckOnCurveResolvedCard candidate in candidates)
        {
            candidateIds.Add(candidate.Entry.EntryId);
        }

        Dictionary<Guid, OnCurveLandRule> rulesById = [];
        foreach (DeckOnCurveLandRuleToolInput? input in inputs)
        {
            if (input is null || input.EntryId == Guid.Empty ||
                !candidateIds.Contains(input.EntryId) ||
                !DeckOnCurveText.TryParseLandRule(input.Rule, out OnCurveLandRule rule) ||
                !rulesById.TryAdd(input.EntryId, rule))
            {
                return Invalid<IReadOnlyList<OnCurveLandRuleInput>>(
                    "Land rules must name each eligible land exactly once with a supported rule.");
            }
        }

        List<Guid> missingEntryIds = [];
        foreach (DeckOnCurveResolvedCard candidate in candidates)
        {
            if (!rulesById.ContainsKey(candidate.Entry.EntryId))
            {
                missingEntryIds.Add(candidate.Entry.EntryId);
            }
        }

        if (missingEntryIds.Count > 0)
        {
            missingEntryIds.Sort();
            return Invalid<IReadOnlyList<OnCurveLandRuleInput>>(
                $"Land rules are missing eligible entry IDs: {string.Join(", ", missingEntryIds)}.");
        }

        List<OnCurveLandRuleInput> rules = [];
        foreach (DeckOnCurveResolvedCard candidate in candidates)
        {
            rules.Add(new OnCurveLandRuleInput(
                candidate.Entry.EntryId,
                rulesById[candidate.Entry.EntryId]));
        }

        return new OperationSuccess<IReadOnlyList<OnCurveLandRuleInput>>(rules);
    }

    /// <summary>
    /// Creates report coverage in saved deck order after every caller rule is validated.
    /// </summary>
    private static DeckOnCurveLandRuleCoverage BuildLandRuleCoverage(
        IReadOnlyList<DeckOnCurveResolvedCard> candidates,
        IReadOnlyList<OnCurveLandRuleInput> rules,
        IReadOnlyList<DeckOnCurveKnownEmptyLand> knownEmptyLands,
        IReadOnlyList<DeckOnCurveMultiFaceCard> multiFaceCards)
    {
        Dictionary<Guid, OnCurveLandRule> ruleByEntryId = [];
        foreach (OnCurveLandRuleInput rule in rules)
        {
            ruleByEntryId.Add(rule.EntryId, rule.Rule);
        }

        List<DeckOnCurveCandidateLand> candidateLands = [];
        foreach (DeckOnCurveResolvedCard candidate in candidates)
        {
            candidateLands.Add(new DeckOnCurveCandidateLand(
                candidate.Entry.EntryId,
                candidate.Card.Id,
                candidate.Card.Name,
                candidate.Card.TypeLine,
                MapProducedManaFact(candidate.Card.ProducedMana),
                DeckOnCurveText.LandRule(ruleByEntryId[candidate.Entry.EntryId])));
        }

        return new DeckOnCurveLandRuleCoverage(candidateLands, knownEmptyLands, multiFaceCards);
    }

    /// <summary>
    /// Preserves the direct produced-mana source state for the pure calculation.
    /// </summary>
    private static OnCurveProducedMana MapProducedMana(ScryfallProducedMana producedMana)
    {
        return producedMana.Value switch
        {
            ScryfallProducedManaMissing => new OnCurveProducedManaMissing(),
            ScryfallProducedManaNull => new OnCurveProducedManaNull(),
            ScryfallProducedManaValues values => new OnCurveProducedManaValues(values.Colors),
            _ => throw new InvalidOperationException("The Scryfall produced-mana fact has no active case."),
        };
    }

    /// <summary>
    /// Preserves the direct produced-mana source state in the public result.
    /// </summary>
    private static DeckOnCurveProducedManaFact MapProducedManaFact(ScryfallProducedMana producedMana)
    {
        return producedMana.Value switch
        {
            ScryfallProducedManaMissing => new DeckOnCurveProducedManaFact("missing", null),
            ScryfallProducedManaNull => new DeckOnCurveProducedManaFact("null", null),
            ScryfallProducedManaValues values => new DeckOnCurveProducedManaFact("values", values.Colors),
            _ => throw new InvalidOperationException("The Scryfall produced-mana fact has no active case."),
        };
    }

    /// <summary>
    /// Maps one exact installed source card to its displayed direct fact set.
    /// </summary>
    private static DeckOnCurveSourceFact MapSourceFact(DeckOnCurveResolvedCard resolved)
    {
        ScryfallCard card = resolved.Card;
        return new DeckOnCurveSourceFact(
            resolved.Entry.EntryId,
            card.Id,
            card.Name,
            card.ManaCost,
            card.TypeLine,
            card.Faces.Count == 0,
            MapProducedManaFact(card.ProducedMana),
            card.Evidence);
    }

    /// <summary>
    /// Identifies the Land card type from the direct printed type line only.
    /// </summary>
    private static bool IsLand(string? typeLine)
    {
        if (string.IsNullOrWhiteSpace(typeLine))
        {
            return false;
        }

        int subtypeSeparator = typeLine.IndexOf('\u2014');
        string cardTypes = subtypeSeparator < 0 ? typeLine : typeLine[..subtypeSeparator];
        foreach (string cardType in cardTypes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(cardType, "Land", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates one unpredictable seed when the caller did not request replay from a prior seed.
    /// </summary>
    private static string CreateSeed()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(sizeof(ulong));
        return OnCurveSeed.Format(BinaryPrimitives.ReadUInt64LittleEndian(bytes));
    }

    /// <summary>
    /// Creates one stable invalid-input result for a malformed intermediate request.
    /// </summary>
    private static OperationInvalidInput Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-on-curve-request", message);
    }

    /// <summary>
    /// Preserves each recognized operation failure while changing only the success payload type.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable(
                "unexpected-on-curve-result",
                "The on-curve estimate returned an unexpected result."),
            _ => new OperationUnavailable(
                "unexpected-on-curve-result",
                "The on-curve estimate returned an unexpected result."),
        };
    }
}

/// <summary>
/// Carries one pure request and the source facts needed to present its result.
/// </summary>
internal sealed record DeckOnCurvePreparedEstimate(
    OnCurveRequest Request,
    DeckOnCurveTargetResult Target,
    IReadOnlyList<DeckOnCurveSourceFact> SourceFacts,
    DeckOnCurveSourceEvidence SourceEvidence,
    DeckOnCurveLandRuleCoverage LandRuleCoverage);
