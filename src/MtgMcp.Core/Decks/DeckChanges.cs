namespace MtgMcp.Core.Decks;

/// <summary>
/// Replaces caller-editable deck metadata without affecting entries or relationships.
/// </summary>
public sealed record UpdateDeckMetadataChange(
    string Name,
    string? Description,
    string Format);

/// <summary>
/// Adds one independently addressable entry without coalescing equivalent cards.
/// </summary>
public sealed record AddDeckEntryChange(DeckEntryDraft Entry);

/// <summary>
/// Replaces the editable fields of one entry identified by its stable ID.
/// </summary>
public sealed record UpdateDeckEntryChange(DeckEntry Entry);

/// <summary>
/// Names the printing a patch gives each entry: a printing ID, or a set code with a collector number.
/// </summary>
public sealed record DeckPrintingPatch(
    Guid? PrintingId = null,
    string? SetCode = null,
    string? CollectorNumber = null);

/// <summary>
/// Holds the printing a patch gives one entry after the card data lookup, so the printing matches that entry's card.
/// </summary>
public sealed record DeckEntryPrinting(
    Guid EntryId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string? Language);

/// <summary>
/// Changes only the listed fields of every listed entry, against each entry's state when the change runs. A field
/// that is null stays as it is. A new primary category removes the previous primary assignment unless the previous
/// primary is also in the added categories. A zone requires a zone categories mode, match or leave. Printings holds
/// the per-entry result of looking up Printing; an entry without a row there gets Printing as given.
/// </summary>
public sealed record PatchDeckEntriesChange(
    IReadOnlyList<Guid> EntryIds,
    int? Quantity = null,
    string? Zone = null,
    int? SortOrder = null,
    string? Finish = null,
    string? Language = null,
    DeckPrintingPatch? Printing = null,
    Guid? PrimaryCategoryId = null,
    IReadOnlyList<Guid>? AddCategoryIds = null,
    IReadOnlyList<Guid>? RemoveCategoryIds = null,
    string? ZoneCategories = null,
    IReadOnlyList<DeckEntryPrinting>? Printings = null);

/// <summary>
/// Removes one entry and its category assignments.
/// </summary>
public sealed record RemoveDeckEntryChange(Guid EntryId);

/// <summary>
/// Adds one functional category without changing any deck zone.
/// </summary>
public sealed record AddDeckCategoryChange(DeckCategoryDraft Category);

/// <summary>
/// Replaces the editable fields of one category identified by its stable ID.
/// </summary>
public sealed record UpdateDeckCategoryChange(DeckCategory Category);

/// <summary>
/// Removes one category and its assignments without deleting entries.
/// </summary>
public sealed record RemoveDeckCategoryChange(Guid CategoryId);

/// <summary>
/// Creates or updates one entry-to-category relationship.
/// </summary>
public sealed record AssignDeckCategoryChange(
    Guid EntryId,
    Guid CategoryId,
    bool IsPrimary);

/// <summary>
/// Removes one entry-to-category relationship.
/// </summary>
public sealed record UnassignDeckCategoryChange(
    Guid EntryId,
    Guid CategoryId);

/// <summary>
/// Creates or replaces one provider-neutral binding and optional canonical baseline.
/// </summary>
public sealed record UpsertDeckProviderBindingChange(
    DeckProviderBinding Binding,
    string? CanonicalBaseline);

/// <summary>
/// Removes one provider binding and its canonical baseline.
/// </summary>
public sealed record RemoveDeckProviderBindingChange(Guid BindingId);

/// <summary>
/// Represents every explicit local mutation accepted by the shared transactional path.
/// </summary>
public readonly union DeckChange(
    UpdateDeckMetadataChange,
    AddDeckEntryChange,
    UpdateDeckEntryChange,
    PatchDeckEntriesChange,
    RemoveDeckEntryChange,
    AddDeckCategoryChange,
    UpdateDeckCategoryChange,
    RemoveDeckCategoryChange,
    AssignDeckCategoryChange,
    UnassignDeckCategoryChange,
    UpsertDeckProviderBindingChange,
    RemoveDeckProviderBindingChange);
