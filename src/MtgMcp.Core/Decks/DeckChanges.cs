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
    RemoveDeckEntryChange,
    AddDeckCategoryChange,
    UpdateDeckCategoryChange,
    RemoveDeckCategoryChange,
    AssignDeckCategoryChange,
    UnassignDeckCategoryChange,
    UpsertDeckProviderBindingChange,
    RemoveDeckProviderBindingChange);
