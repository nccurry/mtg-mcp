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
/// Provides a compact response for opening a workspace through MCP.
/// </summary>
public sealed class DeckOpenResult
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
    /// Gets or sets the has jwt.
    /// </summary>
    public bool HasJwt { get; set; }

    /// <summary>
    /// Gets or sets the has refresh token.
    /// </summary>
    public bool HasRefreshToken { get; set; }

    /// <summary>
    /// Gets or sets whether a user id is configured.
    /// </summary>
    public bool HasUserId { get; set; }

    /// <summary>
    /// Gets or sets whether an email and password are configured.
    /// </summary>
    public bool HasEmailPassword { get; set; }

    /// <summary>
    /// Gets or sets the has username password.
    /// </summary>
    public bool HasUsernamePassword { get; set; }

    /// <summary>
    /// Gets whether any login password credentials are configured.
    /// </summary>
    public bool HasLoginPassword => HasEmailPassword || HasUsernamePassword;

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
        : HasJwt ? "jwt"
        : HasLoginPassword ? "username-password"
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
    /// Gets or sets the category counts.
    /// </summary>
    public Dictionary<string, int> CategoryCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets primary-category counts for cards included in the active deck.
    /// </summary>
    public Dictionary<string, int> IncludedCategoryCounts { get; set; } =
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
                IncludedInPrice = true,
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
