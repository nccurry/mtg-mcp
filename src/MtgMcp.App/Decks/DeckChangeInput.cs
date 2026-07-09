using System.ComponentModel;
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
    [property: Description("The complete replacement entry, including its existing entryId.")] DeckEntry Entry)
    : DeckChangeInput;

/// <summary>
/// Removes one entry by stable entry identifier.
/// </summary>
internal sealed record RemoveDeckEntryInput(
    [property: Description("The stable identifier of the entry to remove.")] Guid EntryId)
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
/// Assigns an existing category to an existing entry.
/// </summary>
internal sealed record AssignDeckCategoryInput(
    [property: Description("The stable identifier of the entry receiving the category.")] Guid EntryId,
    [property: Description("The stable identifier of the category to assign.")] Guid CategoryId,
    [property: Description("Whether this assignment becomes the entry's primary category.")] bool IsPrimary)
    : DeckChangeInput;

/// <summary>
/// Removes one category assignment from one entry.
/// </summary>
internal sealed record UnassignDeckCategoryInput(
    [property: Description("The stable identifier of the entry losing the category.")] Guid EntryId,
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
    /// Maps all inputs or returns an indexed invalid-input result without partial execution.
    /// </summary>
    internal static bool TryMap(
        IReadOnlyList<DeckChangeInput>? inputs,
        out IReadOnlyList<DeckChange> changes,
        out string failureMessage)
    {
        if (inputs is null || inputs.Count == 0)
        {
            changes = [];
            failureMessage = "At least one deck change is required.";
            return false;
        }

        List<DeckChange> mapped = new(inputs.Count);
        for (int index = 0; index < inputs.Count; index++)
        {
            if (!TryMapOne(inputs[index], out DeckChange change, out string kind, out string requiredFields))
            {
                changes = [];
                failureMessage = $"Deck change at index {index} with kind '{kind}' requires {requiredFields}.";
                return false;
            }

            mapped.Add(change);
        }

        changes = mapped.ToArray();
        failureMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Maps one closed variant after checking semantic requirements not represented by JSON Schema.
    /// </summary>
    private static bool TryMapOne(
        DeckChangeInput? input,
        out DeckChange change,
        out string kind,
        out string requiredFields)
    {
        switch (input)
        {
            case UpdateDeckMetadataInput metadata
                when !string.IsNullOrWhiteSpace(metadata.Name) &&
                     !string.IsNullOrWhiteSpace(metadata.Format):
                change = new UpdateDeckMetadataChange(metadata.Name, metadata.Description, metadata.Format);
                kind = "update-metadata";
                requiredFields = string.Empty;
                return true;
            case UpdateDeckMetadataInput:
                return Fail("update-metadata", "nonblank name and format", out change, out kind, out requiredFields);
            case AddDeckEntryInput { EntryDraft: not null } addEntry:
                change = new AddDeckEntryChange(addEntry.EntryDraft);
                kind = "add-entry";
                requiredFields = string.Empty;
                return true;
            case AddDeckEntryInput:
                return Fail("add-entry", "entryDraft", out change, out kind, out requiredFields);
            case UpdateDeckEntryInput { Entry: not null } updateEntry:
                change = new UpdateDeckEntryChange(updateEntry.Entry);
                kind = "update-entry";
                requiredFields = string.Empty;
                return true;
            case UpdateDeckEntryInput:
                return Fail("update-entry", "entry", out change, out kind, out requiredFields);
            case RemoveDeckEntryInput invalidRemoveEntry when invalidRemoveEntry.EntryId == Guid.Empty:
                return Fail("remove-entry", "a non-empty entryId", out change, out kind, out requiredFields);
            case RemoveDeckEntryInput removeEntry:
                change = new RemoveDeckEntryChange(removeEntry.EntryId);
                kind = "remove-entry";
                requiredFields = string.Empty;
                return true;
            case AddDeckCategoryInput { CategoryDraft: not null } addCategory:
                change = new AddDeckCategoryChange(addCategory.CategoryDraft);
                kind = "add-category";
                requiredFields = string.Empty;
                return true;
            case AddDeckCategoryInput:
                return Fail("add-category", "categoryDraft", out change, out kind, out requiredFields);
            case UpdateDeckCategoryInput { Category: not null } updateCategory:
                change = new UpdateDeckCategoryChange(updateCategory.Category);
                kind = "update-category";
                requiredFields = string.Empty;
                return true;
            case UpdateDeckCategoryInput:
                return Fail("update-category", "category", out change, out kind, out requiredFields);
            case RemoveDeckCategoryInput invalidRemoveCategory when invalidRemoveCategory.CategoryId == Guid.Empty:
                return Fail("remove-category", "a non-empty categoryId", out change, out kind, out requiredFields);
            case RemoveDeckCategoryInput removeCategory:
                change = new RemoveDeckCategoryChange(removeCategory.CategoryId);
                kind = "remove-category";
                requiredFields = string.Empty;
                return true;
            case AssignDeckCategoryInput assignment
                when assignment.EntryId != Guid.Empty && assignment.CategoryId != Guid.Empty:
                change = new AssignDeckCategoryChange(
                    assignment.EntryId,
                    assignment.CategoryId,
                    assignment.IsPrimary);
                kind = "assign-category";
                requiredFields = string.Empty;
                return true;
            case AssignDeckCategoryInput:
                return Fail(
                    "assign-category",
                    "non-empty entryId and categoryId",
                    out change,
                    out kind,
                    out requiredFields);
            case UnassignDeckCategoryInput assignment
                when assignment.EntryId != Guid.Empty && assignment.CategoryId != Guid.Empty:
                change = new UnassignDeckCategoryChange(assignment.EntryId, assignment.CategoryId);
                kind = "unassign-category";
                requiredFields = string.Empty;
                return true;
            case UnassignDeckCategoryInput:
                return Fail(
                    "unassign-category",
                    "non-empty entryId and categoryId",
                    out change,
                    out kind,
                    out requiredFields);
            case UpsertDeckProviderBindingInput { ProviderBinding: not null } binding:
                change = new UpsertDeckProviderBindingChange(binding.ProviderBinding, binding.CanonicalBaseline);
                kind = "upsert-provider-binding";
                requiredFields = string.Empty;
                return true;
            case UpsertDeckProviderBindingInput:
                return Fail(
                    "upsert-provider-binding",
                    "providerBinding",
                    out change,
                    out kind,
                    out requiredFields);
            case RemoveDeckProviderBindingInput invalidRemoveBinding when invalidRemoveBinding.BindingId == Guid.Empty:
                return Fail(
                    "remove-provider-binding",
                    "a non-empty bindingId",
                    out change,
                    out kind,
                    out requiredFields);
            case RemoveDeckProviderBindingInput removeBinding:
                change = new RemoveDeckProviderBindingChange(removeBinding.BindingId);
                kind = "remove-provider-binding";
                requiredFields = string.Empty;
                return true;
            case null:
                return Fail("null", "a supported non-null change", out change, out kind, out requiredFields);
            default:
                return Fail("unsupported", "a supported change kind", out change, out kind, out requiredFields);
        }
    }

    /// <summary>
    /// Initializes one bounded semantic-validation failure.
    /// </summary>
    private static bool Fail(
        string failureKind,
        string failureFields,
        out DeckChange change,
        out string kind,
        out string requiredFields)
    {
        change = default;
        kind = failureKind;
        requiredFields = failureFields;
        return false;
    }
}
