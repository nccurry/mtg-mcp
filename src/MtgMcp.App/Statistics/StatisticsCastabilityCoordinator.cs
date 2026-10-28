using System.Globalization;
using System.Numerics;
using MtgMcp.App.CardLists;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Works out <c>stats_castability</c>: reads a local deck, builds the population from the caller's source groups,
/// calls the exact calculator, and writes sorted rows. It judges nothing.
/// </summary>
internal sealed class StatisticsCastabilityCoordinator
{
    /// <summary>
    /// Gives the most skipped cards one result lists.
    /// </summary>
    private const int MaximumSkippedListed = 50;

    /// <summary>
    /// Reads the deck source and installed card data.
    /// </summary>
    private readonly CardListResolver resolver;

    /// <summary>
    /// Runs the exact calculation.
    /// </summary>
    private readonly ExactStatisticsCalculator calculator;

    /// <summary>
    /// Creates the coordinator.
    /// </summary>
    internal StatisticsCastabilityCoordinator(CardListResolver resolver, ExactStatisticsCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(calculator);
        this.resolver = resolver;
        this.calculator = calculator;
    }

    /// <summary>
    /// Calculates the castability of every distinct nonland cost of a deck.
    /// </summary>
    internal async Task<OperationResult<CastabilityToolResult>> CalculateAsync(
        CardListSourceInput source,
        IReadOnlyList<CastabilitySourceGroupInput>? sources,
        CastabilityTurnInput turn,
        bool onThePlay,
        bool requireLandDrops,
        CancellationToken cancellationToken)
    {
        if (turn is FixedTurnInput { Turn: < 1 or > ExactStatisticsCalculator.MaximumCastabilityTurn })
        {
            return new OperationInvalidInput(
                "invalid-turn",
                $"turn.turn must be from 1 to {ExactStatisticsCalculator.MaximumCastabilityTurn}.",
                "turn.turn");
        }

        int idCount = 0;
        OperationInvalidInput? invalid = CardListResolver.Validate(source, "source", ref idCount);
        if (invalid is not null)
        {
            return invalid;
        }

        OperationResult<ResolvedDeckDocument> read = await resolver
            .ReadDeckDocumentAsync(source, "source", cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<ResolvedDeckDocument> document)
        {
            return OperationFailure.Forward<ResolvedDeckDocument, CastabilityToolResult>(read);
        }

        DeckDocument deck = document.Data.Deck;
        List<DeckEntry> entries =
        [
            .. deck.Entries.Where(entry => CardListResolver.InZones(entry.Zone, document.Data.Zones)),
        ];
        OperationResult<CastabilityGroupPlan> planned = CastabilityGroupPlanner.Plan(sources, deck, entries);
        if (planned is not OperationSuccess<CastabilityGroupPlan> plan)
        {
            return OperationFailure.Forward<CastabilityGroupPlan, CastabilityToolResult>(planned);
        }

        List<ScryfallCardLookup?> lookups =
        [
            .. entries.Select(entry => ScryfallEntryLookup.For(
                entry.PrintingId,
                entry.SetCode,
                entry.CollectorNumber,
                entry.OracleId)),
        ];
        OperationResult<ScryfallInstalledListCards> cardRead = await resolver
            .ReadInstalledAsync(
                lookups,
                new CardListReadOptions(IncludeRaw: false, IncludeFacts: false),
                cancellationToken)
            .ConfigureAwait(false);
        if (cardRead is not OperationSuccess<ScryfallInstalledListCards> cards)
        {
            return OperationFailure.Forward<ScryfallInstalledListCards, CastabilityToolResult>(cardRead);
        }

        (List<SpellCost> spells, List<CastabilitySkipped> skipped) = ReadSpells(
            entries,
            lookups,
            cards.Data.Cards,
            turn);
        if (spells.Count == 0)
        {
            return new OperationInvalidInput(
                "no-spells-to-check",
                "The deck has no nonland card with a mana cost that can be checked in the chosen zones.",
                "source");
        }

        List<IGrouping<string, SpellCost>> distinct =
        [
            .. spells.GroupBy(spell => $"{spell.Cost}@{spell.Turn}", StringComparer.Ordinal),
        ];
        CastabilityRequest request = new(
            BuildPopulation(entries, plan.Data),
            plan.Data.Sources,
            [.. distinct.Select(group => new CastabilityCostInput(
                group.Key,
                group.First().Requirement,
                group.First().Turn))],
            onThePlay,
            requireLandDrops);
        OperationResult<StatisticsCalculation<CastabilityResult>> calculated = calculator.CalculateCastability(
            request,
            cancellationToken);
        if (calculated.Value is OperationSuccess<StatisticsCalculation<CastabilityResult>> success)
        {
            return success.Data.Value switch
            {
                StatisticsExact<CastabilityResult> exact => new OperationSuccess<CastabilityToolResult>(
                    Present(document.Data.Info, exact.Data, distinct, plan.Data, skipped)),
                StatisticsBoundedUnsupported bounded => new OperationUnsupported(bounded.ReasonCode, bounded.Message),
                _ => new OperationUnavailable("unexpected-statistics-state", "The calculation returned an " +
                    "unexpected state."),
            };
        }

        return OperationFailure.Forward<StatisticsCalculation<CastabilityResult>, CastabilityToolResult>(calculated);
    }

