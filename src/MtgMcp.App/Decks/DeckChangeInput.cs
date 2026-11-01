using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Defines the closed set of explicitly discriminated batch deck mutations accepted over MCP.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UpdateDeckMetadataInput), "update-metadata")]
[JsonDerivedType(typeof(AddDeckEntryInput), "add-entry")]
[JsonDerivedType(typeof(UpdateDeckEntryInput), "update-entry")]
[JsonDerivedType(typeof(PatchDeckEntriesInput), "patch-entries")]
[JsonDerivedType(typeof(RemoveDeckEntryInput), "remove-entry")]
[JsonDerivedType(typeof(AddDeckCategoryInput), "add-category")]
[JsonDerivedType(typeof(UpdateDeckCategoryInput), "update-category")]
[JsonDerivedType(typeof(RemoveDeckCategoryInput), "remove-category")]
[JsonDerivedType(typeof(AssignDeckCategoryInput), "assign-category")]
[JsonDerivedType(typeof(UnassignDeckCategoryInput), "unassign-category")]
[JsonDerivedType(typeof(UpsertDeckProviderBindingInput), "upsert-provider-binding")]
[JsonDerivedType(typeof(RemoveDeckProviderBindingInput), "remove-provider-binding")]
internal abstract record DeckChangeInput;

/// <summary>
/// Changes deck-level metadata without altering entries, categories, or provider bindings.
/// </summary>
internal sealed record UpdateDeckMetadataInput(
    [property: Description("The complete replacement deck name.")] string Name,
    [property: Description("The complete replacement description, or null to clear it.")] string? Description,
    [property: Description("The complete replacement format label; mtg-mcp does not enforce format legality.")] string Format)
    : DeckChangeInput;

/// <summary>
/// Adds one new entry from an explicit draft.
/// </summary>
internal sealed record AddDeckEntryInput(
    [property: Description("The complete draft for the new deck entry.")] DeckEntryDraft EntryDraft)
    : DeckChangeInput;

/// <summary>
/// Replaces one existing entry by stable entry identifier.
/// </summary>
internal sealed record UpdateDeckEntryInput(
    [property: Description(
        "The complete replacement entry, including its existing entryId. Send only the stored entry fields " +
        "(entryId, quantity, cardName, oracleId, printingId, setCode, collectorNumber, language, finish, zone, " +
        "sortOrder); drop cardData, card, and tags, which local_deck_get adds to entries when you pass include. " +
        "To change only some fields of one or many entries, use patch-entries instead.")]
    DeckEntry Entry)
    : DeckChangeInput;

/// <summary>
/// Patches the listed fields of 1 to 100 entries at once, without restating the rest of each entry.
/// </summary>
internal sealed record PatchDeckEntriesInput(
    [property: Description(
        "The entries to patch (1 to 100 distinct entry IDs, including entries added earlier in this batch with an " +
        "entryId). Every listed entry gets the same patch.")] IReadOnlyList<Guid> EntryIds,
    [property: Description("The new quantity, or omit to keep each entry's quantity.")] int? Quantity = null,
    [property: Description(
        "The new zone (main, commander, sideboard, maybeboard, or another code). Requires zoneCategories.")]
    string? Zone = null,
    [property: Description("The new sort order, or omit to keep each entry's sort order.")] int? SortOrder = null,
    [property: Description("The new finish, or omit to keep each entry's finish.")] string? Finish = null,
    [property: Description("The new language code, or omit to keep each entry's language.")] string? Language = null,
    [property: Description(
        "A new printing, resolved against each entry's own card like add-entry: a printingId, or a setCode with a " +
        "collectorNumber. Omit to keep each entry's printing.")] DeckPrintingPatch? Printing = null,
    [property: Description(
        "The category that becomes each entry's primary category. A category the entry already has as non-primary " +
        "becomes primary. The previous primary assignment is removed unless it is also in addCategoryIds.")]
    Guid? PrimaryCategoryId = null,
    [property: Description("Categories to assign as non-primary; one the entry already has is left as it is.")]
    IReadOnlyList<Guid>? AddCategoryIds = null,
    [property: Description("Categories to unassign from each entry.")] IReadOnlyList<Guid>? RemoveCategoryIds = null,
    [property: Description(
        "Required whenever zone is set. match: the zone's category (Commander, Sideboard, or Maybeboard; created " +
        "if the deck lacks it) becomes each entry's primary category and the entry's other zone categories are " +
        "removed; for zone main, zone categories are removed and a lost primary needs primaryCategoryId or an " +
        "entry with no other category. leave: only the zone changes."),
     AllowedValues(DeckZoneCategories.Match, DeckZoneCategories.Leave)]
    string? ZoneCategories = null)
    : DeckChangeInput;

