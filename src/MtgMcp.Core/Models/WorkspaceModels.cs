namespace MtgMcp.Core;

/// <summary>
/// Lists supported workspace mode values.
/// </summary>
public enum WorkspaceMode
{
    /// <summary>
    /// Represents the local value.
    /// </summary>
    Local,

    /// <summary>
    /// Represents the archidekt value.
    /// </summary>
    Archidekt,
}

/// <summary>
/// Lists supported deck mutation kind values.
/// </summary>
public enum DeckMutationKind
{
    /// <summary>
    /// Represents the card added value.
    /// </summary>
    CardAdded,

    /// <summary>
    /// Represents the card removed value.
    /// </summary>
    CardRemoved,

    /// <summary>
    /// Represents the quantity changed value.
    /// </summary>
    QuantityChanged,

    /// <summary>
    /// Represents the card moved value.
    /// </summary>
    CardMoved,

    /// <summary>
    /// Represents the category changed value.
    /// </summary>
    CategoryChanged,

    /// <summary>
    /// Represents the metadata changed value.
    /// </summary>
    MetadataChanged,
}

/// <summary>
/// Provides deck category behavior.
/// </summary>
public sealed class DeckCategory
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the included in deck.
    /// </summary>
    public bool IncludedInDeck { get; set; } = true;

    /// <summary>
    /// Gets or sets the included in price.
    /// </summary>
    public bool IncludedInPrice { get; set; } = true;

    /// <summary>
    /// Gets or sets whether Archidekt treats this category as the deck's commander zone.
    /// </summary>
    public bool IsPremier { get; set; }

    /// <summary>
    /// Gets or sets the archidekt category id.
    /// </summary>
    public int? ArchidektCategoryId { get; set; }
}

/// <summary>
/// Provides deck card behavior.
/// </summary>
public sealed class DeckCard
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the primary category.
    /// </summary>
    public string PrimaryCategory { get; set; } = DeckDefaults.Mainboard;

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    public List<string> Categories { get; set; } = [DeckDefaults.Mainboard];

    /// <summary>
    /// Gets or sets the scryfall id.
    /// </summary>
    public string? ScryfallId { get; set; }

    /// <summary>
    /// Gets or sets the scryfall oracle id.
    /// </summary>
    public string? ScryfallOracleId { get; set; }

    /// <summary>
    /// Gets or sets the archidekt card id.
    /// </summary>
    public string? ArchidektCardId { get; set; }

    /// <summary>
    /// Gets or sets the archidekt deck relation id.
    /// </summary>
    public long? ArchidektDeckRelationId { get; set; }

    /// <summary>
    /// Gets or sets the modifier.
    /// </summary>
    public string? Modifier { get; set; }

    /// <summary>
    /// Gets or sets the companion.
    /// </summary>
    public bool Companion { get; set; }

    /// <summary>
    /// Gets or sets the flipped default.
    /// </summary>
    public bool FlippedDefault { get; set; }

    /// <summary>
    /// Gets or sets the snapshot.
    /// </summary>
    public CardSnapshot Snapshot { get; set; } = new();

    /// <summary>
    /// Gets or sets the metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Provides deck workspace behavior.
/// </summary>
public sealed class DeckWorkspace
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "Untitled Deck";

    /// <summary>
    /// Gets or sets the format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the mode.
    /// </summary>
    public WorkspaceMode Mode { get; set; } = WorkspaceMode.Local;

    /// <summary>
    /// Gets or sets the write back.
    /// </summary>
    public bool WriteBack { get; set; }

    /// <summary>
    /// Gets or sets the archidekt deck id.
    /// </summary>
    public string? ArchidektDeckId { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt deck format id.
    /// </summary>
    public int? ArchidektDeckFormatId { get; set; }

    /// <summary>
    /// Gets or sets the created at.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the updated at.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    public List<DeckCategory> Categories { get; set; } = DeckDefaults.CreateDefaultCategories();

    /// <summary>
    /// Gets or sets the cards.
    /// </summary>
    public List<DeckCard> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal import or migration warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets external deck sources that contributed to this local workspace.
    /// </summary>
    public List<DeckSourceReference> SourceReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets bounded snapshots captured before explicit provider re-imports.
    /// </summary>
    public List<DeckImportHistoryEntry> ImportHistory { get; set; } = [];

    /// <summary>
    /// Gets or sets local snapshots that can restore non-writeback workspaces.
    /// </summary>
    public List<WorkspaceCheckpoint> LocalCheckpoints { get; set; } = [];
}

