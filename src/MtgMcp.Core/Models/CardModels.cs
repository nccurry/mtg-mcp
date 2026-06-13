namespace MtgMcp.Core;

/// <summary>
/// Provides card info behavior.
/// </summary>
public sealed class CardInfo
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the oracle id.
    /// </summary>
    public string? OracleId { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the mana cost.
    /// </summary>
    public string? ManaCost { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall layout value, such as normal, split, adventure, or modal_dfc.
    /// </summary>
    public string? Layout { get; set; }

    /// <summary>
    /// Gets or sets the mana value.
    /// </summary>
    public double? ManaValue { get; set; }

    /// <summary>
    /// Gets or sets the type line.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets the oracle text.
    /// </summary>
    public string? OracleText { get; set; }

    /// <summary>
    /// Gets or sets printed power when the card has creature stats.
    /// </summary>
    public string? Power { get; set; }

    /// <summary>
    /// Gets or sets printed toughness when the card has creature stats.
    /// </summary>
    public string? Toughness { get; set; }

    /// <summary>
    /// Gets or sets printed loyalty when the card has planeswalker stats.
    /// </summary>
    public string? Loyalty { get; set; }

    /// <summary>
    /// Gets or sets printed defense when the card has battle stats.
    /// </summary>
    public string? Defense { get; set; }

    /// <summary>
    /// Gets or sets the set.
    /// </summary>
    public string? Set { get; set; }

    /// <summary>
    /// Gets or sets the collector number.
    /// </summary>
    public string? CollectorNumber { get; set; }

    /// <summary>
    /// Gets or sets the rarity.
    /// </summary>
    public string? Rarity { get; set; }

    /// <summary>
    /// Gets or sets the printing language code when the provider exposes it.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the scryfall uri.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets why this printing was selected for cached pricing.
    /// </summary>
    public string? SelectedPrintingReason { get; set; }

    /// <summary>
    /// Gets or sets the pricing mode that selected this printing, when known.
    /// </summary>
    public string? PricingMode { get; set; }

    /// <summary>
    /// Gets or sets the edhrec rank.
    /// </summary>
    public int? EdhrecRank { get; set; }

    /// <summary>
    /// Gets or sets the colors.
    /// </summary>
    public List<string> Colors { get; set; } = [];

    /// <summary>
    /// Gets or sets the color identity.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>
    /// Gets or sets the produced mana.
    /// </summary>
    public List<string> ProducedMana { get; set; } = [];

    /// <summary>
    /// Gets or sets the product channels this printing appears in, such as paper or arena.
    /// </summary>
    public List<string> Games { get; set; } = [];

    /// <summary>
    /// Gets or sets available finish names for this printing, such as nonfoil, foil, or etched.
    /// </summary>
    public List<string> Finishes { get; set; } = [];

    /// <summary>
    /// Gets or sets structured face data for split, adventure, modal, and transforming cards.
    /// </summary>
    public List<CardFaceSnapshot> Faces { get; set; } = [];

    /// <summary>
    /// Gets or sets the legalities.
    /// </summary>
    public Dictionary<string, string> Legalities { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the prices.
    /// </summary>
    public Dictionary<string, string> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the image uris.
    /// </summary>
    public Dictionary<string, string> ImageUris { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Provides card search result behavior.
/// </summary>
public sealed class CardSearchResult
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the mana cost.
    /// </summary>
    public string? ManaCost { get; set; }

    /// <summary>
    /// Gets or sets the type line.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets the set.
    /// </summary>
    public string? Set { get; set; }

    /// <summary>
    /// Gets or sets the collector number.
    /// </summary>
    public string? CollectorNumber { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the scryfall uri.
    /// </summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>
/// Provides ruling info behavior.
/// </summary>
public sealed class RulingInfo
{
    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the published at.
    /// </summary>
    public DateOnly PublishedAt { get; set; }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";
}

/// <summary>
/// Provides card snapshot behavior.
/// </summary>
public sealed class CardSnapshot
{
    /// <summary>
    /// Gets or sets the mana cost.
    /// </summary>
    public string? ManaCost { get; set; }

    /// <summary>
    /// Gets or sets the card layout value from the source provider.
    /// </summary>
    public string? Layout { get; set; }

    /// <summary>
    /// Gets or sets the type line.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets the mana value.
    /// </summary>
    public double? ManaValue { get; set; }

    /// <summary>
    /// Gets or sets the oracle text.
    /// </summary>
    public string? OracleText { get; set; }

    /// <summary>
    /// Gets or sets printed power when known.
    /// </summary>
    public string? Power { get; set; }

    /// <summary>
    /// Gets or sets printed toughness when known.
    /// </summary>
    public string? Toughness { get; set; }

    /// <summary>
    /// Gets or sets printed loyalty when known.
    /// </summary>
    public string? Loyalty { get; set; }

    /// <summary>
    /// Gets or sets printed defense when known.
    /// </summary>
    public string? Defense { get; set; }

    /// <summary>
    /// Gets or sets the color identity.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the set.
    /// </summary>
    public string? Set { get; set; }

    /// <summary>
    /// Gets or sets the collector number.
    /// </summary>
    public string? CollectorNumber { get; set; }

    /// <summary>
    /// Gets or sets the rarity.
    /// </summary>
    public string? Rarity { get; set; }

    /// <summary>
    /// Gets or sets the printing language code when the provider exposes it.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the scryfall uri.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets why this printing was selected for cached pricing.
    /// </summary>
    public string? SelectedPrintingReason { get; set; }

    /// <summary>
    /// Gets or sets the pricing mode that selected this printing, when known.
    /// </summary>
    public string? PricingMode { get; set; }

    /// <summary>
    /// Gets or sets freshness and provider provenance for the snapshot.
    /// </summary>
    public CardSnapshotProvenance Provenance { get; set; } = new();

    /// <summary>
    /// Gets or sets the edhrec rank.
    /// </summary>
    public int? EdhrecRank { get; set; }

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>
    /// Gets or sets the produced mana.
    /// </summary>
    public List<string> ProducedMana { get; set; } = [];

    /// <summary>
    /// Gets or sets the product channels this printing appears in, such as paper or arena.
    /// </summary>
    public List<string> Games { get; set; } = [];

    /// <summary>
    /// Gets or sets available finish names for this printing, such as nonfoil, foil, or etched.
    /// </summary>
    public List<string> Finishes { get; set; } = [];

    /// <summary>
    /// Gets or sets structured face data for multi-face cards.
    /// </summary>
    public List<CardFaceSnapshot> Faces { get; set; } = [];

    /// <summary>
    /// Gets or sets the legalities.
    /// </summary>
    public Dictionary<string, string> Legalities { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the prices.
    /// </summary>
    public Dictionary<string, string> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the image uris.
    /// </summary>
    public Dictionary<string, string> ImageUris { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Captures card-face facts used by analysis and conservative simulation.
/// </summary>
public sealed class CardFaceSnapshot
{
    /// <summary>
    /// Gets or sets the face name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the face mana cost.
    /// </summary>
    public string? ManaCost { get; set; }

    /// <summary>
    /// Gets or sets the face type line.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets the face oracle text.
    /// </summary>
    public string? OracleText { get; set; }

    /// <summary>
    /// Gets or sets face power when known.
    /// </summary>
    public string? Power { get; set; }

    /// <summary>
    /// Gets or sets face toughness when known.
    /// </summary>
    public string? Toughness { get; set; }

    /// <summary>
    /// Gets or sets face loyalty when known.
    /// </summary>
    public string? Loyalty { get; set; }

    /// <summary>
    /// Gets or sets face defense when known.
    /// </summary>
    public string? Defense { get; set; }

    /// <summary>
    /// Gets or sets face colors.
    /// </summary>
    public List<string> Colors { get; set; } = [];
}

/// <summary>
/// Records where a cached card snapshot came from and when it was refreshed.
/// </summary>
public sealed class CardSnapshotProvenance
{
    /// <summary>
    /// Gets or sets the provider that supplied the snapshot.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the provider's card id for this snapshot, when known.
    /// </summary>
    public string? ProviderCardId { get; set; }

    /// <summary>
    /// Gets or sets the schema version used by mtg-mcp snapshot mapping.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets when the snapshot was refreshed.
    /// </summary>
    public DateTimeOffset? RefreshedAtUtc { get; set; }
}

/// <summary>
/// Describes whether a card printing has a usable budget price.
/// </summary>
public sealed class CardPriceEvaluation
{
    /// <summary>
    /// Gets or sets the selected price when one is known for a released printing.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets whether the selected price is safe to use for budget math.
    /// </summary>
    public bool PriceKnown { get; set; }

    /// <summary>
    /// Gets or sets the price field used for the selected price.
    /// </summary>
    public string? PriceSource { get; set; }

    /// <summary>
    /// Gets or sets release and pricing status for the inspected printing.
    /// </summary>
    public string PrintingStatus { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the deterministic reason this price was or was not selected.
    /// </summary>
    public string SelectedPrintingReason { get; set; } = "";
}
