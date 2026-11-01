using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the batch edit tool covers every deck edit that the removed single-step tools used to make, and enforces
/// mode authority.
/// </summary>
public sealed class DeckBatchWriteToolsTests
{
    /// <summary>
    /// Verifies a single update-metadata change replaces the name, description, and format (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UpdateMetadata_ReplacesDeckFields()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(
            new UpdateDeckMetadataInput("Renamed", "Description", "future-format"));

        Assert.Equal(("Renamed", "Description", "future-format"), (deck.Name, deck.Description, deck.Format));
        Assert.Equal(fixture.Deck.Revision + 1, deck.Revision);
    }

    /// <summary>
    /// Verifies a single add-entry change adds an independently addressable entry (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AddEntry_AddsEntryWithoutMergingEquivalentCards()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(new AddDeckEntryInput(new DeckEntryDraft(1, "Card")));

        Assert.Equal(["Card", "Card"], deck.Entries.Select(entry => entry.CardName));
    }

    /// <summary>
    /// Verifies a single update-entry change replaces quantity, finish, and zone (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UpdateEntry_ReplacesEntryFields()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        DeckEntry updated = Assert.Single(fixture.Deck.Entries) with
        {
            Quantity = 2,
            Finish = "foil",
            Zone = "sideboard",
        };

        DeckDocument deck = await fixture.ApplyAsync(new UpdateDeckEntryInput(updated));

        Assert.Equal(updated, Assert.Single(deck.Entries));
    }

    /// <summary>
    /// Verifies a single remove-entry change removes the entry and its category assignments (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_RemoveEntry_RemovesEntryAndItsAssignments()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true);

        DeckDocument deck = await fixture.ApplyAsync(new RemoveDeckEntryInput(fixture.EntryId));

        Assert.Empty(deck.Entries);
        Assert.Empty(deck.CategoryAssignments);
        Assert.Single(deck.Categories);
    }

    /// <summary>
    /// Verifies a single add-category change adds a category without changing entries (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AddCategory_AddsCategory()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withCategory: false);
        Guid categoryId = Guid.CreateVersion7();

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckCategoryInput(new DeckCategoryDraft("Draw", "#123456", 7, categoryId)));

        DeckCategory category = Assert.Single(deck.Categories);
        Assert.Equal((categoryId, "Draw", "#123456"), (category.CategoryId, category.Name, category.Color));
        Assert.Single(deck.Entries);
    }

    /// <summary>
    /// Verifies a single update-category change replaces the category name (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UpdateCategory_ReplacesCategoryFields()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        DeckCategory updated = Assert.Single(fixture.Deck.Categories) with { Name = "Mana" };

        DeckDocument deck = await fixture.ApplyAsync(new UpdateDeckCategoryInput(updated));

        Assert.Equal("Mana", Assert.Single(deck.Categories).Name);
    }

    /// <summary>
    /// Verifies a single remove-category change removes the category and its assignments but keeps entries
    /// (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_RemoveCategory_KeepsEntries()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true);

        DeckDocument deck = await fixture.ApplyAsync(new RemoveDeckCategoryInput(fixture.CategoryId));

        Assert.Empty(deck.Categories);
        Assert.Empty(deck.CategoryAssignments);
        Assert.Single(deck.Entries);
    }

    /// <summary>
    /// Verifies a single assign-category change adds a primary assignment (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AssignCategory_AddsPrimaryAssignment()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(
            new AssignDeckCategoryInput(fixture.EntryId, fixture.CategoryId, true));

        Assert.Equal(
            new DeckCategoryAssignment(fixture.EntryId, fixture.CategoryId, true),
            Assert.Single(deck.CategoryAssignments));
    }

    /// <summary>
    /// Verifies assigning a new primary category to an entry that already has one makes the new category primary and
    /// keeps the old one assigned as non-primary, in the same batch as an entry update (PRE-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AssignNewPrimaryCategory_DemotesThePreviousPrimary()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true, assignmentIsPrimary: true);
        Guid drawId = Guid.CreateVersion7();
        DeckEntry updated = Assert.Single(fixture.Deck.Entries) with { Quantity = 3 };

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckCategoryInput(new DeckCategoryDraft("Draw", CategoryId: drawId)),
            new UpdateDeckEntryInput(updated),
            new AssignDeckCategoryInput(fixture.EntryId, drawId, true));

        Assert.Equal(3, Assert.Single(deck.Entries).Quantity);
        Assert.Equal(2, deck.CategoryAssignments.Count);
        Assert.True(deck.CategoryAssignments.Single(value => value.CategoryId == drawId).IsPrimary);
        Assert.False(deck.CategoryAssignments.Single(value => value.CategoryId == fixture.CategoryId).IsPrimary);
    }

    /// <summary>
    /// Verifies a batch that makes two different categories primary for one entry is still refused (PRE-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_TwoPrimaryAssignmentsInOneBatch_AreRefused()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        Guid drawId = Guid.CreateVersion7();

        OperationResult<DeckWriteResult> result = await fixture.TryApplyAsync(
            new AddDeckCategoryInput(new DeckCategoryDraft("Draw", CategoryId: drawId)),
            new AssignDeckCategoryInput(fixture.EntryId, fixture.CategoryId, true),
            new AssignDeckCategoryInput(fixture.EntryId, drawId, true));

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Contains("only one primary category", invalid.Message, StringComparison.Ordinal);
        DeckDocument after = await fixture.ReadAsync();
        Assert.Equal(fixture.Deck.Revision, after.Revision);
        Assert.DoesNotContain(after.Categories, value => value.CategoryId == drawId);
        Assert.Equal(fixture.Deck.CategoryAssignments, after.CategoryAssignments);
    }

    /// <summary>
    /// Verifies a batch that assigns a primary, unassigns it, and then assigns another primary is accepted, because the
    /// entry never has two primary categories at once (PRE-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_PrimaryAssignedUnassignedAndReplaced_Succeeds()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        Guid drawId = Guid.CreateVersion7();

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckCategoryInput(new DeckCategoryDraft("Draw", CategoryId: drawId)),
            new AssignDeckCategoryInput(fixture.EntryId, fixture.CategoryId, true),
            new UnassignDeckCategoryInput(fixture.EntryId, fixture.CategoryId),
            new AssignDeckCategoryInput(fixture.EntryId, drawId, true));

        Assert.Equal(
            new DeckCategoryAssignment(fixture.EntryId, drawId, true),
            Assert.Single(deck.CategoryAssignments));
    }

    /// <summary>
    /// Verifies a batch that assigns a primary, then assigns the same category as non-primary or removes it, and then
    /// assigns another primary is accepted, because the entry never has two primary categories at once (PRE-005).
    /// </summary>
    [Theory]
    [InlineData("non-primary")]
    [InlineData("remove-category")]
    public async Task ApplyChanges_PrimaryReleasedThenReplaced_Succeeds(string release)
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        Guid drawId = Guid.CreateVersion7();
        DeckChangeInput released = release == "non-primary"
            ? new AssignDeckCategoryInput(fixture.EntryId, fixture.CategoryId, false)
            : new RemoveDeckCategoryInput(fixture.CategoryId);

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckCategoryInput(new DeckCategoryDraft("Draw", CategoryId: drawId)),
            new AssignDeckCategoryInput(fixture.EntryId, fixture.CategoryId, true),
            released,
            new AssignDeckCategoryInput(fixture.EntryId, drawId, true));

        Assert.True(deck.CategoryAssignments.Single(value => value.CategoryId == drawId).IsPrimary);
        Assert.DoesNotContain(deck.CategoryAssignments, value => value.CategoryId != drawId && value.IsPrimary);
    }

    /// <summary>
    /// Verifies a single unassign-category change removes the assignment but keeps entry and category (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UnassignCategory_KeepsEntryAndCategory()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true);

        DeckDocument deck = await fixture.ApplyAsync(
            new UnassignDeckCategoryInput(fixture.EntryId, fixture.CategoryId));

        Assert.Empty(deck.CategoryAssignments);
        Assert.Single(deck.Entries);
        Assert.Single(deck.Categories);
    }

    /// <summary>
    /// Verifies one batch of several changes applies in one revision.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_SeveralChanges_ApplyInOneRevision()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckEntryInput(new DeckEntryDraft(1, "Second")),
            new UpdateDeckMetadataInput("Batch", null, "custom"));

        Assert.Equal(2, deck.Entries.Count);
        Assert.Equal("Batch", deck.Name);
        Assert.Equal(fixture.Deck.Revision + 1, deck.Revision);
    }

    /// <summary>
    /// Verifies the batch tool refuses to run in read-only mode.
    /// </summary>
    [Fact]
    public async Task ReadOnlyMode_BatchInvocationReturnsUnsupported()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using UninstalledCardData cardData = new();
        DeckBatchWriteTools tools = new(store, OperationMode.ReadOnly, cardData.Resolver);

        OperationResult<DeckWriteResult> result = await tools.ApplyChangesAsync(
            Guid.CreateVersion7(),
            1,
            [new UpdateDeckMetadataInput("Denied", null, "custom")],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("operation-mode-denied", Assert.IsType<OperationUnsupported>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Applies changes to one deck through the batch tool and returns the new deck state.
    /// </summary>
    private static async Task<DeckDocument> ApplyAsync(
        DeckBatchWriteTools tools,
        DeckDocument deck,
        params DeckChangeInput[] changes)
    {
        return RequireSuccess(await tools.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            changes,
            returnDeck: true,
            cancellationToken: TestContext.Current.CancellationToken)).Deck!;
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Holds a temporary store with one deck, one entry, and optionally one category for single-change tests.
    /// </summary>
    private sealed class BatchFixture : IDisposable
    {
        /// <summary>
        /// Owns the folder that holds the deck database.
        /// </summary>
        private readonly TemporaryDirectory directory;

        /// <summary>
        /// Owns the deck store behind the batch tool.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Gives the batch tool an entry resolver over card data that is not installed.
        /// </summary>
        private readonly UninstalledCardData cardData = new();

        /// <summary>
        /// Applies changes through the batch tool.
        /// </summary>
        private readonly DeckBatchWriteTools tools;

        /// <summary>
        /// Wraps the created deck and the store that holds it.
        /// </summary>
        private BatchFixture(
            TemporaryDirectory directory,
            SqliteDeckStore store,
            DeckDocument deck,
            Guid entryId,
            Guid categoryId)
        {
            this.directory = directory;
            this.store = store;
            tools = new DeckBatchWriteTools(store, OperationMode.Local, cardData.Resolver);
            Deck = deck;
            EntryId = entryId;
            CategoryId = categoryId;
        }

        /// <summary>
        /// Gets the deck as created, before any change.
        /// </summary>
        internal DeckDocument Deck { get; }

        /// <summary>
        /// Gets the stable ID of the deck's one entry.
        /// </summary>
        internal Guid EntryId { get; }

        /// <summary>
        /// Gets the stable ID of the category, which exists only when the fixture was created with one.
        /// </summary>
        internal Guid CategoryId { get; }

        /// <summary>
        /// Creates a deck with one entry, optionally with one category and an assignment of the entry to it, which is
        /// the entry's primary category when asked.
        /// </summary>
        internal static async Task<BatchFixture> CreateAsync(
            bool withCategory = true,
            bool withAssignment = false,
            bool assignmentIsPrimary = false)
        {
            TemporaryDirectory directory = new();
            SqliteDeckStore store = new(directory.Path, "0.9.0-preview.1");
            Guid entryId = Guid.CreateVersion7();
            Guid categoryId = Guid.CreateVersion7();
            DeckCreateRequest request = new(
                "Fixture",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "Card", EntryId: entryId)],
                Categories: withCategory ? [new DeckCategoryDraft("Ramp", CategoryId: categoryId)] : null,
                CategoryAssignments: withCategory && withAssignment
                    ? [new DeckCategoryAssignment(entryId, categoryId, assignmentIsPrimary)]
                    : null);
            DeckDocument deck = RequireSuccess(
                await store.CreateAsync(request, TestContext.Current.CancellationToken));
            return new BatchFixture(directory, store, deck, entryId, categoryId);
        }

        /// <summary>
        /// Applies one batch to the fixture deck at its created revision.
        /// </summary>
        internal Task<DeckDocument> ApplyAsync(params DeckChangeInput[] changes)
        {
            return DeckBatchWriteToolsTests.ApplyAsync(tools, Deck, changes);
        }

        /// <summary>
        /// Applies one batch and returns the outcome without requiring success.
        /// </summary>
        internal async Task<OperationResult<DeckWriteResult>> TryApplyAsync(params DeckChangeInput[] changes)
        {
            return await tools.ApplyChangesAsync(
                Deck.DeckId,
                Deck.Revision,
                changes,
                returnDeck: true,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// Reads the deck as it is stored now.
        /// </summary>
        internal async Task<DeckDocument> ReadAsync()
        {
            return RequireSuccess(await store.GetAsync(Deck.DeckId, TestContext.Current.CancellationToken));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            cardData.Dispose();
            store.Dispose();
            directory.Dispose();
        }
    }
}
