using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises every current public MCP method through an explicitly installed package and real providers.
/// </summary>
[Collection(LiveAcceptanceSerialGroup.Name)]
public sealed class LiveMethodAcceptanceTests
{
    /// <summary>
    /// Identifies the owner-authorized mutable Archidekt acceptance deck.
    /// </summary>
    private const string ArchidektDeckId = "24086044";

    /// <summary>
    /// Identifies the owner-selected authenticated Playgroup read fixture.
    /// </summary>
    private const int PlaygroupId = 49295;

    /// <summary>
    /// Configures the untracked retained-corpus guard file.
    /// </summary>
    private static readonly JsonSerializerOptions SourceStateJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Verifies the packaged surface and every network-free deck method in isolated storage.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task InstalledSurfaceAndLocalDeckMethods_CompleteObekaWorkflow()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        LiveAcceptanceEnvironment environment = await LiveAcceptanceEnvironment.RequireAsync(token).ConfigureAwait(false);
        await VerifySurfaceMatrixAsync(environment, token).ConfigureAwait(false);
        await environment.Journal.RecordCapabilityResourceAsync(token).ConfigureAwait(false);
        await environment.Journal.RecordFixtureOnlyWritesAsync(token).ConfigureAwait(false);

        string dataRoot = environment.PrepareEphemeralPhaseRoot("local-deck-methods");
        try
        {
            await using McpProcessSession session = await StartLiveAsync(dataRoot, token).ConfigureAwait(false);
            JsonElement formats = await CallSuccessAsync(
                environment,
                session,
                "deck_interchange_formats",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.Equal(4, formats.GetArrayLength());
            Assert.All(formats.EnumerateArray(), value => Assert.Equal("available", value.GetProperty("status").GetString()));

            const string importedText =
                "[commander]\n1 Obeka, Splitter of Seconds\n[main]\n1 Sol Ring\n1 Arcane Signet\n1 Paradox Haze";
            JsonElement preview = await CallSuccessAsync(
                environment,
                session,
                "deck_import_preview",
                new Dictionary<string, object?>
                {
                    ["formatId"] = "generic-text-v1",
                    ["content"] = importedText,
                    ["options"] = new { deckName = "Obeka Acceptance Import", format = "commander" },
                },
                token).ConfigureAwait(false);
            JsonElement imported = await CallSuccessAsync(
                environment,
                session,
                "deck_import_create",
                new Dictionary<string, object?>
                {
                    ["formatId"] = "generic-text-v1",
                    ["content"] = importedText,
                    ["expectedFingerprint"] = preview.GetProperty("fingerprint").GetString(),
                    ["options"] = new { deckName = "Obeka Acceptance Import", format = "commander" },
                },
                token).ConfigureAwait(false);
            JsonElement importedDeck = imported.GetProperty("deck");
            Guid importedDeckId = importedDeck.GetProperty("deckId").GetGuid();
            long importedRevision = importedDeck.GetProperty("revision").GetInt64();

            JsonElement deck = await CallSuccessAsync(
                environment,
                session,
                "deck_create",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        name = "Obeka Method Acceptance",
                        description = "Disposable packaged MCP live acceptance",
                        format = "commander",
                        entries = new object[]
                        {
                            new { quantity = 1, cardName = "Obeka, Splitter of Seconds", zone = "commander" },
                            new { quantity = 1, cardName = "Sol Ring", zone = "main" },
                            new { quantity = 1, cardName = "Arcane Signet", zone = "main" },
                            new { quantity = 1, cardName = "Paradox Haze", zone = "main" },
                        },
                    },
                },
                token).ConfigureAwait(false);
            Guid deckId = deck.GetProperty("deckId").GetGuid();
            long revision = deck.GetProperty("revision").GetInt64();

            JsonElement listed = await CallSuccessAsync(
                environment,
                session,
                "deck_list",
                new Dictionary<string, object?> { ["pageSize"] = 1 },
                token).ConfigureAwait(false);
            Assert.Single(listed.GetProperty("items").EnumerateArray());
            string listCursor = listed.GetProperty("nextCursor").GetString()!;
            JsonElement secondPage = await CallSuccessAsync(
                environment,
                session,
                "deck_list",
                new Dictionary<string, object?> { ["cursor"] = listCursor, ["pageSize"] = 100 },
                token).ConfigureAwait(false);
            Assert.Single(secondPage.GetProperty("items").EnumerateArray());
            Assert.False(secondPage.TryGetProperty("nextCursor", out _));

            JsonElement loaded = await CallSuccessAsync(
                environment,
                session,
                "deck_get",
                new Dictionary<string, object?> { ["deckId"] = deckId },
                token).ConfigureAwait(false);
            Assert.Equal("Obeka Method Acceptance", loaded.GetProperty("name").GetString());