    /// <summary>
    /// Reads the cost and turn of each nonland entry with a mana cost; every other entry that is not a land is listed
    /// as skipped with the reason, so no card disappears from the answer without a trace.
    /// </summary>
    private static (List<SpellCost> Spells, List<CastabilitySkipped> Skipped) ReadSpells(
        List<DeckEntry> entries,
        List<ScryfallCardLookup?> lookups,
        IReadOnlyList<ScryfallListCard?> cards,
        CastabilityTurnInput turn)
    {
        List<SpellCost> spells = [];
        List<CastabilitySkipped> skipped = [];
        for (int index = 0; index < entries.Count; index++)
        {
            DeckEntry entry = entries[index];
            if (cards[index]?.Card is not { } card)
            {
                string reason = lookups[index] is null ? "no-scryfall-identity" : "not-in-card-data";
                skipped.Add(new CastabilitySkipped(entry.EntryId, entry.CardName, reason, null));
                continue;
            }

            // Deliberate: a card is a land when its front face is a land. A double-faced card with a spell on the
            // front is cast as that spell, and one with a land on the front is played as a land.
            if (ScryfallCardFaces.IsLand(card, frontOnly: true))
            {
                continue;
            }

            string? cost = ScryfallCardFaces.FrontManaCost(card);
            (ManaRequirementInput? requirement, string? skipReason) = CastabilityManaCost.Parse(cost);
            if (requirement is null)
            {
                skipped.Add(new CastabilitySkipped(entry.EntryId, card.Name, skipReason!, cost));
                continue;
            }

            int turnNumber = turn is FixedTurnInput fixedTurn
                ? fixedTurn.Turn
                : Math.Max(1, (int)Math.Min(Math.Ceiling(card.ManaValue ?? 0m), int.MaxValue));
            if (turnNumber > ExactStatisticsCalculator.MaximumCastabilityTurn)
            {
                skipped.Add(new CastabilitySkipped(entry.EntryId, card.Name, "mana-value-above-turn-limit", cost));
                continue;
            }

            spells.Add(new SpellCost(entry, card.Name, cost!, turnNumber, requirement));
        }

        return (spells, skipped);
    }

    /// <summary>
    /// Builds the population: each entry is a bucket of its copies, in the source group it belongs to, if any.
    /// </summary>
    private static StatisticsPopulation BuildPopulation(List<DeckEntry> entries, CastabilityGroupPlan plan)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (DeckEntry entry in entries)
        {
            string group = plan.MemberOf.TryGetValue(entry.EntryId, out CastabilityMembership? member)
                ? member.CalculatorName
                : string.Empty;
            counts[group] = counts.GetValueOrDefault(group) + entry.Quantity;
        }

