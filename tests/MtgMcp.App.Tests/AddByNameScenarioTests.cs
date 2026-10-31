using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.Scryfall.Tests;

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
    /// Verifies a double-faced card added by its front-face name takes Scryfall's full name, reaches a push preview as
    /// one add, pushes, and then reads clean: the push verifies and no difference or operation is left.
    /// </summary>
    [Fact]
    public async Task DoubleFacedCardAddedByFrontFaceName_PushesAndVerifiesCleanly()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        const string fullName = "Delver of Secrets // Insectile Aberration";
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(
            token,
            remoteDeckSize: 3,
            extraCards: [RecordedCards.Object(RecordedCards.Transform)]);
        JsonObject delver = RecordedCards.Object(RecordedCards.Transform);
        _ = world.Fake.AddCardToCatalog(
            2001,
            fullName,
            "inr",
            "60",
            oracleId: CardJson.OracleOf(delver),
            printingId: CardJson.PrintingOf(delver));
        (string localDeckId, long revision) = await PullAsync(world, token);
        string delverId = Guid.NewGuid().ToString();
        JsonElement pulledDeck = (
            await world.CallAsync("local_deck_get", new JsonObject { ["deckId"] = localDeckId }, token)).Data;
        string categoryId = pulledDeck.GetProperty("categories")[0].GetProperty("categoryId").GetString()!;

        JsonElement summary = (await world.CallAsync(
            "local_deck_apply_changes",
            new JsonObject
            {
                ["deckId"] = localDeckId,
                ["expectedRevision"] = revision,
                ["changes"] = new JsonArray(
                    new JsonObject
                    {
                        ["kind"] = "add-entry",
                        ["entryDraft"] = new JsonObject
                        {
                            ["quantity"] = 1,
                            ["cardName"] = "Delver of Secrets",
                            ["entryId"] = delverId,
                        },
                    },
                    new JsonObject
                    {
                        ["kind"] = "assign-category",
                        ["entryId"] = delverId,
                        ["categoryId"] = categoryId,
                        ["isPrimary"] = true,
                    }),
            },
            token)).Data;
        JsonElement deck = (
            await world.CallAsync("local_deck_get", new JsonObject { ["deckId"] = localDeckId }, token)).Data;
        JsonElement push = (await world.CallAsync(
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = localDeckId },
            token)).Data;
        JsonElement applied = (await world.CallAsync(
            "archidekt_push_apply",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["localDeckId"] = localDeckId,
                    ["expectedLocalRevision"] = summary.GetProperty("revision").GetInt64(),
                    ["expectedRemoteFingerprint"] = push.GetProperty("remoteFingerprint").GetString(),
                    ["previewFingerprint"] = push.GetProperty("previewFingerprint").GetString(),
                },
            },
            token)).Data;
        JsonElement pushAfter = (await world.CallAsync(
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = localDeckId },
            token)).Data;
        JsonElement pullAfter = (await world.CallAsync(
            "archidekt_pull_preview",
            new JsonObject { ["remoteDeckId"] = "42", ["localDeckId"] = localDeckId },
            token)).Data;

        Assert.Equal(0, summary.GetProperty("issueCount").GetInt32());
        Assert.Equal(
            fullName,
            deck.GetProperty("entries").EnumerateArray()
                .Select(entry => entry.GetProperty("cardName").GetString())
                .Single(name => name!.StartsWith("Delver", StringComparison.Ordinal)));
        Assert.Equal(
            1,
            push.GetProperty("summary").GetProperty("operationsByKind").GetProperty("entry-add").GetInt32());
        Assert.Equal("applied", applied.GetProperty("outcome").GetString());
        Assert.Equal(1, world.Fake.QuantityOf(fullName));
        Assert.Equal(0, pushAfter.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(0, pushAfter.GetProperty("summary").GetProperty("differenceCount").GetInt32());
        Assert.Equal(0, pullAfter.GetProperty("summary").GetProperty("differenceCount").GetInt32());
    }

    /// <summary>
    /// Pulls the fake deck into a new local deck and returns the local deck ID and its revision.
    /// </summary>
    private static async Task<(string LocalDeckId, long Revision)> PullAsync(
        ToolBudgetWorld world,
        CancellationToken token)
    {
        JsonElement pull = (
            await world.CallAsync("archidekt_pull_preview", new JsonObject { ["remoteDeckId"] = "42" }, token)).Data;
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
        return (pulled.GetProperty("localDeckId").GetString()!, pulled.GetProperty("localRevision").GetInt64());
    }

    /// <summary>
    /// Verifies cards added by name reach a push preview that holds only adds, with no conflict and no unresolved card,
    /// in four tool calls and without a reconcile call.
    /// </summary>
    [Fact]
    public async Task CardsAddedByName_ReachACleanPushPreview_InFourCalls_WithoutReconcile()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 3);
        string[] added =
            ["Budget Card 310", "Budget Card 311", "Budget Card 312", "budget card 313", "Budget Card 314"];
        for (int index = 0; index < added.Length; index++)
        {
            // Archidekt knows the printing that the installed card data resolved the name to.
            JsonObject card = world.Cards.Single(value => value["name"]!.GetValue<string>() == $"Budget Card {310 + index}");
            _ = world.Fake.AddCardToCatalog(
                2310 + index,
                card["name"]!.GetValue<string>(),
                card["set"]!.GetValue<string>(),
                card["collector_number"]!.GetValue<string>(),
                oracleId: CardJson.OracleOf(card),
                printingId: CardJson.PrintingOf(card));
        }

        JsonElement pull = (
            await world.CallAsync("archidekt_pull_preview", new JsonObject { ["remoteDeckId"] = "42" }, token)).Data;
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
        JsonElement push = (await world.CallAsync(
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = localDeckId },
            token)).Data;

        Assert.Equal(0, summary.GetProperty("issueCount").GetInt32());
        Assert.Equal(added.Length, summary.GetProperty("changeCounts").GetProperty("entriesAdded").GetInt32());
        Assert.False(push.GetProperty("hasConflicts").GetBoolean());
        Assert.True(push.GetProperty("applyAvailable").GetBoolean());
        JsonElement kinds = push.GetProperty("summary").GetProperty("operationsByKind");
        Assert.Equal(added.Length, push.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(added.Length, kinds.GetProperty("entry-add").GetInt32());
        Assert.All(
            push.GetProperty("operations").EnumerateArray(),
            operation => Assert.Equal("entry-add", operation.GetProperty("kind").GetString()));
        Assert.Equal(
            ["archidekt_pull_preview", "archidekt_pull_apply", "local_deck_apply_changes", "archidekt_push_preview"],
            world.CalledTools);
        Assert.Equal(ExpectedToolCalls, world.CalledTools.Count);
        Assert.DoesNotContain(
            world.CalledTools,
            tool => tool.StartsWith("local_deck_identity_reconcile", StringComparison.Ordinal));
    }
}
