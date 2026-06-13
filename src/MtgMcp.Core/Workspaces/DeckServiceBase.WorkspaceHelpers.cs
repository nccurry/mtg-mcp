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
            Layout = cardInfo.Layout,
            TypeLine = cardInfo.TypeLine,
            ManaValue = cardInfo.ManaValue,
            OracleText = cardInfo.OracleText,
            Power = cardInfo.Power,
            Toughness = cardInfo.Toughness,
            Loyalty = cardInfo.Loyalty,
            Defense = cardInfo.Defense,
            ColorIdentity = cardInfo.ColorIdentity.ToList(),
            Set = cardInfo.Set,
            CollectorNumber = cardInfo.CollectorNumber,
            Rarity = cardInfo.Rarity,
            Language = cardInfo.Language,
            ReleasedAt = cardInfo.ReleasedAt,
            ScryfallUri = cardInfo.ScryfallUri,
            SelectedPrintingReason = cardInfo.SelectedPrintingReason,
            PricingMode = cardInfo.PricingMode,
            Provenance = new CardSnapshotProvenance
            {
                Provider = "scryfall",
                ProviderCardId = cardInfo.Id,
                SchemaVersion = 1,
                RefreshedAtUtc = DateTimeOffset.UtcNow,
            },
            EdhrecRank = cardInfo.EdhrecRank,
            Keywords = cardInfo.Keywords.ToList(),
            ProducedMana = cardInfo.ProducedMana.ToList(),
            Games = cardInfo.Games.ToList(),
            Finishes = cardInfo.Finishes.ToList(),
            Faces = cardInfo.Faces.Select(CloneFace).ToList(),
            Legalities = new Dictionary<string, string>(cardInfo.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(cardInfo.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(cardInfo.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Copies one face snapshot without sharing mutable color lists.
    /// </summary>
    protected static CardFaceSnapshot CloneFace(CardFaceSnapshot face)
    {
        return new CardFaceSnapshot
        {
            Name = face.Name,
            ManaCost = face.ManaCost,
            TypeLine = face.TypeLine,
            OracleText = face.OracleText,
            Power = face.Power,
            Toughness = face.Toughness,
            Loyalty = face.Loyalty,
            Defense = face.Defense,
            Colors = face.Colors.ToList(),
        };
    }

    /// <summary>
    /// Copies cached snapshot facts without sharing mutable collections.
    /// </summary>
    protected static CardSnapshot CopyCardSnapshot(CardSnapshot snapshot)
    {
        return new CardSnapshot
        {
            ManaCost = snapshot.ManaCost,
            Layout = snapshot.Layout,
            TypeLine = snapshot.TypeLine,
            ManaValue = snapshot.ManaValue,
            OracleText = snapshot.OracleText,
            Power = snapshot.Power,
            Toughness = snapshot.Toughness,
            Loyalty = snapshot.Loyalty,
            Defense = snapshot.Defense,
            ColorIdentity = snapshot.ColorIdentity.ToList(),
            Set = snapshot.Set,
            CollectorNumber = snapshot.CollectorNumber,
            Rarity = snapshot.Rarity,
            Language = snapshot.Language,
            ReleasedAt = snapshot.ReleasedAt,
            ScryfallUri = snapshot.ScryfallUri,
            SelectedPrintingReason = snapshot.SelectedPrintingReason,
            PricingMode = snapshot.PricingMode,
            Provenance = new CardSnapshotProvenance
            {
                Provider = snapshot.Provenance.Provider,
                ProviderCardId = snapshot.Provenance.ProviderCardId,
                SchemaVersion = snapshot.Provenance.SchemaVersion,
                RefreshedAtUtc = snapshot.Provenance.RefreshedAtUtc,
            },
            EdhrecRank = snapshot.EdhrecRank,
            Keywords = snapshot.Keywords.ToList(),
            ProducedMana = snapshot.ProducedMana.ToList(),
            Games = snapshot.Games.ToList(),
            Finishes = snapshot.Finishes.ToList(),
            Faces = snapshot.Faces.Select(CloneFace).ToList(),
            Legalities = new Dictionary<string, string>(snapshot.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(snapshot.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(snapshot.ImageUris, StringComparer.OrdinalIgnoreCase),
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
            : CardPriceEvaluator.Evaluate(snapshot, CurrentUtcDate());
    }

    /// <summary>
    /// Evaluates whether catalog card details have a usable released-printing price.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardInfo card)
    {
        return CardPriceEvaluator.Evaluate(card, CurrentUtcDate());
    }

    /// <summary>
    /// Evaluates whether a cached snapshot has a usable price against a deterministic reference date.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardSnapshot? snapshot, DateOnly referenceDate)
    {
        return snapshot is null
            ? MissingPrice("missing-snapshot", "No cached card snapshot was available.")
            : CardPriceEvaluator.Evaluate(snapshot, referenceDate);
    }

    /// <summary>
    /// Evaluates whether catalog card details have a usable price against a deterministic reference date.
    /// </summary>
    protected static CardPriceEvaluation EvaluateUsdPrice(CardInfo card, DateOnly referenceDate)
    {
        return CardPriceEvaluator.Evaluate(card, referenceDate);
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
