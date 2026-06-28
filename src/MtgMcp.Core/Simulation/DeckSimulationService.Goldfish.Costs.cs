namespace MtgMcp.Core;

/// <summary>
/// Contains cast-cost estimation helpers for the heuristic goldfish sequencer.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Estimates what the goldfish sequencer must spend to cast a spell right now.
    /// </summary>
    private static GoldfishCastCost EstimateGoldfishCastCost(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens,
        int availableMana,
        bool commanderOnline)
    {
        CardSnapshot snapshot = DeckServiceHelpers.GetSnapshot(card);
        string text = snapshot.OracleText ?? "";
        int printedCost = GoldfishManaValue(card);
        int requiredMana = printedCost;
        if (HasConvoke(text))
        {
            requiredMana = Math.Max(0, requiredMana - ConvokeCreatureCount(battlefield, tokens));
        }

        int affinityReduction = EstimateAffinityReduction(card, battlefield, tokens, artifactTokens);
        if (affinityReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - affinityReduction);
        }

        int dynamicReduction = EstimateDynamicCostReduction(card, battlefield, tokens, artifactTokens, foodTokens);
        if (dynamicReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - dynamicReduction);
        }

        int activeReduction = EstimateActiveCostReduction(card, battlefield);
        if (activeReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - activeReduction);
        }

        int commanderReduction = EstimateCommanderConditionReduction(card, commanderOnline);
        if (commanderReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - commanderReduction);
        }

        int xValue = 0;
        if (HasGoldfishXCost(card) && UsesXAsScalingPayoff(card) && availableMana > requiredMana)
        {
            xValue = Math.Min(8, availableMana - requiredMana);
        }

        return new GoldfishCastCost(
            RequiredMana: Math.Max(0, requiredMana),
            XValue: xValue);
    }

    /// <summary>
    /// Counts creatures that can safely pay convoke costs in a goldfish board.
    /// </summary>
    private static int ConvokeCreatureCount(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int creatures = tokens;
        foreach (DeckCard card in battlefield)
        {
            if (IsCreatureSpell(card))
            {
                creatures++;
            }
        }

        return creatures;
    }

    /// <summary>
    /// Checks whether Oracle text contains the convoke keyword.
    /// </summary>
    private static bool HasConvoke(string oracleText)
    {
        return oracleText.Contains("convoke", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Estimates card-text reductions such as Blasphemous Act without full rules parsing.
    /// </summary>
    private static int EstimateDynamicCostReduction(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        if (!ContainsAny(text, "costs {1} less", "cost {1} less", "costs one less", "cost one less"))
        {
            return 0;
        }

        if (ContainsAny(text, "for each creature"))
        {
            return ConvokeCreatureCount(battlefield, tokens);
        }

        if (ContainsAny(text, "for each token"))
        {
            return tokens;
        }

        if (ContainsAny(text, "for each artifact"))
        {
            return CountArtifactPermanents(battlefield) + artifactTokens;
        }

        if (ContainsAny(text, "for each food"))
        {
            return foodTokens;
        }

        if (ContainsAny(text, "for each enchantment"))
        {
            return battlefield.Count(permanent => ContainsAny(DeckServiceHelpers.GetSnapshot(permanent).TypeLine ?? "", "Enchantment"));
        }

        return 0;
    }

    /// <summary>
    /// Estimates affinity reductions from the current battlefield and token bank.
    /// </summary>
    private static int EstimateAffinityReduction(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        if (!ContainsAny(text, "affinity for"))
        {
            return 0;
        }

        if (ContainsAny(text, "affinity for artifacts"))
        {
            return CountArtifactPermanents(battlefield) + artifactTokens;
        }

        if (ContainsAny(text, "affinity for creatures"))
        {
            return ConvokeCreatureCount(battlefield, tokens);
        }

        if (ContainsAny(text, "affinity for tokens"))
        {
            return tokens;
        }

        if (ContainsAny(text, "affinity for enchantments"))
        {
            return battlefield.Count(permanent => ContainsAny(DeckServiceHelpers.GetSnapshot(permanent).TypeLine ?? "", "Enchantment"));
        }

        return 0;
    }

    /// <summary>
    /// Counts artifact permanents already represented as cards on the battlefield.
    /// </summary>
    private static int CountArtifactPermanents(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Count(permanent => ContainsAny(DeckServiceHelpers.GetSnapshot(permanent).TypeLine ?? "", "Artifact"));
    }

    /// <summary>
    /// Estimates simple reductions gated on controlling a commander.
    /// </summary>
    private static int EstimateCommanderConditionReduction(DeckCard card, bool commanderOnline)
    {
        if (!commanderOnline)
        {
            return 0;
        }

        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return ContainsAny(text, "if you control your commander", "if you control a commander", "as long as you control your commander")
            && ContainsAny(text, "costs {1} less", "cost {1} less", "costs one less", "cost one less")
            ? 1
            : 0;
    }

    /// <summary>
    /// Estimates cost reduction from permanents already deployed in the goldfish board.
    /// </summary>
    private static int EstimateActiveCostReduction(DeckCard spell, IReadOnlyList<DeckCard> battlefield)
    {
        int reduction = 0;
        foreach (DeckCard permanent in battlefield)
        {
            if (CostReducerApplies(permanent, spell))
            {
                reduction++;
            }
        }

        return Math.Min(3, reduction);
    }

    /// <summary>
    /// Checks whether one battlefield permanent reduces the candidate spell's cost.
    /// </summary>
    private static bool CostReducerApplies(DeckCard reducer, DeckCard spell)
    {
        string text = DeckServiceHelpers.GetSnapshot(reducer).OracleText ?? "";
        if (!ContainsAny(text, "cost {1} less", "costs {1} less", "cost one less", "costs one less", "cost less to cast"))
        {
            return false;
        }

        string typeLine = DeckServiceHelpers.GetSnapshot(spell).TypeLine ?? "";
        if (ContainsAny(text, "commander spells") && !IsCommanderCard(spell))
        {
            return false;
        }

        if (ContainsAny(text, "creature spells") && !ContainsAny(typeLine, "Creature"))
        {
            return false;
        }

        if (ContainsAny(text, "instant and sorcery spells")
            && !ContainsAny(typeLine, "Instant", "Sorcery"))
        {
            return false;
        }

        if (ContainsAny(text, "artifact spells") && !ContainsAny(typeLine, "Artifact"))
        {
            return false;
        }

        if (ContainsAny(text, "enchantment spells") && !ContainsAny(typeLine, "Enchantment"))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Keeps generic cost reduction from erasing colored mana that still has to be paid.
    /// </summary>
    private static int MinimumReducedCost(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return 0;
        }

        int coloredSymbols = 0;
        for (int index = 0; index < manaCost.Length; index++)
        {
            if (manaCost[index] != '{')
            {
                continue;
            }

            int close = manaCost.IndexOf('}', index + 1);
            if (close < 0)
            {
                break;
            }

            string symbol = manaCost[(index + 1)..close];
            if (!int.TryParse(symbol, out _)
                && !symbol.Equals("X", StringComparison.OrdinalIgnoreCase))
            {
                coloredSymbols++;
            }

            index = close;
        }

        return coloredSymbols;
    }

    /// <summary>
    /// Checks whether the card can spend extra mana through an X cost.
    /// </summary>
    private static bool HasGoldfishXCost(DeckCard card)
    {
        CardSnapshot snapshot = DeckServiceHelpers.GetSnapshot(card);
        return ContainsAny(snapshot.ManaCost ?? "", "{X}", "{x}");
    }

    /// <summary>
    /// Checks whether spending extra mana on X changes a board or damage outcome.
    /// </summary>
    private static bool UsesXAsScalingPayoff(DeckCard card)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return ContainsAny(text, "create X", "draw X", "deals X", "get +X/+X", "gets +X/+X", "lose X life");
    }
}
