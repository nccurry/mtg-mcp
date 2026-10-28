using MtgMcp.App.CardLists;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.OnCurve;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Holds what the on-curve preset expanded to and the card facts it read to get there.
/// </summary>
internal sealed record ExpandedPreset(SimulationScenario Scenario, SimulationPresetFacts Facts);

/// <summary>
/// Expands the on-curve preset into the groups, rule, and condition it stands for. The preset reads installed Scryfall
/// card data once to learn which entries are lands, what colors they make, and what the target costs; everything it
/// then asks of the engine is stated as ordinary rules.
/// </summary>
internal static class SimulationPresetExpander
{
    /// <summary>
    /// Names the land rule for mana usable on the turn the land is played.
    /// </summary>
    private const string SameTurnRule = "one-mana-same-turn";

    /// <summary>
    /// Names the land rule for mana usable from the next turn.
    /// </summary>
    private const string NextTurnRule = "one-mana-next-turn";

    /// <summary>
    /// Names the land rule for a land that stays in the library.
    /// </summary>
    private const string NotModeledRule = "not-modeled";

    /// <summary>
    /// Names the preset's one group.
    /// </summary>
    private const string LandsGroup = "lands";

    /// <summary>
    /// Names the preset's one condition.
    /// </summary>
    private const string ConditionId = "on-curve";

    /// <summary>
    /// Gives the most entries one failure message names.
    /// </summary>
    private const int MaximumNamed = 5;

    /// <summary>
    /// Expands the preset over the main-zone entries of a deck.
    /// </summary>
    internal static async Task<OperationResult<ExpandedPreset>> ExpandAsync(
        CardListResolver resolver,
        SimulationOnCurvePresetInput preset,
        IReadOnlyList<DeckEntry> library,
        DeckDocument deck,
        CancellationToken cancellationToken)
    {
        DeckEntry? target = library.FirstOrDefault(entry => entry.EntryId == preset.TargetEntryId);
        if (target is null)
        {
            return deck.Entries.Any(entry => entry.EntryId == preset.TargetEntryId)
                ? new OperationInvalidInput(
                    "target-not-in-main-zone",
                    "The target entry must be in the main zone.",
                    "preset.targetEntryId")
                : new OperationNotFound("deck-entry-not-found", "The target entry was not found in the local deck.");
        }

        List<ScryfallCardLookup?> lookups =
        [
            .. library.Select(entry => ScryfallEntryLookup.For(
                entry.PrintingId,
                entry.SetCode,
                entry.CollectorNumber,
                entry.OracleId)),
        ];
        OperationResult<ScryfallInstalledListCards> read = await resolver
            .ReadInstalledAsync(
                lookups,
                new CardListReadOptions(IncludeRaw: false, IncludeFacts: false),
                cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<ScryfallInstalledListCards> cards)
        {
            return OperationFailure.Forward<ScryfallInstalledListCards, ExpandedPreset>(read);
        }

        OperationUnavailable? missing = FindMissingCards(library, cards.Data.Cards);
        if (missing is not null)
        {
            return missing;
        }

        return Expand(preset, target, library, cards.Data.Cards, library.ToList().IndexOf(target));
    }