            JsonElement validation = await CallSuccessAsync(
                environment,
                session,
                "deck_validate",
                new Dictionary<string, object?> { ["deckId"] = deckId },
                token).ConfigureAwait(false);
            Assert.True(validation.GetProperty("isStructurallyValid").GetBoolean());

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["name"] = "Obeka Method Acceptance Updated",
                    ["description"] = "Updated through the packaged MCP",
                    ["format"] = "commander",
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            JsonElement stale = await CallResultAsync(
                session,
                "deck_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision - 1,
                    ["name"] = "Stale mutation",
                    ["description"] = string.Empty,
                    ["format"] = "commander",
                },
                token).ConfigureAwait(false);
            Assert.Equal("conflict", stale.GetProperty("kind").GetString());

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_entry_add",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["entry"] = new { quantity = 2, cardName = "Island", zone = "main" },
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();
            Guid islandId = deck.GetProperty("entries").EnumerateArray()
                .Single(value => value.GetProperty("cardName").GetString() == "Island")
                .GetProperty("entryId").GetGuid();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_entry_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["entry"] = new
                    {
                        entryId = islandId,
                        quantity = 3,
                        cardName = "Island",
                        language = "en",
                        finish = "nonfoil",
                        zone = "main",
                        sortOrder = 10,
                    },
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_create",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["category"] = new { name = "Acceptance Added", color = "#3366ff", sortOrder = 1 },
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();
            Guid categoryId = deck.GetProperty("categories")[0].GetProperty("categoryId").GetGuid();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["category"] = new
                    {
                        categoryId,
                        name = "Acceptance Evidence",
                        color = "#6633ff",
                        sortOrder = 2,
                    },
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_assign",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["entryId"] = islandId,
                    ["categoryId"] = categoryId,
                    ["isPrimary"] = true,
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_unassign",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["entryId"] = islandId,
                    ["categoryId"] = categoryId,
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_apply_changes",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["changes"] = new[]
                    {
                        new
                        {
                            kind = "update-metadata",
                            name = "Obeka Batched Acceptance",
                            description = "Atomic batch accepted",
                            format = "commander",
                        },
                    },
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();
            JsonElement invalidBatch = await CallResultAsync(
                session,
                "deck_apply_changes",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["changes"] = new[] { new { kind = "not-a-change" } },
                },
                token).ConfigureAwait(false);
            Assert.Equal("invalid-input", invalidBatch.GetProperty("kind").GetString());

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_entry_remove",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["entryId"] = islandId,
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            deck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_delete",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["categoryId"] = categoryId,
                },
                token).ConfigureAwait(false);
            revision = deck.GetProperty("revision").GetInt64();

            foreach (string formatId in new[]
                     {
                         "mtg-mcp-json-v1",
                         "generic-text-v1",
                         "archidekt-text-v1",
                         "moxfield-bulk-edit-v1",
                     })
            {
                JsonElement bundle = await CallSuccessAsync(
                    environment,
                    session,
                    "deck_export_bundle",
                    new Dictionary<string, object?> { ["deckId"] = deckId, ["formatId"] = formatId },
                    token).ConfigureAwait(false);
                Assert.Equal("available", bundle.GetProperty("status").GetString());
                Assert.All(
                    bundle.GetProperty("artifacts").EnumerateArray(),
                    artifact => Assert.Matches("^[0-9a-f]{64}$", artifact.GetProperty("sha256").GetString()));
            }

            JsonElement backup = await CallSuccessAsync(
                environment,
                session,
                "deck_backup_create",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Guid backupId = backup.GetProperty("backupId").GetGuid();
            JsonElement backups = await CallSuccessAsync(
                environment,
                session,
                "deck_backup_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.Contains(backups.GetProperty("items").EnumerateArray(), value => value.GetProperty("backupId").GetGuid() == backupId);

            _ = await CallSuccessAsync(
                environment,
                session,
                "deck_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["name"] = "State after backup",
                    ["description"] = "Must be restored",
                    ["format"] = "commander",
                },
                token).ConfigureAwait(false);
            JsonElement changedInventory = await CallSuccessAsync(
                environment,
                session,
                "deck_backup_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            JsonElement restored = await CallSuccessAsync(
                environment,
                session,
                "deck_backup_restore",
                new Dictionary<string, object?>
                {
                    ["backupId"] = backupId,
                    ["expectedDatabaseFingerprint"] = changedInventory.GetProperty("currentDatabaseFingerprint").GetString(),
                },
                token).ConfigureAwait(false);
            Guid rollbackBackupId = restored.GetProperty("rollbackBackupId").GetGuid();
            loaded = await CallSuccessAsync(
                environment,
                session,
                "deck_get",
                new Dictionary<string, object?> { ["deckId"] = deckId },
                token).ConfigureAwait(false);
            Assert.Equal("Obeka Batched Acceptance", loaded.GetProperty("name").GetString());
            revision = loaded.GetProperty("revision").GetInt64();

            foreach (Guid id in new[] { backupId, rollbackBackupId })
            {
                _ = await CallSuccessAsync(
                    environment,
                    session,
                    "deck_backup_delete",
                    new Dictionary<string, object?> { ["backupId"] = id },
                    token).ConfigureAwait(false);
            }

            _ = await CallSuccessAsync(
                environment,
                session,
                "deck_delete",
                new Dictionary<string, object?>
                {
                    ["deckId"] = importedDeckId,
                    ["expectedRevision"] = importedRevision,
                },
                token).ConfigureAwait(false);
            _ = await CallSuccessAsync(
                environment,
                session,
                "deck_delete",
                new Dictionary<string, object?> { ["deckId"] = deckId, ["expectedRevision"] = revision },
                token).ConfigureAwait(false);
            listed = await CallSuccessAsync(
                environment,
                session,
                "deck_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.Empty(listed.GetProperty("items").EnumerateArray());
        }
        finally
        {
            await DeletePhaseRootAsync(dataRoot, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Exercises every bounded Scryfall read and immutable snapshot operation against official evidence.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task BoundedScryfallMethods_ResolveObekaEvidenceAndSnapshots()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        LiveAcceptanceEnvironment environment = await LiveAcceptanceEnvironment.RequireAsync(token).ConfigureAwait(false);
        string dataRoot = environment.PrepareEphemeralPhaseRoot("bounded-scryfall-methods");
        try
        {
            await BackupRetainedCorpusAsync(dataRoot, token).ConfigureAwait(false);
            await using McpProcessSession session = await StartLiveAsync(dataRoot, token).ConfigureAwait(false);

            JsonElement status = await CallSuccessAsync(
                environment,
                session,
                "scryfall_corpus_status",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.Equal("available", status.GetProperty("state").GetString());

            JsonElement metadata = await CallSuccessAsync(
                environment,
                session,
                "scryfall_bulk_metadata",
                new Dictionary<string, object?> { ["freshnessPolicy"] = "refresh" },
                token).ConfigureAwait(false);
            Assert.Equal(
                ["all_cards", "rulings", "oracle_tags", "art_tags"],
                metadata.GetProperty("datasets").EnumerateArray().Select(value => value.GetProperty("type").GetString()));

            JsonElement obeka = await CallSuccessAsync(
                environment,
                session,
                "scryfall_card_get",
                new Dictionary<string, object?>
                {
                    ["lookup"] = new { kind = "exact-name", value = "Obeka, Splitter of Seconds" },
                    ["freshnessPolicy"] = "cache-only",
                    ["includeRaw"] = true,
                },
                token).ConfigureAwait(false);
            JsonElement obekaCard = obeka.GetProperty("card");
            Assert.Equal("Obeka, Splitter of Seconds", obekaCard.GetProperty("name").GetString());
            Assert.Equal("corpus", obeka.GetProperty("origin").GetString());
            Assert.True(obekaCard.TryGetProperty("raw", out _));
            Guid obekaCardId = obekaCard.GetProperty("id").GetGuid();
            Guid obekaOracleId = obekaCard.GetProperty("oracleId").GetGuid();

            object[] collectionLookups =
            [
                new { kind = "exact-name", value = "Obeka, Splitter of Seconds" },
                new { kind = "exact-name", value = "Sol Ring" },
                new { kind = "exact-name", value = "Arcane Signet" },
                new { kind = "exact-name", value = "Paradox Haze" },
                new { kind = "scryfall-id", value = "00000000-0000-4000-8000-000000000001" },
            ];
            JsonElement collectionFirst = await CallSuccessAsync(
                environment,
                session,
                "scryfall_card_collection",
                new Dictionary<string, object?>
                {
                    ["lookups"] = collectionLookups,
                    ["freshnessPolicy"] = "refresh",
                    ["pageSize"] = 2,
                },
                token).ConfigureAwait(false);
            JsonElement firstPage = collectionFirst.GetProperty("page");
            Assert.Equal(5, firstPage.GetProperty("totalCount").GetInt32());
            Assert.Equal([0, 1], firstPage.GetProperty("items").EnumerateArray().Select(value => value.GetProperty("index").GetInt32()));
            string collectionCursor = firstPage.GetProperty("nextCursor").GetString()!;
            JsonElement collectionRest = await CallSuccessAsync(
                environment,
                session,
                "scryfall_card_collection",
                new Dictionary<string, object?>
                {
                    ["lookups"] = collectionLookups,
                    ["freshnessPolicy"] = "cache-only",
                    ["cursor"] = collectionCursor,
                    ["pageSize"] = 100,
                },
                token).ConfigureAwait(false);
            JsonElement[] remainingRows = collectionRest.GetProperty("page").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal([2, 3, 4], remainingRows.Select(value => value.GetProperty("index").GetInt32()));
            Assert.Equal("not-found", remainingRows[^1].GetProperty("status").GetString());

            JsonElement prints = await CallSuccessAsync(
                environment,
                session,
                "scryfall_card_prints",
                new Dictionary<string, object?>
                {
                    ["oracleId"] = obekaOracleId,
                    ["freshnessPolicy"] = "cache-only",
                    ["pageSize"] = 2,
                },
                token).ConfigureAwait(false);
            Assert.NotEmpty(prints.GetProperty("page").GetProperty("items").EnumerateArray());

            JsonElement rulings = await CallSuccessAsync(
                environment,
                session,
                "scryfall_card_rulings",
                new Dictionary<string, object?>
                {
                    ["oracleId"] = obekaOracleId,
                    ["scryfallCardId"] = obekaCardId,
                    ["freshnessPolicy"] = "cache-only",
                },
                token).ConfigureAwait(false);
            Assert.NotEqual(Guid.Empty, rulings.GetProperty("corpusGenerationId").GetGuid());
            Assert.False(rulings.TryGetProperty("snapshot", out _));

            JsonElement sets = await CallSuccessAsync(
                environment,
                session,
                "scryfall_sets",
                new Dictionary<string, object?>
                {
                    ["freshnessPolicy"] = "refresh",
                    ["pageSize"] = 2,
                },
                token).ConfigureAwait(false);
            JsonElement firstSet = sets.GetProperty("page").GetProperty("items")[0];
            string setCode = firstSet.GetProperty("code").GetString()!;
            JsonElement exactSet = await CallSuccessAsync(
                environment,
                session,
                "scryfall_sets",
                new Dictionary<string, object?>
                {
                    ["codeOrId"] = setCode,
                    ["freshnessPolicy"] = "default",
                    ["pageSize"] = 25,
                },
                token).ConfigureAwait(false);
            Assert.Contains(
                exactSet.GetProperty("page").GetProperty("items").EnumerateArray(),
                value => value.GetProperty("code").GetString() == setCode);

            JsonElement catalog = await CallSuccessAsync(
                environment,
                session,
                "scryfall_catalog",
                new Dictionary<string, object?>
                {
                    ["catalog"] = "card-types",
                    ["freshnessPolicy"] = "refresh",
                    ["pageSize"] = 5,
                },
                token).ConfigureAwait(false);
            Assert.NotEmpty(catalog.GetProperty("page").GetProperty("items").EnumerateArray());

            JsonElement autocomplete = await CallSuccessAsync(
                environment,
                session,
                "scryfall_autocomplete",
                new Dictionary<string, object?>
                {
                    ["query"] = "Obek",
                    ["freshnessPolicy"] = "refresh",
                    ["pageSize"] = 25,
                },
                token).ConfigureAwait(false);
            Assert.Contains(
                autocomplete.GetProperty("page").GetProperty("items").EnumerateArray(),
                value => value.GetString()!.Contains("Obeka", StringComparison.OrdinalIgnoreCase));

            const string query = "!\"Obeka, Splitter of Seconds\" or !\"Paradox Haze\"";
            JsonElement searchFirst = await CallSuccessAsync(
                environment,
                session,
                "scryfall_search",
                new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["freshnessPolicy"] = "refresh",
                    ["pageSize"] = 1,
                },
                token).ConfigureAwait(false);
            JsonElement searchPage = searchFirst.GetProperty("page");
            Assert.True(searchPage.GetProperty("totalCount").GetInt32() >= 2);
            string searchCursor = searchPage.GetProperty("nextCursor").GetString()!;
            JsonElement searchRest = await CallSuccessAsync(
                environment,
                session,
                "scryfall_search",
                new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["freshnessPolicy"] = "cache-only",
                    ["cursor"] = searchCursor,
                    ["pageSize"] = 100,
                },
                token).ConfigureAwait(false);
            Assert.NotEmpty(searchRest.GetProperty("page").GetProperty("items").EnumerateArray());
            Guid searchSnapshotId = searchFirst.GetProperty("snapshot").GetProperty("snapshotId").GetGuid();
            string searchSnapshotChecksum = searchFirst.GetProperty("snapshot").GetProperty("checksum").GetString()!;

            JsonElement tags = await CallSuccessAsync(
                environment,
                session,
                "scryfall_tag_search",
                new Dictionary<string, object?>
                {
                    ["query"] = "extra-upkeep",
                    ["tagType"] = "oracle",
                    ["pageSize"] = 25,
                },
                token).ConfigureAwait(false);
            JsonElement extraUpkeep = tags.GetProperty("items").EnumerateArray()
                .Single(value => value.GetProperty("slug").GetString() == "extra-upkeep");
            string tagIdentity = extraUpkeep.GetProperty("id").GetString()!;
            JsonElement cardsByTag = await CallSuccessAsync(
                environment,
                session,
                "scryfall_cards_by_tag",
                new Dictionary<string, object?>
                {
                    ["tagIdentity"] = tagIdentity,
                    ["tagType"] = "oracle",
                    ["includeDescendants"] = false,
                    ["minimumWeight"] = "weak",
                    ["pageSize"] = 25,
                },
                token).ConfigureAwait(false);
            Assert.NotEmpty(cardsByTag.GetProperty("assignments").EnumerateArray());

            JsonElement snapshots = await CallSuccessAsync(
                environment,
                session,
                "scryfall_snapshot_list",
                new Dictionary<string, object?> { ["operation"] = "search", ["pageSize"] = 25 },
                token).ConfigureAwait(false);
            Assert.Contains(
                snapshots.GetProperty("items").EnumerateArray(),
                value => value.GetProperty("snapshotId").GetGuid() == searchSnapshotId);
            JsonElement replay = await CallSuccessAsync(
                environment,
                session,
                "scryfall_snapshot_get",
                new Dictionary<string, object?>
                {
                    ["snapshotId"] = searchSnapshotId,
                    ["includeRaw"] = true,
                    ["pageSize"] = 25,
                },
                token).ConfigureAwait(false);
            Assert.Equal(searchSnapshotId, replay.GetProperty("summary").GetProperty("snapshotId").GetGuid());
            Assert.NotEmpty(replay.GetProperty("items").EnumerateArray());

            _ = await CallSuccessAsync(
                environment,
                session,
                "scryfall_snapshot_delete",
                new Dictionary<string, object?>
                {
                    ["snapshotId"] = searchSnapshotId,
                    ["expectedChecksum"] = searchSnapshotChecksum,
                    ["acknowledgeDataLoss"] = true,
                },
                token).ConfigureAwait(false);
            JsonElement deleted = await CallResultAsync(
                session,
                "scryfall_snapshot_get",
                new Dictionary<string, object?> { ["snapshotId"] = searchSnapshotId },
                token).ConfigureAwait(false);
            Assert.Equal("not-found", deleted.GetProperty("kind").GetString());
        }
        finally
        {
            await DeletePhaseRootAsync(dataRoot, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronizes two real corpus generations, proves reversible activation, and deletes only the scratch corpus.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    [Trait("Category", "ManualCorpus")]
    public async Task ScryfallCorpusMethods_ProveTwoGenerationLifecycle()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_FULL_SCRYFALL_CORPUS"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_FULL_SCRYFALL_CORPUS=1 to run the multi-gigabyte corpus lifecycle.");
        }

        CancellationToken token = TestContext.Current.CancellationToken;
        LiveAcceptanceEnvironment environment = await LiveAcceptanceEnvironment.RequireAsync(token).ConfigureAwait(false);
        string dataRoot = environment.PreparePersistentPhaseRoot("scryfall-corpus-lifecycle");
        CorpusSourceState sourceState = await EnsurePersistentCorpusScratchAsync(dataRoot, token).ConfigureAwait(false);
        await using McpProcessSession session = await StartLiveAsync(dataRoot, token).ConfigureAwait(false);

        JsonElement before = await CallSuccessAsync(
            environment,
            session,
            "scryfall_corpus_status",
            EmptyArguments(),
            token).ConfigureAwait(false);
        Assert.Equal("available", before.GetProperty("state").GetString());
        Guid expectedActive = before.GetProperty("active").GetProperty("generationId").GetGuid();

        JsonElement sync = await CallSuccessAsync(
            environment,
            session,
            "scryfall_corpus_sync",
            new Dictionary<string, object?>
            {
                ["metadataPolicy"] = "refresh",
                ["expectedActiveGeneration"] = expectedActive,
            },
            token).ConfigureAwait(false);
        Assert.Equal(4, sync.GetProperty("datasets").GetArrayLength());
        Guid activeGeneration = sync.GetProperty("activeGenerationId").GetGuid();
        if (!sync.TryGetProperty("previousGenerationId", out JsonElement previousValue) ||
            previousValue.ValueKind == JsonValueKind.Null)
        {
            await environment.Journal.RecordAsync(
                "scryfall_corpus_rollback",
                "pending-provider-generation",
                "provider-has-not-published-a-new-generation",
                token).ConfigureAwait(false);
            Assert.Skip("The scratch corpus is current; rerun after Scryfall publishes a newer generation.");
        }

        Guid previousGeneration = previousValue.GetGuid();
        JsonElement rolledBack = await CallSuccessAsync(
            environment,
            session,
            "scryfall_corpus_rollback",
            new Dictionary<string, object?>
            {
                ["expectedActiveGeneration"] = activeGeneration,
                ["expectedPreviousGeneration"] = previousGeneration,
                ["acknowledgeActivationChange"] = true,
            },
            token).ConfigureAwait(false);
        JsonElement restored = await CallSuccessAsync(
            environment,
            session,
            "scryfall_corpus_rollback",
            new Dictionary<string, object?>
            {
                ["expectedActiveGeneration"] = rolledBack.GetProperty("activeGenerationId").GetGuid(),
                ["expectedPreviousGeneration"] = rolledBack.GetProperty("previousGenerationId").GetGuid(),
                ["acknowledgeActivationChange"] = true,
            },
            token).ConfigureAwait(false);
        Assert.Equal(activeGeneration, restored.GetProperty("activeGenerationId").GetGuid());

        _ = await CallSuccessAsync(
            environment,
            session,
            "scryfall_corpus_delete",
            new Dictionary<string, object?>
            {
                ["expectedActiveGeneration"] = activeGeneration,
                ["acknowledgeDataLoss"] = true,
            },
            token).ConfigureAwait(false);
        JsonElement after = await CallSuccessAsync(
            environment,
            session,
            "scryfall_corpus_status",
            EmptyArguments(),
            token).ConfigureAwait(false);
        Assert.Equal("not-cached", after.GetProperty("state").GetString());
        await VerifyCorpusSourceUnchangedAsync(sourceState, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Exercises every Archidekt method while restoring the owner-selected deck to its exact baseline content.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task ArchidektMethods_MutateAndRestoreOwnerAuthorizedDeck()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        LiveAcceptanceEnvironment environment = await LiveAcceptanceEnvironment.RequireAsync(token).ConfigureAwait(false);
        string dataRoot = environment.PrepareEphemeralPhaseRoot("archidekt-methods");
        McpProcessSession session = await StartLiveAsync(dataRoot, token).ConfigureAwait(false);

        string? recoverySnapshotId = null;
        string? temporaryDeckId = null;
        string? temporaryDeckInitialFolderId = null;
        string? temporaryFolderId = null;
        string? temporaryFolderName = null;
        JsonElement baseline = default;
        bool baselineCaptured = false;
        bool baselineRestored = false;
        try
        {
            JsonElement auth = await CallSuccessAsync(
                environment,
                session,
                "archidekt_auth_status",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.True(auth.GetProperty("credentialsConfigured").GetBoolean());

            baseline = await CallSuccessAsync(
                environment,
                session,
                "archidekt_deck_get",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId },
                token).ConfigureAwait(false);
            baselineCaptured = true;
            Assert.Equal(ArchidektDeckId, baseline.GetProperty("remoteId").GetString());
            string baselineContentFingerprint = baseline.GetProperty("contentFingerprint").GetString()!;
            baselineRestored = true;
            await CleanupStaleArchidektRecoverySnapshotsAsync(
                session,
                token).ConfigureAwait(false);

            JsonElement decks = await CallSuccessAsync(
                environment,
                session,
                "archidekt_deck_list",
                new Dictionary<string, object?> { ["pageSize"] = 100 },
                token).ConfigureAwait(false);
            Assert.Contains(
                decks.GetProperty("items").EnumerateArray(),
                value => value.GetProperty("remoteId").GetString() == ArchidektDeckId);

            auth = await CallSuccessAsync(
                environment,
                session,
                "archidekt_auth_status",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.True(auth.GetProperty("sessionAuthenticated").GetBoolean());

            string suffix = Guid.NewGuid().ToString("N")[..10];
            JsonElement snapshot = await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_create",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        deckId = ArchidektDeckId,
                        expectedRemoteFingerprint = baseline.GetProperty("remoteFingerprint").GetString(),
                        name = $"mtg-mcp acceptance recovery {suffix}",
                        description = "Retain until method acceptance restores the original deck.",
                    },
                },
                token).ConfigureAwait(false);
            recoverySnapshotId = snapshot.GetProperty("snapshotId").GetString();

            JsonElement snapshots = await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_list",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId },
                token).ConfigureAwait(false);
            Assert.Contains(
                snapshots.GetProperty("items").EnumerateArray(),
                value => value.GetProperty("snapshotId").GetString() == recoverySnapshotId);

            JsonElement fullSnapshot = await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_get",
                new Dictionary<string, object?>
                {
                    ["deckId"] = ArchidektDeckId,
                    ["snapshotId"] = recoverySnapshotId,
                },
                token).ConfigureAwait(false);
            JsonElement unchangedRestore = RequireSuccess(await CallResultAsync(
                session,
                "archidekt_snapshot_restore_preview",
                new Dictionary<string, object?>
                {
                    ["deckId"] = ArchidektDeckId,
                    ["snapshotId"] = recoverySnapshotId,
                },
                token).ConfigureAwait(false));
            Assert.Empty(unchangedRestore.GetProperty("differences").EnumerateArray());
            Assert.Empty(unchangedRestore.GetProperty("operations").EnumerateArray());

            snapshot = await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_update",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        deckId = ArchidektDeckId,
                        snapshotId = recoverySnapshotId,
                        expectedChecksum = fullSnapshot.GetProperty("summary").GetProperty("checksum").GetString(),
                        name = $"mtg-mcp recovery {suffix}",
                    },
                },
                token).ConfigureAwait(false);

            JsonElement pullPreview = await CallSuccessAsync(
                environment,
                session,
                "archidekt_pull_preview",
                new Dictionary<string, object?> { ["remoteDeckId"] = ArchidektDeckId },
                token).ConfigureAwait(false);
            JsonElement pull = await CallSuccessAsync(
                environment,
                session,
                "archidekt_pull_apply",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        remoteDeckId = ArchidektDeckId,
                        localDeckId = (Guid?)null,
                        expectedLocalRevision = (long?)null,
                        expectedRemoteFingerprint = pullPreview.GetProperty("remoteFingerprint").GetString(),
                        previewFingerprint = pullPreview.GetProperty("previewFingerprint").GetString(),
                    },
                },
                token).ConfigureAwait(false);
            Guid localDeckId = pull.GetProperty("localDeckId").GetGuid();
            long localRevision = pull.GetProperty("localRevision").GetInt64();

            JsonElement initialDiff = await CallSuccessAsync(
                environment,
                session,
                "archidekt_sync_diff",
                new Dictionary<string, object?> { ["localDeckId"] = localDeckId },
                token).ConfigureAwait(false);
            Assert.False(initialDiff.GetProperty("hasConflicts").GetBoolean());
            Assert.Empty(initialDiff.GetProperty("differences").EnumerateArray());

            List<Guid> addedEntries = [];
            foreach (string cardName in new[] { "Sol Ring", "Arcane Signet", "Paradox Haze" })
            {
                JsonElement cardResult = await CallSuccessAsync(
                    environment,
                    session,
                    "scryfall_card_get",
                    new Dictionary<string, object?>
                    {
                        ["lookup"] = new { kind = "exact-name", value = cardName },
                        ["freshnessPolicy"] = "refresh",
                    },
                    token).ConfigureAwait(false);
                JsonElement card = cardResult.GetProperty("card");
                JsonElement localDeck = await CallSuccessAsync(
                    environment,
                    session,
                    "deck_entry_add",
                    new Dictionary<string, object?>
                    {
                        ["deckId"] = localDeckId,
                        ["expectedRevision"] = localRevision,
                        ["entry"] = new
                        {
                            quantity = 1,
                            cardName,
                            oracleId = card.GetProperty("oracleId").GetGuid(),
                            printingId = card.GetProperty("id").GetGuid(),
                            setCode = card.GetProperty("setCode").GetString(),
                            collectorNumber = card.GetProperty("collectorNumber").GetString(),
                            language = card.GetProperty("language").GetString(),
                            finish = "nonfoil",
                            zone = "main",
                        },
                    },
                    token).ConfigureAwait(false);
                localRevision = localDeck.GetProperty("revision").GetInt64();
                addedEntries.Add(localDeck.GetProperty("entries").EnumerateArray()
                    .Last(value => value.GetProperty("cardName").GetString() == cardName)
                    .GetProperty("entryId").GetGuid());
            }

            JsonElement categorizedDeck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_create",
                new Dictionary<string, object?>
                {
                    ["deckId"] = localDeckId,
                    ["expectedRevision"] = localRevision,
                    ["category"] = new { name = "Acceptance Added", color = "#6633ff", sortOrder = 0 },
                },
                token).ConfigureAwait(false);
            localRevision = categorizedDeck.GetProperty("revision").GetInt64();
            Guid categoryId = categorizedDeck.GetProperty("categories").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == "Acceptance Added")
                .GetProperty("categoryId").GetGuid();
            Guid commanderCategoryId = categorizedDeck.GetProperty("categories").EnumerateArray()
                .Single(value => value.GetProperty("name").GetString() == "Commander")
                .GetProperty("categoryId").GetGuid();
            categorizedDeck = await CallSuccessAsync(
                environment,
                session,
                "deck_category_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = localDeckId,
                    ["expectedRevision"] = localRevision,
                    ["category"] = new
                    {
                        categoryId = commanderCategoryId,
                        name = "Commander",
                        color = (string?)null,
                        sortOrder = 1,
                    },
                },
                token).ConfigureAwait(false);
            localRevision = categorizedDeck.GetProperty("revision").GetInt64();
            foreach (Guid entryId in addedEntries)
            {
                categorizedDeck = await CallSuccessAsync(
                    environment,
                    session,
                    "deck_category_assign",
                    new Dictionary<string, object?>
                    {
                        ["deckId"] = localDeckId,
                        ["expectedRevision"] = localRevision,
                        ["entryId"] = entryId,
                        ["categoryId"] = categoryId,
                        ["isPrimary"] = true,
                    },
                    token).ConfigureAwait(false);
                localRevision = categorizedDeck.GetProperty("revision").GetInt64();
            }

            JsonElement pushPreview = await CallSuccessAsync(
                environment,
                session,
                "archidekt_push_preview",
                new Dictionary<string, object?> { ["localDeckId"] = localDeckId },
                token).ConfigureAwait(false);
            Assert.NotEmpty(pushPreview.GetProperty("operations").EnumerateArray());
            JsonElement stalePush = await CallResultAsync(
                session,
                "archidekt_push_apply",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        localDeckId,
                        expectedLocalRevision = localRevision,
                        expectedRemoteFingerprint = new string('0', 64),
                        previewFingerprint = pushPreview.GetProperty("previewFingerprint").GetString(),
                    },
                },
                token).ConfigureAwait(false);
            Assert.Equal("conflict", stalePush.GetProperty("kind").GetString());

            baselineRestored = false;
            JsonElement pushed = RequireSuccess(await CallResultAsync(
                session,
                "archidekt_push_apply",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        localDeckId,
                        expectedLocalRevision = localRevision,
                        expectedRemoteFingerprint = pushPreview.GetProperty("remoteFingerprint").GetString(),
                        previewFingerprint = pushPreview.GetProperty("previewFingerprint").GetString(),
                    },
                },
                token).ConfigureAwait(false));
            if (!string.Equals(pushed.GetProperty("outcome").GetString(), "applied", StringComparison.Ordinal))
            {
                JsonElement residual = RequireSuccess(await CallResultAsync(
                    session,
                    "archidekt_push_preview",
                    new Dictionary<string, object?> { ["localDeckId"] = localDeckId },
                    token).ConfigureAwait(false));
                JsonElement observedRemote = RequireSuccess(await CallResultAsync(
                    session,
                    "archidekt_deck_get",
                    new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId },
                    token).ConfigureAwait(false));
                JsonElement observedLocal = RequireSuccess(await CallResultAsync(
                    session,
                    "deck_get",
                    new Dictionary<string, object?> { ["deckId"] = localDeckId },
                    token).ConfigureAwait(false));
                string diagnostic = JsonSerializer.Serialize(
                    new { pushed, residual, observedRemote, observedLocal },
                    SourceStateJsonOptions);
                await File.WriteAllTextAsync(
                    Path.Combine(environment.RootPath, "archidekt-push-diagnostic.json"),
                    diagnostic + Environment.NewLine,
                    token).ConfigureAwait(false);
                string attempted = string.Join(
                    ", ",
                    pushed.GetProperty("operations").EnumerateArray().Select(value =>
                        $"{value.GetProperty("kind").GetString()}:{value.GetProperty("subject").GetString()}={value.GetProperty("status").GetString()}"));
                string remaining = string.Join(
                    ", ",
                    residual.GetProperty("operations").EnumerateArray().Select(value =>
                        $"{value.GetProperty("kind").GetString()}:{value.GetProperty("subject").GetString()}"));
                Assert.Fail($"Archidekt push verification failed; attempted [{attempted}], remaining [{remaining}].");
            }

            await environment.Journal.RecordAsync(
                "archidekt_push_apply",
                "live-pass",
                "packaged-mcp-call-passed",
                token).ConfigureAwait(false);

            JsonElement cleanDiff = await CallSuccessAsync(
                environment,
                session,
                "archidekt_sync_diff",
                new Dictionary<string, object?> { ["localDeckId"] = localDeckId },
                token).ConfigureAwait(false);
            Assert.False(cleanDiff.GetProperty("hasConflicts").GetBoolean());
            Assert.Empty(cleanDiff.GetProperty("differences").EnumerateArray());

            await RestoreArchidektBaselineAsync(
                environment,
                session,
                baselineContentFingerprint,
                recoverySnapshotId!,
                recordMethods: true,
                token).ConfigureAwait(false);
            baselineRestored = true;

            fullSnapshot = await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_get",
                new Dictionary<string, object?>
                {
                    ["deckId"] = ArchidektDeckId,
                    ["snapshotId"] = recoverySnapshotId,
                },
                token).ConfigureAwait(false);
            _ = await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_delete",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        deckId = ArchidektDeckId,
                        snapshotId = recoverySnapshotId,
                        expectedChecksum = fullSnapshot.GetProperty("summary").GetProperty("checksum").GetString(),
                        confirmation = $"delete snapshot {recoverySnapshotId}",
                    },
                },
                token).ConfigureAwait(false);
            recoverySnapshotId = null;

            JsonElement temporaryDeck = await CallSuccessAsync(
                environment,
                session,
                "archidekt_deck_create",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        name = $"mtg-mcp disposable method acceptance {suffix}",
                        format = "commander",
                        description = "Safe to delete after method acceptance.",
                        visibility = "private",
                    },
                },
                token).ConfigureAwait(false);
            temporaryDeckId = temporaryDeck.GetProperty("remoteId").GetString();
            temporaryDeckInitialFolderId = temporaryDeck.TryGetProperty(
                "parentFolderId",
                out JsonElement initialParentFolder)
                ? initialParentFolder.GetString()
                : null;
            Assert.Equal("private", temporaryDeck.GetProperty("visibility").GetString());

            temporaryFolderName = $"mcp method {suffix}";
            JsonElement temporaryFolder = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_create",
                new Dictionary<string, object?>
                {
                    ["request"] = new { name = temporaryFolderName, visibility = "private" },
                },
                token).ConfigureAwait(false);
            temporaryFolderId = temporaryFolder.GetProperty("folderId").GetString();

            JsonElement folderTree = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.Contains(
                folderTree.GetProperty("items").EnumerateArray(),
                value => value.GetProperty("folderId").GetString() == temporaryFolderId);
            JsonElement folderDetail = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_get",
                new Dictionary<string, object?> { ["folderId"] = temporaryFolderId },
                token).ConfigureAwait(false);
            Assert.Single(folderDetail.GetProperty("items").EnumerateArray());

            temporaryFolderName = $"mcp method r {suffix}";
            temporaryFolder = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_update",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        folderId = temporaryFolderId,
                        expectedTreeFingerprint = folderTree.GetProperty("treeFingerprint").GetString(),
                        name = temporaryFolderName,
                    },
                },
                token).ConfigureAwait(false);

            folderTree = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            JsonElement moved = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_move_items",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        expectedTreeFingerprint = folderTree.GetProperty("treeFingerprint").GetString(),
                        items = new[]
                        {
                            new
                            {
                                kind = "deck",
                                id = temporaryDeckId,
                                expectedParentFolderId = temporaryDeckInitialFolderId,
                            },
                        },
                        destinationFolderId = temporaryFolderId,
                    },
                },
                token).ConfigureAwait(false);
            Assert.Equal("applied", moved.GetProperty("items")[0].GetProperty("status").GetString());

            temporaryDeck = await CallSuccessAsync(
                environment,
                session,
                "archidekt_deck_get",
                new Dictionary<string, object?> { ["deckId"] = temporaryDeckId },
                token).ConfigureAwait(false);
            folderTree = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            moved = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_move_items",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        expectedTreeFingerprint = folderTree.GetProperty("treeFingerprint").GetString(),
                        items = new[]
                        {
                            new
                            {
                                kind = "deck",
                                id = temporaryDeckId,
                                expectedParentFolderId = temporaryFolderId,
                            },
                        },
                        destinationFolderId = (string?)null,
                    },
                },
                token).ConfigureAwait(false);
            Assert.Equal("applied", moved.GetProperty("items")[0].GetProperty("status").GetString());

            temporaryDeck = await CallSuccessAsync(
                environment,
                session,
                "archidekt_deck_get",
                new Dictionary<string, object?> { ["deckId"] = temporaryDeckId },
                token).ConfigureAwait(false);
            _ = await CallSuccessAsync(
                environment,
                session,
                "archidekt_deck_delete",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        deckId = temporaryDeckId,
                        expectedRemoteFingerprint = temporaryDeck.GetProperty("remoteFingerprint").GetString(),
                        confirmation = $"delete {temporaryDeckId}",
                    },
                },
                token).ConfigureAwait(false);
            temporaryDeckId = null;

            folderTree = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_list",
                EmptyArguments(),
                token).ConfigureAwait(false);
            _ = await CallSuccessAsync(
                environment,
                session,
                "archidekt_folder_delete",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        folderId = temporaryFolderId,
                        expectedName = temporaryFolderName,
                        expectedTreeFingerprint = folderTree.GetProperty("treeFingerprint").GetString(),
                        confirmation = $"delete folder {temporaryFolderId}",
                    },
                },
                token).ConfigureAwait(false);
            temporaryFolderId = null;
        }
        finally
        {
            try
            {
                if (baselineCaptured && !baselineRestored && recoverySnapshotId is not null)
                {
                    await RestoreArchidektBaselineAsync(
                        environment,
                        session,
                        baseline.GetProperty("contentFingerprint").GetString()!,
                        recoverySnapshotId,
                        recordMethods: false,
                        token).ConfigureAwait(false);
                    baselineRestored = true;
                }

                await CleanupArchidektResourcesAsync(
                    session,
                    recoverySnapshotId,
                    temporaryDeckId,
                    temporaryFolderId,
                    temporaryFolderName,
                    baselineRestored,
                    token).ConfigureAwait(false);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
                await DeletePhaseRootAsync(dataRoot, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Exercises all fourteen safe Playgroup methods and follows one real game's linked evidence.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task PlaygroupReadMethods_FollowAuthenticatedGameEvidence()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        LiveAcceptanceEnvironment environment = await LiveAcceptanceEnvironment.RequireAsync(token).ConfigureAwait(false);
        await environment.Journal.RecordFixtureOnlyWritesAsync(token).ConfigureAwait(false);
        string dataRoot = environment.PrepareEphemeralPhaseRoot("playgroup-read-methods");
        try
        {
            await using McpProcessSession session = await StartLiveAsync(dataRoot, token).ConfigureAwait(false);
            JsonElement auth = await CallSuccessAsync(
                environment,
                session,
                "playgroup_auth_status",
                EmptyArguments(),
                token).ConfigureAwait(false);
            Assert.True(auth.GetProperty("credentialsConfigured").GetBoolean());

            JsonElement meEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_me_get",
                EmptyArguments(),
                token).ConfigureAwait(false);
            JsonElement me = ProviderData(meEvidence, "getCurrentUser");
            int userId = me.GetProperty("id").GetInt32();

            JsonElement userEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_user_get",
                new Dictionary<string, object?> { ["userId"] = userId },
                token).ConfigureAwait(false);
            Assert.Equal(userId, ProviderData(userEvidence, "getUserById").GetProperty("id").GetInt32());

            JsonElement decksEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_user_decks_list",
                new Dictionary<string, object?> { ["userId"] = userId, ["includeArchived"] = false },
                token).ConfigureAwait(false);
            Assert.Equal(JsonValueKind.Array, ProviderData(decksEvidence, "listUserDecks").ValueKind);

            JsonElement playgroupsEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_user_playgroups_list",
                new Dictionary<string, object?> { ["userId"] = userId },
                token).ConfigureAwait(false);
            JsonElement playgroups = ProviderData(playgroupsEvidence, "listUserPlaygroups");
            Assert.Contains(playgroups.EnumerateArray(), value => value.GetProperty("id").GetInt32() == PlaygroupId);

            JsonElement playgroupEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_user_playgroup_get",
                new Dictionary<string, object?> { ["userId"] = userId, ["playgroupId"] = PlaygroupId },
                token).ConfigureAwait(false);
            Assert.Equal(
                PlaygroupId,
                ProviderData(playgroupEvidence, "getUserPlaygroup").GetProperty("id").GetInt32());

            JsonElement membersEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_playgroup_members_list",
                new Dictionary<string, object?> { ["playgroupId"] = PlaygroupId },
                token).ConfigureAwait(false);
            JsonElement members = ProviderData(membersEvidence, "listPlaygroupMembers");
            Assert.Contains(members.EnumerateArray(), value => value.GetProperty("user_id").GetInt32() == userId);

            JsonElement gamesEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_playgroup_games_list",
                new Dictionary<string, object?>
                {
                    ["playgroupId"] = PlaygroupId,
                    ["page"] = 1,
                    ["limit"] = 100,
                    ["includeEvents"] = false,
                },
                token).ConfigureAwait(false);
            JsonElement games = ProviderData(gamesEvidence, "listPlaygroupGames");
            (int GameId, int DeckId, int LinkedUserId)? fixture = FindPlaygroupFixture(games);
            if (fixture is null)
            {
                foreach (string toolName in new[]
                         {
                             "playgroup_playgroup_game_get",
                             "playgroup_deck_get",
                             "playgroup_deck_elo_history_get",
                             "playgroup_commander_get",
                             "playgroup_commander_get_by_name",
                         })
                {
                    await environment.Journal.RecordAsync(
                        toolName,
                        "fixture-unavailable",
                        "no-game-with-linked-user-and-deck",
                        token).ConfigureAwait(false);
                }

                Assert.Skip("Playgroup 49295 has no completed game with linked user and deck identities.");
            }

            JsonElement gameEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_playgroup_game_get",
                new Dictionary<string, object?>
                {
                    ["playgroupId"] = PlaygroupId,
                    ["gameId"] = fixture.Value.GameId,
                    ["includeEvents"] = true,
                },
                token).ConfigureAwait(false);
            JsonElement game = ProviderData(gameEvidence, "getPlaygroupGame");
            Assert.Equal(fixture.Value.GameId, game.GetProperty("id").GetInt32());

            JsonElement linkedUserEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_user_get",
                new Dictionary<string, object?> { ["userId"] = fixture.Value.LinkedUserId },
                token).ConfigureAwait(false);
            Assert.Equal(
                fixture.Value.LinkedUserId,
                ProviderData(linkedUserEvidence, "getUserById").GetProperty("id").GetInt32());

            JsonElement deckEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_deck_get",
                new Dictionary<string, object?> { ["deckId"] = fixture.Value.DeckId, ["includeArchived"] = true },
                token).ConfigureAwait(false);
            JsonElement deck = ProviderData(deckEvidence, "getDeckById");
            Assert.Equal(fixture.Value.DeckId, deck.GetProperty("id").GetInt32());

            JsonElement eloEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_deck_elo_history_get",
                new Dictionary<string, object?>
                {
                    ["deckId"] = fixture.Value.DeckId,
                    ["playgroupId"] = PlaygroupId,
                    ["includeArchived"] = true,
                },
                token).ConfigureAwait(false);
            _ = ProviderData(eloEvidence, "getDeckEloHistory");

            Assert.True(deck.TryGetProperty("commander", out JsonElement commander));
            Assert.Equal(JsonValueKind.Object, commander.ValueKind);
            int commanderId = commander.GetProperty("id").GetInt32();
            string commanderName = commander.GetProperty("name").GetString()!;
            JsonElement commanderEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_commander_get",
                new Dictionary<string, object?> { ["commanderId"] = commanderId },
                token).ConfigureAwait(false);
            Assert.Equal(
                commanderId,
                ProviderData(commanderEvidence, "getCommanderById").GetProperty("id").GetInt32());

            JsonElement commanderByNameEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_commander_get_by_name",
                new Dictionary<string, object?> { ["name"] = commanderName },
                token).ConfigureAwait(false);
            Assert.Equal(
                commanderId,
                ProviderData(commanderByNameEvidence, "getCommanderByName").GetProperty("id").GetInt32());

            JsonElement damageEvidence = await CallSuccessAsync(
                environment,
                session,
                "playgroup_commander_turn_damage_get",
                new Dictionary<string, object?> { ["commanderId"] = commanderId },
                token).ConfigureAwait(false);
            Assert.Equal(
                commanderId,
                ProviderData(damageEvidence, "getCommandersTurnDamage").GetProperty("id").GetInt32());
        }
        finally
        {
            await DeletePhaseRootAsync(dataRoot, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Validates one provider evidence envelope and returns its lossless data element.
    /// </summary>
    private static JsonElement ProviderData(JsonElement evidence, string expectedOperationId)
    {
        Assert.Equal(expectedOperationId, evidence.GetProperty("operationId").GetString());
        Assert.Equal("1.0.0", evidence.GetProperty("apiVersion").GetString());
        Assert.Matches("^[0-9a-f]{64}$", evidence.GetProperty("contractChecksum").GetString());
        Assert.Matches("^[0-9a-f]{64}$", evidence.GetProperty("sourceChecksum").GetString());
        Assert.False(string.IsNullOrWhiteSpace(evidence.GetProperty("endpoint").GetString()));
        return evidence.GetProperty("data");
    }

    /// <summary>
    /// Selects the newest listed game participation with the identities required by downstream reads.
    /// </summary>
    private static (int GameId, int DeckId, int LinkedUserId)? FindPlaygroupFixture(JsonElement games)
    {
        foreach (JsonElement game in games.EnumerateArray())
        {
            if (!game.TryGetProperty("id", out JsonElement gameId) ||
                !game.TryGetProperty("participations", out JsonElement participations))
            {
                continue;
            }

            foreach (JsonElement participation in participations.EnumerateArray())
            {
                if (participation.TryGetProperty("deck_id", out JsonElement deckId) &&
                    deckId.ValueKind == JsonValueKind.Number &&
                    participation.TryGetProperty("user_id", out JsonElement linkedUserId) &&
                    linkedUserId.ValueKind == JsonValueKind.Number)
                {
                    return (gameId.GetInt32(), deckId.GetInt32(), linkedUserId.GetInt32());
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Removes one known ephemeral phase root after bounded retries for Windows process-handle release.
    /// </summary>
    private static async Task DeletePhaseRootAsync(string dataRoot, CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (!Directory.Exists(dataRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(dataRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }

        Assert.False(Directory.Exists(dataRoot), "The ephemeral live-acceptance phase directory could not be removed.");
    }

    /// <summary>
    /// Removes only a prior acceptance snapshot after proving it contains the still-current baseline.
    /// </summary>
    private static async Task CleanupStaleArchidektRecoverySnapshotsAsync(
        McpProcessSession session,
        CancellationToken cancellationToken)
    {
        JsonElement page = RequireSuccess(await CallResultAsync(
            session,
            "archidekt_snapshot_list",
            new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId },
            cancellationToken).ConfigureAwait(false));
        foreach (JsonElement summary in page.GetProperty("items").EnumerateArray())
        {
            string name = summary.GetProperty("name").GetString()!;
            if (!name.StartsWith("mtg-mcp acceptance recovery", StringComparison.Ordinal) &&
                !name.StartsWith("mtg-mcp recovery", StringComparison.Ordinal))
            {
                continue;
            }

            string snapshotId = summary.GetProperty("snapshotId").GetString()!;
            JsonElement snapshot = RequireSuccess(await CallResultAsync(
                session,
                "archidekt_snapshot_get",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId, ["snapshotId"] = snapshotId },
                cancellationToken).ConfigureAwait(false));
            JsonElement preview = RequireSuccess(await CallResultAsync(
                session,
                "archidekt_snapshot_restore_preview",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId, ["snapshotId"] = snapshotId },
                cancellationToken).ConfigureAwait(false));
            Assert.Empty(preview.GetProperty("differences").EnumerateArray());
            Assert.Empty(preview.GetProperty("operations").EnumerateArray());
            _ = RequireSuccess(await CallResultAsync(
                session,
                "archidekt_snapshot_delete",
                new Dictionary<string, object?>
                {
                    ["request"] = new
                    {
                        deckId = ArchidektDeckId,
                        snapshotId,
                        expectedChecksum = snapshot.GetProperty("summary").GetProperty("checksum").GetString(),
                        confirmation = $"delete snapshot {snapshotId}",
                    },
                },
                cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Restores the recovery snapshot and verifies provider-generated identifiers do not affect content equality.
    /// </summary>
    private static async Task RestoreArchidektBaselineAsync(
        LiveAcceptanceEnvironment environment,
        McpProcessSession session,
        string baselineContentFingerprint,
        string snapshotId,
        bool recordMethods,
        CancellationToken cancellationToken)
    {
        JsonElement preview = recordMethods
            ? await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_restore_preview",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId, ["snapshotId"] = snapshotId },
                cancellationToken).ConfigureAwait(false)
            : RequireSuccess(await CallResultAsync(
                session,
                "archidekt_snapshot_restore_preview",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId, ["snapshotId"] = snapshotId },
                cancellationToken).ConfigureAwait(false));
        JsonElement applied = recordMethods
            ? await CallSuccessAsync(
                environment,
                session,
                "archidekt_snapshot_restore_apply",
                new Dictionary<string, object?>
                {
                    ["request"] = SnapshotRestoreRequest(preview, snapshotId),
                },
                cancellationToken).ConfigureAwait(false)
            : RequireSuccess(await CallResultAsync(
                session,
                "archidekt_snapshot_restore_apply",
                new Dictionary<string, object?> { ["request"] = SnapshotRestoreRequest(preview, snapshotId) },
                cancellationToken).ConfigureAwait(false));
        Assert.Equal("applied", applied.GetProperty("outcome").GetString());

        JsonElement current = RequireSuccess(await CallResultAsync(
            session,
            "archidekt_deck_get",
            new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId },
            cancellationToken).ConfigureAwait(false));
        Assert.Equal(baselineContentFingerprint, current.GetProperty("contentFingerprint").GetString());
    }

    /// <summary>
    /// Builds the complete guarded snapshot-restore request from a fresh preview.
    /// </summary>
    private static object SnapshotRestoreRequest(JsonElement preview, string snapshotId)
    {
        return new
        {
            deckId = ArchidektDeckId,
            snapshotId,
            expectedSnapshotChecksum = preview.GetProperty("snapshotChecksum").GetString(),
            expectedSnapshotContentFingerprint = preview.GetProperty("snapshotContentFingerprint").GetString(),
            expectedRemoteFingerprint = preview.GetProperty("remoteFingerprint").GetString(),
            previewFingerprint = preview.GetProperty("previewFingerprint").GetString(),
            confirmation = $"restore snapshot {snapshotId}",
        };
    }

    /// <summary>
    /// Best-effort deletes only acceptance-owned objects after a verified seed-deck restoration.
    /// </summary>
    private static async Task CleanupArchidektResourcesAsync(
        McpProcessSession session,
        string? snapshotId,
        string? temporaryDeckId,
        string? temporaryFolderId,
        string? temporaryFolderName,
        bool baselineRestored,
        CancellationToken cancellationToken)
    {
        if (temporaryDeckId is not null)
        {
            JsonElement current = await CallResultAsync(
                session,
                "archidekt_deck_get",
                new Dictionary<string, object?> { ["deckId"] = temporaryDeckId },
                cancellationToken).ConfigureAwait(false);
            if (current.GetProperty("kind").GetString() == "success")
            {
                JsonElement deck = current.GetProperty("data");
                _ = await CallResultAsync(
                    session,
                    "archidekt_deck_delete",
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            deckId = temporaryDeckId,
                            expectedRemoteFingerprint = deck.GetProperty("remoteFingerprint").GetString(),
                            confirmation = $"delete {temporaryDeckId}",
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (temporaryFolderId is not null && temporaryFolderName is not null)
        {
            JsonElement treeResult = await CallResultAsync(
                session,
                "archidekt_folder_list",
                EmptyArguments(),
                cancellationToken).ConfigureAwait(false);
            if (treeResult.GetProperty("kind").GetString() == "success")
            {
                JsonElement tree = treeResult.GetProperty("data");
                JsonElement? folder = tree.GetProperty("items").EnumerateArray()
                    .Cast<JsonElement?>()
                    .FirstOrDefault(value => value!.Value.GetProperty("folderId").GetString() == temporaryFolderId);
                if (folder is not null && !folder.Value.GetProperty("decks").EnumerateArray().Any())
                {
                    _ = await CallResultAsync(
                        session,
                        "archidekt_folder_delete",
                        new Dictionary<string, object?>
                        {
                            ["request"] = new
                            {
                                folderId = temporaryFolderId,
                                expectedName = temporaryFolderName,
                                expectedTreeFingerprint = tree.GetProperty("treeFingerprint").GetString(),
                                confirmation = $"delete folder {temporaryFolderId}",
                            },
                        },
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (baselineRestored && snapshotId is not null)
        {
            JsonElement snapshotResult = await CallResultAsync(
                session,
                "archidekt_snapshot_get",
                new Dictionary<string, object?> { ["deckId"] = ArchidektDeckId, ["snapshotId"] = snapshotId },
                cancellationToken).ConfigureAwait(false);
            if (snapshotResult.GetProperty("kind").GetString() == "success")
            {
                JsonElement snapshot = snapshotResult.GetProperty("data");
                _ = await CallResultAsync(
                    session,
                    "archidekt_snapshot_delete",
                    new Dictionary<string, object?>
                    {
                        ["request"] = new
                        {
                            deckId = ArchidektDeckId,
                            snapshotId,
                            expectedChecksum = snapshot.GetProperty("summary").GetProperty("checksum").GetString(),
                            confirmation = $"delete snapshot {snapshotId}",
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Extracts data from one required successful closed operation result.
    /// </summary>
    private static JsonElement RequireSuccess(JsonElement result)
    {
        string? kind = result.GetProperty("kind").GetString();
        if (!string.Equals(kind, "success", StringComparison.Ordinal))
        {
            string reason = result.TryGetProperty("reasonCode", out JsonElement reasonElement)
                ? reasonElement.GetString() ?? "unknown-reason"
                : "unknown-reason";
            string message = result.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString() ?? "No failure message was returned."
                : "No failure message was returned.";
            Assert.Fail($"Expected operation success but received {kind} ({reason}): {message}");
        }

        return result.GetProperty("data");
    }

    /// <summary>
    /// Creates a consistent SQLite online backup of the retained corpus in one isolated data root.
    /// </summary>
    private static async Task BackupRetainedCorpusAsync(string destinationRoot, CancellationToken cancellationToken)
    {
        string sourcePath = RetainedCorpusPath();
        Assert.True(File.Exists(sourcePath), "The retained Scryfall corpus is not installed.");
        Directory.CreateDirectory(destinationRoot);
        string destinationPath = Path.Combine(destinationRoot, "scryfall.db");

        await Task.Run(() =>
        {
            try
            {
                using SqliteConnection source = new($"Data Source={sourcePath};Mode=ReadOnly");
                using SqliteConnection destination = new($"Data Source={destinationPath}");
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Initializes or resumes the persistent lifecycle copy and captures immutable source evidence.
    /// </summary>
    private static async Task<CorpusSourceState> EnsurePersistentCorpusScratchAsync(
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        string statePath = Path.Combine(destinationRoot, "retained-source-state.json");
        string destinationPath = Path.Combine(destinationRoot, "scryfall.db");
        if (File.Exists(destinationPath))
        {
            Assert.True(File.Exists(statePath), "An existing corpus scratch database lacks its source-state guard.");
            string priorJson = await File.ReadAllTextAsync(statePath, cancellationToken).ConfigureAwait(false);
            CorpusSourceState prior = JsonSerializer.Deserialize<CorpusSourceState>(priorJson)
                ?? throw new InvalidDataException("The retained source-state guard is invalid.");
            if (await CorpusScratchHasActiveGenerationAsync(
                    destinationPath,
                    cancellationToken).ConfigureAwait(false))
            {
                return prior;
            }

            SqliteConnection.ClearAllPools();
            File.Delete(destinationPath);
            await BackupRetainedCorpusAsync(destinationRoot, cancellationToken).ConfigureAwait(false);
            return prior;
        }

        CorpusSourceState state = await ReadCorpusSourceStateAsync(cancellationToken).ConfigureAwait(false);
        await BackupRetainedCorpusAsync(destinationRoot, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            statePath,
            JsonSerializer.Serialize(state, SourceStateJsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        return state;
    }

    /// <summary>
    /// Distinguishes a resumable corpus copy from a successfully deleted empty database.
    /// </summary>
    private static async Task<bool> CorpusScratchHasActiveGenerationAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT active_generation_id FROM corpus_state WHERE singleton = 1;";
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is string generationId && !string.IsNullOrWhiteSpace(generationId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Verifies the retained corpus bytes, length, and write timestamp were never changed by acceptance.
    /// </summary>
    private static async Task VerifyCorpusSourceUnchangedAsync(
        CorpusSourceState expected,
        CancellationToken cancellationToken)
    {
        CorpusSourceState actual = await ReadCorpusSourceStateAsync(cancellationToken).ConfigureAwait(false);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Reads a content hash and metadata for the normal retained corpus without opening it for writes.
    /// </summary>
    private static async Task<CorpusSourceState> ReadCorpusSourceStateAsync(CancellationToken cancellationToken)
    {
        string sourcePath = RetainedCorpusPath();
        FileInfo source = new(sourcePath);
        Assert.True(source.Exists, "The retained Scryfall corpus is not installed.");
        await using FileStream stream = new(
            source.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            useAsync: true);
        byte[] checksum = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        source.Refresh();
        return new CorpusSourceState(
            source.Length,
            source.LastWriteTimeUtc,
            Convert.ToHexStringLower(checksum));
    }

    /// <summary>
    /// Resolves the normal versioned corpus path used only as a read-only online-backup source.
    /// </summary>
    private static string RetainedCorpusPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mtg-mcp",
            "v0.9",
            "scryfall.db");
    }

    /// <summary>
    /// Verifies the packaged capability resource and exact tool counts for every profile and mode.
    /// </summary>
    private static async Task VerifySurfaceMatrixAsync(
        LiveAcceptanceEnvironment environment,
        CancellationToken cancellationToken)
    {
        (string Mode, string Toolsets, int Count)[] cases =
        [
            ("read-only", "default", 21),
            ("local", "default", 41),
            ("remote", "default", 41),
            ("read-only", "all", 46),
            ("local", "all", 67),
            ("remote", "all", 80),
            ("read-only", "none", 0),
            ("local", "none", 0),
            ("remote", "none", 0),
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            (string mode, string toolsets, int expectedCount) = cases[index];
            string dataRoot = environment.PrepareEphemeralPhaseRoot($"surface-{index}");
            await using McpProcessSession session = await McpProcessSession.StartLiveAsync(
                dataRoot,
                mode,
                toolsets,
                LiveAcceptanceEnvironment.ProviderEnvironment(),
                cancellationToken).ConfigureAwait(false);

            IList<McpClientTool> tools = session.Client.ServerCapabilities.Tools is null
                ? []
                : await session.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            Assert.Equal(expectedCount, tools.Count);
            Assert.Equal(
                tools.Select(value => value.Name).Order(StringComparer.Ordinal),
                tools.Select(value => value.Name));
            if (mode == "remote" && toolsets == "all")
            {
                Assert.Equal(LiveAcceptanceManifest.ToolNames, tools.Select(value => value.Name));
            }

            Assert.NotNull(session.Client.ServerCapabilities.Resources);
            Assert.Null(session.Client.ServerCapabilities.Prompts);
            IList<McpClientResource> resources = await session.Client.ListResourcesAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            McpClientResource resource = Assert.Single(resources);
            Assert.Equal("mtg://server/capabilities", resource.Uri);
            ReadResourceResult read = await session.Client.ReadResourceAsync(
                resource.Uri,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            TextResourceContents content = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
            JsonElement capability = JsonSerializer.Deserialize<JsonElement>(content.Text);
            Assert.Equal(expectedCount, capability.GetProperty("surface").GetProperty("toolCount").GetInt32());
            Assert.Equal(mode, capability.GetProperty("operationMode").GetString());
            Assert.DoesNotContain(dataRoot, content.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Starts the installed package in the full remote profile with private provider configuration.
    /// </summary>
    private static Task<McpProcessSession> StartLiveAsync(string dataRoot, CancellationToken cancellationToken)
    {
        return McpProcessSession.StartLiveAsync(
            dataRoot,
            "remote",
            "all",
            LiveAcceptanceEnvironment.ProviderEnvironment(),
            cancellationToken);
    }

    /// <summary>
    /// Calls one MCP tool and returns its closed structured result.
    /// </summary>
    private static async Task<JsonElement> CallResultAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        CallToolResult call = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, call.IsError);
        JsonElement content = Assert.IsType<JsonElement>(call.StructuredContent);
        return content.GetProperty("result");
    }

    /// <summary>
    /// Calls one MCP tool, requires success, and records its path-free live disposition.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        LiveAcceptanceEnvironment environment,
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        JsonElement result = await CallResultAsync(
            session,
            toolName,
            arguments,
            cancellationToken).ConfigureAwait(false);
        JsonElement data = RequireSuccess(result);
        await environment.Journal.RecordAsync(
            toolName,
            "live-pass",
            "packaged-mcp-call-passed",
            cancellationToken).ConfigureAwait(false);
        return data;
    }

    /// <summary>
    /// Creates an allocation-light empty tool argument object.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> EmptyArguments()
    {
        return new Dictionary<string, object?>();
    }
}

/// <summary>
/// Guards the retained corpus against accidental live-acceptance mutation.
/// </summary>
internal sealed record CorpusSourceState(
    long Length,
    DateTime LastWriteTimeUtc,
    string Sha256);
