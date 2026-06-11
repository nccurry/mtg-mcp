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
            if (DeckDefaults.IsCommanderCategory(existing.Name))
            {
                existing.IsPremier = true;
            }

            return existing;
        }

        DeckCategory created = new()
        {
            Name = category,
            IncludedInDeck = !DeckDefaults.IsDefaultExcludedCategory(category),
            IncludedInPrice = true,
            IsPremier = DeckDefaults.IsCommanderCategory(category),
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
        return EvaluateUsdPrice(snapshot).Price;
    }

    /// <summary>
    /// Reads a USD price from catalog card details.
    /// </summary>
    protected static decimal? ReadUsdPrice(CardInfo card)
    {
        return EvaluateUsdPrice(card).Price;
    }

    /// <summary>
    /// Evaluates whether a cached snapshot has a usable released-printing price.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardSnapshot? snapshot)
    {
        return snapshot is null
            ? MissingPrice("missing-snapshot", "No cached card snapshot was available.")
            : EvaluateUsdPrice(snapshot.ReleasedAt, snapshot.Prices, CurrentUtcDate());
    }

    /// <summary>
    /// Evaluates whether catalog card details have a usable released-printing price.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardInfo card)
    {
        return EvaluateUsdPrice(card.ReleasedAt, card.Prices, CurrentUtcDate());
    }

    /// <summary>
    /// Evaluates whether a cached snapshot has a usable price against a deterministic reference date.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardSnapshot? snapshot, DateOnly referenceDate)
    {
        return snapshot is null
            ? MissingPrice("missing-snapshot", "No cached card snapshot was available.")
            : EvaluateUsdPrice(snapshot.ReleasedAt, snapshot.Prices, referenceDate);
    }

    /// <summary>
    /// Evaluates whether catalog card details have a usable price against a deterministic reference date.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardInfo card, DateOnly referenceDate)
    {
        return EvaluateUsdPrice(card.ReleasedAt, card.Prices, referenceDate);
    }

    /// <summary>
    /// Evaluates price status from release date and provider price fields.
    /// </summary>
    private static CardPriceEvaluation EvaluateUsdPrice(
        DateOnly? releasedAt,
        IReadOnlyDictionary<string, string> prices,
        DateOnly referenceDate)
    {
        if (releasedAt.HasValue && releasedAt.Value > referenceDate)
        {
            return MissingPrice(
                "future",
                $"Printing releases on {releasedAt.Value:yyyy-MM-dd}, after reference date {referenceDate:yyyy-MM-dd}.");
        }

        foreach (string key in new[] { "usd", "usd_etched", "usd_foil", "tcgplayer", "tcgplayer_price" })
        {
            decimal? price = TryReadDecimal(prices, key);
            if (price.HasValue)
            {
                return new CardPriceEvaluation
                {
                    Price = price.Value,
                    PriceKnown = true,
                    PriceSource = key,
                    PrintingStatus = releasedAt.HasValue ? "released" : "unknown-release-date",
                    SelectedPrintingReason = releasedAt.HasValue
                        ? $"Selected {key} price for released printing {releasedAt.Value:yyyy-MM-dd}."
                        : $"Selected {key} price; release date was unavailable."
                };
            }
        }

        return MissingPrice(
            releasedAt.HasValue ? "unpriced" : "unknown-release-date-unpriced",
            releasedAt.HasValue
                ? $"Released printing {releasedAt.Value:yyyy-MM-dd} did not include a usable USD or TCG price."
                : "Release date and usable USD or TCG price were unavailable.");
    }

    /// <summary>
    /// Builds an unknown price evaluation with a deterministic reason.
    /// </summary>
    private static CardPriceEvaluation MissingPrice(string printingStatus, string reason)
    {
        return new CardPriceEvaluation
        {
            PriceKnown = false,
            PrintingStatus = printingStatus,
            SelectedPrintingReason = reason
        };
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

    /// <summary>
    /// Reads the current UTC date for static legacy price callers.
    /// </summary>
    private static DateOnly CurrentUtcDate()
    {
        return DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }
}
