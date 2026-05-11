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
    /// Gets or sets the release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the scryfall uri.
    /// </summary>
    public string? ScryfallUri { get; set; }

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
    public int? ArchidektDeckRelationId { get; set; }

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
/// Provides card snapshot behavior.
/// </summary>
public sealed class CardSnapshot
{
    /// <summary>
    /// Gets or sets the mana cost.
    /// </summary>
    public string? ManaCost { get; set; }

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
    /// Gets or sets the release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the scryfall uri.
    /// </summary>
    public string? ScryfallUri { get; set; }

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
