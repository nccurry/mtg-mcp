using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MtgMcp.Core.Decks;

/// <summary>
/// Describes a deck without loading its detailed entries and relationships.
/// </summary>
public sealed record DeckSummary(
    Guid DeckId,
    string Name,
    string Description,
    string Format,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Carries a stable page of canonically ordered deck summaries.
/// </summary>
public sealed record DeckPage(
    IReadOnlyList<DeckSummary> Items,
    string? NextCursor)
{
    /// <summary>
    /// Gets the immutable snapshot of summaries in this page.
    /// </summary>
    public IReadOnlyList<DeckSummary> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Stores one independently addressable card row in a deck zone.
/// </summary>
public sealed record DeckEntry(
    [property: Description("The stable entry ID.")] Guid EntryId,
    [property: Description("How many copies the entry holds.")] int Quantity,
    [property: Description("The card name.")] string CardName,
    [property: Description("The Scryfall oracle ID, or null when unknown.")] Guid? OracleId,
    [property: Description("The Scryfall printing ID, or null when unknown.")] Guid? PrintingId,
    [property: Description("The set code of the printing, or null.")] string? SetCode,
    [property: Description("The collector number of the printing, or null.")] string? CollectorNumber,
    [property: Description("The language code, such as en.")] string Language,
    [property: Description("The finish, such as nonfoil or foil.")] string Finish,
    [property: Description("The zone code, such as main or sideboard.")] string Zone,
    [property: Description("The order of the entry within its zone.")] int SortOrder);

/// <summary>
/// Describes caller-supplied values for a new deck entry. The category IDs name categories the deck already has or an
/// earlier change of the same batch adds; the primary category is assigned as primary even when it is not listed.
/// A zone categories mode of match gives a non-main zone its zone category (created when missing) as the primary
/// one and drops other zone categories; leave or no mode assigns only what the draft lists.
/// </summary>
public sealed record DeckEntryDraft(
    [property: Description("How many copies to add.")] int Quantity,
    [property: Description("The card name; with only this, card data fills in the IDs.")] string CardName,
    [property: Description("The Scryfall oracle ID; omit to look it up from the name.")] Guid? OracleId = null,
    [property: Description("The Scryfall printing ID, to choose a printing; omit to use the default one.")]
    Guid? PrintingId = null,
    [property: Description("The set code, to choose a printing; give it with collectorNumber.")]
    string? SetCode = null,
    [property: Description("The collector number, to choose a printing; give it with setCode.")]
    string? CollectorNumber = null,
    [property: Description("The language code; default en.")] string Language = "en",
    [property: Description("The finish, such as nonfoil or foil; default nonfoil.")] string Finish = "nonfoil",
    [property: Description("The zone code: main (default), commander, sideboard, or maybeboard.")]
    string Zone = DeckZoneCategories.MainZone,
    [property: Description("The order of the entry within its zone; default 0.")] int SortOrder = 0,
    [property: Description(
        "An ID for the new entry so later changes in the batch can refer to it; omit to generate one.")]
    Guid? EntryId = null,
    [property: Description("IDs of categories to assign to the new entry.")] IReadOnlyList<Guid>? CategoryIds = null,
    [property: Description("The category assigned as primary, even when it is not in categoryIds.")]
    Guid? PrimaryCategoryId = null,
    [property: Description(
        "match: a non-main zone's category (created when missing) becomes the primary category and other zone " +
        "categories are dropped; leave or omit: assign only the listed categories."),
     AllowedValues(DeckZoneCategories.Match, DeckZoneCategories.Leave)]
    string? ZoneCategories = null);

/// <summary>
/// Defines one functional category whose assignments never affect deck zones.
/// </summary>
public sealed record DeckCategory(
    [property: Description("The stable category ID.")] Guid CategoryId,
    [property: Description("The category name.")] string Name,
    [property: Description("A color for the category, or null.")] string? Color,
    [property: Description("The order of the category among the deck's categories.")] int SortOrder);

/// <summary>
/// Describes caller-supplied values for a new deck category.
/// </summary>
public sealed record DeckCategoryDraft(
    [property: Description("The category name.")] string Name,
    [property: Description("A color for the category; omit for none.")] string? Color = null,
    [property: Description("The order of the category among the deck's categories; default 0.")] int SortOrder = 0,
    [property: Description(
        "An ID for the new category so later changes in the batch can refer to it; omit to generate one.")]
    Guid? CategoryId = null);

/// <summary>
/// Links an entry to a functional category with an optional primary designation.
/// </summary>
public sealed record DeckCategoryAssignment(
    [property: Description("The entry that receives the category.")] Guid EntryId,
    [property: Description("The category to assign.")] Guid CategoryId,
    [property: Description("Whether this is the entry's primary category.")] bool IsPrimary);

/// <summary>
/// Stores provider-neutral synchronization identity without provider transport payloads.
/// </summary>
public sealed record DeckProviderBinding(
    [property: Description("The stable binding ID.")] Guid BindingId,
    [property: Description("The provider name, such as archidekt.")] string Provider,
    [property: Description("The provider's own deck ID.")] string RemoteId,
    [property: Description("The provider's deck URL, or null.")] string? RemoteUri,
    [property: Description("The provider's version marker for the last sync, or null.")] string? RemoteVersion,
    [property: Description("The fingerprint of the last synced state, or null.")] string? BaselineFingerprint,
    [property: Description("When the deck was last pulled, or null.")] DateTimeOffset? LastPulledAtUtc,
    [property: Description("When the deck was last pushed, or null.")] DateTimeOffset? LastPushedAtUtc);

/// <summary>
/// Carries the complete canonical local representation of one revisioned deck.
/// </summary>
public sealed record DeckDocument(
    Guid DeckId,
    string Name,
    string Description,
    string Format,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DeckEntry> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<DeckCategoryAssignment> CategoryAssignments,
    IReadOnlyList<DeckProviderBinding> ProviderBindings)
{
    /// <summary>
    /// Gets the immutable snapshot of independently addressable entries.
    /// </summary>
    public IReadOnlyList<DeckEntry> Entries { get; init; } =
        Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets the immutable snapshot of functional categories.
    /// </summary>
    public IReadOnlyList<DeckCategory> Categories { get; init; } =
        Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets the immutable snapshot of entry-category relationships.
    /// </summary>
    public IReadOnlyList<DeckCategoryAssignment> CategoryAssignments { get; init; } =
        Array.AsReadOnly(CategoryAssignments.ToArray());

    /// <summary>
    /// Gets the immutable snapshot of provider-neutral synchronization bindings.
    /// </summary>
    public IReadOnlyList<DeckProviderBinding> ProviderBindings { get; init; } =
        Array.AsReadOnly(ProviderBindings.ToArray());
}

/// <summary>
/// Describes caller-supplied content for a new local deck.
/// </summary>
public sealed record DeckCreateRequest(
    [property: Description("The deck name.")] string Name,
    [property: Description("The deck description; omit for none.")] string? Description = null,
    [property: Description("The format label, such as commander; mtg-mcp does not enforce format legality.")]
    string Format = "commander",
    IReadOnlyList<DeckEntryDraft>? Entries = null,
    IReadOnlyList<DeckCategoryDraft>? Categories = null,
    IReadOnlyList<DeckCategoryAssignment>? CategoryAssignments = null,
    IReadOnlyList<DeckProviderBinding>? ProviderBindings = null,
    [property: Description("An ID for the new deck; omit to generate one.")] Guid? DeckId = null)
{
    /// <summary>
    /// Gets a stable snapshot of requested entries when supplied.
    /// </summary>
    [Description("The starting entries; a name-only entry gets its card IDs from installed card data.")]
    public IReadOnlyList<DeckEntryDraft>? Entries { get; init; } =
        Entries is null ? null : Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets a stable snapshot of requested categories when supplied.
    /// </summary>
    [Description("The starting categories.")]
    public IReadOnlyList<DeckCategoryDraft>? Categories { get; init; } =
        Categories is null ? null : Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets a stable snapshot of requested category assignments when supplied.
    /// </summary>
    [Description("Links between the starting entries and categories, by entryId and categoryId.")]
    public IReadOnlyList<DeckCategoryAssignment>? CategoryAssignments { get; init; } =
        CategoryAssignments is null ? null : Array.AsReadOnly(CategoryAssignments.ToArray());

    /// <summary>
    /// Gets a stable snapshot of requested provider bindings when supplied.
    /// </summary>
    [Description("Provider bindings for the new deck.")]
    public IReadOnlyList<DeckProviderBinding>? ProviderBindings { get; init; } =
        ProviderBindings is null ? null : Array.AsReadOnly(ProviderBindings.ToArray());
}

/// <summary>
/// Reports one local structural defect without making a format-legality judgment.
/// </summary>
public sealed record DeckValidationIssue(
    string ReasonCode,
    string Message,
    Guid? EntryId = null,
    Guid? CategoryId = null);

/// <summary>
/// Reports deterministic local structure checks over one stored deck.
/// </summary>
public sealed record DeckValidationReport(
    Guid DeckId,
    long Revision,
    bool IsStructurallyValid,
    IReadOnlyList<DeckValidationIssue> Issues)
{
    /// <summary>
    /// Gets the immutable snapshot of deterministic structural findings.
    /// </summary>
    public IReadOnlyList<DeckValidationIssue> Issues { get; init; } =
        Array.AsReadOnly(Issues.ToArray());
}

/// <summary>
/// Describes one opaque backup without revealing its local filesystem location.
/// </summary>
public sealed record DeckBackup(
    Guid BackupId,
    int SchemaVersion,
    string Fingerprint,
    DateTimeOffset CreatedAtUtc,
    int DeckCount,
    bool IsRollback);

/// <summary>
/// Carries backup inventory together with the guarded current database fingerprint.
/// </summary>
public sealed record DeckBackupPage(
    string? CurrentDatabaseFingerprint,
    IReadOnlyList<DeckBackup> Items)
{
    /// <summary>
    /// Gets the immutable snapshot of opaque backup metadata.
    /// </summary>
    public IReadOnlyList<DeckBackup> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Reports a completed restore and the retained rollback backup.
/// </summary>
public sealed record DeckRestoreResult(
    Guid RestoredBackupId,
    Guid RollbackBackupId,
    string CurrentDatabaseFingerprint);

/// <summary>
/// Confirms deletion of a revision-guarded local entity or opaque backup.
/// </summary>
public sealed record DeckDeleteResult(
    Guid DeletedId,
    long? FinalRevision = null);