/// <summary>
/// Removes one entry, or 1 to 100 entries, by stable entry identifier.
/// </summary>
internal sealed record RemoveDeckEntryInput(
    [property: Description("The stable identifier of the entry to remove; give this or entryIds, not both.")]
    Guid? EntryId = null,
    [property: Description(
        "The stable identifiers of 1 to 100 distinct entries to remove; give this or entryId, not both.")]
    IReadOnlyList<Guid>? EntryIds = null)
    : DeckChangeInput;

/// <summary>
/// Adds one new ordered deck category from an explicit draft.
/// </summary>
internal sealed record AddDeckCategoryInput(
    [property: Description("The complete draft for the new deck category.")] DeckCategoryDraft CategoryDraft)
    : DeckChangeInput;

/// <summary>
/// Replaces one existing category by stable category identifier.
/// </summary>
internal sealed record UpdateDeckCategoryInput(
    [property: Description("The complete replacement category, including its existing categoryId.")] DeckCategory Category)
    : DeckChangeInput;

/// <summary>
/// Removes one category and its assignments by stable category identifier.
/// </summary>
internal sealed record RemoveDeckCategoryInput(
    [property: Description("The stable identifier of the category to remove.")] Guid CategoryId)
    : DeckChangeInput;

/// <summary>
/// Assigns an existing category to an existing entry. To edit the categories of many entries at once, use
/// patch-entries.
/// </summary>
internal sealed record AssignDeckCategoryInput(
    [property: Description(
        "The stable identifier of the entry receiving the category; to edit the categories of many entries at " +
        "once, use patch-entries instead.")] Guid EntryId,
    [property: Description("The stable identifier of the category to assign.")] Guid CategoryId,
    [property: Description(
        "Whether this assignment becomes the entry's primary category; a previous primary category stays assigned " +
        "as non-primary.")] bool IsPrimary)
    : DeckChangeInput;

/// <summary>
/// Removes one category assignment from one entry. To edit the categories of many entries at once, use
/// patch-entries.
/// </summary>
internal sealed record UnassignDeckCategoryInput(
    [property: Description(
        "The stable identifier of the entry losing the category; to edit the categories of many entries at " +
        "once, use patch-entries instead.")] Guid EntryId,
    [property: Description("The stable identifier of the category to unassign.")] Guid CategoryId)
    : DeckChangeInput;

/// <summary>
/// Creates or replaces one explicit provider binding and its optional canonical baseline.
/// </summary>
internal sealed record UpsertDeckProviderBindingInput(
    [property: Description("The complete provider binding to create or replace.")] DeckProviderBinding ProviderBinding,
    [property: Description("The provider-specific canonical baseline, or null when no baseline is retained.")] string? CanonicalBaseline)
    : DeckChangeInput;

/// <summary>
/// Removes one provider binding by stable binding identifier.
/// </summary>
internal sealed record RemoveDeckProviderBindingInput(
    [property: Description("The stable identifier of the provider binding to remove.")] Guid BindingId)
    : DeckChangeInput;