/// <summary>
/// Identifies an external deck source imported into a provider-neutral workspace.
/// </summary>
public sealed class DeckSourceReference
{
    /// <summary>
    /// Gets or sets the source provider key, such as moxfield or archidekt.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Gets or sets the provider's deck id or public id.
    /// </summary>
    public string ExternalId { get; set; } = "";

    /// <summary>
    /// Gets or sets the source URL when one is known.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets when the source was imported into mtg-mcp.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Stores the previous local state for a provider import into the same workspace id.
/// </summary>
public sealed class DeckImportHistoryEntry
{
    /// <summary>
    /// Gets or sets the source provider key.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Gets or sets the provider deck id.
    /// </summary>
    public string ExternalId { get; set; } = "";

    /// <summary>
    /// Gets or sets the local workspace id whose import produced this history entry.
    /// </summary>
    public string LocalWorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets when the import occurred.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the local workspace snapshot from immediately before the import.
    /// </summary>
    public DeckWorkspace? BaselineWorkspace { get; set; }
}

/// <summary>
/// Stores a local workspace snapshot for manual restore workflows.
/// </summary>
public sealed class WorkspaceCheckpoint
{
    /// <summary>
    /// Gets or sets the checkpoint id used by restore and delete tools.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the workspace id that owns the checkpoint.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the checkpoint display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets optional human context for why the checkpoint was captured.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets when the checkpoint was captured.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the non-recursive workspace snapshot.
    /// </summary>
    public DeckWorkspace Snapshot { get; set; } = new();
}

/// <summary>
/// Summarizes a local checkpoint without returning the saved workspace snapshot.
/// </summary>
public sealed class WorkspaceCheckpointSummary
{
    /// <summary>
    /// Gets or sets the checkpoint id used by restore and delete tools.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id that owns the checkpoint.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the checkpoint display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets optional human context for why the checkpoint was captured.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets when the checkpoint was captured.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Reports the result of restoring a local workspace checkpoint.
/// </summary>
public sealed class WorkspaceCheckpointRestoreResult
{
    /// <summary>
    /// Gets or sets the workspace id restored from the checkpoint.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the restored checkpoint id.
    /// </summary>
    public string CheckpointId { get; set; } = "";

    /// <summary>
    /// Gets or sets a concise restore status.
    /// </summary>
    public string Status { get; set; } = "restored";

    /// <summary>
    /// Gets or sets the restored workspace after persistence.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();
}

/// <summary>
/// Provides compact workspace list data without card payloads or cached card snapshots.
/// </summary>
public sealed class DeckWorkspaceSummary
{
    /// <summary>
    /// Gets or sets the workspace id to use with follow-up tools.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the workspace source mode.
    /// </summary>
    public WorkspaceMode Mode { get; set; }

    /// <summary>
    /// Gets or sets when the workspace was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets where future mutations will be persisted.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets total card quantity across all workspace categories.
    /// </summary>
    public int TotalCards { get; set; }

    /// <summary>
    /// Gets or sets card quantity included in the active deck.
    /// </summary>
    public int IncludedCards { get; set; }

    /// <summary>
    /// Gets or sets card quantity in excluded maybeboard-style categories.
    /// </summary>
    public int MaybeboardCards { get; set; }

    /// <summary>
    /// Gets or sets commander card names found in included commander categories.
    /// </summary>
    public List<string> Commanders { get; set; } = [];

    /// <summary>
    /// Gets or sets external deck sources that contributed to this local workspace.
    /// </summary>
    public List<DeckSourceReference> SourceReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal import or migration warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Provides well-known deck import provider keys.
/// </summary>
public static class DeckImportProviders
{
    /// <summary>
    /// Stores the Archidekt provider key.
    /// </summary>
    public const string Archidekt = "archidekt";

