namespace MtgMcp.Core;

public enum WorkspaceMode
{
    Local,
    Archidekt
}

public enum DeckMutationKind
{
    CardAdded,
    CardRemoved,
    QuantityChanged,
    CardMoved,
    CategoryChanged,
    MetadataChanged
}

public sealed class CardInfo
{
    public string Id { get; set; } = "";
    public string? OracleId { get; set; }
    public string Name { get; set; } = "";
    public string? ManaCost { get; set; }
    public double? ManaValue { get; set; }
    public string? TypeLine { get; set; }
    public string? OracleText { get; set; }
    public string? Set { get; set; }
    public string? CollectorNumber { get; set; }
    public string? Rarity { get; set; }
    public string? ScryfallUri { get; set; }
    public List<string> Colors { get; set; } = [];
    public List<string> ColorIdentity { get; set; } = [];
    public Dictionary<string, string> Legalities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ImageUris { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CardSearchResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ManaCost { get; set; }
    public string? TypeLine { get; set; }
    public string? Set { get; set; }
    public string? CollectorNumber { get; set; }
    public string? ScryfallUri { get; set; }
}

public sealed class RulingInfo
{
    public string Source { get; set; } = "";
    public DateOnly PublishedAt { get; set; }
    public string Text { get; set; } = "";
}

public sealed class DeckCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IncludedInDeck { get; set; } = true;
    public bool IncludedInPrice { get; set; } = true;
    public int? ArchidektCategoryId { get; set; }
}

public sealed class DeckCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string PrimaryCategory { get; set; } = DeckDefaults.Mainboard;
    public List<string> Categories { get; set; } = [DeckDefaults.Mainboard];
    public string? ScryfallId { get; set; }
    public string? ScryfallOracleId { get; set; }
    public string? ArchidektCardId { get; set; }
    public int? ArchidektDeckRelationId { get; set; }
    public string? Modifier { get; set; }
    public bool Companion { get; set; }
    public bool FlippedDefault { get; set; }
    public CardSnapshot Snapshot { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CardSnapshot
{
    public string? TypeLine { get; set; }
    public double? ManaValue { get; set; }
    public List<string> ColorIdentity { get; set; } = [];
    public string? Set { get; set; }
    public string? CollectorNumber { get; set; }
    public string? ScryfallUri { get; set; }
}

public sealed class DeckWorkspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled Deck";
    public string Format { get; set; } = "commander";
    public string? Description { get; set; }
    public WorkspaceMode Mode { get; set; } = WorkspaceMode.Local;
    public bool WriteBack { get; set; }
    public string? ArchidektDeckId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<DeckCategory> Categories { get; set; } = DeckDefaults.CreateDefaultCategories();
    public List<DeckCard> Cards { get; set; } = [];
}

public sealed class ArchidektDeckSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Format { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class DeckCheckpoint
{
    public string Id { get; set; } = "";
    public string DeckId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class AuthStatus
{
    public bool HasJwt { get; set; }
    public bool HasRefreshToken { get; set; }
    public bool HasUserId { get; set; }
    public bool HasEmailPassword { get; set; }
    public bool HasUsernamePassword { get; set; }
    public bool HasLoginPassword => HasEmailPassword || HasUsernamePassword;
    public bool HasCredentialsFile { get; set; }
    public string? CredentialsFileError { get; set; }
    public bool HasCredentialsFileError => !string.IsNullOrWhiteSpace(CredentialsFileError);
    public string Mode => HasCredentialsFileError
        ? "credentials-file-error"
        : HasJwt ? "jwt" : HasLoginPassword ? "username-password" : "anonymous";
}

public sealed class ParsedDecklist
{
    public List<ParsedDecklistLine> Cards { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class ParsedDecklistLine
{
    public int Quantity { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = DeckDefaults.Mainboard;
    public int LineNumber { get; set; }
}

public sealed class DeckValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class DeckAnalysis
{
    public int TotalCards { get; set; }
    public int IncludedCards { get; set; }
    public Dictionary<string, int> CategoryCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> TypeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ColorIdentityCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ManaCurve { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Notes { get; set; } = [];
}

public sealed class DeckChangeResult
{
    public string WorkspaceId { get; set; } = "";
    public DeckMutationKind Kind { get; set; }
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;
    public string Message { get; set; } = "";
    public DeckWorkspace Workspace { get; set; } = new();
}

public static class DeckPersistence
{
    public const string LocalOnly = "local-only";
    public const string ArchidektWriteBack = "archidekt-writeback";

    public static string For(DeckWorkspace workspace)
    {
        return workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack
            ? ArchidektWriteBack
            : LocalOnly;
    }
}

public static class DeckDefaults
{
    public const string Mainboard = "Mainboard";
    public const string Sideboard = "Sideboard";
    public const string Maybeboard = "Maybeboard";

    public static List<DeckCategory> CreateDefaultCategories()
    {
        return
        [
            new DeckCategory { Name = Mainboard, IncludedInDeck = true, IncludedInPrice = true },
            new DeckCategory { Name = Sideboard, IncludedInDeck = false, IncludedInPrice = true },
            new DeckCategory { Name = Maybeboard, IncludedInDeck = false, IncludedInPrice = true }
        ];
    }
}
