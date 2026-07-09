using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises representative local deck workflows through the official MCP client.
/// </summary>
public sealed class DeckMcpTests
{
    /// <summary>
    /// Verifies the complete local deck-store tool family in one disposable lifecycle.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalMode_AllDeckTools_CompleteDummyCommanderLifecycle()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Dummy Commander Deck",
                    description = "Disposable MCP acceptance fixture",
                    format = "commander",
                    entries = new[]
                    {
                        new { quantity = 1, cardName = "Dummy Commander", zone = "commander" },
                    },
                },
            });
        Guid deckId = deck.GetProperty("deckId").GetGuid();
        long revision = deck.GetProperty("revision").GetInt64();

        JsonElement listed = await CallSuccessAsync(
            session,
            "deck_list",
            new Dictionary<string, object?> { ["pageSize"] = 10 });
        Assert.Equal(deckId, listed.GetProperty("items")[0].GetProperty("deckId").GetGuid());
        JsonElement loaded = await CallSuccessAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = deckId });
        Assert.Equal("Dummy Commander Deck", loaded.GetProperty("name").GetString());
        JsonElement validation = await CallSuccessAsync(
            session,
            "deck_validate",
            new Dictionary<string, object?> { ["deckId"] = deckId });
        Assert.True(validation.GetProperty("isStructurallyValid").GetBoolean());

        deck = await CallSuccessAsync(
            session,
            "deck_update",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["name"] = "Dummy Commander Deck Updated",
                ["description"] = "Metadata updated through MCP",
                ["format"] = "commander",
            });
        revision = deck.GetProperty("revision").GetInt64();
        deck = await CallSuccessAsync(
            session,
            "deck_entry_add",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entry"] = new { quantity = 2, cardName = "Island", zone = "main" },
            });
        revision = deck.GetProperty("revision").GetInt64();
        Guid islandId = deck.GetProperty("entries")
            .EnumerateArray()
            .Single(value => value.GetProperty("cardName").GetString() == "Island")
            .GetProperty("entryId")
            .GetGuid();
        deck = await CallSuccessAsync(
            session,
            "deck_entry_update",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entry"] = new
                {
                    entryId = islandId,
                    quantity = 10,
                    cardName = "Island",
                    oracleId = (Guid?)null,
                    printingId = (Guid?)null,
                    setCode = (string?)null,
                    collectorNumber = (string?)null,
                    language = "en",
                    finish = "nonfoil",
                    zone = "main",
                    sortOrder = 1,
                },
            });
        revision = deck.GetProperty("revision").GetInt64();
        Assert.Equal(
            10,
            deck.GetProperty("entries")
                .EnumerateArray()
                .Single(value => value.GetProperty("entryId").GetGuid() == islandId)
                .GetProperty("quantity")
                .GetInt32());

        deck = await CallSuccessAsync(
            session,
            "deck_category_create",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["category"] = new { name = "Lands", color = "#123456", sortOrder = 1 },
            });
        revision = deck.GetProperty("revision").GetInt64();
        Guid categoryId = deck.GetProperty("categories")[0].GetProperty("categoryId").GetGuid();
        deck = await CallSuccessAsync(
            session,
            "deck_category_update",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["category"] = new
                {
                    categoryId,
                    name = "Mana Sources",
                    color = "#654321",
                    sortOrder = 2,
                },
            });
        revision = deck.GetProperty("revision").GetInt64();
        deck = await CallSuccessAsync(
            session,
            "deck_category_assign",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entryId"] = islandId,
                ["categoryId"] = categoryId,
                ["isPrimary"] = true,
            });
        revision = deck.GetProperty("revision").GetInt64();
        Assert.True(deck.GetProperty("categoryAssignments")[0].GetProperty("isPrimary").GetBoolean());
        deck = await CallSuccessAsync(
            session,
            "deck_category_unassign",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entryId"] = islandId,
                ["categoryId"] = categoryId,
            });
        revision = deck.GetProperty("revision").GetInt64();
        Assert.Empty(deck.GetProperty("categoryAssignments").EnumerateArray());

        JsonElement invalidBatch = await CallAsync(
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
                        kind = "remove-entry",
                        entryId = Guid.Empty,
                    },
                },
            });
        Assert.Equal("invalid-input", invalidBatch.GetProperty("kind").GetString());
        Assert.Equal("invalid-deck-change", invalidBatch.GetProperty("reasonCode").GetString());
        Assert.Equal(
            "Deck change at index 0 with kind 'remove-entry' requires a non-empty entryId.",
            invalidBatch.GetProperty("message").GetString());

        deck = await CallSuccessAsync(
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
                        name = "Dummy Commander Batch Updated",
                        description = "Metadata updated through deck_apply_changes",
                        format = "commander",
                    },
                },
            });
        revision = deck.GetProperty("revision").GetInt64();
        deck = await CallSuccessAsync(
            session,
            "deck_entry_remove",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entryId"] = islandId,
            });
        revision = deck.GetProperty("revision").GetInt64();
        deck = await CallSuccessAsync(
            session,
            "deck_category_delete",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["categoryId"] = categoryId,
            });
        revision = deck.GetProperty("revision").GetInt64();
        Assert.Single(deck.GetProperty("entries").EnumerateArray());
        Assert.Empty(deck.GetProperty("categories").EnumerateArray());

        JsonElement backup = await CallSuccessAsync(
            session,
            "deck_backup_create",
            new Dictionary<string, object?>());
        Guid backupId = backup.GetProperty("backupId").GetGuid();
        JsonElement backupInventory = await CallSuccessAsync(
            session,
            "deck_backup_list",
            new Dictionary<string, object?>());
        Assert.Equal(backupId, backupInventory.GetProperty("items")[0].GetProperty("backupId").GetGuid());
        _ = await CallSuccessAsync(
            session,
            "deck_update",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["name"] = "Changed After Backup",
                ["description"] = "This state should be rolled back",
                ["format"] = "commander",
            });
        JsonElement changedInventory = await CallSuccessAsync(
            session,
            "deck_backup_list",
            new Dictionary<string, object?>());
        string currentFingerprint = changedInventory
            .GetProperty("currentDatabaseFingerprint")
            .GetString()!;
        JsonElement restored = await CallSuccessAsync(
            session,
            "deck_backup_restore",
            new Dictionary<string, object?>
            {
                ["backupId"] = backupId,
                ["expectedDatabaseFingerprint"] = currentFingerprint,
            });
        Guid rollbackBackupId = restored.GetProperty("rollbackBackupId").GetGuid();
        loaded = await CallSuccessAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = deckId });
        Assert.Equal("Dummy Commander Batch Updated", loaded.GetProperty("name").GetString());
        Assert.Equal(revision, loaded.GetProperty("revision").GetInt64());

        _ = await CallSuccessAsync(
            session,
            "deck_backup_delete",
            new Dictionary<string, object?> { ["backupId"] = backupId });
        _ = await CallSuccessAsync(
            session,
            "deck_backup_delete",
            new Dictionary<string, object?> { ["backupId"] = rollbackBackupId });
        _ = await CallSuccessAsync(
            session,
            "deck_delete",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
            });
        listed = await CallSuccessAsync(
            session,
            "deck_list",
            new Dictionary<string, object?>());
        backupInventory = await CallSuccessAsync(
            session,
            "deck_backup_list",
            new Dictionary<string, object?>());

        Assert.Empty(listed.GetProperty("items").EnumerateArray());
        Assert.Empty(backupInventory.GetProperty("items").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(session.DataRoot, "decks.db")));
    }

    /// <summary>
    /// Verifies create, granular mutation, stale conflict, canonical read, and backup workflows.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalMode_RepresentativeDeckWorkflow_PersistsStructuredResults()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement created = await CallAsync(
            session,
            "deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Workflow Deck",
                    description = "MCP E2E",
                    format = "commander",
                    entries = new[]
                    {
                        new { quantity = 1, cardName = "Commander", zone = "commander" },
                    },
                },
            });
        JsonElement createdDeck = RequireSuccessData(created);
        Guid deckId = createdDeck.GetProperty("deckId").GetGuid();
        long revision = createdDeck.GetProperty("revision").GetInt64();

        JsonElement addedEntry = await CallAsync(
            session,
            "deck_entry_add",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entry"] = new { quantity = 2, cardName = "Island", zone = "main" },
            });
        JsonElement afterEntry = RequireSuccessData(addedEntry);
        revision = afterEntry.GetProperty("revision").GetInt64();
        Guid islandId = afterEntry.GetProperty("entries")
            .EnumerateArray()
            .Single(value => value.GetProperty("cardName").GetString() == "Island")
            .GetProperty("entryId")
            .GetGuid();

        JsonElement categoryResult = await CallAsync(
            session,
            "deck_category_create",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["category"] = new { name = "Lands", color = "#123456" },
            });
        JsonElement afterCategory = RequireSuccessData(categoryResult);
        revision = afterCategory.GetProperty("revision").GetInt64();
        Guid categoryId = afterCategory.GetProperty("categories")[0]
            .GetProperty("categoryId")
            .GetGuid();

        JsonElement assigned = await CallAsync(
            session,
            "deck_category_assign",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["entryId"] = islandId,
                ["categoryId"] = categoryId,
                ["isPrimary"] = true,
            });
        JsonElement afterAssignment = RequireSuccessData(assigned);
        revision = afterAssignment.GetProperty("revision").GetInt64();

        JsonElement stale = await CallAsync(
            session,
            "deck_update",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = 1,
                ["name"] = "Stale",
                ["description"] = string.Empty,
                ["format"] = "commander",
            });
        JsonElement loaded = RequireSuccessData(await CallAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = deckId }));
        JsonElement backup = RequireSuccessData(await CallAsync(
            session,
            "deck_backup_create",
            new Dictionary<string, object?>()));
        JsonElement backupList = RequireSuccessData(await CallAsync(
            session,
            "deck_backup_list",
            new Dictionary<string, object?>()));

        Assert.Equal("conflict", stale.GetProperty("kind").GetString());
        Assert.Equal(revision, loaded.GetProperty("revision").GetInt64());
        Assert.Equal("main", loaded.GetProperty("entries")
            .EnumerateArray()
            .Single(value => value.GetProperty("entryId").GetGuid() == islandId)
            .GetProperty("zone")
            .GetString());
        Assert.True(loaded.GetProperty("categoryAssignments")[0].GetProperty("isPrimary").GetBoolean());
        Assert.Equal(
            backup.GetProperty("backupId").GetGuid(),
            backupList.GetProperty("items")[0].GetProperty("backupId").GetGuid());
        Assert.DoesNotContain(session.DataRoot, backupList.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(session.DataRoot, "decks.db")));
    }

    /// <summary>
    /// Calls one MCP tool and returns its required structured JSON object.
    /// </summary>
    private static async Task<JsonElement> CallAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, result.IsError);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        return structured.GetProperty("result");
    }

    /// <summary>
    /// Calls one MCP tool and extracts its successful structured data payload.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        JsonElement result = await CallAsync(session, toolName, arguments).ConfigureAwait(false);
        return RequireSuccessData(result);
    }

    /// <summary>
    /// Extracts the data payload from one successful operation result.
    /// </summary>
    private static JsonElement RequireSuccessData(JsonElement result)
    {
        Assert.True(result.TryGetProperty("kind", out JsonElement kind), result.ToString());
        Assert.Equal("success", kind.GetString());
        return result.GetProperty("data");
    }
}
