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
