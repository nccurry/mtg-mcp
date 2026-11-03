using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.App.Tests;

/// <summary>
/// Follows the two sideboard workflows an agent reported, through a real MCP client and server over the offline fakes,
/// on a pulled 300-entry deck, and counts the tool calls and checks the categories pushed to Archidekt.
/// </summary>
public sealed class DeckEditWorkflowScenarioTests
{
    /// <summary>
    /// Names the maybeboard cards the first workflow moves.
    /// </summary>
    private const int MaybeboardSize = 13;

    /// <summary>
    /// Verifies moving 13 maybeboard cards to the sideboard and adding 5 sideboard cards by name takes one filtered
    /// read, one apply, one push preview, and one push apply, with no category or Scryfall ID lookup, and leaves every
    /// one of the 18 cards in the Sideboard category on Archidekt.
    /// </summary>
    [Fact]
    public async Task MoveMaybeboardToSideboardAndAddFive_PushesInFourCalls_WithSideboardCategories()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token);
        string[] maybeboard = AddMaybeboardCards(world.Fake);
        string[] added = RegisterNewCards(world, 310, 5);
        (string localDeckId, long revision) = await AddByNameScenarioTests.PullAsync(world, token);
        world.CalledTools.Clear();

        JsonElement deck = (await world.CallAsync(
            "local_deck_get",
            new JsonObject
            {
                ["deckId"] = localDeckId,
                ["categoryNames"] = new JsonArray("Maybeboard"),
                ["responseFormat"] = "concise",
            },
            token)).Data;
        JsonArray moved = [.. deck.GetProperty("entries").EnumerateArray()
            .Select(entry => (JsonNode?)entry.GetProperty("entryId").GetString())];
        JsonArray changes =
        [
            new JsonObject
            {
                ["kind"] = "patch-entries",
                ["entryIds"] = moved,
                ["zone"] = "sideboard",
                ["zoneCategories"] = "match",
            },
        ];
        AddSideboardAdds(changes, added);
        JsonElement summary = (await ApplyAsync(world, localDeckId, revision, changes, token)).Data;
        (JsonElement preview, JsonElement applied) = await PushAsync(world, localDeckId, summary, token);

        Assert.Equal(MaybeboardSize, moved.Count);
        Assert.Equal(0, summary.GetProperty("issueCount").GetInt32());
        Assert.True(preview.GetProperty("applyAvailable").GetBoolean());
        Assert.Equal("applied", applied.GetProperty("outcome").GetString());
        Assert.Equal(
            ["local_deck_get", "local_deck_apply_changes", "archidekt_push_preview", "archidekt_push_apply"],
            world.CalledTools);
        Assert.All(maybeboard.Concat(added), name => Assert.Equal(["Sideboard"], world.Fake.CategoriesOf(name)));
        Assert.All(added, name => Assert.Equal(1, world.Fake.QuantityOf(name)));
    }

    /// <summary>
    /// Verifies adding 15 sideboard cards by name takes one apply, one push preview, and one push apply, and leaves
    /// each card in the Sideboard category on Archidekt.
    /// </summary>
    [Fact]
    public async Task AddFifteenSideboardCards_PushesInThreeCalls_WithSideboardCategories()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token);
        _ = AddMaybeboardCards(world.Fake);
        string[] added = RegisterNewCards(world, 300, 15);
        (string localDeckId, long revision) = await AddByNameScenarioTests.PullAsync(world, token);
        world.CalledTools.Clear();

        JsonArray changes = [];
        AddSideboardAdds(changes, added);
        JsonElement summary = (await ApplyAsync(world, localDeckId, revision, changes, token)).Data;
        (JsonElement preview, JsonElement applied) = await PushAsync(world, localDeckId, summary, token);

        Assert.Equal(0, summary.GetProperty("issueCount").GetInt32());
        Assert.Equal(added.Length, summary.GetProperty("changeCounts").GetProperty("entriesAdded").GetInt32());
        Assert.True(preview.GetProperty("applyAvailable").GetBoolean());
        Assert.Equal("applied", applied.GetProperty("outcome").GetString());
        Assert.Equal(
            ["local_deck_apply_changes", "archidekt_push_preview", "archidekt_push_apply"],
            world.CalledTools);
        Assert.All(added, name => Assert.Equal(["Sideboard"], world.Fake.CategoriesOf(name)));
    }

    /// <summary>
    /// Adds the Sideboard and Maybeboard categories and 13 maybeboard cards to the fake Archidekt deck, and returns the
    /// card names.
    /// </summary>
    private static string[] AddMaybeboardCards(FakeArchidekt fake)
    {
        fake.AddCategory("Sideboard", includedInDeck: false);
        fake.AddCategory("Maybeboard", includedInDeck: false);
        string[] names = [.. Enumerable.Range(0, MaybeboardSize).Select(index => $"Maybe Card {index:00}")];
        for (int index = 0; index < names.Length; index++)
        {
            fake.AddEntry(fake.AddCardToCatalog(3000 + index, names[index]), 1, "Maybeboard");
        }

        return names;
    }

    /// <summary>
    /// Makes Archidekt know the printing of the installed fixture cards whose names end in the given numbers, so a card
    /// added by name resolves to a printing Archidekt can add, and returns the names.
    /// </summary>
    private static string[] RegisterNewCards(ToolBudgetWorld world, int first, int count)
    {
        string[] names = [.. Enumerable.Range(first, count).Select(number => $"Budget Card {number}")];
        for (int index = 0; index < names.Length; index++)
        {
            JsonObject card = world.Cards.Single(value => value["name"]!.GetValue<string>() == names[index]);
            _ = world.Fake.AddCardToCatalog(
                2000 + first + index,
                names[index],
                card["set"]!.GetValue<string>(),
                card["collector_number"]!.GetValue<string>(),
                oracleId: CardJson.OracleOf(card),
                printingId: CardJson.PrintingOf(card));
        }

        return names;
    }

    /// <summary>
    /// Appends one add-entry change per name that gives only the card name, the sideboard zone, and the zone category
    /// match mode.
    /// </summary>
    private static void AddSideboardAdds(JsonArray changes, string[] names)
    {
        foreach (string name in names)
        {
            changes.Add(new JsonObject
            {
                ["kind"] = "add-entry",
                ["entryDraft"] = new JsonObject
                {
                    ["quantity"] = 1,
                    ["cardName"] = name,
                    ["zone"] = "sideboard",
                    ["zoneCategories"] = "match",
                },
            });
        }
    }

    /// <summary>
    /// Applies the changes to the local deck in one call.
    /// </summary>
    private static Task<ToolBudgetWorld.ToolOutcome> ApplyAsync(
        ToolBudgetWorld world,
        string localDeckId,
        long revision,
        JsonArray changes,
        CancellationToken token)
    {
        return world.CallAsync(
            "local_deck_apply_changes",
            new JsonObject { ["deckId"] = localDeckId, ["expectedRevision"] = revision, ["changes"] = changes },
            token);
    }

    /// <summary>
    /// Previews the push in the concise format and applies it, and returns both results.
    /// </summary>
    private static async Task<(JsonElement Preview, JsonElement Applied)> PushAsync(
        ToolBudgetWorld world,
        string localDeckId,
        JsonElement summary,
        CancellationToken token)
    {
        JsonElement preview = (await world.CallAsync(
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = localDeckId, ["responseFormat"] = "concise" },
            token)).Data;
        JsonElement applied = (await world.CallAsync(
            "archidekt_push_apply",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["localDeckId"] = localDeckId,
                    ["expectedRevision"] = summary.GetProperty("revision").GetInt64(),
                    ["expectedRemoteFingerprint"] = preview.GetProperty("remoteFingerprint").GetString(),
                    ["previewFingerprint"] = preview.GetProperty("previewFingerprint").GetString(),
                },
            },
            token)).Data;
        return (preview, applied);
    }
}