    /// <summary>
    /// Stores the Moxfield provider key.
    /// </summary>
    public const string Moxfield = "moxfield";
}

/// <summary>
/// Provides well-known metadata keys stored on workspace cards.
/// </summary>
public static class DeckCardMetadataKeys
{
    /// <summary>
    /// Stores how an Archidekt card id was resolved.
    /// </summary>
    public const string ArchidektCardIdResolution = "archidektCardIdResolution";
}

/// <summary>
/// Provides a compact response for opening a workspace through MCP.
/// </summary>
public sealed class DeckOpenResult
{
    /// <summary>
    /// Gets or sets the detail level used to shape the open response.
    /// </summary>
    public string DetailLevel { get; set; } = "summary";

    /// <summary>
    /// Gets or sets the workspace id using the legacy raw-workspace field name.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id to use with follow-up tools.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the workspace mode.
    /// </summary>
    public WorkspaceMode Mode { get; set; }

    /// <summary>
    /// Gets or sets whether Archidekt writeback is enabled.
    /// </summary>
    public bool WriteBack { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt deck id when the workspace is Archidekt-backed.
    /// </summary>
    public string? ArchidektDeckId { get; set; }

    /// <summary>
    /// Gets or sets source deck references imported into this workspace.
    /// </summary>
    public List<DeckSourceReference> SourceReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal warnings from opening or importing the workspace.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets where future mutations will be persisted.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets total card quantity across all categories.
    /// </summary>
    public int TotalCards { get; set; }

    /// <summary>
    /// Gets or sets card quantity included in the active deck.
    /// </summary>
    public int IncludedCards { get; set; }

    /// <summary>
    /// Gets or sets card quantity in excluded maybeboard categories.
    /// </summary>
    public int MaybeboardCards { get; set; }

    /// <summary>
    /// Gets or sets commander card names found in included commander categories.
    /// </summary>
    public List<string> Commanders { get; set; } = [];

    /// <summary>
    /// Gets or sets compact category counts.
    /// </summary>
    public List<DeckOpenCategorySummary> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets compact card rows when normal detail is requested.
    /// </summary>
    public List<DeckOpenCardSummary> Cards { get; set; } = [];
}

/// <summary>
/// Describes one category in a compact deck-open response.
/// </summary>
public sealed class DeckOpenCategorySummary
{
    /// <summary>
    /// Gets or sets the category name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the category counts toward the active deck.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets the card quantity in this category.
    /// </summary>
    public int CardCount { get; set; }
}

/// <summary>
/// Describes one compact card row in a normal-detail deck-open response.
/// </summary>
public sealed class DeckOpenCardSummary
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets card quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the card's primary category.
    /// </summary>
    public string PrimaryCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets all category labels on the card.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets cached type line when known.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets Scryfall page when known.
    /// </summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>
/// Describes the card zone requested by a compact workspace listing.
/// </summary>
public static class DeckCardZones
{
    /// <summary>
    /// Lists cards whose primary category contributes to the active deck.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// Lists cards whose primary category is Sideboard.
    /// </summary>
    public const string Sideboard = "sideboard";

    /// <summary>
    /// Lists cards whose primary category is Maybeboard.
    /// </summary>
    public const string Maybeboard = "maybeboard";

    /// <summary>
    /// Lists cards whose primary category is excluded from the active deck.
    /// </summary>
    public const string Excluded = "excluded";

    /// <summary>
    /// Lists every workspace card row.
    /// </summary>
    public const string All = "all";
}

/// <summary>
/// Returns a compact card listing for a workspace zone.
/// </summary>
public sealed class DeckCardsByZoneResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized requested zone.
    /// </summary>
    public string Zone { get; set; } = DeckCardZones.Active;

    /// <summary>
    /// Gets or sets whether duplicate card identities were collapsed.
    /// </summary>
    public bool CollapseDuplicates { get; set; } = true;

