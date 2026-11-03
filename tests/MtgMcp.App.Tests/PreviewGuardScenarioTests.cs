using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Runs the import, category rules, identity, and Archidekt push workflows through a real MCP client and server over
/// the offline fakes, paging each preview and applying it with only the shared paging and guard arguments, and checks
/// the three stale outcomes use the shared reason codes.
/// </summary>
public sealed class PreviewGuardScenarioTests
{
    /// <summary>
    /// Names the paging and guard arguments every workflow may use.
    /// </summary>
    private static readonly string[] SharedGuardArguments = ["cursor", "pageSize", "previewFingerprint", "expectedRevision"];

    /// <summary>
    /// Names the other guard values a preview hands back for its apply to repeat, which belong to one tool family.
    /// </summary>
    private static readonly string[] CarriedGuardArguments =
        ["applyToken", "expectedActiveGeneration", "expectedRemoteFingerprint"];

    /// <summary>
    /// Verifies the four workflows page a preview over at least two pages and apply it using no paging or guard
    /// argument outside the shared names and the values their previews carry.
    /// </summary>
    [Fact]
    public async Task Workflows_PageAndApply_UsingOnlyTheSharedGuardArguments()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token);
        HashSet<string> used = new(StringComparer.Ordinal);

        await ImportAsync(world, used, token);
        await CategoryRulesAsync(world, used, token);
        await IdentityAsync(world, used, token);
        await PushAsync(world, used, token);

        Assert.All(SharedGuardArguments, name => Assert.Contains(name, used));
        string[] allowed = [.. SharedGuardArguments, .. CarriedGuardArguments];
        string[] stray = [.. used.Where(IsGuardName).Where(name => !allowed.Contains(name)).Order(StringComparer.Ordinal)];
        Assert.True(stray.Length == 0, "Guard arguments outside the shared names: " + string.Join(", ", stray));

        world.AssertNoPathlessRefusals();
    }

    /// <summary>
    /// Verifies a content change between two pages of an import preview is cursor-stale, and an import create with the
    /// fingerprint of other content is preview-changed.
    /// </summary>
    [Fact]
    public async Task Import_ChangedContent_IsCursorStaleBetweenPages_AndPreviewChangedOnCreate()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        JsonObject arguments = ImportArguments(world, take: 300);
        arguments["pageSize"] = 100;
        JsonElement first = (await world.CallAsync("local_deck_import_preview", arguments, token)).Data;
        JsonObject changed = ImportArguments(world, take: 299);
        changed["pageSize"] = 100;
        changed["cursor"] = first.GetProperty("nextCursor").GetString();

        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync("local_deck_import_preview", changed, token);
        changed.Remove("pageSize");
        changed.Remove("cursor");
        changed["previewFingerprint"] = first.GetProperty("previewFingerprint").GetString();
        ToolBudgetWorld.ToolOutcome create = await world.CallAsync("local_deck_import_create", changed, token);

        AssertConflict(stale, SharedReasonCodes.CursorStale);
        AssertConflict(create, SharedReasonCodes.PreviewChanged);

        world.AssertNoPathlessRefusals();
    }

    /// <summary>
    /// Verifies a category rules preview whose deck changed between pages is cursor-stale, and an apply that names the
    /// revision the deck had before the change is stale-deck-revision.
    /// </summary>
    [Fact]
    public async Task CategoryRules_ChangedDeck_IsCursorStaleBetweenPages_AndStaleRevisionOnApply()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument deck = await world.CreateLocalDeckAsync("Rules stale", resolved: true, token);
        JsonObject preview = RulesPreviewArguments(deck);
        preview["pageSize"] = 100;
        JsonElement first = (await world.CallAsync("local_deck_category_rules_preview", preview, token)).Data;
        DeckDocument edited = await EditAsync(world, deck, token);
        preview["expectedRevision"] = edited.Revision;
        preview["cursor"] = first.GetProperty("nextCursor").GetString();

        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync("local_deck_category_rules_preview", preview, token);
        JsonObject apply = RulesApplyArguments(deck, first);
        ToolBudgetWorld.ToolOutcome applied = await world.CallAsync("local_deck_category_rules_apply", apply, token);

        AssertConflict(stale, SharedReasonCodes.CursorStale);
        AssertConflict(applied, SharedReasonCodes.StaleDeckRevision);

        world.AssertNoPathlessRefusals();
    }

    /// <summary>
    /// Verifies an Archidekt push preview whose Archidekt deck changed between pages is cursor-stale, and an apply
    /// that names the revision the local deck had before an edit is stale-deck-revision.
    /// </summary>
    [Fact]
    public async Task Push_ChangedSource_IsCursorStaleBetweenPages_AndStaleRevisionOnApply()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 40);
        (string localDeckId, long revision) = await AddByNameScenarioTests.PullAsync(world, token);
        DeckDocument local = await EditAsync(world, await GetAsync(world, localDeckId, token), token);
        JsonObject preview = new() { ["localDeckId"] = localDeckId, ["pageSize"] = 10 };
        JsonElement first = (await world.CallAsync("archidekt_push_preview", preview, token)).Data;
        world.Fake.DeckName = "Renamed on Archidekt";
        preview["cursor"] = first.GetProperty("nextCursor").GetString();

        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync("archidekt_push_preview", preview, token);
        ToolBudgetWorld.ToolOutcome applied = await world.CallAsync(
            "archidekt_push_apply",
            PushApplyArguments(localDeckId, revision, first),
            token);

        Assert.True(local.Revision > revision);
        AssertConflict(stale, SharedReasonCodes.CursorStale);
        AssertConflict(applied, SharedReasonCodes.StaleDeckRevision);

        world.AssertNoPathlessRefusals();
    }

    /// <summary>
    /// Verifies the guard check recognizes a paging or guard argument by its name and ignores the other arguments.
    /// </summary>
    [Theory]
    [InlineData("cursor", true)]
    [InlineData("offset", true)]
    [InlineData("expectedFingerprint", true)]
    [InlineData("expectedCorpusGeneration", true)]
    [InlineData("deckId", false)]
    [InlineData("content", false)]
    public void GuardNameCheck_RecognizesGuardNames(string name, bool expected)
    {
        Assert.Equal(expected, IsGuardName(name));
    }

    /// <summary>
    /// Pages an import preview over three pages and creates the deck with the previewed fingerprint.
    /// </summary>
    private static async Task ImportAsync(ToolBudgetWorld world, HashSet<string> used, CancellationToken token)
    {
        JsonObject arguments = ImportArguments(world, take: 300);
        List<JsonElement> pages = await PageAsync(world, used, "local_deck_import_preview", arguments, 100, token);
        arguments.Remove("pageSize");
        arguments.Remove("cursor");
        arguments["previewFingerprint"] = pages[0].GetProperty("previewFingerprint").GetString();

        JsonElement created = (await CallAsync(world, used, "local_deck_import_create", arguments, token)).Data;

        Assert.Equal(3, pages.Count);
        Assert.Equal(300, created.GetProperty("summary").GetProperty("entryCount").GetInt32());
    }

    /// <summary>
    /// Pages a category rules preview over three pages and applies it with the previewed guards.
    /// </summary>
    private static async Task CategoryRulesAsync(ToolBudgetWorld world, HashSet<string> used, CancellationToken token)
    {
        DeckDocument deck = await world.CreateLocalDeckAsync("Rules paged", resolved: true, token);
        JsonObject preview = RulesPreviewArguments(deck);
        List<JsonElement> pages = await PageAsync(world, used, "local_deck_category_rules_preview", preview, 100, token);

        JsonElement applied = (await CallAsync(
            world,
            used,
            "local_deck_category_rules_apply",
            RulesApplyArguments(deck, pages[0]),
            token)).Data;

        Assert.Equal(3, pages.Count);
        Assert.Equal(deck.Revision + 1, applied.GetProperty("revision").GetInt64());
    }

    /// <summary>
    /// Pages an identity reconcile preview over three pages and applies it with the previewed guards.
    /// </summary>
    private static async Task IdentityAsync(ToolBudgetWorld world, HashSet<string> used, CancellationToken token)
    {
        DeckDocument deck = await world.CreateLocalDeckAsync("Identity paged", resolved: false, token, size: 120);
        JsonObject preview = new()
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["freshnessPolicy"] = "cache-only",
        };
        List<JsonElement> pages = await PageAsync(world, used, "local_deck_identity_reconcile_preview", preview, 50, token);

        JsonElement applied = (await CallAsync(
            world,
            used,
            "local_deck_identity_reconcile_apply",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["expectedRevision"] = deck.Revision,
                ["previewFingerprint"] = pages[0].GetProperty("previewFingerprint").GetString(),
                ["applyToken"] = pages[0].GetProperty("applyToken").GetString(),
            },
            token)).Data;

        Assert.True(pages.Count >= 2);
        Assert.Equal(deck.Revision + 1, applied.GetProperty("revision").GetInt64());
    }

    /// <summary>
    /// Pulls an Archidekt deck, edits it locally, pages the push preview over at least two pages, and applies it with
    /// the previewed guards.
    /// </summary>
    private static async Task PushAsync(ToolBudgetWorld world, HashSet<string> used, CancellationToken token)
    {
        (string localDeckId, _) = await AddByNameScenarioTests.PullAsync(world, token);
        DeckDocument edited = await EditAsync(world, await GetAsync(world, localDeckId, token), token);
        JsonObject preview = new() { ["localDeckId"] = localDeckId };
        List<JsonElement> pages = await PageAsync(world, used, "archidekt_push_preview", preview, 10, token);

        JsonElement applied = (await CallAsync(
            world,
            used,
            "archidekt_push_apply",
            PushApplyArguments(localDeckId, edited.Revision, pages[0]),
            token)).Data;

        Assert.True(pages.Count >= 2);
        Assert.Equal("applied", applied.GetProperty("outcome").GetString());
    }

    /// <summary>
    /// Reads every page of a preview, passing only the cursor of the previous page, and returns the pages.
    /// </summary>
    private static async Task<List<JsonElement>> PageAsync(
        ToolBudgetWorld world,
        HashSet<string> used,
        string tool,
        JsonObject arguments,
        int pageSize,
        CancellationToken token)
    {
        arguments["pageSize"] = pageSize;
        List<JsonElement> pages = [];
        while (true)
        {
            JsonElement page = (await CallAsync(world, used, tool, arguments, token)).Data;
            pages.Add(page);
            Assert.True(pages.Count < 50, $"{tool} never ended.");
            if (!page.TryGetProperty("nextCursor", out JsonElement next) || next.ValueKind != JsonValueKind.String)
            {
                return pages;
            }

            arguments["cursor"] = next.GetString();
        }
    }

    /// <summary>
    /// Calls a tool and records the name of every argument sent.
    /// </summary>
    private static Task<ToolBudgetWorld.ToolOutcome> CallAsync(
        ToolBudgetWorld world,
        HashSet<string> used,
        string tool,
        JsonObject arguments,
        CancellationToken token)
    {
        foreach ((string name, _) in arguments)
        {
            _ = used.Add(name);
        }

        return world.CallAsync(tool, arguments, token);
    }

    /// <summary>
    /// Tells whether an argument name pages a result or guards an apply.
    /// </summary>
    private static bool IsGuardName(string name)
    {
        string[] markers = ["ursor", "ingerprint", "evision", "Generation", "Token", "offset", "page", "Page"];
        return markers.Any(marker => name.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// Asserts a tool returned a conflict with the given reason code.
    /// </summary>
    private static void AssertConflict(ToolBudgetWorld.ToolOutcome outcome, string reasonCode)
    {
        Assert.Equal("conflict", outcome.Kind);
        Assert.Equal(reasonCode, outcome.Structured.GetProperty("reasonCode").GetString());
    }

    /// <summary>
    /// Builds the arguments that import the first cards of the fixture as plain text.
    /// </summary>
    private static JsonObject ImportArguments(ToolBudgetWorld world, int take)
    {
        string text = string.Join('\n', world.Cards.Take(take).Select(card => "1 " + card["name"]!.GetValue<string>()));
        return new JsonObject { ["formatId"] = "generic-text-v1", ["content"] = text };
    }

    /// <summary>
    /// Builds the arguments of a category rules preview that files every tagged card into the Utility category.
    /// </summary>
    private static JsonObject RulesPreviewArguments(DeckDocument deck)
    {
        return new JsonObject
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["source"] = RulesSource(deck),
            ["freshnessPolicy"] = "cache-only",
        };
    }

    /// <summary>
    /// Builds the arguments of a category rules apply from the first page of its preview.
    /// </summary>
    private static JsonObject RulesApplyArguments(DeckDocument deck, JsonElement firstPage)
    {
        return new JsonObject
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["expectedActiveGeneration"] = firstPage.GetProperty("corpusGenerationId").GetString(),
            ["source"] = RulesSource(deck),
            ["freshnessPolicy"] = "cache-only",
            ["previewFingerprint"] = firstPage.GetProperty("previewFingerprint").GetString(),
            ["applyToken"] = firstPage.GetProperty("applyToken").GetString(),
        };
    }

    /// <summary>
    /// Builds the inline rule source that adds every card with the fixture tag to the Utility category.
    /// </summary>
    private static JsonObject RulesSource(DeckDocument deck)
    {
        return new JsonObject
        {
            ["kind"] = "inline",
            ["ruleSet"] = new JsonObject
            {
                ["assignmentMode"] = "add-only",
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["categoryId"] = deck.Categories.Single(value => value.Name == "Utility").CategoryId.ToString(),
                    ["allOf"] = new JsonArray(
                        new JsonObject { ["tagType"] = "oracle", ["exactSlug"] = ToolBudgetWorld.TagSlug }),
                    ["anyOf"] = new JsonArray(),
                    ["noneOf"] = new JsonArray(),
                }),
            },
        };
    }

    /// <summary>
    /// Builds the arguments of a push apply from the first page of its preview.
    /// </summary>
    private static JsonObject PushApplyArguments(string localDeckId, long revision, JsonElement firstPage)
    {
        return new JsonObject
        {
            ["localDeckId"] = localDeckId,
            ["expectedRevision"] = revision,
            ["expectedRemoteFingerprint"] = firstPage.GetProperty("remoteFingerprint").GetString(),
            ["previewFingerprint"] = firstPage.GetProperty("previewFingerprint").GetString(),
        };
    }

    /// <summary>
    /// Reads a local deck from the store.
    /// </summary>
    private static async Task<DeckDocument> GetAsync(ToolBudgetWorld world, string deckId, CancellationToken token)
    {
        return Assert.IsType<OperationSuccess<DeckDocument>>(
            (await world.Store.GetAsync(Guid.Parse(deckId), token)).Value).Data;
    }

    /// <summary>
    /// Raises the quantity of the first thirty entries of a deck, and returns the deck at its new revision.
    /// </summary>
    private static async Task<DeckDocument> EditAsync(ToolBudgetWorld world, DeckDocument deck, CancellationToken token)
    {
        return Assert.IsType<OperationSuccess<DeckDocument>>(
            (await world.Store.ApplyChangesAsync(
                deck.DeckId,
                deck.Revision,
                [.. deck.Entries.Take(30).Select(
                    value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
                token)).Value).Data;
    }
}