/// <summary>
/// Validates batch variants and constructs the single shared mutation vocabulary.
/// </summary>
internal static class DeckChangeInputMapper
{
    /// <summary>
    /// Maps all inputs or returns an indexed invalid-input result without partial execution. A remove-entry with
    /// entryIds maps to one change per ID, so callerIndexes gives, for each mapped change, the position of the input
    /// the caller sent it in.
    /// </summary>
    internal static bool TryMap(
        IReadOnlyList<DeckChangeInput>? inputs,
        out IReadOnlyList<DeckChange> changes,
        out IReadOnlyList<int> callerIndexes,
        out string failureMessage)
    {
        changes = [];
        callerIndexes = [];
        if (inputs is null || inputs.Count == 0)
        {
            failureMessage = "At least one deck change is required.";
            return false;
        }

        List<DeckChange> mapped = new(inputs.Count);
        List<int> origins = new(inputs.Count);
        for (int index = 0; index < inputs.Count; index++)
        {
            ChangeFailure failure;
            if (inputs[index] is RemoveDeckEntryInput removal)
            {
                failure = MapRemoval(removal, mapped);
            }
            else if (TryMapOne(inputs[index], out DeckChange change, out failure))
            {
                mapped.Add(change);
            }

            if (failure.Kind is not null)
            {
                failureMessage = $"Deck change at index {index} with kind '{failure.Kind}' {failure.Detail}";
                return false;
            }

            while (origins.Count < mapped.Count)
            {
                origins.Add(index);
            }
        }

        changes = mapped.ToArray();
        callerIndexes = origins;
        failureMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Maps a removal of one entry, or of 1 to 100 distinct entries, into one remove change per entry. Returns no
    /// failure on success, or what the removal requires when exactly one of the two fields is not given or an ID is
    /// unusable.
    /// </summary>
    private static ChangeFailure MapRemoval(RemoveDeckEntryInput input, List<DeckChange> mapped)
    {
        if ((input.EntryId is null) == (input.EntryIds is null))
        {
            return Requires("remove-entry", "exactly one of entryId or entryIds");
        }

        if (input.EntryId == Guid.Empty)
        {
            return Requires("remove-entry", "a non-empty entryId");
        }

        IReadOnlyList<Guid> ids = input.EntryIds ?? [input.EntryId!.Value];
        if (PatchDeckEntriesRules.EntryIdsProblem(ids) is not null)
        {
            return Requires(
                "remove-entry",
                $"entryIds with 1 to {PatchDeckEntriesRules.MaxEntries} distinct, non-empty IDs");
        }

        foreach (Guid id in ids)
        {
            mapped.Add(new RemoveDeckEntryChange(id));
        }

        return default;
    }

    /// <summary>
    /// Maps one closed variant after checking semantic requirements not represented by JSON Schema.
    /// </summary>
    private static bool TryMapOne(
        DeckChangeInput? input,
        out DeckChange change,
        out ChangeFailure failure)
    {
        switch (input)
        {
            case UpdateDeckMetadataInput metadata
                when !string.IsNullOrWhiteSpace(metadata.Name) &&
                     !string.IsNullOrWhiteSpace(metadata.Format):
                change = new UpdateDeckMetadataChange(metadata.Name, metadata.Description, metadata.Format);
                failure = default;
                return true;
            case UpdateDeckMetadataInput:
                return Fail("update-metadata", "nonblank name and format", out change, out failure);
            case AddDeckEntryInput { EntryDraft: not null } addEntry:
                change = new AddDeckEntryChange(addEntry.EntryDraft);
                failure = default;
                return true;
            case AddDeckEntryInput:
                return Fail("add-entry", "entryDraft", out change, out failure);
            case UpdateDeckEntryInput { Entry: not null } updateEntry:
                change = new UpdateDeckEntryChange(updateEntry.Entry);
                failure = default;
                return true;
            case UpdateDeckEntryInput:
                return Fail("update-entry", "entry", out change, out failure);
            case PatchDeckEntriesInput patch:
                PatchDeckEntriesChange patchChange = new(
                    patch.EntryIds ?? [],
                    patch.Quantity,
                    patch.Zone,
                    patch.SortOrder,
                    patch.Finish,
                    patch.Language,
                    patch.Printing,
                    patch.PrimaryCategoryId,
                    patch.AddCategoryIds,
                    patch.RemoveCategoryIds,
                    patch.ZoneCategories);
                if (PatchDeckEntriesRules.FirstProblem(patchChange) is string problem)
                {
                    return FailInvalid("patch-entries", problem, out change, out failure);
                }

                change = patchChange;
                failure = default;
                return true;
            case AddDeckCategoryInput { CategoryDraft: not null } addCategory:
                change = new AddDeckCategoryChange(addCategory.CategoryDraft);
                failure = default;
                return true;
            case AddDeckCategoryInput:
                return Fail("add-category", "categoryDraft", out change, out failure);
            case UpdateDeckCategoryInput { Category: not null } updateCategory:
                change = new UpdateDeckCategoryChange(updateCategory.Category);
                failure = default;
                return true;
            case UpdateDeckCategoryInput:
                return Fail("update-category", "category", out change, out failure);
            case RemoveDeckCategoryInput invalidRemoveCategory when invalidRemoveCategory.CategoryId == Guid.Empty:
                return Fail("remove-category", "a non-empty categoryId", out change, out failure);
            case RemoveDeckCategoryInput removeCategory:
                change = new RemoveDeckCategoryChange(removeCategory.CategoryId);
                failure = default;
                return true;
            case AssignDeckCategoryInput assignment
                when assignment.EntryId != Guid.Empty && assignment.CategoryId != Guid.Empty:
                change = new AssignDeckCategoryChange(
                    assignment.EntryId,
                    assignment.CategoryId,
                    assignment.IsPrimary);
                failure = default;
                return true;
            case AssignDeckCategoryInput:
                return Fail(
                    "assign-category",
                    "non-empty entryId and categoryId",
                    out change, out failure);
            case UnassignDeckCategoryInput assignment
                when assignment.EntryId != Guid.Empty && assignment.CategoryId != Guid.Empty:
                change = new UnassignDeckCategoryChange(assignment.EntryId, assignment.CategoryId);
                failure = default;
                return true;
            case UnassignDeckCategoryInput:
                return Fail(
                    "unassign-category",
                    "non-empty entryId and categoryId",
                    out change, out failure);
            case UpsertDeckProviderBindingInput { ProviderBinding: not null } binding:
                change = new UpsertDeckProviderBindingChange(binding.ProviderBinding, binding.CanonicalBaseline);
                failure = default;
                return true;
            case UpsertDeckProviderBindingInput:
                return Fail(
                    "upsert-provider-binding",
                    "providerBinding",
                    out change, out failure);
            case RemoveDeckProviderBindingInput invalidRemoveBinding when invalidRemoveBinding.BindingId == Guid.Empty:
                return Fail(
                    "remove-provider-binding",
                    "a non-empty bindingId",
                    out change, out failure);
            case RemoveDeckProviderBindingInput removeBinding:
                change = new RemoveDeckProviderBindingChange(removeBinding.BindingId);
                failure = default;
                return true;
            case null:
                return Fail("null", "a supported non-null change", out change, out failure);
            default:
                return Fail("unsupported", "a supported change kind", out change, out failure);
        }
    }

    /// <summary>
    /// Initializes one bounded semantic-validation failure that names the fields the change requires.
    /// </summary>
    private static bool Fail(string kind, string fields, out DeckChange change, out ChangeFailure failure)
    {
        change = default;
        failure = Requires(kind, fields);
        return false;
    }

    /// <summary>
    /// Initializes one semantic-validation failure that gives the problem a rule found.
    /// </summary>
    private static bool FailInvalid(string kind, string problem, out DeckChange change, out ChangeFailure failure)
    {
        change = default;
        failure = new ChangeFailure(kind, $"is invalid: {problem}");
        return false;
    }

    /// <summary>
    /// Builds the failure of a change that lacks the fields it requires.
    /// </summary>
    private static ChangeFailure Requires(string kind, string fields)
    {
        return new ChangeFailure(kind, $"requires {fields}.");
    }

    /// <summary>
    /// Holds why one input could not be mapped: its kind, and the rest of the message after the kind. The kind is
    /// null when there was no failure.
    /// </summary>
    private readonly record struct ChangeFailure(string? Kind, string Detail);
}
