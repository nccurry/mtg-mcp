using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

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
    [InlineData(
        "archidekt_snapshots_get",
        """{"kind":"get","remoteDeckId":"42","snapshotId":"77","cursor":"nope"}""",
        "cursor")]
    [InlineData(
        "archidekt_snapshot_restore_preview",
        """{"remoteDeckId":"42","snapshotId":"77","cursor":"nope"}""",
        "cursor")]
    [InlineData(
        "archidekt_snapshot_restore_preview",
        """{"remoteDeckId":"42","snapshotId":"77","pageSize":101}""",
        "pageSize")]
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

    /// <summary>
    /// Verifies a deck list cursor is refused as stale once the owned decks changed, and a cursor from another tool is
    /// refused as invalid.
    /// </summary>
    [Fact]
    public async Task DeckList_CursorAfterTheListChanged_IsStale_AndAnotherToolsCursorIsInvalid()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        string cursor = await FirstCursorAsync(world, "archidekt_deck_list", new JsonObject { ["pageSize"] = 50 }, token);
        string foldersCursor = await FirstCursorAsync(
            world,
            "archidekt_folders_get",
            new JsonObject { ["kind"] = "list", ["pageSize"] = 50 },
            token,
            "folders");

        ToolBudgetWorld.ToolOutcome other = await world.CallAsync(
            "archidekt_deck_list",
            new JsonObject { ["pageSize"] = 50, ["cursor"] = foldersCursor },
            token);
        world.Fake.DeckName = "Renamed after the first page";
        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync(
            "archidekt_deck_list",
            new JsonObject { ["pageSize"] = 50, ["cursor"] = cursor },
            token);

        AssertRefused(other, "invalid-input", "invalid-cursor");
        AssertRefused(stale, "conflict", "cursor-stale");
    }

    /// <summary>
    /// Verifies a pull preview cursor is refused as stale once the Archidekt deck changed.
    /// </summary>
    [Fact]
    public async Task PullPreview_CursorAfterTheDeckChanged_IsStale()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 7);
        Guid localDeckId = await PullAndEditAsync(world, token);
        world.Fake.Description = "Described on Archidekt";
        JsonObject arguments = new()
        {
            ["remoteDeckId"] = "42",
            ["localDeckId"] = localDeckId.ToString("D"),
            ["discardLocalChanges"] = true,
            ["pageSize"] = 3,
        };
        string cursor = await FirstCursorAsync(world, "archidekt_pull_preview", arguments, token);

        world.Fake.Description = "Described again after the first page";
        arguments["cursor"] = cursor;
        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync("archidekt_pull_preview", arguments, token);

        AssertRefused(stale, "conflict", "cursor-stale");
    }

    /// <summary>
    /// Verifies a folder cursor is refused as stale once the folder changed.
    /// </summary>
    [Fact]
    public async Task FoldersGet_CursorAfterTheFolderChanged_IsStale()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        JsonObject arguments = new() { ["kind"] = "get", ["folderId"] = "9", ["pageSize"] = 3 };
        string cursor = await FirstCursorAsync(world, "archidekt_folders_get", arguments, token, "folder");

        world.Fake.FolderName = "Renamed after the first page";
        arguments["cursor"] = cursor;
        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync("archidekt_folders_get", arguments, token);

        AssertRefused(stale, "conflict", "cursor-stale");
    }

    /// <summary>
    /// Verifies a snapshot cursor is refused as stale once the Archidekt deck changed.
    /// </summary>
    [Fact]
    public async Task SnapshotsGet_CursorAfterTheSnapshotChanged_IsStale()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 7);
        JsonObject arguments = new()
        {
            ["kind"] = "get",
            ["remoteDeckId"] = "42",
            ["snapshotId"] = "77",
            ["pageSize"] = 3,
        };
        string cursor = await FirstCursorAsync(world, "archidekt_snapshots_get", arguments, token, "snapshot.deck");

        world.Fake.DeckName = "Renamed after the first page";
        arguments["cursor"] = cursor;
        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync("archidekt_snapshots_get", arguments, token);

        AssertRefused(stale, "conflict", "cursor-stale");
    }

    /// <summary>
    /// Verifies a push preview cursor read for one local deck is refused as invalid for another local deck.
    /// </summary>
    [Fact]
    public async Task PushPreview_CursorOfAnotherLocalDeck_IsInvalid()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 7);
        Guid first = await PullAndEditAsync(world, token);
        Guid second = await PullAndEditAsync(world, token);
        string cursor = await FirstCursorAsync(
            world,
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = first.ToString("D"), ["pageSize"] = 1 },
            token);

        ToolBudgetWorld.ToolOutcome other = await world.CallAsync(
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = second.ToString("D"), ["pageSize"] = 1, ["cursor"] = cursor },
            token);

        AssertRefused(other, "invalid-input", "invalid-cursor");
    }

    /// <summary>
    /// Pulls the fake Archidekt deck into a new local deck and raises every quantity, so a push has several rows.
    /// </summary>
    private static async Task<Guid> PullAndEditAsync(ToolBudgetWorld world, CancellationToken token)
    {
        Guid localDeckId = await PullAsync(world, token);
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Select(value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
            token));
        return localDeckId;
    }

    /// <summary>
    /// Pulls the fake Archidekt deck into a new local deck and returns its ID.
    /// </summary>
    private static async Task<Guid> PullAsync(ToolBudgetWorld world, CancellationToken token)
    {
        JsonElement preview = (await world.CallAsync(
            "archidekt_pull_preview",
            new JsonObject { ["remoteDeckId"] = "42" },
            token)).Data;
        JsonElement applied = (await world.CallAsync(
            "archidekt_pull_apply",
            new JsonObject
            {
                ["remoteDeckId"] = "42",
                ["expectedRemoteFingerprint"] = preview.GetProperty("remoteFingerprint").GetString(),
                ["previewFingerprint"] = preview.GetProperty("previewFingerprint").GetString(),
            },
            token)).Data;
        return applied.GetProperty("localDeckId").GetGuid();
    }

    /// <summary>
    /// Reads the first page of a tool that pages and returns the cursor for its second page.
    /// </summary>
    private static async Task<string> FirstCursorAsync(
        ToolBudgetWorld world,
        string tool,
        JsonObject arguments,
        CancellationToken token,
        string container = "")
    {
        JsonElement page = (await world.CallAsync(tool, arguments, token)).Data;
        foreach (string name in container.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            page = page.GetProperty(name);
        }

        return page.GetProperty("nextCursor").GetString()!;
    }

    /// <summary>
    /// Returns the data of a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Asserts a tool outcome is the refusal kind with the reason code.
    /// </summary>
    private static void AssertRefused(ToolBudgetWorld.ToolOutcome outcome, string kind, string reasonCode)
    {
        Assert.True(outcome.IsError);
        Assert.Equal(kind, outcome.Kind);
        Assert.Equal(reasonCode, outcome.Structured.GetProperty("reasonCode").GetString());
    }

    /// <summary>
    /// Verifies the deck rows inside a folder name their identifier remoteDeckId, as the deck list does.
    /// </summary>
    [Fact]
    public async Task FoldersGet_DeckRows_NameTheirIdentifierRemoteDeckId()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);

        JsonObject arguments = new() { ["kind"] = "get", ["folderId"] = "9", ["pageSize"] = 100 };
        List<JsonElement> decks = [];
        for (int page = 0; page < 5; page++)
        {
            JsonElement folder = (await world.CallAsync("archidekt_folders_get", arguments, token)).Data
                .GetProperty("folder");
            foreach (JsonElement item in folder.GetProperty("items").EnumerateArray())
            {
                decks.AddRange(item.GetProperty("decks").EnumerateArray());
            }

            if (!folder.TryGetProperty("nextCursor", out JsonElement next) || next.ValueKind != JsonValueKind.String)
            {
                break;
            }

            arguments["cursor"] = next.GetString();
        }

        Assert.NotEmpty(decks);
        Assert.All(decks, deck => Assert.True(deck.TryGetProperty("remoteDeckId", out _)));
        Assert.All(decks, deck => Assert.False(deck.TryGetProperty("remoteId", out _)));
    }
}