    /// <summary>
    /// Gets or sets total quantity represented by the returned rows.
    /// </summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Gets or sets the number of rows returned after optional duplicate collapsing.
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// Gets or sets cards matching the zone filter.
    /// </summary>
    public List<DeckCardZoneRow> Cards { get; set; } = [];
}

/// <summary>
/// Describes one card in a compact zone listing.
/// </summary>
public sealed class DeckCardZoneRow
{
    /// <summary>
    /// Gets or sets the display card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets total card quantity for this row.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the primary category when the row is not collapsed across categories.
    /// </summary>
    public string? PrimaryCategory { get; set; }

    /// <summary>
    /// Gets or sets whether any primary location contributes to the active deck.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets all categories represented by this row.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets category-level locations represented by this row.
    /// </summary>
    public List<DeckCardZoneLocation> Locations { get; set; } = [];

    /// <summary>
    /// Gets or sets cached type line when known.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets Scryfall page when known.
    /// </summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>
/// Describes a card quantity in one workspace category.
/// </summary>
public sealed class DeckCardZoneLocation
{
    /// <summary>
    /// Gets or sets the category name.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this location is the card row's primary category.
    /// </summary>
    public bool Primary { get; set; }

    /// <summary>
    /// Gets or sets whether the category contributes to the active deck.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets card quantity in this location.
    /// </summary>
    public int Quantity { get; set; }
}

/// <summary>
/// Carries one card/category transfer requested by deck_move_cards_bulk.
/// </summary>
public sealed class BulkDeckCardMove
{
    /// <summary>
    /// Gets or sets the card name to move.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the destination primary category.
    /// </summary>
    public string ToCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets the optional source primary category.
    /// </summary>
    public string? FromCategory { get; set; }

    /// <summary>
    /// Gets or sets the optional partial quantity to move.
    /// </summary>
    public int? Quantity { get; set; }
}

/// <summary>
/// Provides archidekt deck summary behavior.
/// </summary>
public sealed class ArchidektDeckSummary
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
    /// Gets or sets the format.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the updated at.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt folder id when available.
    /// </summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt folder name when available.
    /// </summary>
    public string? FolderName { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt folder path when the listing response includes it.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Gets or sets deck visibility when available.
    /// </summary>
    public string? Visibility { get; set; }

    /// <summary>
    /// Gets or sets the deck card count when available.
    /// </summary>
    public int? CardCount { get; set; }
}

/// <summary>
/// Describes filters and pagination for Archidekt deck listing.
/// </summary>
public sealed class ArchidektDeckListRequest
{
    /// <summary>
    /// Gets or sets the page number requested from Archidekt.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the requested page size.
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Gets or sets a folder id filter.
    /// </summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// Gets or sets a folder name filter applied after mapping results.
    /// </summary>
    public string? FolderName { get; set; }
}

/// <summary>
/// Describes an Archidekt folder.
/// </summary>
public sealed class ArchidektFolder
{
    /// <summary>
    /// Gets or sets the folder id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the folder name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the parent folder id, when present.
    /// </summary>
    public string? ParentFolderId { get; set; }

    /// <summary>
    /// Gets or sets the folder path when Archidekt exposes one.
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>
/// Reports a bulk Archidekt deck move.
/// </summary>
public sealed class ArchidektMoveDecksResult
{
    /// <summary>
    /// Gets or sets the destination folder id, or null for root.
    /// </summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// Gets or sets the deck ids requested for the move.
    /// </summary>
    public List<string> DeckIds { get; set; } = [];

    /// <summary>
    /// Gets or sets how many deck ids were submitted.
    /// </summary>
    public int Moved { get; set; }
}

/// <summary>
/// Describes a new Archidekt deck to create before optional card migration.
/// </summary>
public sealed class ArchidektDeckCreateRequest
{
    /// <summary>
    /// Gets or sets the new deck name.
    /// </summary>
    public string Name { get; set; } = "Untitled Deck";

    /// <summary>
    /// Gets or sets the deck format, such as commander.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the deck description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the requested visibility: private, unlisted, or public.
    /// </summary>
    public string Visibility { get; set; } = "private";

    /// <summary>
    /// Gets or sets the parent Archidekt folder id for the created deck.
    /// </summary>
    public string? ParentFolderId { get; set; }

