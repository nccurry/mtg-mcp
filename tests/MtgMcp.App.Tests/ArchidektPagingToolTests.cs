using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the paging arguments of the Archidekt read tools that return deck, folder, snapshot, difference, and restore
/// rows, through a real MCP client and server over the offline fake.
/// </summary>
public sealed class ArchidektPagingToolTests
{
    /// <summary>
    /// Verifies a cursor that is not one the server wrote is refused with the cursor argument, and a page size outside
    /// its range with the page size argument, for every paged read.
    /// </summary>
    [Theory]
    [InlineData("archidekt_deck_list", """{"cursor":"nope"}""", "cursor")]
    [InlineData("archidekt_deck_list", """{"pageSize":0}""", "pageSize")]
    [InlineData("archidekt_deck_get", """{"remoteDeckId":"42","cursor":"nope"}""", "cursor")]
    [InlineData("archidekt_deck_get", """{"remoteDeckId":"42","pageSize":201}""", "pageSize")]
    [InlineData("archidekt_folders_get", """{"kind":"list","cursor":"nope"}""", "cursor")]
    [InlineData("archidekt_folders_get", """{"kind":"get","folderId":"9","pageSize":-1}""", "pageSize")]
    [InlineData("archidekt_snapshots_get", """{"kind":"get","remoteDeckId":"42","snapshotId":"77","cursor":"nope"}""", "cursor")]
    [InlineData("archidekt_snapshot_restore_preview", """{"remoteDeckId":"42","snapshotId":"77","cursor":"nope"}""", "cursor")]
    [InlineData("archidekt_snapshot_restore_preview", """{"remoteDeckId":"42","snapshotId":"77","pageSize":101}""", "pageSize")]
    public async Task BadCursorOrPageSize_IsRefusedWithTheArgumentName(string tool, string arguments, string path)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);

        ToolBudgetWorld.ToolOutcome outcome = await world.CallAsync(tool, JsonNode.Parse(arguments)!.AsObject(), token);

        Assert.True(outcome.IsError);
        Assert.Equal("invalid-input", outcome.Kind);
        Assert.Equal(path, outcome.Structured.GetProperty("path").GetString());
    }

    /// <summary>
    /// Verifies a deck read follows nextCursor to the last page, which has no next cursor, and returns every entry once
    /// in pages of the requested size.
    /// </summary>
    [Fact]
    public async Task DeckGet_FollowingNextCursor_ReadsEveryEntryOnceAndEndsWithoutOne()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 7);
        JsonObject arguments = new() { ["remoteDeckId"] = "42", ["pageSize"] = 3 };
        int entries = 0;
        int pages = 0;

        while (true)
        {
            JsonElement page = (await world.CallAsync("archidekt_deck_get", arguments, token)).Data;
            entries += page.GetProperty("entries").GetArrayLength();
            pages++;
            if (!page.TryGetProperty("nextCursor", out JsonElement next) || next.ValueKind == JsonValueKind.Null)
            {
                break;
            }

            Assert.True(pages < 10);
            arguments["cursor"] = next.GetString();
        }

        Assert.Equal(7, entries);
        Assert.Equal(3, pages);
    }

    /// <summary>
    /// Verifies a deck cursor is refused as stale, carrying the current remote fingerprint, once the Archidekt deck
    /// changed, and as invalid when the other arguments of the read differ.
    /// </summary>
    [Fact]
    public async Task DeckGet_CursorAfterTheDeckChanged_IsStale_AndAfterOtherArgumentsIsInvalid()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 7);
        JsonObject arguments = new() { ["remoteDeckId"] = "42", ["pageSize"] = 3 };
        JsonElement first = (await world.CallAsync("archidekt_deck_get", arguments, token)).Data;
        string cursor = first.GetProperty("nextCursor").GetString()!;

        ToolBudgetWorld.ToolOutcome otherZones = await world.CallAsync(
            "archidekt_deck_get",
            new JsonObject
            {
                ["remoteDeckId"] = "42",
                ["pageSize"] = 3,
                ["zones"] = new JsonArray("main"),
                ["cursor"] = cursor,
            },
            token);
        world.Fake.DeckName = "Renamed after the first page";
        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync(
            "archidekt_deck_get",
            new JsonObject { ["remoteDeckId"] = "42", ["pageSize"] = 3, ["cursor"] = cursor },
            token);

        Assert.Equal("invalid-input", otherZones.Kind);
        Assert.Equal("cursor", otherZones.Structured.GetProperty("path").GetString());
        Assert.Equal("conflict", stale.Kind);
        Assert.Equal("cursor-stale", stale.Structured.GetProperty("reasonCode").GetString());
        Assert.Equal(
            JsonValueKind.String,
            stale.Structured.GetProperty("current").GetProperty("remoteFingerprint").ValueKind);
    }

    /// <summary>
    /// Verifies a small deck reads its difference count in one page with no next cursor.
    /// </summary>
    [Fact]
    public async Task SyncDiff_SmallDeck_ReadsInOnePageWithItsCount()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        JsonElement pull = (await world.CallAsync(
            "archidekt_pull_preview",
            new JsonObject { ["remoteDeckId"] = "42" },
            token)).Data;
        JsonElement pulled = (await world.CallAsync(
            "archidekt_pull_apply",
            new JsonObject
            {
                ["remoteDeckId"] = "42",
                ["expectedRemoteFingerprint"] = pull.GetProperty("remoteFingerprint").GetString(),
                ["previewFingerprint"] = pull.GetProperty("previewFingerprint").GetString(),
            },
            token)).Data;
        string localDeckId = pulled.GetProperty("localDeckId").GetString()!;

        ToolBudgetWorld.ToolOutcome page = await world.CallAsync(
            "archidekt_sync_diff",
            new JsonObject { ["localDeckId"] = localDeckId },
            token);

        Assert.Equal(0, page.Data.GetProperty("differenceCount").GetInt32());
        Assert.False(page.Data.TryGetProperty("nextCursor", out JsonElement next) &&
            next.ValueKind == JsonValueKind.String);
    }
}
