using System.Text.Json.Nodes;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the paging arguments of the Archidekt read tools that return folder, snapshot, difference, and restore
/// rows, through a real MCP client and server over the offline fake.
/// </summary>
public sealed class ArchidektPagingToolTests
{
    /// <summary>
    /// Verifies an offset outside the rows is refused with the name of the argument, for every paged read.
    /// </summary>
    [Theory]
    [InlineData("archidekt_folders_get", """{"kind":"list","offset":100000}""", "offset")]
    [InlineData("archidekt_folders_get", """{"kind":"get","folderId":"9","offset":-1}""", "offset")]
    [InlineData("archidekt_snapshots_get", """{"kind":"get","deckId":"42","snapshotId":"77","offset":100000}""", "offset")]
    [InlineData("archidekt_snapshot_restore_preview", """{"deckId":"42","snapshotId":"77","operationOffset":100000}""", "operationOffset")]
    [InlineData("archidekt_snapshot_restore_preview", """{"deckId":"42","snapshotId":"77","differenceOffset":-1}""", "differenceOffset")]
    public async Task OffsetOutsideTheRows_IsRefusedWithTheArgumentName(string tool, string arguments, string path)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);

        ToolBudgetWorld.ToolOutcome outcome = await world.CallAsync(tool, JsonNode.Parse(arguments)!.AsObject(), token);

        Assert.True(outcome.IsError);
        Assert.Equal("invalid-input", outcome.Kind);
        Assert.Equal(path, outcome.Structured.GetProperty("path").GetString());
    }

    /// <summary>
    /// Verifies a difference offset outside a local deck's differences is refused, and a small deck reads in one page
    /// that names its difference count and has no next page.
    /// </summary>
    [Fact]
    public async Task SyncDiff_SmallDeck_ReadsInOnePageWithItsCount()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        System.Text.Json.JsonElement pull = (await world.CallAsync(
            "archidekt_pull_preview",
            new JsonObject { ["remoteDeckId"] = "42" },
            token)).Data;
        System.Text.Json.JsonElement pulled = (await world.CallAsync(
            "archidekt_pull_apply",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["remoteDeckId"] = "42",
                    ["localDeckId"] = null,
                    ["expectedLocalRevision"] = null,
                    ["expectedRemoteFingerprint"] = pull.GetProperty("remoteFingerprint").GetString(),
                    ["previewFingerprint"] = pull.GetProperty("previewFingerprint").GetString(),
                },
            },
            token)).Data;
        string localDeckId = pulled.GetProperty("localDeckId").GetString()!;

        ToolBudgetWorld.ToolOutcome page = await world.CallAsync(
            "archidekt_sync_diff",
            new JsonObject { ["localDeckId"] = localDeckId },
            token);
        ToolBudgetWorld.ToolOutcome outside = await world.CallAsync(
            "archidekt_sync_diff",
            new JsonObject { ["localDeckId"] = localDeckId, ["differenceOffset"] = 5 },
            token);

        Assert.Equal(0, page.Data.GetProperty("differenceCount").GetInt32());
        Assert.False(page.Data.TryGetProperty("nextDifferenceOffset", out System.Text.Json.JsonElement next) &&
            next.ValueKind == System.Text.Json.JsonValueKind.Number);
        Assert.Equal("differenceOffset", outside.Structured.GetProperty("path").GetString());
    }
}