        return new StatisticsPopulation(
            [.. counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new StatisticsPopulationBucket(
                pair.Value,
                pair.Key.Length == 0 ? [] : [pair.Key]))],
            [.. plan.Sources.Select(source => source.Group)]);
    }

    /// <summary>
    /// Builds the tool result: rows sorted by probability and name, cut to the size limit.
    /// </summary>
    private static CastabilityToolResult Present(
        CardListSourceInfo info,
        CastabilityResult result,
        List<IGrouping<string, SpellCost>> distinct,
        CastabilityGroupPlan plan,
        List<CastabilitySkipped> skipped)
    {
        Dictionary<string, SpellCost[]> byKey = distinct.ToDictionary(
            group => group.Key,
            group => group.ToArray(),
            StringComparer.Ordinal);
        List<CastabilityRowView> rows =
        [
            .. result.Rows.Select(row =>
            {
                SpellCost[] spells = byKey[row.Key];
                return new CastabilityRowView(
                    spells[0].Cost,
                    row.Turn,
                    row.CardsSeen,
                    row.Probability,
                    [.. spells.OrderBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(spell => new RuleEntryRef(spell.Entry.EntryId, spell.Name))]);
            }),
        ];
        rows.Sort(CompareRows);
        List<string> limits =
        [
            "Probabilities are exact for the declared source groups; which cards count as sources and what " +
                "probability is good enough are your judgments.",
            "Cards with no mana cost, with X, with hybrid or Phyrexian symbols, or with two costs, and entries with " +
                "no card data, are listed in skipped and not checked.",
            "Whether a land enters tapped, activation costs, and alternate costs are not modeled.",
        ];
        if (result.RequireLandDrops)
        {
            limits.Add("A probability is null when the deck holds fewer sources than the turn number, so the " +
                "condition cannot happen.");
        }

        List<CastabilitySkipped> listed = [.. skipped.Take(MaximumSkippedListed)];
        List<int> sizes = [.. rows.Select(row => ResultSize.Measure(row) + 1)];
        int fixedBytes = listed.Sum(item => ResultSize.Measure(item) + 1) + ResultSize.Measure(limits);
        int keep = ResultSizeLimit.CountFitting(sizes, fixedBytes);
        return new CastabilityToolResult(
            [info],
            result.Derivation,
            result.OnThePlay,
            result.RequireLandDrops,
            plan.Views,
            [.. rows.Take(keep)],
            listed,
            limits)
        {
            SkippedOmitted = skipped.Count > listed.Count ? skipped.Count - listed.Count : null,
            Truncated = keep < rows.Count ? true : null,
            Narrow = keep < rows.Count
                ? "Rows were left out to stay under the size limit. Choose fewer zones, or ask for a fixed turn."
                : null,
        };
    }

    /// <summary>
    /// Orders rows by probability (lowest first, undefined last), then by the first card name.
    /// </summary>
    private static int CompareRows(CastabilityRowView first, CastabilityRowView second)
    {
        if (first.Probability is null || second.Probability is null)
        {
            if (first.Probability is null && second.Probability is null)
            {
                return CompareNames(first, second);
            }

            return first.Probability is null ? 1 : -1;
        }

        BigInteger left = BigInteger.Parse(first.Probability.Numerator, CultureInfo.InvariantCulture) *
            BigInteger.Parse(second.Probability.Denominator, CultureInfo.InvariantCulture);
        BigInteger right = BigInteger.Parse(second.Probability.Numerator, CultureInfo.InvariantCulture) *
            BigInteger.Parse(first.Probability.Denominator, CultureInfo.InvariantCulture);
        int byProbability = left.CompareTo(right);
        return byProbability != 0 ? byProbability : CompareNames(first, second);
    }

    /// <summary>
    /// Orders two rows by the name of their first card, then by cost.
    /// </summary>
    private static int CompareNames(CastabilityRowView first, CastabilityRowView second)
    {
        int byName = StringComparer.OrdinalIgnoreCase.Compare(first.Cards[0].Name, second.Cards[0].Name);
        return byName != 0 ? byName : string.CompareOrdinal(first.Cost, second.Cost);
    }

    /// <summary>
    /// Holds one spell with its exact requirement and turn.
    /// </summary>
    private sealed record SpellCost(
        DeckEntry Entry,
        string Name,
        string Cost,
        int Turn,
        ManaRequirementInput Requirement);
}
