namespace MtgMcp.Core;

/// <summary>
/// Describes the active command-zone cards without adapter-specific source semantics.
/// </summary>
public sealed class CommandZoneContext
{
    /// <summary>
    /// Gets cards in the active command zone, preserving workspace order.
    /// </summary>
    public List<DeckCard> CommandZoneCards { get; set; } = [];

    /// <summary>
    /// Gets non-Background commander names in workspace order.
    /// </summary>
    public List<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets Background names in workspace order.
    /// </summary>
    public List<string> BackgroundNames { get; set; } = [];

    /// <summary>
    /// Gets the first non-Background commander name for legacy single-commander callers.
    /// </summary>
    public string? PrimaryCommanderName { get; set; }

    /// <summary>
    /// Gets the command-zone display name suitable for provider-neutral deck context.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets whether the command zone has multiple non-Background commanders.
    /// </summary>
    public bool HasPartnerPair { get; set; }

    /// <summary>
    /// Gets whether the command zone has a Background and a commander that chooses a Background.
    /// </summary>
    public bool HasBackgroundPair { get; set; }

    /// <summary>
    /// Builds command-zone facts from active workspace cards.
    /// </summary>
    public static CommandZoneContext FromWorkspace(DeckWorkspace workspace)
    {
        List<DeckCard> commandZoneCards = [];
        List<string> commanderNames = [];
        List<string> backgroundNames = [];
        bool hasChooseBackgroundCommander = false;

        foreach (DeckCard card in DeckCategoryInclusion.IncludedCards(workspace))
        {
            if (!IsCommandZoneCard(card))
            {
                continue;
            }

            commandZoneCards.Add(card);
            if (IsBackground(card))
            {
                AddDistinct(backgroundNames, card.Name);
                continue;
            }

            AddDistinct(commanderNames, card.Name);
            if (ChoosesBackground(card))
            {
                hasChooseBackgroundCommander = true;
            }
        }

        bool hasBackgroundPair = backgroundNames.Count > 0
            && commanderNames.Count > 0
            && hasChooseBackgroundCommander;
        return new CommandZoneContext
        {
            CommandZoneCards = commandZoneCards,
            CommanderNames = commanderNames,
            BackgroundNames = backgroundNames,
            PrimaryCommanderName = commanderNames.FirstOrDefault(),
            DisplayName = BuildDisplayName(commanderNames, backgroundNames, hasBackgroundPair),
            HasPartnerPair = commanderNames.Count > 1 && !hasBackgroundPair,
            HasBackgroundPair = hasBackgroundPair,
        };
    }

    /// <summary>
    /// Checks whether a card's primary category places it in the command zone.
    /// </summary>
    private static bool IsCommandZoneCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
            DeckRoles.Commander,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a command-zone card is a Background.
    /// </summary>
    private static bool IsBackground(DeckCard card)
    {
        return (card.Snapshot?.TypeLine ?? "").Contains(
            "Background",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a command-zone commander can pair with a Background.
    /// </summary>
    private static bool ChoosesBackground(DeckCard card)
    {
        return (card.Snapshot?.OracleText ?? "").Contains(
            "Choose a Background",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a name once while preserving source order.
    /// </summary>
    private static void AddDistinct(List<string> names, string name)
    {
        if (names.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        names.Add(name);
    }

    /// <summary>
    /// Builds a provider-neutral command-zone display string.
    /// </summary>
    private static string? BuildDisplayName(
        IReadOnlyList<string> commanderNames,
        IReadOnlyList<string> backgroundNames,
        bool hasBackgroundPair)
    {
        List<string> displayNames = commanderNames.ToList();
        if (hasBackgroundPair)
        {
            displayNames.AddRange(backgroundNames);
        }

        return displayNames.Count == 0 ? null : string.Join(" // ", displayNames);
    }
}