    /// <summary>
    /// Gets or sets a parent folder name to resolve before creating the deck.
    /// </summary>
    public string? FolderName { get; set; }
}

/// <summary>
/// Reports the outcome or dry run for copying a workspace to Archidekt.
/// </summary>
public sealed class ArchidektCopyResult
{
    /// <summary>
    /// Gets or sets whether this result describes a dry run.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets the source workspace id.
    /// </summary>
    public string SourceWorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the destination workspace id after a real copy.
    /// </summary>
    public string? DestinationWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the destination Archidekt deck id when known.
    /// </summary>
    public string? DestinationArchidektDeckId { get; set; }

    /// <summary>
    /// Gets or sets whether the destination deck would be or was created.
    /// </summary>
    public bool CreatedNewDeck { get; set; }

    /// <summary>
    /// Gets or sets the destination deck name.
    /// </summary>
    public string DestinationName { get; set; } = "";

    /// <summary>
    /// Gets or sets copied card quantity.
    /// </summary>
    public int TotalCards { get; set; }

    /// <summary>
    /// Gets or sets included card quantity.
    /// </summary>
    public int IncludedCards { get; set; }

    /// <summary>
    /// Gets or sets copied category names.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Latest copy phase reached before the result was returned.
    /// </summary>
    public string CopyPhase { get; set; } = "initialized";

    /// <summary>
    /// Estimated number of Archidekt HTTP requests needed for this copy attempt.
    /// </summary>
    public int EstimatedArchidektRequests { get; set; }

    /// <summary>
    /// Copied cards whose Archidekt ids were supplied by the local resolution cache.
    /// </summary>
    public int CardIdCacheHits { get; set; }

    /// <summary>
    /// Copied cards whose Archidekt ids were resolved from any source.
    /// </summary>
    public int ResolvedCount { get; set; }

    /// <summary>
    /// Copied cards whose Archidekt ids were supplied by the local resolution cache.
    /// </summary>
    public int CacheHits { get; set; }

    /// <summary>
    /// Archidekt card-search lookups performed while resolving card ids.
    /// </summary>
    public int RemoteLookups { get; set; }

    /// <summary>
    /// Card rows written to the destination during this copy attempt.
    /// </summary>
    public int WrittenRows { get; set; }

    /// <summary>
    /// Copied cards whose Archidekt ids were resolved through Archidekt during this copy.
    /// </summary>
    public int CardIdsResolved { get; set; }

    /// <summary>
    /// Copied cards that still lacked Archidekt card ids after resolution.
    /// </summary>
    public int MissingArchidektCardIds { get; set; }

    /// <summary>
    /// Explains whether missing Archidekt card ids are cache misses or unresolved apply failures.
    /// </summary>
    public string CardIdDiagnostics { get; set; } = "";

    /// <summary>
    /// Gets or sets the phase that failed, when the copy stopped before completion.
    /// </summary>
    public string? FailedPhase { get; set; }

    /// <summary>
    /// Gets or sets the checkpoint created before mutating an existing destination deck.
    /// </summary>
    public string? CheckpointId { get; set; }

    /// <summary>
    /// Gets or sets the expected final destination card row count after the copy.
    /// </summary>
    public int ExpectedCardRows { get; set; }

    /// <summary>
    /// Gets or sets the detected destination card row count after verification or failure inspection.
    /// </summary>
    public int? DetectedCardRows { get; set; }

    /// <summary>
    /// Gets or sets final verification status, such as not-run, verified, mismatch, blocked, or failed.
    /// </summary>
    public string VerificationStatus { get; set; } = "not-run";

