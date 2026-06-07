namespace MtgMcp.Core;

/// <summary>
/// Detects narrow commander-specific simulation rules that can be modeled without becoming a rules engine.
/// </summary>
internal sealed class CommanderSpecificSimulationRules
{
    /// <summary>
    /// Gets whether Inga and Esika was detected as a command-zone card.
    /// </summary>
    public bool HasIngaAndEsika { get; private init; }

    /// <summary>
    /// Gets assumption notes that should be surfaced with simulation output.
    /// </summary>
    public List<string> Assumptions { get; private init; } = [];

    /// <summary>
    /// Creates commander-specific rule flags from included workspace cards.
    /// </summary>
    public static CommanderSpecificSimulationRules Build(IEnumerable<DeckCard> includedCards)
    {
        bool hasIngaAndEsika = false;
        foreach (DeckCard card in includedCards)
        {
            if (!IsCommanderCard(card))
            {
                continue;
            }

            if (card.Name.Equals("Inga and Esika", StringComparison.OrdinalIgnoreCase))
            {
                hasIngaAndEsika = true;
                break;
            }
        }

        List<string> assumptions = [];
        if (hasIngaAndEsika)
        {
            assumptions.Add("Inga and Esika detected: goldfish treats Inga-granted creature mana as usable only for creature spells while the commander is online.");
            assumptions.Add("Inga and Esika detected: goldfish draws a card from the commander only when a creature spell spends at least three modeled creature mana.");
            assumptions.Add("Inga and Esika assumption: native creature mana abilities, summoning sickness, tapping choices, and replacement effects are approximated rather than fully rules-modeled.");
        }

        return new CommanderSpecificSimulationRules
        {
            HasIngaAndEsika = hasIngaAndEsika,
            Assumptions = assumptions
        };
    }

    /// <summary>
    /// Checks whether a card is a command-zone card.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
                DeckRoles.Commander,
                StringComparison.OrdinalIgnoreCase)
            || DeckCategoryOrdering.HasCategory(card, DeckRoles.Commander);
    }
}
