namespace MtgMcp.Core;

/// <summary>
/// Names the built-in local card collection document.
/// </summary>
public static class CardCollectionIds
{
    /// <summary>
    /// Identifies the workstation-local collection used by ownership tools.
    /// </summary>
    public const string Default = "default";
}

/// <summary>
/// Persists the workstation-local card collection.
/// </summary>
public sealed class CardCollectionDocument
{
    /// <summary>
    /// Gets or sets the stable collection id.
    /// </summary>
    public string Id { get; set; } = CardCollectionIds.Default;

    /// <summary>
    /// Gets or sets the persisted schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets when the collection was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the owned card rows keyed by display name.
    /// </summary>
    public List<CardCollectionEntry> Cards { get; set; } = [];
}

/// <summary>
/// Represents one owned card quantity in the local collection.
/// </summary>
public sealed class CardCollectionEntry
{
    /// <summary>
    /// Gets or sets the card display name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the owned quantity.
    /// </summary>
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Summarizes the current local card collection.
/// </summary>
public sealed class CardCollectionSnapshot
{
    /// <summary>
    /// Gets or sets the collection id.
    /// </summary>
    public string CollectionId { get; set; } = CardCollectionIds.Default;

    /// <summary>
    /// Gets or sets when the collection was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the total owned quantity across all rows.
    /// </summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct card names in the collection.
    /// </summary>
    public int UniqueCards { get; set; }

    /// <summary>
    /// Gets or sets the sorted owned card rows.
    /// </summary>
    public List<CardCollectionEntry> Cards { get; set; } = [];
}

/// <summary>
/// Reports the result of replacing or merging collection entries.
/// </summary>
public sealed class CardCollectionSetResult
{
    /// <summary>
    /// Gets or sets whether the update replaced the collection or merged into it.
    /// </summary>
    public string Mode { get; set; } = "replace";

    /// <summary>
    /// Gets or sets the quantity supplied by structured entries, decklist text, or workspace import.
    /// </summary>
    public int InputQuantity { get; set; }

    /// <summary>
    /// Gets or sets warnings from decklist parsing.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets the saved collection snapshot.
    /// </summary>
    public CardCollectionSnapshot Collection { get; set; } = new();
}

/// <summary>
/// Compares a workspace's included cards against the local collection.
/// </summary>
public sealed class CollectionWorkspaceDiffResult
{
    /// <summary>
    /// Gets or sets the collection id used for the comparison.
    /// </summary>
    public string CollectionId { get; set; } = CardCollectionIds.Default;

    /// <summary>
    /// Gets or sets the compared workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the compared workspace name.
    /// </summary>
    public string WorkspaceName { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the collection covers every included workspace card.
    /// </summary>
    public bool FullyOwned { get; set; }

    /// <summary>
    /// Gets or sets the total included card quantity needed by the workspace.
    /// </summary>
    public int TotalNeededQuantity { get; set; }

    /// <summary>
    /// Gets or sets the needed quantity already present in the collection.
    /// </summary>
    public int TotalOwnedForWorkspaceQuantity { get; set; }

    /// <summary>
    /// Gets or sets the total included card quantity missing from the collection.
    /// </summary>
    public int TotalMissingQuantity { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct included workspace cards.
    /// </summary>
    public int UniqueNeededCards { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct included cards with missing copies.
    /// </summary>
    public int UniqueMissingCards { get; set; }

    /// <summary>
    /// Gets or sets the known replacement cost for missing copies with cached prices.
    /// </summary>
    public decimal KnownMissingUsd { get; set; }

    /// <summary>
    /// Gets or sets missing cards that lacked cached prices for ownership-cost estimation.
    /// </summary>
    public List<string> MissingPriceCards { get; set; } = [];

    /// <summary>
    /// Gets or sets all compared card rows, sorted with missing cards first.
    /// </summary>
    public List<CollectionWorkspaceDiffCard> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets only rows with one or more missing copies.
    /// </summary>
    public List<CollectionWorkspaceDiffCard> MissingCards { get; set; } = [];
}

/// <summary>
/// Describes ownership status for one workspace card.
/// </summary>
public sealed class CollectionWorkspaceDiffCard
{
    /// <summary>
    /// Gets or sets the card display name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace quantity required.
    /// </summary>
    public int NeededQuantity { get; set; }

    /// <summary>
    /// Gets or sets the total quantity owned in the collection.
    /// </summary>
    public int OwnedQuantity { get; set; }

    /// <summary>
    /// Gets or sets the quantity covered by the collection for this workspace.
    /// </summary>
    public int OwnedForWorkspaceQuantity { get; set; }

    /// <summary>
    /// Gets or sets the quantity missing from the collection.
    /// </summary>
    public int MissingQuantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price used for known missing-cost estimation.
    /// </summary>
    public decimal? UnitPriceUsd { get; set; }

    /// <summary>
    /// Gets or sets the known replacement cost for missing copies.
    /// </summary>
    public decimal? MissingUsd { get; set; }

    /// <summary>
    /// Gets or sets the source label for the selected price.
    /// </summary>
    public string? PriceSource { get; set; }
}