    /// <summary>
    /// Gets recovery steps for partial or blocked copy attempts.
    /// </summary>
    public List<string> RecoveryInstructions { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the result can be resumed with the returned destination id.
    /// </summary>
    public bool CanResume { get; set; }

    /// <summary>
    /// Gets or sets a destination deck id or URL that can resume the copy.
    /// </summary>
    public string? ResumeDeckIdOrUrl { get; set; }

    /// <summary>
    /// Gets or sets the recommended next action for an agent after this copy result.
    /// </summary>
    public string? NextAction { get; set; }

    /// <summary>
    /// Gets or sets commander names detected in the source workspace.
    /// </summary>
    public List<string> Commanders { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings that should be reviewed before applying a copy.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Provides deck checkpoint behavior.
/// </summary>
public sealed class DeckCheckpoint
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck id.
    /// </summary>
    public string DeckId { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the created at.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// Provides auth status behavior.
/// </summary>
public sealed class AuthStatus
{
    /// <summary>
    /// Gets or sets whether an Archidekt username and password are configured.
    /// </summary>
    public bool HasUsernamePassword { get; set; }

    /// <summary>
    /// Gets or sets the has credentials file.
    /// </summary>
    public bool HasCredentialsFile { get; set; }

    /// <summary>
    /// Gets or sets the credentials file error.
    /// </summary>
    public string? CredentialsFileError { get; set; }

    /// <summary>
    /// Gets whether credential file parsing failed.
    /// </summary>
    public bool HasCredentialsFileError => !string.IsNullOrWhiteSpace(CredentialsFileError);

    /// <summary>
    /// Gets the effective authentication mode.
    /// </summary>
    public string Mode =>
        HasCredentialsFileError ? "credentials-file-error"
        : HasUsernamePassword ? "username-password"
        : "anonymous";
}

/// <summary>
/// Provides parsed decklist behavior.
/// </summary>
public sealed class ParsedDecklist
{
    /// <summary>
    /// Gets or sets the cards.
    /// </summary>
    public List<ParsedDecklistLine> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets the warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Provides parsed decklist line behavior.
/// </summary>
public sealed class ParsedDecklistLine
{
    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category { get; set; } = DeckDefaults.Mainboard;

    /// <summary>
    /// Gets or sets the line number.
    /// </summary>
    public int LineNumber { get; set; }
}

/// <summary>
/// Provides deck validation result behavior.
/// </summary>
public sealed class DeckValidationResult
{
    /// <summary>
    /// Stores the is valid.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets or sets the errors.
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Gets or sets the warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Reports cached-metadata legality and deck-construction findings for a workspace.
/// </summary>
public sealed class DeckLegalityAudit
{
    /// <summary>
    /// Gets whether the audit found no legality errors.
    /// </summary>
    public bool IsLegal => Errors.Count == 0
        && CardLegalityIssues.All(static issue => !issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
        && ColorIdentityIssues.All(static issue => !issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
        && CopyLimitIssues.All(static issue => !issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
        && SideboardIssues.All(static issue => !issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets or sets the audited workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized format used for legality checks.
    /// </summary>
    public string Format { get; set; } = "";

    /// <summary>
    /// Gets or sets whether excluded cards were included in card-level legality checks.
    /// </summary>
    public bool IncludeExcluded { get; set; }

    /// <summary>
    /// Gets or sets the active included card count.
    /// </summary>
    public int IncludedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of card rows audited for card-level legality.
    /// </summary>
    public int AuditedCardRows { get; set; }

    /// <summary>
    /// Gets or sets command-zone facts used by Commander legality checks.
    /// </summary>
    public DeckLegalityCommandZoneSummary CommandZone { get; set; } = new();

    /// <summary>
    /// Gets or sets construction or command-zone errors.
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal construction or metadata warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets cards whose cached format legality is not legal.
    /// </summary>
    public List<DeckLegalityIssue> CardLegalityIssues { get; set; } = [];

    /// <summary>
    /// Gets or sets cards outside the Commander color identity.
    /// </summary>
    public List<DeckLegalityIssue> ColorIdentityIssues { get; set; } = [];

    /// <summary>
    /// Gets or sets singleton or constructed copy-limit findings.
    /// </summary>
    public List<DeckLegalityIssue> CopyLimitIssues { get; set; } = [];

    /// <summary>
    /// Gets or sets sideboard-size and sideboard-category findings.
    /// </summary>
    public List<DeckLegalityIssue> SideboardIssues { get; set; } = [];

    /// <summary>
    /// Gets or sets missing cached metadata needed for confident legality checks.
    /// </summary>
    public List<DeckLegalityIssue> MetadataGaps { get; set; } = [];

    /// <summary>
    /// Gets or sets audit assumptions and data-source caveats.
    /// </summary>
    public List<string> Assumptions { get; set; } = [];
}

/// <summary>
/// Summarizes the active command zone for legality audits.
/// </summary>
public sealed class DeckLegalityCommandZoneSummary
{
    /// <summary>
    /// Gets or sets command-zone display name for single or paired commanders.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets non-Background commander names.
    /// </summary>
    public List<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets or sets Background names.
    /// </summary>
    public List<string> BackgroundNames { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the command zone appears to be a partner pair.
    /// </summary>
    public bool HasPartnerPair { get; set; }

    /// <summary>
    /// Gets or sets whether the command zone appears to be a Background pair.
    /// </summary>
    public bool HasBackgroundPair { get; set; }

    /// <summary>
    /// Gets or sets unioned command-zone color identity.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of active command-zone rows.
    /// </summary>
    public int CardRows { get; set; }
}

/// <summary>
/// Describes one structured legality finding.
/// </summary>
public sealed class DeckLegalityIssue
{
    /// <summary>
    /// Gets or sets error or warning severity.
    /// </summary>
    public string Severity { get; set; } = "warning";

    /// <summary>
    /// Gets or sets the card name when the finding applies to one card.
    /// </summary>
    public string? CardName { get; set; }

    /// <summary>
    /// Gets or sets the primary category for the finding.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the card quantity associated with the finding.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the relevant format legality value when known.
    /// </summary>
    public string? Legality { get; set; }

    /// <summary>
    /// Gets or sets card color identity values associated with the finding.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the human-readable finding.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets a card source URI when cached.
    /// </summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>
/// Provides deck analysis behavior.
/// </summary>
public sealed class DeckAnalysis
{
    /// <summary>
    /// Gets or sets the total cards.
    /// </summary>
    public int TotalCards { get; set; }

    /// <summary>
    /// Gets or sets the included cards.
    /// </summary>
    public int IncludedCards { get; set; }

    /// <summary>
    /// Gets or sets primary-category counts across all cards.
    /// </summary>
    public Dictionary<string, int> CategoryCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets primary-category counts for cards included in the active deck.
    /// </summary>
    public Dictionary<string, int> IncludedCategoryCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets counts for every user category on each card, including secondary categories.
    /// </summary>
    public Dictionary<string, int> AllCategoryCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets all-category counts for cards included in the active deck.
    /// </summary>
    public Dictionary<string, int> IncludedAllCategoryCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the type counts.
    /// </summary>
    public Dictionary<string, int> TypeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the color identity counts.
    /// </summary>
    public Dictionary<string, int> ColorIdentityCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the mana curve.
    /// </summary>
    public Dictionary<string, int> ManaCurve { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the role counts.
    /// </summary>
    public Dictionary<string, int> RoleCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets additive functional role counts where one included card can count for multiple jobs.
    /// </summary>
    public Dictionary<string, int> FunctionalRoleCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the tag counts.
    /// </summary>
    public Dictionary<string, int> TagCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Provides deck change result behavior.
/// </summary>
public sealed class DeckChangeResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public DeckMutationKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the persistence.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();
}

/// <summary>
/// Describes one card add in a bulk deck mutation.
/// </summary>
public sealed class BulkDeckCardAdd
{
    /// <summary>
    /// Gets or sets the exact card name to add.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the quantity to add; values below one are treated as one.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the primary workspace category for the added card.
    /// </summary>
    public string PrimaryCategory { get; set; } = DeckDefaults.Mainboard;

    /// <summary>
    /// Gets or sets secondary categories to add after the primary category.
    /// </summary>
    public List<string> SecondaryCategories { get; set; } = [];
}

/// <summary>
/// Describes one category operation in a bulk card-category mutation.
/// </summary>
public sealed class BulkCardCategoryChange
{
    /// <summary>
    /// Gets or sets the workspace card name to update.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the category action: add-secondary, remove, or set-primary.
    /// </summary>
    public string Action { get; set; } = BulkCardCategoryActions.AddSecondary;

    /// <summary>
    /// Gets or sets the category name used by the action.
    /// </summary>
    public string Category { get; set; } = "";
}

/// <summary>
/// Provides supported bulk card-category action names.
/// </summary>
public static class BulkCardCategoryActions
{
    /// <summary>
    /// Adds a category after the card's primary category.
    /// </summary>
    public const string AddSecondary = "add-secondary";

    /// <summary>
    /// Removes a category from the card.
    /// </summary>
    public const string Remove = "remove";

    /// <summary>
    /// Moves a category to the card's primary slot.
    /// </summary>
    public const string SetPrimary = "set-primary";
}

/// <summary>
/// Reports compact local cards that belong to a workspace category.
/// </summary>
public sealed class DeckCategoryCardListResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized category requested by the caller.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets whether secondary categories were included in matching.
    /// </summary>
    public bool IncludeSecondary { get; set; }

    /// <summary>
    /// Gets or sets total matching rows before the response limit was applied.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets total matching card quantity before the response limit was applied.
    /// </summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// Gets or sets compact card rows, capped by the requested limit.
    /// </summary>
    public List<DeckCategoryCardListRow> Cards { get; set; } = [];
}

/// <summary>
/// Describes one compact card row returned for a workspace category.
/// </summary>
public sealed class DeckCategoryCardListRow
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the card's primary category.
    /// </summary>
    public string PrimaryCategory { get; set; } = DeckDefaults.Mainboard;

    /// <summary>
    /// Gets or sets all card categories in primary-first order.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets the local role classifier's primary role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets local classifier tags for the card.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the cached mana value.
    /// </summary>
    public double? ManaValue { get; set; }

    /// <summary>
    /// Gets or sets the cached type line.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets the cached USD price when known and safe for budget math.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets the cached Scryfall card URL.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets whether the card's primary category counts toward deck size.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets whether the card's primary category counts toward price totals.
    /// </summary>
    public bool IncludedInPrice { get; set; }
}

/// <summary>
/// Provides deck persistence behavior.
/// </summary>
public static class DeckPersistence
{
    /// <summary>
    /// Stores the local only.
    /// </summary>
    public const string LocalOnly = "local-only";

    /// <summary>
    /// Stores the archidekt write back.
    /// </summary>
    public const string ArchidektWriteBack = "archidekt-writeback";

    /// <summary>
    /// Returns the persistence label for a workspace.
    /// </summary>
    public static string For(DeckWorkspace workspace)
    {
        return workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack
            ? ArchidektWriteBack
            : LocalOnly;
    }
}

/// <summary>
/// Provides deck defaults behavior.
/// </summary>
public static class DeckDefaults
{
    /// <summary>
    /// Stores the mainboard.
    /// </summary>
    public const string Mainboard = "Mainboard";

    /// <summary>
    /// Stores the sideboard.
    /// </summary>
    public const string Sideboard = "Sideboard";

    /// <summary>
    /// Stores the maybeboard.
    /// </summary>
    public const string Maybeboard = "Maybeboard";

    /// <summary>
    /// Stores the considering category used for candidate cards.
    /// </summary>
    public const string Considering = "Considering";

    /// <summary>
    /// Checks whether a category should be excluded when mtg-mcp creates it implicitly.
    /// </summary>
    public static bool IsDefaultExcludedCategory(string category)
    {
        return category.Equals(Maybeboard, StringComparison.OrdinalIgnoreCase)
            || category.Equals(Sideboard, StringComparison.OrdinalIgnoreCase)
            || category.Equals(Considering, StringComparison.OrdinalIgnoreCase)
            || category.Equals("Maybe", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Consider", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a category should be excluded from price totals when mtg-mcp creates it implicitly.
    /// </summary>
    public static bool IsDefaultPriceExcludedCategory(string category)
    {
        return category.Equals(Sideboard, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a category name should represent Archidekt's commander category.
    /// </summary>
    public static bool IsCommanderCategory(string category)
    {
        return category.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates the default categories.
    /// </summary>
    public static List<DeckCategory> CreateDefaultCategories()
    {
        return
        [
            new DeckCategory
            {
                Name = Mainboard,
                IncludedInDeck = true,
                IncludedInPrice = true,
            },
            new DeckCategory
            {
                Name = Sideboard,
                IncludedInDeck = false,
                IncludedInPrice = false,
            },
            new DeckCategory
            {
                Name = Maybeboard,
                IncludedInDeck = false,
                IncludedInPrice = true,
            },
        ];
    }
}
