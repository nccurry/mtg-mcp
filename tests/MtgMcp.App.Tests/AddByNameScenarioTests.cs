using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.App.Tests;

/// <summary>
/// Follows what an agent does to put new cards, named only by text, on an Archidekt deck, through a real MCP client and
/// server over the offline fakes, and counts the tool calls it takes.
/// </summary>
public sealed class AddByNameScenarioTests
{
    /// <summary>
    /// Holds the most tool calls the scenario may take: pull preview, pull apply, add the cards, push preview.
    /// </summary>
    private const int ExpectedToolCalls = 4;

    /// <summary>
    /// Verifies cards added by name reach a push preview that holds only adds, with no conflict and no unresolved card,
    /// in four tool calls and without a reconcile call.
    /// </summary>
    [Fact]
    public async Task CardsAddedByName_ReachACleanPushPreview_InFourCalls_WithoutReconcile()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        string[] added = ["Budget Card 310", "Budget Card 311", "Budget Card 312", "budget card 313", "Budget Card 314"];

        JsonElement pull = (await world.CallAsync("archidekt_pull_preview", new JsonObject { ["remoteDeckId"] = "42" }, token)).Data;
        JsonElement pulled = (await world.CallAsync(
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
        JsonArray changes = [];
        foreach (string name in added)
        {
            changes.Add(new JsonObject
            {
                ["kind"] = "add-entry",
                ["entryDraft"] = new JsonObject { ["quantity"] = 1, ["cardName"] = name },
            });
        }

        JsonElement summary = (await world.CallAsync(
            "local_deck_apply_changes",
            new JsonObject
            {
                ["deckId"] = localDeckId,
                ["expectedRevision"] = pulled.GetProperty("localRevision").GetInt64(),
                ["changes"] = changes,
            },
            token)).Data;
        JsonElement push = (await world.CallAsync("archidekt_push_preview", new JsonObject { ["localDeckId"] = localDeckId }, token)).Data;

        Assert.Equal(0, summary.GetProperty("issueCount").GetInt32());
        Assert.Equal(added.Length, summary.GetProperty("changeCounts").GetProperty("entriesAdded").GetInt32());
        Assert.False(push.GetProperty("hasConflicts").GetBoolean());
        Assert.True(push.GetProperty("applyAvailable").GetBoolean());
        JsonElement kinds = push.GetProperty("summary").GetProperty("operationsByKind");
        Assert.Equal(added.Length, push.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(added.Length, kinds.GetProperty("entry-add").GetInt32());
        Assert.All(push.GetProperty("operations").EnumerateArray(), operation => Assert.Equal("entry-add", operation.GetProperty("kind").GetString()));
        Assert.Equal(
            ["archidekt_pull_preview", "archidekt_pull_apply", "local_deck_apply_changes", "archidekt_push_preview"],
            world.CalledTools);
        Assert.Equal(ExpectedToolCalls, world.CalledTools.Count);
        Assert.DoesNotContain(world.CalledTools, tool => tool.StartsWith("local_deck_identity_reconcile", StringComparison.Ordinal));
    }
}
