using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies MCP batch inputs map deterministically into the closed Core mutation union.
/// </summary>
public sealed class DeckChangeInputTests
{
    /// <summary>
    /// Verifies every supported discriminator maps without inspecting unrelated optional fields.
    /// </summary>
    [Fact]
    public void TryMap_AllDiscriminators_ProduceExpectedCases()
    {
        Guid id = Guid.CreateVersion7();
        DeckEntry entry = new(id, 1, "Card", null, null, null, null, "en", "nonfoil", "main", 0);
        DeckCategory category = new(id, "Ramp", null, 0);
        DeckProviderBinding binding = new(id, "archidekt", "42", null, null, null, null, null);
        DeckChangeInput[] inputs =
        [
            new("update-metadata", Name: "Deck", Format: "commander"),
            new("add-entry", EntryDraft: new DeckEntryDraft(1, "Card")),
            new("update-entry", Entry: entry),
            new("remove-entry", EntryId: id),
            new("add-category", CategoryDraft: new DeckCategoryDraft("Ramp")),
            new("update-category", Category: category),
            new("remove-category", CategoryId: id),
            new("assign-category", EntryId: id, CategoryId: id, IsPrimary: true),
            new("unassign-category", EntryId: id, CategoryId: id),
            new("upsert-provider-binding", ProviderBinding: binding, CanonicalBaseline: "{}"),
            new("remove-provider-binding", BindingId: id),
        ];

        bool success = DeckChangeInputMapper.TryMap(inputs, out IReadOnlyList<DeckChange> changes, out string failure);

        Assert.True(success);
        Assert.Equal(string.Empty, failure);
        Assert.Collection(
            changes,
            value => Assert.IsType<UpdateDeckMetadataChange>(value.Value),
            value => Assert.IsType<AddDeckEntryChange>(value.Value),
            value => Assert.IsType<UpdateDeckEntryChange>(value.Value),
            value => Assert.IsType<RemoveDeckEntryChange>(value.Value),
            value => Assert.IsType<AddDeckCategoryChange>(value.Value),
            value => Assert.IsType<UpdateDeckCategoryChange>(value.Value),
            value => Assert.IsType<RemoveDeckCategoryChange>(value.Value),
            value => Assert.IsType<AssignDeckCategoryChange>(value.Value),
            value => Assert.IsType<UnassignDeckCategoryChange>(value.Value),
            value => Assert.IsType<UpsertDeckProviderBindingChange>(value.Value),
            value => Assert.IsType<RemoveDeckProviderBindingChange>(value.Value));
    }

    /// <summary>
    /// Verifies empty, unknown, and incomplete changes fail before any transaction can begin.
    /// </summary>
    [Fact]
    public void TryMap_InvalidInputs_ReturnBoundedFailure()
    {
        Assert.False(DeckChangeInputMapper.TryMap(null, out IReadOnlyList<DeckChange> nullChanges, out string nullFailure));
        Assert.Empty(nullChanges);
        Assert.NotEmpty(nullFailure);

        Assert.False(DeckChangeInputMapper.TryMap(
            [new DeckChangeInput("future-change")],
            out IReadOnlyList<DeckChange> unknownChanges,
            out string unknownFailure));
        Assert.Empty(unknownChanges);
        Assert.NotEmpty(unknownFailure);

        Assert.False(DeckChangeInputMapper.TryMap(
            [new DeckChangeInput("add-entry")],
            out IReadOnlyList<DeckChange> incompleteChanges,
            out string incompleteFailure));
        Assert.Empty(incompleteChanges);
        Assert.NotEmpty(incompleteFailure);
    }
}
