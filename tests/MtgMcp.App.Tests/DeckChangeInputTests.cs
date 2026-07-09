using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies MCP batch inputs map deterministically into the closed Core mutation union.
/// </summary>
public sealed class DeckChangeInputTests
{
    /// <summary>
    /// Verifies every supported variant maps to its exact Core mutation case.
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
            new UpdateDeckMetadataInput("Deck", null, "commander"),
            new AddDeckEntryInput(new DeckEntryDraft(1, "Card")),
            new UpdateDeckEntryInput(entry),
            new RemoveDeckEntryInput(id),
            new AddDeckCategoryInput(new DeckCategoryDraft("Ramp")),
            new UpdateDeckCategoryInput(category),
            new RemoveDeckCategoryInput(id),
            new AssignDeckCategoryInput(id, id, true),
            new UnassignDeckCategoryInput(id, id),
            new UpsertDeckProviderBindingInput(binding, "{}"),
            new RemoveDeckProviderBindingInput(id),
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
    /// Verifies empty, unsupported, and semantically incomplete changes fail before execution.
    /// </summary>
    [Fact]
    public void TryMap_InvalidInputs_ReturnBoundedFailure()
    {
        Assert.False(DeckChangeInputMapper.TryMap(null, out IReadOnlyList<DeckChange> nullChanges, out string nullFailure));
        Assert.Empty(nullChanges);
        Assert.NotEmpty(nullFailure);

        Assert.False(DeckChangeInputMapper.TryMap(
            [new UnsupportedDeckChangeInput()],
            out IReadOnlyList<DeckChange> unknownChanges,
            out string unknownFailure));
        Assert.Empty(unknownChanges);
        Assert.Equal(
            "Deck change at index 0 with kind 'unsupported' requires a supported change kind.",
            unknownFailure);

        Assert.False(DeckChangeInputMapper.TryMap(
            [new RemoveDeckEntryInput(Guid.Empty)],
            out IReadOnlyList<DeckChange> incompleteChanges,
            out string incompleteFailure));
        Assert.Empty(incompleteChanges);
        Assert.Equal(
            "Deck change at index 0 with kind 'remove-entry' requires a non-empty entryId.",
            incompleteFailure);
    }

    /// <summary>
    /// Represents a future union case so the mapper's defensive failure remains covered.
    /// </summary>
    private sealed record UnsupportedDeckChangeInput : DeckChangeInput;
}
