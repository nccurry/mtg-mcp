using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies local deck persistence, optimistic transactions, canonical reads, and isolation.
/// </summary>
public sealed class SqliteDeckStoreTests
{
    /// <summary>
    /// Verifies malformed identities, pagination, and empty mutations return bounded input failures.
    /// </summary>
    [Fact]
    public async Task PublicOperations_WithMalformedInput_ReturnStructuredFailuresWithoutCreatingStorage()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);

        Assert.IsType<OperationInvalidInput>((await store.ListAsync(
            null,
            0,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await store.ListAsync(
            "-1",
            10,
            TestContext.Current.CancellationToken)).Value);
        Assert.Empty(RequireSuccess(await store.ListAsync(
            null,
            10,
            TestContext.Current.CancellationToken)).Items);
        Assert.IsType<OperationInvalidInput>((await store.GetAsync(
            Guid.Empty,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationNotFound>((await store.GetAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await store.DeleteAsync(
            Guid.Empty,
            0,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationNotFound>((await store.DeleteAsync(
            Guid.CreateVersion7(),
            1,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await store.ApplyChangesAsync(
            Guid.Empty,
            0,
            [],
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await store.ApplyChangesAsync(
            Guid.CreateVersion7(),
            1,
            [],
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationNotFound>((await store.ApplyChangesAsync(
            Guid.CreateVersion7(),
            1,
            [new UpdateDeckMetadataChange("Missing", null, "custom")],
            TestContext.Current.CancellationToken)).Value);
        Assert.False(File.Exists(System.IO.Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Verifies malformed initial fields and relationships fail atomically as caller input.
    /// </summary>
    [Fact]
    public async Task Create_WithMalformedGraph_ReturnsInvalidInputAndLeavesNoDeck()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        Guid entryId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        Guid secondCategoryId = Guid.CreateVersion7();
        DeckCreateRequest[] malformed =
        [
            new DeckCreateRequest(" "),
            new DeckCreateRequest("Deck", DeckId: Guid.Empty),
            new DeckCreateRequest("Deck", Entries: [new DeckEntryDraft(0, "Card")]),
            new DeckCreateRequest("Deck", Entries: [new DeckEntryDraft(1, " ")]),
            new DeckCreateRequest("Deck", Entries: [new DeckEntryDraft(1, "Card", OracleId: Guid.Empty)]),
            new DeckCreateRequest("Deck", Entries: [new DeckEntryDraft(1, "Card", PrintingId: Guid.Empty)]),
            new DeckCreateRequest("Deck", Categories: [new DeckCategoryDraft(" ")]),
            new DeckCreateRequest(
                "Deck",
                Categories: [new DeckCategoryDraft("Same"), new DeckCategoryDraft("same")]),
            new DeckCreateRequest(
                "Deck",
                Entries: [new DeckEntryDraft(1, "Card", EntryId: entryId)],
                Categories: [new DeckCategoryDraft("One", CategoryId: categoryId)],
                CategoryAssignments: [
                    new DeckCategoryAssignment(entryId, Guid.CreateVersion7(), false),
                ]),
            new DeckCreateRequest(
                "Deck",
                Entries: [new DeckEntryDraft(1, "Card", EntryId: entryId)],
                Categories: [
                    new DeckCategoryDraft("One", CategoryId: categoryId),
                    new DeckCategoryDraft("Two", CategoryId: secondCategoryId),
                ],
                CategoryAssignments: [
                    new DeckCategoryAssignment(entryId, categoryId, true),
                    new DeckCategoryAssignment(entryId, secondCategoryId, true),
                ]),
        ];

        foreach (DeckCreateRequest request in malformed)
        {
            OperationResult<DeckDocument> result = await store.CreateAsync(
                request,
                TestContext.Current.CancellationToken);
            Assert.IsType<OperationInvalidInput>(result.Value);
        }

        Assert.Empty(RequireSuccess(await store.ListAsync(
            null,
            100,
            TestContext.Current.CancellationToken)).Items);
    }

    /// <summary>
    /// Verifies a name-only Commander graph and duplicate printings round-trip without coalescing.
    /// </summary>
    [Fact]
    public async Task CreateAndGet_NameOnlyAndDuplicateEntries_RoundTripLosslessly()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        Guid commanderId = Guid.CreateVersion7();
        Guid firstPrintingId = Guid.CreateVersion7();
        Guid secondPrintingId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        DeckCreateRequest request = new(
            " Test Deck ",
            " Description ",
            "Commander",
            [
                new DeckEntryDraft(1, "Commander", Zone: "COMMANDER", EntryId: commanderId),
                new DeckEntryDraft(
                    1,
                    "Same Card",
                    PrintingId: Guid.CreateVersion7(),
                    SetCode: "TST",
                    CollectorNumber: "1",
                    Finish: "foil",
                    EntryId: firstPrintingId),
                new DeckEntryDraft(
                    1,
                    "Same Card",
                    PrintingId: Guid.CreateVersion7(),
                    SetCode: "TST",
                    CollectorNumber: "2",
                    Finish: "nonfoil",
                    EntryId: secondPrintingId),
            ],
            [new DeckCategoryDraft(" Ramp ", "#123456", CategoryId: categoryId)],
            [new DeckCategoryAssignment(firstPrintingId, categoryId, true)]);

        DeckDocument created = RequireSuccess(
            await store.CreateAsync(request, TestContext.Current.CancellationToken));
        DeckDocument loaded = RequireSuccess(
            await store.GetAsync(created.DeckId, TestContext.Current.CancellationToken));

        Assert.Equal("Test Deck", loaded.Name);
        Assert.Equal("Description", loaded.Description);
        Assert.Equal("commander", loaded.Format);
        Assert.Equal(1, loaded.Revision);
        Assert.Equal(7, loaded.DeckId.Version);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAtUtc.Offset);
        Assert.Equal(loaded.CreatedAtUtc, loaded.UpdatedAtUtc);
        Assert.Equal(3, loaded.Entries.Count);
        Assert.Equal(2, loaded.Entries.Count(value => value.CardName == "Same Card"));
        Assert.Equal(["commander", "main", "main"], loaded.Entries.Select(value => value.Zone));
        DeckEntry foil = Assert.Single(loaded.Entries, value => value.EntryId == firstPrintingId);
        Assert.Equal("tst", foil.SetCode);
        Assert.Equal("1", foil.CollectorNumber);
        Assert.Equal("foil", foil.Finish);
        Assert.NotNull(foil.PrintingId);
        Assert.Equal(categoryId, Assert.Single(loaded.Categories).CategoryId);
        Assert.True(Assert.Single(loaded.CategoryAssignments).IsPrimary);
    }

    /// <summary>
    /// Verifies category edits never change entry zones or delete card rows.
    /// </summary>
    [Fact]
    public async Task CategoryChanges_PreserveEntriesAndZones()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = await CreateCommanderAsync(store);
        DeckEntry entry = Assert.Single(deck.Entries);
        DeckDocument withCategory = RequireSuccess(await store.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new AddDeckCategoryChange(new DeckCategoryDraft("Draw"))],
            TestContext.Current.CancellationToken));
        DeckCategory category = Assert.Single(withCategory.Categories);
        DeckDocument assigned = RequireSuccess(await store.ApplyChangesAsync(
            deck.DeckId,
            withCategory.Revision,
            [new AssignDeckCategoryChange(entry.EntryId, category.CategoryId, true)],
            TestContext.Current.CancellationToken));
        DeckDocument removed = RequireSuccess(await store.ApplyChangesAsync(
            deck.DeckId,
            assigned.Revision,
            [new RemoveDeckCategoryChange(category.CategoryId)],
            TestContext.Current.CancellationToken));

        DeckEntry remaining = Assert.Single(removed.Entries);
        Assert.Equal(entry.EntryId, remaining.EntryId);
        Assert.Equal("commander", remaining.Zone);
        Assert.Empty(removed.Categories);
        Assert.Empty(removed.CategoryAssignments);
    }

    /// <summary>
    /// Verifies one stale writer and one invalid final operation leave no partial state.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_StaleOrInvalidBatch_RollsBackEntireTransaction()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = await CreateCommanderAsync(store);
        DeckDocument updated = RequireSuccess(await store.ApplyChangesAsync(
            deck.DeckId,
            1,
            [new UpdateDeckMetadataChange("Updated", null, "commander")],
            TestContext.Current.CancellationToken));

        OperationResult<DeckDocument> stale = await store.ApplyChangesAsync(
            deck.DeckId,
            1,
            [new UpdateDeckMetadataChange("Stale", null, "commander")],
            TestContext.Current.CancellationToken);
        OperationResult<DeckDocument> invalid = await store.ApplyChangesAsync(
            deck.DeckId,
            updated.Revision,
            [
                new AddDeckEntryChange(new DeckEntryDraft(1, "Would Roll Back")),
                new RemoveDeckCategoryChange(Guid.CreateVersion7()),
            ],
            TestContext.Current.CancellationToken);
        DeckDocument persisted = RequireSuccess(
            await store.GetAsync(deck.DeckId, TestContext.Current.CancellationToken));

        Assert.IsType<OperationConflict>(stale.Value);
        Assert.Equal(
            "deck-category-not-found",
            Assert.IsType<OperationNotFound>(invalid.Value).ReasonCode);
        Assert.Equal("Updated", persisted.Name);
        Assert.Equal(2, persisted.Revision);
        Assert.Single(persisted.Entries);
    }

    /// <summary>
    /// Verifies two independent writers using one revision produce one commit and one conflict.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_TwoStoresWithSameRevision_CommitExactlyOnce()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore firstStore = CreateStore(temporary.Path);
        using SqliteDeckStore secondStore = CreateStore(temporary.Path);
        DeckDocument deck = await CreateCommanderAsync(firstStore);

        Task<OperationResult<DeckDocument>> firstWrite = firstStore.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpdateDeckMetadataChange("First Writer", null, "commander")],
            TestContext.Current.CancellationToken);
        Task<OperationResult<DeckDocument>> secondWrite = secondStore.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpdateDeckMetadataChange("Second Writer", null, "commander")],
            TestContext.Current.CancellationToken);
        OperationResult<DeckDocument>[] results = await Task.WhenAll(firstWrite, secondWrite);
        DeckDocument persisted = RequireSuccess(await firstStore.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken));

        Assert.Single(results, result => result.Value is OperationSuccess<DeckDocument>);
        Assert.Single(results, result => result.Value is OperationConflict);
        Assert.Equal(2, persisted.Revision);
        Assert.True(persisted.Name is "First Writer" or "Second Writer");
    }

    /// <summary>
    /// Verifies the database enforces one primary category per entry and rolls back the batch.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_TwoPrimaryCategories_RejectsWholeBatch()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = await CreateCommanderAsync(store);
        Guid entryId = Assert.Single(deck.Entries).EntryId;
        Guid firstCategoryId = Guid.CreateVersion7();
        Guid secondCategoryId = Guid.CreateVersion7();

        OperationResult<DeckDocument> result = await store.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new AddDeckCategoryChange(new DeckCategoryDraft("First", CategoryId: firstCategoryId)),
                new AddDeckCategoryChange(new DeckCategoryDraft("Second", CategoryId: secondCategoryId)),
                new AssignDeckCategoryChange(entryId, firstCategoryId, true),
                new AssignDeckCategoryChange(entryId, secondCategoryId, true),
            ],
            TestContext.Current.CancellationToken);
        DeckDocument persisted = RequireSuccess(
            await store.GetAsync(deck.DeckId, TestContext.Current.CancellationToken));

        Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Empty(persisted.Categories);
        Assert.Empty(persisted.CategoryAssignments);
        Assert.Equal(deck.Revision, persisted.Revision);
    }

    /// <summary>
    /// Verifies deck deletion is revision guarded and removes the complete relational graph.
    /// </summary>
    [Fact]
    public async Task Delete_RequiresCurrentRevisionAndRemovesDeck()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = await CreateCommanderAsync(store);

        OperationResult<DeckDeleteResult> stale = await store.DeleteAsync(
            deck.DeckId,
            deck.Revision + 1,
            TestContext.Current.CancellationToken);
        DeckDeleteResult deleted = RequireSuccess(await store.DeleteAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken));
        OperationResult<DeckDocument> missing = await store.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationConflict>(stale.Value);
        Assert.Equal(deck.DeckId, deleted.DeletedId);
        Assert.Equal(2, deleted.FinalRevision);
        Assert.IsType<OperationNotFound>(missing.Value);
    }

    /// <summary>
    /// Verifies provider bindings and canonical ordering persist independently.
    /// </summary>
    [Fact]
    public async Task ProviderBindingsAndPagination_AreIndependentAndCanonical()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument zulu = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Zulu", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckDocument alpha = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("alpha", Format: "custom"),
            TestContext.Current.CancellationToken));
        Guid archidektId = Guid.CreateVersion7();
        Guid otherId = Guid.CreateVersion7();
        DeckDocument bound = RequireSuccess(await store.ApplyChangesAsync(
            alpha.DeckId,
            alpha.Revision,
            [
                new UpsertDeckProviderBindingChange(
                    new DeckProviderBinding(
                        archidektId, "Archidekt", "10", null, "v1", "one", null, null),
                    "{\"name\":\"baseline-one\"}"),
                new UpsertDeckProviderBindingChange(
                    new DeckProviderBinding(
                        otherId, "Other", "20", null, "v2", "two", null, null),
                    "{\"name\":\"baseline-two\"}"),
            ],
            TestContext.Current.CancellationToken));
        DeckPage firstPage = RequireSuccess(
            await store.ListAsync(null, 1, TestContext.Current.CancellationToken));
        DeckPage secondPage = RequireSuccess(
            await store.ListAsync(firstPage.NextCursor, 1, TestContext.Current.CancellationToken));

        Assert.Equal(["archidekt", "other"], bound.ProviderBindings.Select(value => value.Provider));
        Assert.Equal("alpha", Assert.Single(firstPage.Items).Name);
        Assert.Equal(zulu.DeckId, Assert.Single(secondPage.Items).DeckId);
        Assert.Null(secondPage.NextCursor);

        string databasePath = System.IO.Path.Combine(temporary.Path, "decks.db");
        DeckDatabase database = new(databasePath);
        await using SqliteConnection connection = await database.OpenConnectionAsync(
            writable: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(2L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM sync_baselines;"));
        Assert.Equal(
            "{\"name\":\"baseline-one\"}",
            await ScalarStringAsync(
                connection,
                $"SELECT canonical_snapshot FROM sync_baselines WHERE binding_id='{archidektId:D}';"));
    }

    /// <summary>
    /// Verifies one binding ID cannot update a different deck or its synchronization baseline.
    /// </summary>
    [Fact]
    public async Task ProviderBinding_WithAnotherDecksBindingId_IsRejectedAtomically()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument first = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("First", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckDocument second = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Second", Format: "custom"),
            TestContext.Current.CancellationToken));
        Guid bindingId = Guid.CreateVersion7();
        first = RequireSuccess(await store.ApplyChangesAsync(
            first.DeckId,
            first.Revision,
            [new UpsertDeckProviderBindingChange(
                new DeckProviderBinding(bindingId, "one", "1", null, null, "before", null, null),
                "before")],
            TestContext.Current.CancellationToken));

        OperationResult<DeckDocument> rejected = await store.ApplyChangesAsync(
            second.DeckId,
            second.Revision,
            [new UpsertDeckProviderBindingChange(
                new DeckProviderBinding(bindingId, "two", "2", null, null, "after", null, null),
                "after")],
            TestContext.Current.CancellationToken);
        DeckDocument persisted = RequireSuccess(await store.GetAsync(
            first.DeckId,
            TestContext.Current.CancellationToken));

        Assert.IsType<OperationInvalidInput>(rejected.Value);
        Assert.Equal("one", Assert.Single(persisted.ProviderBindings).Provider);
        Assert.Equal(first.Revision, persisted.Revision);
    }

    /// <summary>
    /// Verifies schema pragmas, migration metadata, and legacy JSON isolation.
    /// </summary>
    [Fact]
    public async Task DatabaseInitialization_EnablesRequiredPragmasAndIgnoresLegacyJson()
    {
        using TemporaryDeckDirectory temporary = new();
        string legacyPath = System.IO.Path.Combine(temporary.Path, "workspace.json");
        await File.WriteAllTextAsync(legacyPath, "{\"legacy\":true}");
        using SqliteDeckStore store = CreateStore(temporary.Path);
        await CreateCommanderAsync(store);
        string databasePath = System.IO.Path.Combine(temporary.Path, "decks.db");
        DeckDatabase database = new(databasePath);
        await using SqliteConnection connection = await database.OpenConnectionAsync(
            writable: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(1L, await ScalarInt64Async(connection, "PRAGMA foreign_keys;"));
        Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(5000L, await ScalarInt64Async(connection, "PRAGMA busy_timeout;"));
        Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Matches(
            "^[0-9a-f]{64}$",
            await ScalarStringAsync(connection, "SELECT checksum FROM schema_migrations WHERE version=1;"));
        Assert.True(File.Exists(legacyPath));
        Assert.Equal(
            "{\"legacy\":true}",
            await File.ReadAllTextAsync(legacyPath, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies a failed destructive migration leaves the schema unchanged and a recoverable copy present.
    /// </summary>
    [Fact]
    public async Task DestructiveMigrationFailure_RollsBackAndPreservesPreMigrationBackup()
    {
        using TemporaryDeckDirectory temporary = new();
        using (SqliteDeckStore store = CreateStore(temporary.Path))
        {
            await CreateCommanderAsync(store);
        }

        string databasePath = System.IO.Path.Combine(temporary.Path, "decks.db");
        DeckDatabase database = new(databasePath);
        await using SqliteConnection connection = await database.OpenConnectionAsync(
            writable: true,
            TestContext.Current.CancellationToken);
        SqliteMigration migration = new(
            2,
            true,
            "ALTER TABLE decks ADD COLUMN note TEXT; SELECT * FROM missing_table;");

        await Assert.ThrowsAsync<SqliteException>(async () =>
            await database.ApplyMigrationAsync(
                connection,
                migration,
                DateTimeOffset.UtcNow,
                "0.9.0-preview.1",
                TestContext.Current.CancellationToken));
        Assert.Equal(1, await DeckDatabase.GetSchemaVersionAsync(
            connection,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            0L,
            await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM pragma_table_info('decks') WHERE name='note';"));
        string backupDirectory = System.IO.Path.Combine(temporary.Path, "backups", "decks");
        Assert.Single(Directory.GetFiles(backupDirectory, "pre-migration-*.db"));
    }

    /// <summary>
    /// Verifies an existing database with altered migration evidence fails closed.
    /// </summary>
    [Fact]
    public async Task Get_WithMismatchedMigrationChecksum_ReturnsUnsupported()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = await CreateCommanderAsync(store);
        string databasePath = System.IO.Path.Combine(temporary.Path, "decks.db");
        await using (SqliteConnection connection = new($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_migrations SET checksum='altered' WHERE version=1;";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        OperationResult<DeckDocument> result = await store.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnsupported>(result.Value);
    }

    /// <summary>
    /// Verifies local validation reports only documented structural observations.
    /// </summary>
    [Fact]
    public async Task Validate_ReportsCommanderStructureWithoutStrategicClaims()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Empty Commander"),
            TestContext.Current.CancellationToken));

        DeckValidationReport report = RequireSuccess(
            await store.ValidateAsync(deck.DeckId, TestContext.Current.CancellationToken));

        DeckValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal("commander-zone-empty", issue.ReasonCode);
        Assert.False(report.IsStructurallyValid);
        Assert.DoesNotContain("legal", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quality", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies repeated canonical reads serialize to exactly the same bytes.
    /// </summary>
    [Fact]
    public async Task Get_WithoutMutation_SerializesByteEquivalently()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument created = await CreateCommanderAsync(store);

        DeckDocument first = RequireSuccess(await store.GetAsync(
            created.DeckId,
            TestContext.Current.CancellationToken));
        DeckDocument second = RequireSuccess(await store.GetAsync(
            created.DeckId,
            TestContext.Current.CancellationToken));

        Assert.Equal(
            JsonSerializer.SerializeToUtf8Bytes(first),
            JsonSerializer.SerializeToUtf8Bytes(second));
    }

    /// <summary>
    /// Verifies canceled initialization propagates instead of becoming an unavailable result.
    /// </summary>
    [Fact]
    public async Task Create_WithCancellation_PropagatesCancellation()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.CreateAsync(
                new DeckCreateRequest("Canceled"),
                cancellation.Token));
        Assert.False(File.Exists(System.IO.Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Creates a store using the current package version for migration metadata.
    /// </summary>
    private static SqliteDeckStore CreateStore(string path)
    {
        return new SqliteDeckStore(path, "0.9.0-preview.1");
    }

    /// <summary>
    /// Creates the smallest structurally valid Commander fixture.
    /// </summary>
    private static async Task<DeckDocument> CreateCommanderAsync(SqliteDeckStore store)
    {
        return RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Commander",
                Entries: [new DeckEntryDraft(1, "Commander", Zone: "commander")]),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Extracts a successful result or fails the test with its actual union case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Executes one scalar integer query.
    /// </summary>
    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? 0L);
    }

    /// <summary>
    /// Executes one scalar text query.
    /// </summary>
    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
