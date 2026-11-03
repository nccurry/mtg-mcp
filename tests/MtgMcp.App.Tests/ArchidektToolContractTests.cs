using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the shape every Archidekt tool shares with the rest of the surface: flat write arguments, confirmation
/// phrases that name the action and the target, and one cursor rule on the deck list.
/// </summary>
public sealed class ArchidektToolContractTests
{
    /// <summary>
    /// Verifies no Archidekt write tool takes a <c>request</c> object, that the folder move keeps only its item list as
    /// an object, and that every deck identifier is named for the deck it identifies.
    /// </summary>
    [Fact]
    public void WriteTools_TakeFlatArguments_AndNameTheirDecks()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);

        foreach (ModelContextProtocol.Server.McpServerTool tool in registered.Tools.Where(value =>
            value.ProtocolTool.Name.StartsWith("archidekt_", StringComparison.Ordinal)))
        {
            JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
            string[] names = [.. properties.EnumerateObject().Select(value => value.Name)];
            Assert.DoesNotContain("request", names);
            Assert.DoesNotContain("deckId", names);
            Assert.DoesNotContain("expectedLocalRevision", names);
            Assert.DoesNotContain("expectedPreviewFingerprint", names);
            Assert.DoesNotContain(names, name => name.EndsWith("Offset", StringComparison.Ordinal));
        }

        JsonElement move = registered.Tools.Single(value => value.ProtocolTool.Name == "archidekt_folder_move_items")
            .ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Equal("array", move.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("string", move.GetProperty("expectedTreeFingerprint").GetProperty("type").GetString());
    }

    /// <summary>
    /// Verifies a wrong or missing confirmation is refused with the exact phrase to pass and the name of the
    /// confirmation argument, before any provider read, for every destructive Archidekt operation.
    /// </summary>
    [Theory]
    [InlineData("archidekt_deck_delete", """{"remoteDeckId":"42","expectedRemoteFingerprint":"f","confirmation":"delete 42"}""", "delete deck 42")]
    [InlineData("archidekt_folder_delete", """{"folderId":"9","expectedName":"n","expectedTreeFingerprint":"f","confirmation":"yes"}""", "delete folder 9")]
    [InlineData("archidekt_snapshot_delete", """{"remoteDeckId":"42","snapshotId":"77","expectedChecksum":"c","confirmation":"delete 77"}""", "delete snapshot 77")]
    [InlineData("archidekt_snapshot_restore_apply", """{"remoteDeckId":"42","snapshotId":"77","expectedSnapshotChecksum":"c","expectedSnapshotContentFingerprint":"c","expectedRemoteFingerprint":"r","previewFingerprint":"p","confirmation":"restore 77"}""", "restore snapshot 77")]
    [InlineData("archidekt_pull_apply", """{"remoteDeckId":"42","localDeckId":"6f1c7a0e-52a6-4f5c-b0a4-0f3d5b8f2a11","expectedRevision":1,"expectedRemoteFingerprint":"r","previewFingerprint":"p","discardLocalChanges":true}""", "discard local changes 6f1c7a0e-52a6-4f5c-b0a4-0f3d5b8f2a11")]
    public async Task DestructiveOperations_RefuseAWrongConfirmationWithTheExactPhrase(
        string tool,
        string arguments,
        string phrase)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);

        ToolBudgetWorld.ToolOutcome outcome = await world.CallAsync(tool, JsonNode.Parse(arguments)!.AsObject(), token);

        Assert.True(outcome.IsError);
        Assert.Equal("invalid-input", outcome.Kind);
        Assert.Equal("confirmation-required", outcome.Structured.GetProperty("reasonCode").GetString());
        Assert.Equal("confirmation", outcome.Structured.GetProperty("path").GetString());
        Assert.Contains(phrase, outcome.Structured.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies the deck list follows nextCursor through every page of the owned decks in pages of the requested size,
    /// ends with no next cursor, and refuses a page size above its limit.
    /// </summary>
    [Fact]
    public async Task DeckList_FollowingNextCursor_ReadsEveryDeckOnceAndEndsWithoutOne()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        JsonObject arguments = new() { ["pageSize"] = 50 };
        List<int> pageSizes = [];
        HashSet<string> ids = [];

        while (true)
        {
            JsonElement page = (await world.CallAsync("archidekt_deck_list", arguments, token)).Data;
            JsonElement items = page.GetProperty("items");
            pageSizes.Add(items.GetArrayLength());
            foreach (JsonElement item in items.EnumerateArray())
            {
                Assert.True(ids.Add(item.GetProperty("remoteDeckId").GetString()!));
            }

            if (!page.TryGetProperty("nextCursor", out JsonElement next) || next.ValueKind == JsonValueKind.Null)
            {
                break;
            }

            Assert.True(pageSizes.Count < 10);
            arguments["cursor"] = next.GetString();
        }

        ToolBudgetWorld.ToolOutcome tooBig = await world.CallAsync(
            "archidekt_deck_list",
            new JsonObject { ["pageSize"] = 101 },
            token);

        Assert.Equal([50, 50, 20], pageSizes);
        Assert.Equal(120, ids.Count);
        Assert.Equal("pageSize", tooBig.Structured.GetProperty("path").GetString());
    }
}
