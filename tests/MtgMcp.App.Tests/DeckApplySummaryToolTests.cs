using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the default result of the two apply tools that follow a preview, through a real MCP client and server: a
/// summary that stays small however large the deck is, with the deck itself only when asked.
/// </summary>
public sealed class DeckApplySummaryToolTests
{
    /// <summary>
    /// Holds the most characters a default apply summary may take.
    /// </summary>
    private const int SummaryCeiling = 10_000;

    /// <summary>
    /// Verifies a partial identity apply returns the changed entry IDs and a capped list of skipped issues for the rows
    /// it left out, with the exact count, no deck, and fewer than 10,000 characters.
    /// </summary>
    [Fact]
    public async Task IdentityApply_Partial_ReturnsUpdatedIdsAndSkippedIssues_WithoutTheDeck()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument known = await world.CreateLocalDeckAsync("Partial", resolved: false, token, size: 100);
        List<DeckEntryDraft> drafts =
        [
            .. known.Entries.Select(value => new DeckEntryDraft(1, value.CardName, SortOrder: value.SortOrder)),
            .. Enumerable.Range(
                0,
                50).Select(index => new DeckEntryDraft(1, $"Missing Card {index:00}", SortOrder: 100 + index)),
        ];
        DeckDocument deck = Success(await world.Store.CreateAsync(
            new DeckCreateRequest("Partial identity", Format: "custom", Entries: drafts),
            token));
        JsonObject preview = new()
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["entryIds"] = new JsonArray([.. deck.Entries.Select(value => (JsonNode?)value.EntryId.ToString())]),
            ["freshnessPolicy"] = "cache-only",
        };
        JsonElement guards = (await world.CallAsync("local_deck_identity_reconcile_preview", preview, token)).Data;
        JsonObject apply = new()
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["previewFingerprint"] = guards.GetProperty("previewFingerprint").GetString(),
            ["applyToken"] = guards.GetProperty("applyToken").GetString(),
            ["allowPartial"] = true,
        };

        ToolBudgetWorld.ToolOutcome applied = await world.CallAsync(
            "local_deck_identity_reconcile_apply",
            apply,
            token);

        JsonElement summary = applied.Data;
        Assert.True(applied.Characters < SummaryCeiling, $"{applied.Characters} characters");
        Assert.Equal(deck.Revision + 1, summary.GetProperty("revision").GetInt64());
        Assert.Equal(100, summary.GetProperty("changeCounts").GetProperty("entriesUpdated").GetInt32());
        Assert.Equal(100, summary.GetProperty("updatedEntryIds").GetArrayLength());
        Assert.Equal(50, summary.GetProperty("issueCount").GetInt32());
        JsonElement[] issues = [.. summary.GetProperty("issues").EnumerateArray()];
        Assert.Equal(30, issues.Length);
        Assert.True(summary.GetProperty("truncated").GetBoolean());
        Assert.All(issues, issue => Assert.Equal("skipped", issue.GetProperty("kind").GetString()));
        Assert.All(
            issues,
            issue => Assert.Contains("not cached", issue.GetProperty("message").GetString(), StringComparison.Ordinal));
        Assert.False(summary.TryGetProperty("deck", out _));

        JsonElement fresh = (await world.CallAsync(
            "local_deck_get",
            new JsonObject { ["deckId"] = deck.DeckId.ToString() },
            token)).Data;
        Assert.Equal(100, fresh.GetProperty("entries").EnumerateArray().Count(
            entry => entry.TryGetProperty("oracleId", out JsonElement id) && id.ValueKind == JsonValueKind.String));
    }

    /// <summary>
    /// Verifies a category rules apply on a 300-entry deck returns the changed entry IDs with exact counts, no deck,
    /// and fewer than 10,000 characters, and that returnDeck adds the deck.
    /// </summary>
    [Fact]
    public async Task CategoryRulesApply_OnThreeHundredEntries_ReturnsASmallSummary_AndTheDeckOnRequest()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument plain = await world.CreateLocalDeckAsync("Rules", resolved: true, token);
        DeckDocument whole = await world.CreateLocalDeckAsync("Rules with deck", resolved: true, token);

        ToolBudgetWorld.ToolOutcome applied = await ApplyRulesAsync(world, plain, returnDeck: false, token);
        ToolBudgetWorld.ToolOutcome withDeck = await ApplyRulesAsync(world, whole, returnDeck: true, token);

        JsonElement summary = applied.Data;
        Assert.True(applied.Characters < SummaryCeiling, $"{applied.Characters} characters");
        int updated = summary.GetProperty("changeCounts").GetProperty("entriesUpdated").GetInt32();
        Assert.True(updated > 50, $"{updated} entries were updated.");
        Assert.Equal(updated, summary.GetProperty("updatedEntryIds").GetArrayLength());
        Assert.False(summary.TryGetProperty("truncated", out _));
        Assert.Equal(ToolBudgetWorld.DeckSize, summary.GetProperty("entryCount").GetInt32());
        Assert.False(summary.TryGetProperty("deck", out _));
        Assert.Equal(
            ToolBudgetWorld.DeckSize,
            withDeck.Data.GetProperty("deck").GetProperty("entries").GetArrayLength());
    }

    /// <summary>
    /// Previews the category rules that file every tagged card of a deck into its Utility category, applies them, and
    /// returns the apply result.
    /// </summary>
    private static async Task<ToolBudgetWorld.ToolOutcome> ApplyRulesAsync(
        ToolBudgetWorld world,
        DeckDocument deck,
        bool returnDeck,
        CancellationToken token)
    {
        Guid category = deck.Categories.Single(value => value.Name == "Utility").CategoryId;
        JsonObject source = new()
        {
            ["kind"] = "inline",
            ["ruleSet"] = new JsonObject
            {
                ["assignmentMode"] = "add-only",
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["categoryId"] = category.ToString(),
                    ["allOf"] = new JsonArray(
                        new JsonObject { ["tagType"] = "oracle", ["exactSlug"] = ToolBudgetWorld.TagSlug }),
                    ["anyOf"] = new JsonArray(),
                    ["noneOf"] = new JsonArray(),
                }),
            },
        };
        JsonElement guards = (await world.CallAsync(
            "local_deck_category_rules_preview",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["expectedRevision"] = deck.Revision,
                ["source"] = source.DeepClone(),
                ["freshnessPolicy"] = "cache-only",
            },
            token)).Data;
        return await world.CallAsync(
            "local_deck_category_rules_apply",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["expectedRevision"] = deck.Revision,
                ["expectedCorpusGeneration"] = guards.GetProperty("corpusGenerationId").GetString(),
                ["source"] = source.DeepClone(),
                ["freshnessPolicy"] = "cache-only",
                ["previewFingerprint"] = guards.GetProperty("previewFingerprint").GetString(),
                ["applyToken"] = guards.GetProperty("applyToken").GetString(),
                ["returnDeck"] = returnDeck,
            },
            token);
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
