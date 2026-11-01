using System.Text.Json;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies deck write tools return a small summary by default, return the whole deck only on request, and fill in
/// card identity for entries added by name.
/// </summary>
public sealed class DeckWriteSummaryTests
{
    /// <summary>
    /// Verifies a deck with a 50,000-character name gets a summary with the name cut and marked, so the summary stays
    /// small.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_HugeDeckName_CutsTheNameInTheSummary()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = Success(await setup.Store.CreateAsync(
            new DeckCreateRequest(new string('n', 50_000), Format: "custom"),
            TestContext.Current.CancellationToken));

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckCategoryInput(new DeckCategoryDraft("Ramp"))],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(summary.NameTruncated);
        Assert.Equal(ResultText.MaximumNameBytes, summary.Name.Length);
        Assert.True(Json(summary).Length < 10_000);
    }

    /// <summary>
    /// Verifies a batch on a 300-entry deck returns a summary under 10,000 characters with the listed fields, and
    /// returnDeck adds the whole deck.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_ThreeHundredEntryDeck_ReturnsSmallSummaryUnlessDeckRequested()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(300);
        DeckEntry target = deck.Entries[7];
        Guid categoryId = Guid.CreateVersion7();
        DeckChangeInput[] changes =
        [
            new AddDeckCategoryInput(new DeckCategoryDraft("Ramp", CategoryId: categoryId)),
            new AssignDeckCategoryInput(target.EntryId, categoryId, true),
            new RemoveDeckEntryInput(deck.Entries[8].EntryId),
        ];

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId, deck.Revision, changes, cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(Json(summary).Length < 10_000);
        Assert.Equal(deck.DeckId, summary.DeckId);
        Assert.Equal(deck.Revision + 1, summary.Revision);
        Assert.Equal(deck.Revision, summary.PreviousRevision);
        Assert.Equal(299, summary.EntryCount);
        Assert.Equal(299, summary.CardCount);
        Assert.Equal(new Dictionary<string, int> { ["main"] = 299 }, summary.CardsByZone);
        Assert.Equal([target.EntryId], summary.UpdatedEntryIds);
        Assert.Equal([deck.Entries[8].EntryId], summary.RemovedEntryIds);
        Assert.Equal([categoryId], summary.AddedCategoryIds);
        Assert.Equal(new DeckChangeCounts(0, 1, 1, 1, 0, 0), summary.ChangeCounts);
        Assert.Null(summary.Deck);
        Assert.Equal(0, summary.IssueCount);

        DeckWriteResult whole = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            summary.Revision,
            [new UpdateDeckMetadataInput("Renamed", null, "custom")],
            returnDeck: true,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(299, whole.Deck!.Entries.Count);
        Assert.Equal("Renamed", whole.Deck.Name);
    }

    /// <summary>
    /// Verifies a create with 300 entries lists a capped set of IDs, keeps exact counts, and says it was cut.
    /// </summary>
    [Fact]
    public async Task Create_ThreeHundredEntries_CapsListedIdsAndKeepsCounts()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        List<DeckEntryDraft> entries =
            [.. Enumerable.Range(0, 300).Select(index => new DeckEntryDraft(1, $"Card {index:D3}", SortOrder: index))];

        DeckWriteResult summary = Success(await setup.Create.CreateAsync(
            new DeckCreateRequest("Big", Format: "custom", Entries: entries),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(300, summary.ChangeCounts.EntriesAdded);
        Assert.Equal(DeckWriteResults.MaximumListedIds, summary.AddedEntryIds!.Count);
        Assert.True(summary.Truncated);
        Assert.False(string.IsNullOrWhiteSpace(summary.Narrow));
        Assert.Null(summary.PreviousRevision);
        Assert.True(Json(summary).Length < 10_000);
    }

    /// <summary>
    /// Verifies a name resolves to its Oracle ID, a set and number resolve the printing, and a missing name or a
    /// set and number that name another card is added unresolved and reported.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AddedEntries_ResolveFromInstalledCardData()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(0);
        Guid unknownId = Guid.CreateVersion7();

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new AddDeckEntryInput(new DeckEntryDraft(1, "fixture ramp card")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Fixture Ramp Card", SetCode: "tst", CollectorNumber: "1")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Not A Real Card", EntryId: unknownId)),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Set Mismatch", SetCode: "tst", CollectorNumber: "1")),
            ],
            returnDeck: true,
            cancellationToken: TestContext.Current.CancellationToken));

        DeckEntry byName = summary.Deck!.Entries[0];
        Assert.Equal("Fixture Ramp Card", byName.CardName);
        Assert.Equal(DeckCategorizationSourceFixture.OracleId, byName.OracleId);
        Assert.Null(byName.PrintingId);
        DeckEntry byPrinting = summary.Deck.Entries[1];
        Assert.Equal(DeckCategorizationSourceFixture.CardId, byPrinting.PrintingId);
        Assert.Equal("tst", byPrinting.SetCode);
        DeckEntry unknown = summary.Deck.Entries.Single(entry => entry.EntryId == unknownId);
        Assert.Null(unknown.OracleId);
        Assert.Equal(3, summary.IssueCount);
        Assert.Contains(
            summary.Issues!,
            issue => issue.EntryId == unknownId && issue.Kind == DeckWriteIssue.Unresolved);
        Assert.Contains(summary.Issues!, issue => issue.CardName == "Set Mismatch");
        Assert.Single(summary.Issues!, issue => issue.Kind == DeckWriteIssue.AlreadyInDeck);
        Assert.Equal(4, summary.EntryCount);
    }

    /// <summary>
    /// Verifies entries in a create request resolve too, and the deck is still created for names that do not.
    /// </summary>
    [Fact]
    public async Task Create_EntryDrafts_ResolveFromInstalledCardData()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: true);

        DeckWriteResult summary = Success(await setup.Create.CreateAsync(
            new DeckCreateRequest(
                "Named",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "Fixture Ramp Card"), new DeckEntryDraft(1, "Nope")]),
            returnDeck: true,
            TestContext.Current.CancellationToken));

        Assert.Equal(DeckCategorizationSourceFixture.OracleId, summary.Deck!.Entries[0].OracleId);
        Assert.Null(summary.Deck.Entries[1].OracleId);
        DeckWriteIssue issue = Assert.Single(summary.Issues!);
        Assert.Equal("Nope", issue.CardName);
    }

    /// <summary>
    /// Verifies that with no card data installed the entries are added as given and one note says resolution was
    /// skipped.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_NoCardDataInstalled_AddsEntriesAndReportsSkippedResolution()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(0);

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckEntryInput(
                new DeckEntryDraft(1, "Sol Ring")), new AddDeckEntryInput(new DeckEntryDraft(1, "Island"))],
            returnDeck: true,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, summary.Deck!.Entries.Count);
        Assert.All(summary.Deck.Entries, entry => Assert.Null(entry.OracleId));
        DeckWriteIssue note = Assert.Single(summary.Issues!);
        Assert.Equal(DeckWriteIssue.Skipped, note.Kind);
        Assert.Contains("not installed", note.Message, StringComparison.Ordinal);
        Assert.Contains("default printing", note.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a write that names a revision another write has already replaced is refused with a conflict and
    /// changes nothing, and the first write's summary compares exactly the state it started from. The expected
    /// revision is what keeps a summary from being built on a deck another write changed between the read and the
    /// apply; this test checks the refusal, one write after the other, not an interleaving.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_SecondWriteFromTheSameRevision_IsRefusedAndLeavesTheFirstSummaryExact()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(3);
        DeckEntry first = deck.Entries[0];

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new RemoveDeckEntryInput(first.EntryId)],
            cancellationToken: TestContext.Current.CancellationToken));
        OperationResult<DeckWriteResult> second = await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new RemoveDeckEntryInput(deck.Entries[1].EntryId)],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(deck.Revision, summary.PreviousRevision);
        Assert.Equal([first.EntryId], summary.RemovedEntryIds);
        Assert.Equal(2, summary.EntryCount);
        Assert.IsType<OperationConflict>(second.Value);
        DeckDocument after = Success(await setup.Store.GetAsync(deck.DeckId, TestContext.Current.CancellationToken));
        Assert.Equal(summary.Revision, after.Revision);
        Assert.Equal(2, after.Entries.Count);
    }

    /// <summary>
    /// Verifies that without card data an add of a card already in the deck, matched by name, is still made and
    /// reported as already-in-deck with the existing entry, its zone, copies, and categories, and that the issue
    /// is counted.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_CardAlreadyInDeck_NoCardData_AddsAndReportsAlreadyInDeck()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckWithAsync(new DeckEntryDraft(2, "Sol Ring"));

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckEntryInput(new DeckEntryDraft(1, "sol ring", Zone: "sideboard"))],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, summary.EntryCount);
        Assert.Equal(2, summary.IssueCount);
        DeckWriteIssue issue = Assert.Single(summary.Issues!, value => value.Kind == DeckWriteIssue.AlreadyInDeck);
        Guid existing = deck.Entries[0].EntryId;
        Assert.Equal(existing, issue.EntryId);
        Assert.Equal("sol ring", issue.CardName);
        Assert.Equal(
            $"'sol ring' was already in the deck as entry {existing} (zone main, 2 copies, categories: Ramp). " +
            $"This add made another entry; to change copies use update-entry on {existing}, or remove-entry on the " +
            "new entry.",
            issue.Message);
    }

    /// <summary>
    /// Verifies that with card data the match uses the Oracle ID, so a differently named entry of the same card is
    /// found even when the printings differ.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_CardAlreadyInDeck_WithCardData_MatchesByOracleId()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckWithAsync(new DeckEntryDraft(
            1,
            "Older Name",
            OracleId: DeckCategorizationSourceFixture.OracleId,
            PrintingId: Guid.CreateVersion7()));

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckEntryInput(new DeckEntryDraft(1, "Fixture Ramp Card"))],
            cancellationToken: TestContext.Current.CancellationToken));

        DeckWriteIssue issue = Assert.Single(summary.Issues!);
        Assert.Equal(DeckWriteIssue.AlreadyInDeck, issue.Kind);
        Assert.Equal(deck.Entries[0].EntryId, issue.EntryId);
        Assert.Equal(1, summary.IssueCount);
        Assert.Equal(2, summary.EntryCount);
    }

    /// <summary>
    /// Verifies basic lands may be added again without an issue, and a different card is not matched.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_BasicLandOrOtherCard_GivesNoAlreadyInDeckIssue()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckWithAsync(new DeckEntryDraft(5, "Forest"));

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new AddDeckEntryInput(new DeckEntryDraft(1, "Forest")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Snow-Covered Forest")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Cultivate")),
            ],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(4, summary.EntryCount);
        Assert.DoesNotContain(summary.Issues!, value => value.Kind == DeckWriteIssue.AlreadyInDeck);
    }

    /// <summary>
    /// Verifies a card added twice in one batch is reported on the second add, naming the first add's entry.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_CardAddedTwiceInOneBatch_ReportsTheSecondAdd()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(0);
        Guid first = Guid.CreateVersion7();

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring", EntryId: first)),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Arcane Signet")),
            ],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(3, summary.EntryCount);
        DeckWriteIssue issue = Assert.Single(summary.Issues!, value => value.Kind == DeckWriteIssue.AlreadyInDeck);
        Assert.Equal(first, issue.EntryId);
        Assert.Equal(2, summary.IssueCount);
    }

    /// <summary>
    /// Verifies the skip choice leaves out an add whose card is already in the zone, so a retried batch changes
    /// nothing and keeps the revision.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_SkipTwice_IsIdempotent()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(0);
        DeckChangeInput[] batch =
        [
            new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring")),
            new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring")),
        ];

        DeckWriteResult first = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            batch,
            whenCardPresent: "skip",
            cancellationToken: TestContext.Current.CancellationToken));
        DeckWriteResult second = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            first.Revision,
            batch,
            whenCardPresent: "skip",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, first.EntryCount);
        Assert.Equal(1, first.ChangeCounts.EntriesAdded);
        Assert.Contains(
            first.Issues!,
            value => value.Kind == DeckWriteIssue.Skipped &&
                value.Message.Contains("is already in zone main as an earlier add in this batch; not added.", StringComparison.Ordinal));
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(1, second.EntryCount);
        Assert.Equal(0, second.ChangeCounts.EntriesAdded);
        Assert.Equal(3, second.IssueCount);
        Guid existing = Success(await setup.Store.GetAsync(deck.DeckId, TestContext.Current.CancellationToken))
            .Entries.Single().EntryId;
        Assert.All(
            second.Issues!.Where(value => value.CardName is not null),
            value => Assert.Equal($"'Sol Ring' is already in zone main as entry {existing}; not added.", value.Message));
    }

    /// <summary>
    /// Verifies the skip choice still adds a card that is in another zone, and reports it as already-in-deck.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_SkipWithCardInAnotherZone_AddsAndReportsAlreadyInDeck()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckWithAsync(new DeckEntryDraft(1, "Sol Ring"));

        DeckWriteResult summary = Success(await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring", Zone: "sideboard"))],
            whenCardPresent: "skip",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, summary.EntryCount);
        Assert.Contains(summary.Issues!, value => value.Kind == DeckWriteIssue.AlreadyInDeck);
        Assert.DoesNotContain(summary.Issues!, value => value.Kind == DeckWriteIssue.Skipped && value.CardName == "Sol Ring");
    }

    /// <summary>
    /// Verifies a later change that refers to the entry of a skipped add refuses the whole batch, names both
    /// changes, and writes nothing.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_ChangeReferringToASkippedAdd_IsRefusedWithoutAWrite()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckWithAsync(new DeckEntryDraft(1, "Sol Ring"));
        Guid skipped = Guid.CreateVersion7();

        OperationResult<DeckWriteResult> result = await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new AddDeckEntryInput(new DeckEntryDraft(1, "Arcane Signet")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring", EntryId: skipped)),
                new RemoveDeckEntryInput(skipped),
            ],
            whenCardPresent: "skip",
            cancellationToken: TestContext.Current.CancellationToken);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-deck-change", invalid.ReasonCode);
        Assert.Contains("Change 3", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("change 2", invalid.Message, StringComparison.Ordinal);
        DeckDocument after = Success(await setup.Store.GetAsync(deck.DeckId, TestContext.Current.CancellationToken));
        Assert.Equal(deck.Revision, after.Revision);
        Assert.Single(after.Entries);
    }

    /// <summary>
    /// Verifies a value other than add or skip is refused.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UnknownWhenCardPresent_IsRefused()
    {
        using Setup setup = await Setup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(1);

        OperationResult<DeckWriteResult> result = await setup.Batch.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckEntryInput(new DeckEntryDraft(1, "Sol Ring"))],
            whenCardPresent: "merge",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("invalid-when-card-present", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Reads the result as the server writes it.
    /// </summary>
    private static string Json(DeckWriteResult value)
    {
        return JsonSerializer.Serialize(value, ToolCallPolicy.SerializerOptions);
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Owns a deck store, a Scryfall service, and the write tools over them.
    /// </summary>
    private sealed class Setup : IDisposable
    {
        /// <summary>
        /// Holds the folder that backs the stores.
        /// </summary>
        private readonly TemporaryDirectory directory;

        /// <summary>
        /// Holds the Scryfall service the resolver reads.
        /// </summary>
        private readonly ScryfallService scryfall;

        /// <summary>
        /// Creates a setup around already created services.
        /// </summary>
        private Setup(TemporaryDirectory directory, SqliteDeckStore store, ScryfallService scryfall)
        {
            this.directory = directory;
            this.scryfall = scryfall;
            Store = store;
            DeckEntryResolver resolver = new(scryfall);
            Batch = new DeckBatchWriteTools(store, OperationMode.Local, resolver);
            Create = new DeckWriteTools(store, OperationMode.Local, resolver);
        }

        /// <summary>
        /// Gets the deck store.
        /// </summary>
        internal SqliteDeckStore Store { get; }

        /// <summary>
        /// Gets the batch tool under test.
        /// </summary>
        internal DeckBatchWriteTools Batch { get; }

        /// <summary>
        /// Gets the create tool under test.
        /// </summary>
        internal DeckWriteTools Create { get; }

        /// <summary>
        /// Creates a setup with or without installed Scryfall card data.
        /// </summary>
        internal static async Task<Setup> CreateAsync(bool installCardData)
        {
            TemporaryDirectory directory = new();
            SqliteDeckStore store = new(directory.Path, "test");
            ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(
                directory.Path,
                DeckCategorizationSourceFixture.CreateHandler());
            if (installCardData)
            {
                _ = Success(await scryfall.SyncCorpusAsync("refresh", null, TestContext.Current.CancellationToken));
            }

            return new Setup(directory, store, scryfall);
        }

        /// <summary>
        /// Creates a deck with the given number of plain entries.
        /// </summary>
        internal async Task<DeckDocument> CreateDeckAsync(int entryCount)
        {
            List<DeckEntryDraft> entries =
                [.. Enumerable.Range(0, entryCount).Select(index => new DeckEntryDraft(
                    1,
                    $"Entry {index:D3}",
                    OracleId: Guid.CreateVersion7(),
                    SortOrder: index))];
            return Success(await Store.CreateAsync(
                new DeckCreateRequest("Summary deck", Format: "custom", Entries: entries),
                TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Creates a deck with one entry that is assigned to a category named Ramp.
        /// </summary>
        internal async Task<DeckDocument> CreateDeckWithAsync(DeckEntryDraft entry)
        {
            Guid entryId = Guid.CreateVersion7();
            Guid categoryId = Guid.CreateVersion7();
            return Success(await Store.CreateAsync(
                new DeckCreateRequest(
                    "Presence deck",
                    Format: "custom",
                    Entries: [entry with { EntryId = entryId }],
                    Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)],
                    CategoryAssignments: [new DeckCategoryAssignment(entryId, categoryId, true)]),
                TestContext.Current.CancellationToken));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            scryfall.Dispose();
            Store.Dispose();
            directory.Dispose();
        }
    }
}
