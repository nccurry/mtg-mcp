namespace MtgMcp.Core;

/// <summary>
/// Shares card and category mutation helpers across workspace, plan, and recommendation services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Copies catalog card facts into a workspace snapshot.
    /// </summary>
    protected static void ApplyCardSnapshot(DeckCard card, CardInfo cardInfo)
    {
        card.Snapshot = new CardSnapshot
        {
            ManaCost = cardInfo.ManaCost,
            TypeLine = cardInfo.TypeLine,
            ManaValue = cardInfo.ManaValue,
            OracleText = cardInfo.OracleText,
            ColorIdentity = cardInfo.ColorIdentity.ToList(),
            Set = cardInfo.Set,
            CollectorNumber = cardInfo.CollectorNumber,
            Rarity = cardInfo.Rarity,
            ReleasedAt = cardInfo.ReleasedAt,
            ScryfallUri = cardInfo.ScryfallUri,
            EdhrecRank = cardInfo.EdhrecRank,
            Keywords = cardInfo.Keywords.ToList(),
            ProducedMana = cardInfo.ProducedMana.ToList(),
            Legalities = new Dictionary<string, string>(cardInfo.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(cardInfo.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(cardInfo.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Finds a card by name and optional primary category, falling back to secondary tags for legacy callers.
    /// </summary>
    protected static DeckCard? FindCard(DeckWorkspace workspace, string cardName, string? category)
    {
        DeckCard? secondaryMatch = null;
        string? normalizedCategory = category is null ? null : NormalizeCategoryName(category);
        foreach (DeckCard card in workspace.Cards)
        {
            if (!card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normalizedCategory is null)
            {
                return card;
            }

            if (DeckCategoryOrdering.PrimaryCategory(card).Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }

            if (secondaryMatch is null && DeckCategoryOrdering.HasCategory(card, normalizedCategory))
            {
                secondaryMatch = card;
            }
        }

        return secondaryMatch;
    }

    /// <summary>
    /// Ensures a category row exists in the workspace and returns it.
    /// </summary>
    protected static DeckCategory EnsureCategory(DeckWorkspace workspace, string category)
    {
        DeckCategory? existing = workspace.Categories.FirstOrDefault(value =>
            value.Name.Equals(category, StringComparison.OrdinalIgnoreCase)
        );
        if (existing is not null)
        {
            return existing;
        }

        DeckCategory created = new()
        {
            Name = category,
            IncludedInDeck = !DeckDefaults.IsDefaultExcludedCategory(category),
            IncludedInPrice = true,
        };

        workspace.Categories.Add(created);
        return created;
    }

    /// <summary>
    /// Adds a category name to a card without duplicating existing values.
    /// </summary>
    protected static void AddCategoryName(DeckCard card, string category)
    {
        DeckCategoryOrdering.AddSecondary(card, category);
    }

    /// <summary>
    /// Normalizes empty category input to the mainboard.
    /// </summary>
    protected static string NormalizeCategoryName(string category)
    {
        return DeckCategoryOrdering.NormalizeCategoryName(category);
    }

    /// <summary>
    /// Checks whether a card is categorized as the commander.
    /// </summary>
    protected static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
            DeckRoles.Commander,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a cached USD price from a card snapshot.
    /// </summary>
    protected static decimal? ReadUsdPrice(CardSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return TryReadDecimal(snapshot.Prices, "usd")
            ?? TryReadDecimal(snapshot.Prices, "usd_etched")
            ?? TryReadDecimal(snapshot.Prices, "usd_foil");
    }

    /// <summary>
    /// Reads a USD price from catalog card details.
    /// </summary>
    protected static decimal? ReadUsdPrice(CardInfo card)
    {
        return TryReadDecimal(card.Prices, "usd")
            ?? TryReadDecimal(card.Prices, "usd_etched")
            ?? TryReadDecimal(card.Prices, "usd_foil");
    }

    /// <summary>
    /// Parses a decimal value from a case-insensitive dictionary key.
    /// </summary>
    protected static decimal? TryReadDecimal(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            && decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal result)
                ? result
                : null;
    }
}
