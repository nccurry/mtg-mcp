using MtgMcp.Core.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Carries one explicitly discriminated batch mutation from MCP into the closed Core union.
/// </summary>
internal sealed record DeckChangeInput(
    string Kind,
    string? Name = null,
    string? Description = null,
    string? Format = null,
    DeckEntryDraft? EntryDraft = null,
    DeckEntry? Entry = null,
    Guid? EntryId = null,
    DeckCategoryDraft? CategoryDraft = null,
    DeckCategory? Category = null,
    Guid? CategoryId = null,
    bool IsPrimary = false,
    DeckProviderBinding? ProviderBinding = null,
    Guid? BindingId = null,
    string? CanonicalBaseline = null);

/// <summary>
/// Validates batch discriminators and constructs the single shared mutation vocabulary.
/// </summary>
internal static class DeckChangeInputMapper
{
    /// <summary>
    /// Maps all inputs or returns a stable invalid-input result without partial execution.
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
        foreach (DeckChangeInput input in inputs)
        {
            if (!TryMapOne(input, out DeckChange change))
            {
                changes = [];
                failureMessage = "A deck change is missing required fields or has an unknown kind.";
                return false;
            }

            mapped.Add(change);
        }

        changes = mapped.ToArray();
        failureMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Maps one exact discriminator without inferring intent from populated optional fields.
    /// </summary>
    private static bool TryMapOne(DeckChangeInput? input, out DeckChange change)
    {
        switch (input?.Kind.Trim().ToLowerInvariant())
        {
            case "update-metadata" when input.Name is not null && input.Format is not null:
                change = new UpdateDeckMetadataChange(input.Name, input.Description, input.Format);
                return true;
            case "add-entry" when input.EntryDraft is not null:
                change = new AddDeckEntryChange(input.EntryDraft);
                return true;
            case "update-entry" when input.Entry is not null:
                change = new UpdateDeckEntryChange(input.Entry);
                return true;
            case "remove-entry" when input.EntryId is not null:
                change = new RemoveDeckEntryChange(input.EntryId.Value);
                return true;
            case "add-category" when input.CategoryDraft is not null:
                change = new AddDeckCategoryChange(input.CategoryDraft);
                return true;
            case "update-category" when input.Category is not null:
                change = new UpdateDeckCategoryChange(input.Category);
                return true;
            case "remove-category" when input.CategoryId is not null:
                change = new RemoveDeckCategoryChange(input.CategoryId.Value);
                return true;
            case "assign-category" when input.EntryId is not null && input.CategoryId is not null:
                change = new AssignDeckCategoryChange(
                    input.EntryId.Value,
                    input.CategoryId.Value,
                    input.IsPrimary);
                return true;
            case "unassign-category" when input.EntryId is not null && input.CategoryId is not null:
                change = new UnassignDeckCategoryChange(input.EntryId.Value, input.CategoryId.Value);
                return true;
            case "upsert-provider-binding" when input.ProviderBinding is not null:
                change = new UpsertDeckProviderBindingChange(
                    input.ProviderBinding,
                    input.CanonicalBaseline);
                return true;
            case "remove-provider-binding" when input.BindingId is not null:
                change = new RemoveDeckProviderBindingChange(input.BindingId.Value);
                return true;
            default:
                change = default;
                return false;
        }
    }
}