    /// <summary>
    /// Refuses a deck with main-zone entries that have no card in installed data, because a land that cannot be
    /// identified would silently count as a nonland card.
    /// </summary>
    private static OperationUnavailable? FindMissingCards(
        IReadOnlyList<DeckEntry> library,
        IReadOnlyList<ScryfallListCard?> cards)
    {
        List<string> names = [];
        for (int index = 0; index < library.Count; index++)
        {
            if (cards[index] is null)
            {
                names.Add(library[index].CardName);
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        string listed = string.Join(", ", names.Take(MaximumNamed));
        string more = names.Count > MaximumNamed ? $" and {names.Count - MaximumNamed} more" : string.Empty;
        return new OperationUnavailable(
            "on-curve-card-fact-unavailable",
            $"The on-curve preset needs installed card data for every main-zone entry, and {names.Count} " +
            $"have none: {listed}{more}. Give those entries exact printings, or run scryfall_card_data_sync.");
    }

    /// <summary>
    /// Builds the scenario and the facts from the resolved cards.
    /// </summary>
    private static OperationResult<ExpandedPreset> Expand(
        SimulationOnCurvePresetInput preset,
        DeckEntry target,
        IReadOnlyList<DeckEntry> library,
        IReadOnlyList<ScryfallListCard?> cards,
        int targetIndex)
    {
        ScryfallCard targetCard = cards[targetIndex]!.Card;
        if (targetCard.Faces.Count > 0)
        {
            return new OperationUnsupported(
                "unsupported-on-curve-multiface-target",
                "The on-curve preset does not model a card with more than one face as the target.");
        }

        string? manaCost = ScryfallCardFaces.FrontManaCost(targetCard);
        OperationInvalidInput? costProblem = string.IsNullOrWhiteSpace(manaCost)
            ? new OperationInvalidInput(
                "unsupported-mana-cost",
                "The target has no mana cost, so there is nothing to pay.",
                "preset.targetEntryId")
            : SimulationEngine.CheckManaCost(manaCost, "preset.targetEntryId");
        if (costProblem is not null)
        {
            return costProblem;
        }

        OperationResult<Classification> classified = Classify(library, cards);
        if (classified is not OperationSuccess<Classification> lands)
        {
            return OperationFailure.Forward<Classification, ExpandedPreset>(classified);
        }

        OperationResult<Dictionary<Guid, string>> ruled = ReadLandRules(preset.LandRules, lands.Data.Candidates);
        if (ruled is not OperationSuccess<Dictionary<Guid, string>> rules)
        {
            return OperationFailure.Forward<Dictionary<Guid, string>, ExpandedPreset>(ruled);
        }

        SimulationScenario scenario = BuildScenario(preset, target, manaCost!, lands.Data.Candidates, rules.Data);
        SimulationPresetFacts facts = new(
            "on-curve",
            new SimulationPresetTarget(target.EntryId, targetCard.Id, targetCard.Name, manaCost!),
            [.. lands.Data.Candidates.Select(land => new SimulationPresetLand(
                land.Entry.EntryId,
                land.Card.Id,
                land.Card.Name,
                land.Colors,
                rules.Data[land.Entry.EntryId]))],
            lands.Data.KnownEmpty,
            lands.Data.MultiFace,
            "installed-scryfall-card-data");
        return new OperationSuccess<ExpandedPreset>(new ExpandedPreset(scenario, facts));
    }

    /// <summary>
    /// Holds the entries sorted into modeled lands and the entries the preset leaves out.
    /// </summary>
    private sealed record Classification(
        List<Candidate> Candidates,
        List<SimulationPresetUnmodeled> KnownEmpty,
        List<SimulationPresetUnmodeled> MultiFace);

    /// <summary>
    /// Holds one single-faced land that lists colors it can add.
    /// </summary>
    private sealed record Candidate(DeckEntry Entry, ScryfallCard Card, IReadOnlyList<string> Colors);

    /// <summary>
    /// Sorts the main-zone entries: a single-faced land with listed colors can be played; a land with an empty color
    /// list and a card with several faces are listed as left out; a land with no produced-mana fact stops the preset.
    /// </summary>
    private static OperationResult<Classification> Classify(
        IReadOnlyList<DeckEntry> library,
        IReadOnlyList<ScryfallListCard?> cards)
    {
        Classification result = new([], [], []);
        for (int index = 0; index < library.Count; index++)
        {
            ScryfallCard card = cards[index]!.Card;
            DeckEntry entry = library[index];
            if (card.Faces.Count > 0)
            {
                result.MultiFace.Add(new SimulationPresetUnmodeled(entry.EntryId, card.Id, card.Name));
                continue;
            }

            if (!ScryfallCardFaces.IsLand(card, frontOnly: true))
            {
                continue;
            }

            switch (card.ProducedMana.Value)
            {
                case ScryfallProducedManaValues { Colors.Count: 0 }:
                    result.KnownEmpty.Add(new SimulationPresetUnmodeled(entry.EntryId, card.Id, card.Name));
                    break;
                case ScryfallProducedManaValues values:
                    if (values.Colors.Any(color => !ScryfallCardFaces.ManaColors.Contains(color)))
                    {
                        return new OperationUnsupported(
                            "unsupported-on-curve-source-mana",
                            $"{card.Name} lists a produced-mana value outside W, U, B, R, G, and C.");
                    }

                    result.Candidates.Add(new Candidate(entry, card, values.Colors));
                    break;
                default:
                    return new OperationUnavailable(
                        "on-curve-land-produced-mana-unavailable",
                        $"{card.Name} is a single-faced land with no produced-mana fact in installed card data.");
            }
        }

        return new OperationSuccess<Classification>(result);
    }

    /// <summary>
    /// Reads the caller's rule for each eligible land: each land is named once, and every eligible land is named.
    /// </summary>
    private static OperationResult<Dictionary<Guid, string>> ReadLandRules(
        IReadOnlyList<SimulationLandRuleInput> inputs,
        List<Candidate> candidates)
    {
        HashSet<Guid> eligible = [.. candidates.Select(candidate => candidate.Entry.EntryId)];
        Dictionary<Guid, string> rules = [];
        for (int position = 0; position < inputs.Count; position++)
        {
            SimulationLandRuleInput? input = inputs[position];
            string path = $"preset.landRules[{position}]";
            if (input is null || input.Rule is not (SameTurnRule or NextTurnRule or NotModeledRule))
            {
                return new OperationInvalidInput(
                    "invalid-land-rules",
                    "Each land rule needs an entry and a rule of one-mana-same-turn, one-mana-next-turn, or " +
                    "not-modeled.",
                    path);
            }

            if (!eligible.Contains(input.EntryId) || !rules.TryAdd(input.EntryId, input.Rule))
            {
                return new OperationInvalidInput(
                    "invalid-land-rules",
                    $"Land rules must name each eligible single-faced land once; {input.EntryId:D} is not an " +
                    "eligible land or is named twice.",
                    path + ".entryId");
            }
        }

        List<Guid> missing = [.. eligible.Where(id => !rules.ContainsKey(id)).OrderBy(id => id)];
        if (missing.Count > 0)
        {
            return new OperationInvalidInput(
                "invalid-land-rules",
                $"Land rules are missing {missing.Count} eligible land entry IDs: " +
                string.Join(", ", missing.Take(MaximumNamed).Select(id => id.ToString("D"))) +
                (missing.Count > MaximumNamed ? ", ..." : ".") +
                " Use not-modeled for a land that should stay in the library.",
                "preset.landRules");
        }

        return new OperationSuccess<Dictionary<Guid, string>>(rules);
    }

    /// <summary>
    /// States the preset as ordinary rules: one group of the modeled lands with their colors, one <c>play</c> rule
    /// with <c>target-first</c>, no mulligan, and one <c>can-cast</c> condition that needs the target in hand.
    /// </summary>
    private static SimulationScenario BuildScenario(
        SimulationOnCurvePresetInput preset,
        DeckEntry target,
        string manaCost,
        List<Candidate> candidates,
        Dictionary<Guid, string> rules)
    {
        List<Candidate> modeled = [.. candidates.Where(candidate => rules[candidate.Entry.EntryId] != NotModeledRule)];
        List<SimulationGroupInput> groups = [];
        List<SimulationRuleInput> eachTurn = [];
        if (modeled.Count > 0)
        {
            groups.Add(new SimulationGroupInput
            {
                Id = LandsGroup,
                Select = new CastabilityEntryIdsSelect([.. modeled.Select(candidate => candidate.Entry.EntryId)]),
                Produces = BuildProduces(modeled),
            });
            List<Guid> nextTurn =
            [
                .. modeled
                    .Where(candidate => rules[candidate.Entry.EntryId] == NextTurnRule)
                    .Select(candidate => candidate.Entry.EntryId),
            ];
            eachTurn.Add(new SimulationPlayInput
            {
                Group = LandsGroup,
                Pick = "target-first",
                MaxPerTurn = 1,
                AddsMana = 1,
                ManaFrom = "same-turn",
                ManaFromByEntry = nextTurn.Count == 0
                    ? null
                    : [new SimulationManaFromOverrideInput { EntryIds = nextTurn, ManaFrom = "next-turn" }],
                TargetCost = manaCost,
            });
        }

        return new SimulationScenario(
            groups,
            new SimulationOpeningHandInput { Size = 7, OnThePlay = preset.OnThePlay },
            eachTurn,
            preset.TurnLimit,
            [
                new SimulationConditionInput
                {
                    Id = ConditionId,
                    Check = new SimulationCanCastInput
                    {
                        ManaCost = manaCost,
                        ByTurn = preset.TurnLimit,
                        InHand = target.EntryId,
                    },
                },
            ]);
    }

    /// <summary>
    /// Maps each land to its own colors: the first land's colors are the group default and the lands whose colors
    /// differ are listed in <c>byEntry</c>, one item for each distinct color list.
    /// </summary>
    private static SimulationProducesInput BuildProduces(List<Candidate> modeled)
    {
        IReadOnlyList<string> defaults = modeled[0].Colors;
        List<(IReadOnlyList<string> Colors, List<Guid> EntryIds)> overrides = [];
        foreach (Candidate candidate in modeled)
        {
            if (candidate.Colors.SequenceEqual(defaults))
            {
                continue;
            }

            int existing = overrides.FindIndex(item => item.Colors.SequenceEqual(candidate.Colors));
            if (existing < 0)
            {
                overrides.Add((candidate.Colors, [candidate.Entry.EntryId]));
            }
            else
            {
                overrides[existing].EntryIds.Add(candidate.Entry.EntryId);
            }
        }

        return new SimulationProducesInput
        {
            Default = defaults,
            ByEntry = overrides.Count == 0
                ? null
                : [.. overrides.Select(item => new SimulationProducesOverrideInput
                {
                    EntryIds = item.EntryIds,
                    Colors = item.Colors,
                })],
        };
    }
}
