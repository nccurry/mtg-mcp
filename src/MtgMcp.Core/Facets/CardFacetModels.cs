namespace MtgMcp.Core;

/// <summary>
/// Provides stable facet names for card data exposed by mtg-mcp.
/// </summary>
public static class CardFacetNames
{
    /// <summary>
    /// Identifies the local card name facet.
    /// </summary>
    public const string CardName = "card.name";

    /// <summary>
    /// Identifies the card quantity in the workspace.
    /// </summary>
    public const string CardQuantity = "card.quantity";

    /// <summary>
    /// Identifies whether the card's primary category contributes to the active deck.
    /// </summary>
    public const string CardIncludedInDeck = "card.included_in_deck";

    /// <summary>
    /// Identifies the card's ordered primary category.
    /// </summary>
    public const string WorkspacePrimaryCategory = "workspace.primary_category";

    /// <summary>
    /// Identifies all workspace or Archidekt category labels attached to the card.
    /// </summary>
    public const string WorkspaceCategories = "workspace.categories";

    /// <summary>
    /// Identifies local user-defined tags stored on the workspace card.
    /// </summary>
    public const string UserTags = "user.tags";

    /// <summary>
    /// Identifies local user-defined category labels stored on the workspace card.
    /// </summary>
    public const string UserCategories = "user.categories";

    /// <summary>
    /// Identifies Scryfall Tagger oracle tags stored as local annotations.
    /// </summary>
    public const string TaggerOracleTags = "tagger.oracle_tags";

    /// <summary>
    /// Identifies Scryfall Tagger art tags stored as local annotations.
    /// </summary>
    public const string TaggerArtTags = "tagger.art_tags";
}

/// <summary>
/// Names the source family behind a facet value.
/// </summary>
public static class CardFacetSourceNames
{
    /// <summary>
    /// Indicates values copied from Scryfall card objects.
    /// </summary>
    public const string Scryfall = "scryfall";

    /// <summary>
    /// Indicates workspace or Archidekt-backed category values.
    /// </summary>
    public const string Workspace = "workspace";

    /// <summary>
    /// Indicates local user annotations.
    /// </summary>
    public const string User = "user";

    /// <summary>
    /// Indicates locally stored Scryfall Tagger annotations.
    /// </summary>
    public const string Tagger = "scryfall-tagger";

    /// <summary>
    /// Indicates metadata persisted on the workspace card.
    /// </summary>
    public const string Metadata = "metadata";
}

/// <summary>
/// Captures one normalized card facet and the concrete values behind it.
/// </summary>
public sealed class CardFacet
{
    /// <summary>
    /// Gets or sets the stable facet name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the source family for the facet.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized values for the facet.
    /// </summary>
    public List<string> Values { get; set; } = [];
}

/// <summary>
/// Provides normalized facets for one card in a workspace.
/// </summary>
public sealed class CardFacetSnapshot
{
    /// <summary>
    /// Gets or sets the workspace id that supplied the card.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the card id within the workspace.
    /// </summary>
    public string CardId { get; set; } = "";

    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card quantity in the workspace.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets whether the card contributes to active deck counts.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card id when known.
    /// </summary>
    public string? ScryfallId { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall Oracle id when known.
    /// </summary>
    public string? ScryfallOracleId { get; set; }

    /// <summary>
    /// Gets or sets normalized facets by name.
    /// </summary>
    public Dictionary<string, CardFacet> Facets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Provides facet snapshots for cards in a workspace.
/// </summary>
public sealed class DeckFacetSnapshot
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace format.
    /// </summary>
    public string Format { get; set; } = "";

    /// <summary>
    /// Gets or sets whether only included deck cards were returned.
    /// </summary>
    public bool IncludedOnly { get; set; }

    /// <summary>
    /// Gets or sets card facet snapshots.
    /// </summary>
    public List<CardFacetSnapshot> Cards { get; set; } = [];
}

/// <summary>
/// Describes a concrete predicate check against one facet.
/// </summary>
public sealed class FacetMatchEvidence
{
    /// <summary>
    /// Gets or sets the facet name.
    /// </summary>
    public string Facet { get; set; } = "";

    /// <summary>
    /// Gets or sets the source family for the matched facet.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the operation applied to the facet.
    /// </summary>
    public string Operation { get; set; } = "";

    /// <summary>
    /// Gets or sets the expected value or values.
    /// </summary>
    public List<string> Expected { get; set; } = [];

    /// <summary>
    /// Gets or sets the actual facet values inspected.
    /// </summary>
    public List<string> Actual { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the predicate matched.
    /// </summary>
    public bool Matched { get; set; }
}

/// <summary>
/// Reports whether one card matched a caller-supplied facet predicate.
/// </summary>
public sealed class CardFacetMatchResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the card matched the predicate.
    /// </summary>
    public bool Matched { get; set; }

    /// <summary>
    /// Gets or sets the normalized predicate JSON.
    /// </summary>
    public string PredicateJson { get; set; } = "";

    /// <summary>
    /// Gets or sets concrete predicate evidence rows.
    /// </summary>
    public List<FacetMatchEvidence> Evidence { get; set; } = [];
}

/// <summary>
/// Captures one card that matched a deck-level facet predicate.
/// </summary>
public sealed class DeckFacetCountCard
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card quantity counted.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets whether the card contributes to active deck counts.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets evidence rows that explain the match.
    /// </summary>
    public List<FacetMatchEvidence> Evidence { get; set; } = [];
}

/// <summary>
/// Reports deck cards matching a caller-supplied facet predicate.
/// </summary>
public sealed class DeckFacetCountResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether only included deck cards were counted.
    /// </summary>
    public bool IncludedOnly { get; set; }

    /// <summary>
    /// Gets or sets the total counted quantity of matching cards.
    /// </summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct matching card rows.
    /// </summary>
    public int DistinctCards { get; set; }

    /// <summary>
    /// Gets or sets the normalized predicate JSON.
    /// </summary>
    public string PredicateJson { get; set; } = "";

    /// <summary>
    /// Gets or sets matching card rows.
    /// </summary>
    public List<DeckFacetCountCard> Matches { get; set; } = [];
}

/// <summary>
/// Reports the result of saving local facet annotations for one card.
/// </summary>
public sealed class CardFacetAnnotationResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the updated card facets.
    /// </summary>
    public CardFacetSnapshot Facets { get; set; } = new();

    /// <summary>
    /// Gets or sets notes about the annotation write.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
