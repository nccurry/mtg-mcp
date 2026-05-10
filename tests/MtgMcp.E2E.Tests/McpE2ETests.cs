using System.Text.Json;
using System.Net;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises the MCP server through stdio with fake Scryfall and Archidekt HTTP backends.
/// </summary>
public sealed class McpE2ETests
{
    /// <summary>
    /// Verifies that the MCP server advertises card and workspace tool groups.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ToolDiscovery_ListsExpectedToolGroups()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(tool => tool.Name).Should().Contain(
        [
            "search_cards",
            "start_deck_workspace",
            "add_card",
            "analyze_deck"
        ]);
    }

    /// <summary>
    /// Verifies that a local workspace can add, export, and analyze a Scryfall card without Archidekt calls.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalWorkspaceFlow_UsesScryfallWithoutArchidekt()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Lightning%20Bolt", LightningBoltJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Local",
                ["format"] = "modern"
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement change = await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 2,
                ["category"] = "Mainboard"
            });
        string export = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "analyze_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetString(change, "persistence").Should().Be("local-only");
        export.Should().Contain("2 Lightning Bolt");
        GetInt32(analysis, "totalCards").Should().Be(2);
        GetObject(analysis, "typeCounts").GetProperty("Instant").GetInt32().Should().Be(2);
        scryfall
            .Requests.Should()
            .ContainSingle(request =>
                request.Method == "GET"
                && DecodeRepeatedly(request.PathAndQuery) == "cards/named?fuzzy=Lightning Bolt");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that MCP analysis tools return accurate numeric payloads for a known local deck.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalAnalysisFlow_ReturnsAccuratePayloadMetrics()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Tinybones%2C%20Trinket%20Thief", TinybonesJson);
        scryfall.GetJson("cards/named?fuzzy=Swamp", SwampJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        scryfall.GetJson("cards/named?fuzzy=Phyrexian%20Arena", PhyrexianArenaJson);
        scryfall.GetJson(ScryfallSearchPath("is:game-changer"), EmptySearchJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Analysis Metrics",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await AddCardAsync(session.Client, workspaceId, "Tinybones, Trinket Thief", 1, "Commander");
        await AddCardAsync(session.Client, workspaceId, "Swamp", 36, "Lands");
        await AddCardAsync(session.Client, workspaceId, "Arcane Signet", 1, "Ramp");
        await AddCardAsync(session.Client, workspaceId, "Phyrexian Arena", 1, "Draw");

        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "analyze_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement cost = await CallJsonAsync(
            session.Client,
            "analyze_deck_cost",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement mana = await CallJsonAsync(
            session.Client,
            "analyze_mana_base",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement consistency = await CallJsonAsync(
            session.Client,
            "analyze_deck_consistency",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement odds = await CallJsonAsync(
            session.Client,
            "analyze_draw_odds",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targets"] = "Lands,Ramp,Draw",
                ["turn"] = 3,
                ["openingHandSize"] = 7,
                ["simulations"] = 100,
                ["seed"] = 42
            });
        JsonElement bracket = await CallJsonAsync(
            session.Client,
            "estimate_commander_bracket",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement goldfish = await CallJsonAsync(
            session.Client,
            "simulate_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });

        GetInt32(analysis, "totalCards").Should().Be(39);
        GetInt32(analysis, "includedCards").Should().Be(39);
        GetObject(analysis, "typeCounts").GetProperty("Land").GetInt32().Should().Be(36);
        GetObject(analysis, "typeCounts").GetProperty("Artifact").GetInt32().Should().Be(1);
        GetObject(analysis, "typeCounts").GetProperty("Enchantment").GetInt32().Should().Be(1);
        GetObject(analysis, "typeCounts").GetProperty("Creature").GetInt32().Should().Be(1);

        GetProperty(cost, "includedTotal").GetDecimal().Should().Be(10.80m);
        GetInt32(cost, "pricedIncludedCards").Should().Be(4);
        GetProperty(cost, "topCostDrivers").EnumerateArray()
            .Select(driver => GetString(driver, "cardName"))
            .Should()
            .Equal(["Phyrexian Arena", "Tinybones, Trinket Thief", "Swamp", "Arcane Signet"]);

        GetInt32(mana, "landCount").Should().Be(36);
        GetInt32(mana, "untappedLandCount").Should().Be(36);
        GetInt32(mana, "fixingCount").Should().Be(1);
        GetInt32(mana, "rampFixingCount").Should().Be(1);
        GetObject(mana, "colorSources").GetProperty("B").GetInt32().Should().Be(36);
        GetObject(mana, "producedManaSources").GetProperty("B").GetInt32().Should().Be(37);

        GetInt32(consistency, "deckSize").Should().Be(39);
        GetInt32(consistency, "rampCount").Should().Be(1);
        GetInt32(consistency, "drawCount").Should().Be(1);
        GetInt32(consistency, "lowCurveNonlandCount").Should().Be(2);

        GetInt32(odds, "deckSize").Should().Be(39);
        GetInt32(odds, "cardsSeen").Should().Be(9);
        JsonElement landRow = GetOddsRow(odds, "Lands");
        JsonElement rampRow = GetOddsRow(odds, "Ramp");
        GetInt32(landRow, "successesInDeck").Should().Be(36);
        GetProperty(landRow, "hypergeometricAtLeastOne").GetDouble().Should().Be(1);
        GetInt32(rampRow, "successesInDeck").Should().Be(1);
        GetProperty(rampRow, "hypergeometricAtLeastOne").GetDouble().Should().BeApproximately(9.0 / 39.0, 0.000001);
        GetProperty(rampRow, "hypergeometricAtLeastTwo").GetDouble().Should().Be(0);
        GetInt32(GetOddsRow(odds, "Draw"), "successesInDeck").Should().Be(1);

        GetInt32(bracket, "estimatedBracket").Should().Be(1);
        GetInt32(bracket, "gameChangerCount").Should().Be(0);
        GetInt32(goldfish, "targetTurn").Should().Be(3);
        GetInt32(goldfish, "simulations").Should().Be(100);
        GetProperty(goldfish, "turnSummaries").GetArrayLength().Should().Be(3);
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that deck intent tools preserve rich Quill descriptions through MCP stdio.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalIntentFlow_PreservesQuillDescriptionContent()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Intent",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");
        string richDescription = """
        {"ops":[{"insert":"Primer","attributes":{"bold":true}},{"insert":" before\n"},{"insert":{"image":"https://example.test/card.jpg"}},{"insert":"\nPrimer after\n","attributes":{"italic":true}}]}
        """;

        await CallJsonAsync(
            session.Client,
            "update_deck_metadata",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["description"] = richDescription
            });
        JsonElement setResult = await CallJsonAsync(
            session.Client,
            "set_deck_intent",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["intentText"] = "Archetype: discard-control"
            });
        string setDescription = GetString(GetObject(setResult, "workspace"), "description");

        setDescription.Should().Contain("\"bold\":true");
        setDescription.Should().Contain("\"italic\":true");
        setDescription.Should().Contain("\"image\":\"https://example.test/card.jpg\"");
        setDescription.Should().Contain("MTG MCP Deck Intent");
        GetString(setResult, "persistence").Should().Be("local-only");

        JsonElement clearResult = await CallJsonAsync(
            session.Client,
            "clear_deck_intent",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        string clearedDescription = GetString(GetObject(clearResult, "workspace"), "description");

        clearedDescription.Should().Contain("\"bold\":true");
        clearedDescription.Should().Contain("\"italic\":true");
        clearedDescription.Should().Contain("\"image\":\"https://example.test/card.jpg\"");
        clearedDescription.Should().NotContain("MTG MCP Deck Intent");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that local MCP deck mutations compose across cards, quantities, categories, and exports.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalMutationFlow_ExercisesCardQuantityAndCategoryChanges()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Lightning%20Bolt", LightningBoltJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Mutations",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await CallJsonAsync(
            session.Client,
            "create_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["category"] = "Removal",
                ["includedInDeck"] = true,
                ["includedInPrice"] = true
            });
        await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 2,
                ["category"] = "Removal"
            });
        await CallJsonAsync(
            session.Client,
            "set_card_quantity",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 3,
                ["category"] = "Removal"
            });
        await CallJsonAsync(
            session.Client,
            "add_card_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["category"] = "Tempo"
            });
        await CallJsonAsync(
            session.Client,
            "set_primary_card_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["category"] = "Tempo"
            });
        await CallJsonAsync(
            session.Client,
            "remove_card_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["category"] = "Removal"
            });
        await CallJsonAsync(
            session.Client,
            "rename_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["oldName"] = "Tempo",
                ["newName"] = "Interaction"
            });
        await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Arcane Signet",
                ["quantity"] = 1,
                ["category"] = "Ramp"
            });
        await CallJsonAsync(
            session.Client,
            "move_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Arcane Signet",
                ["fromCategory"] = "Ramp",
                ["toCategory"] = "Artifacts"
            });
        await CallJsonAsync(
            session.Client,
            "delete_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["category"] = "Artifacts",
                ["replacementCategory"] = "Mainboard"
            });
        await CallJsonAsync(
            session.Client,
            "remove_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 1,
                ["category"] = "Interaction"
            });
        await CallJsonAsync(
            session.Client,
            "set_card_quantity",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Arcane Signet",
                ["quantity"] = 0,
                ["category"] = "Mainboard"
            });

        string export = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement resource = await CallJsonAsync(
            session.Client,
            "open_local_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "analyze_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        export.Should().Contain("Interaction");
        export.Should().Contain("2 Lightning Bolt");
        export.Should().NotContain("Arcane Signet");
        GetObject(analysis, "typeCounts").GetProperty("Instant").GetInt32().Should().Be(2);
        JsonElement card = GetProperty(resource, "cards").EnumerateArray().Should().ContainSingle().Subject;
        GetString(card, "name").Should().Be("Lightning Bolt");
        GetString(card, "primaryCategory").Should().Be("Interaction");
        GetInt32(card, "quantity").Should().Be(2);
        card.GetProperty("categories").EnumerateArray().Select(value => value.GetString())
            .Should()
            .BeEquivalentTo(["Interaction"]);
        resource.GetProperty("categories").EnumerateArray()
            .Select(value => GetString(value, "name"))
            .Should()
            .Contain(["Mainboard", "Removal", "Interaction", "Ramp"]);
        resource.GetProperty("categories").EnumerateArray()
            .Select(value => GetString(value, "name"))
            .Should()
            .NotContain("Artifacts");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that corpus budget replacement plans preview and apply through MCP.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task CorpusBudgetReplacementFlow_PreviewsAndAppliesLocalPlan()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Mana%20Crypt", ManaCryptJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        scryfall.GetJson(
            ScryfallSearchPath("(o:add or o:treasure or o:\"search your library for a land\") legal:commander usd<=5"),
            ArcaneSignetSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("legal:commander usd<=5"),
            ArcaneSignetSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("legal:commander -t:basic"),
            ArcaneSignetSearchJson);
        scryfall.PostJson("cards/collection", ArcaneSignetCollectionJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Corpus Budget",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Mana Crypt",
                ["quantity"] = 1,
                ["category"] = "Ramp"
            });
        JsonElement replacement = await CallJsonAsync(
            session.Client,
            "find_corpus_budget_replacements",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["maxPrice"] = 5,
                ["minSavings"] = 1,
                ["limit"] = 1,
                ["analysisDepth"] = "minimal"
            });
        string planId = GetString(GetObject(replacement, "plan"), "planId");
        JsonElement recommendation = GetProperty(replacement, "recommendations").EnumerateArray()
            .Should()
            .ContainSingle()
            .Subject;

        GetString(recommendation, "cardName").Should().Be("Arcane Signet");
        GetString(recommendation, "replaceCard").Should().Be("Mana Crypt");
        GetProperty(recommendation, "evidence").GetArrayLength().Should().BeGreaterThan(0);

        JsonElement preview = await CallJsonAsync(
            session.Client,
            "preview_deck_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["resolveAddedCards"] = true
            });
        string beforeApplyExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement apply = await CallJsonAsync(
            session.Client,
            "apply_deck_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["createCheckpoint"] = false
            });
        string afterApplyExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        JsonElement beforeSnapshot = GetObject(preview, "before");
        JsonElement afterSnapshot = GetObject(preview, "after");
        GetInt32(GetObject(beforeSnapshot, "analysis"), "includedCards")
            .Should()
            .Be(1);
        GetInt32(GetObject(afterSnapshot, "analysis"), "includedCards")
            .Should()
            .Be(1);
        GetProperty(GetObject(beforeSnapshot, "cost"), "includedTotal").GetDecimal()
            .Should()
            .BeGreaterThan(GetProperty(GetObject(afterSnapshot, "cost"), "includedTotal").GetDecimal());
        beforeApplyExport.Should().Contain("1 Mana Crypt");
        beforeApplyExport.Should().NotContain("Arcane Signet");
        GetInt32(apply, "appliedOperations").Should().Be(2);
        afterApplyExport.Should().Contain("1 Arcane Signet");
        afterApplyExport.Should().NotContain("Mana Crypt");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that corpus refresh bypasses cached source facts through MCP.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task CorpusRefreshFlow_UsesCacheAndRefreshesSourceFacts()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        string searchPath = ScryfallSearchPath("legal:commander o:discard usd<=5");
        scryfall.GetJson(searchPath, HiddenGemSearchJson);
        scryfall.PostJson("cards/collection", HiddenGemCollectionJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Corpus Refresh",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        Dictionary<string, object?> args = new()
        {
            ["workspaceId"] = workspaceId,
            ["goal"] = "discard",
            ["maxPrice"] = 5,
            ["analysisDepth"] = "balanced"
        };
        JsonElement first = await CallJsonAsync(session.Client, "find_lesser_known_cards", args);
        JsonElement second = await CallJsonAsync(session.Client, "find_lesser_known_cards", args);
        args["refresh"] = true;
        JsonElement refreshed = await CallJsonAsync(session.Client, "find_lesser_known_cards", args);

        GetProperty(first, "recommendations").GetArrayLength().Should().Be(1);
        GetProperty(second, "notes").EnumerateArray()
            .Select(note => note.GetString())
            .Should()
            .Contain(note => note != null && note.Contains("cache", StringComparison.OrdinalIgnoreCase));
        GetProperty(refreshed, "recommendations").GetArrayLength().Should().Be(1);
        scryfall.Requests.Count(request =>
                request.Method == "GET"
                && request.PathAndQuery.Equals(searchPath, StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(2);
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the one-stop deckbuilding tools work through the MCP stdio surface.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task OneStopDeckbuildingFlow_ExercisesNewToolsThroughMcp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using FakeHttpServer spellbook = new();
        DateOnly defaultSince = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddYears(-1);
        scryfall.GetJson(
            ScryfallSearchPath("(o:\"each opponent\" or o:\"each player\" or o:\"each creature\") legal:commander usd<=5"),
            TableEdictSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("(o:\"destroy all\" or o:\"exile all\") legal:commander usd<=5"),
            EmptySearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("legal:commander -t:basic"),
            TableEdictSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath($"legal:commander date>={defaultSince:yyyy-MM-dd} usd<=5"),
            TableEdictSearchJson);
        scryfall.PostJson("cards/collection", TableEdictCollectionJson);
        spellbook.PostJson("find-my-combos", EmptySpellbookJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken,
            commanderSpellbookBaseAddress: spellbook.BaseAddress);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E One Stop",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement goal = await CallJsonAsync(
            session.Client,
            "find_cards_for_deck_goal",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["goal"] = "add a few cards that interact with the whole table",
                ["count"] = 1,
                ["maxPrice"] = 5
            });
        JsonElement best = await CallJsonAsync(
            session.Client,
            "analyze_deck_best_practices",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement goldfish = await CallJsonAsync(
            session.Client,
            "simulate_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targetTurn"] = 3,
                ["simulations"] = 100
            });
        JsonElement combos = await CallJsonAsync(
            session.Client,
            "find_deck_combos",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement brainstorm = await CallJsonAsync(
            session.Client,
            "brainstorm_deck_improvements",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["goal"] = "add a few cards that interact with the whole table",
                ["budget"] = 5
            });

        GetObject(goal, "plan").GetProperty("operations").GetArrayLength().Should().BeGreaterThan(0);
        GetString(best, "recommendedProfile").Should().Be("commander-baseline");
        GetInt32(goldfish, "targetTurn").Should().Be(3);
        GetObject(combos, "pressure").ValueKind.Should().Be(JsonValueKind.Object);
        GetObject(brainstorm, "goalPackage").ValueKind.Should().Be(JsonValueKind.Object);
        scryfall.Requests.Should().Contain(request => request.PathAndQuery.Contains("date%3E%3D", StringComparison.OrdinalIgnoreCase)
            || DecodeRepeatedly(request.PathAndQuery).Contains("date>=", StringComparison.OrdinalIgnoreCase));
        spellbook.Requests.Should().ContainSingle(request => request.Method == "POST" && request.PathAndQuery == "find-my-combos");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that an Archidekt workspace writes card additions back to the fake deck API.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ArchidektWritebackFlow_PatchesFakeArchidektDeck()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Lightning%20Bolt", LightningBoltJson);
        archidekt.GetJson("api/decks/123/", """
        {
          "id": 123,
          "name": "Remote E2E",
          "deckFormat": "commander",
          "categories": [
            { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true }
          ],
          "cards": []
        }
        """);
        archidekt.GetJson("api/cards/v2/?name=Lightning%20Bolt&pageSize=25", """
        {
          "results": [
            { "id": 151147, "oracleCard": { "name": "Lightning Bolt" } }
          ]
        }
        """);
        archidekt.PatchJson("api/decks/123/modifyCards/v2/", "{}");

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            });
        string workspaceId = GetString(workspace, "id");
        JsonElement change = await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 1,
                ["category"] = "Mainboard"
            });

        GetString(change, "persistence").Should().Be("archidekt-writeback");
        FakeHttpRequest patch = archidekt.Requests.Single(request => request.Method == "PATCH");
        patch.PathAndQuery.Should().Be("api/decks/123/modifyCards/v2/");
        using JsonDocument payload = JsonDocument.Parse(patch.Body);
        JsonElement card = payload.RootElement.GetProperty("cards")[0];
        card.GetProperty("action").GetString().Should().Be("add");
        card.GetProperty("cardid").GetInt32().Should().Be(151147);
        card.GetProperty("categories")[0].GetString().Should().Be("Mainboard");
        card.GetProperty("modifications").GetProperty("quantity").GetInt32().Should().Be(1);
        card.TryGetProperty("deckRelationId", out _).Should().BeFalse();
        card.GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that workflow primitives preview locally and only write to Archidekt when applied.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ArchidektWorkflowPrimitiveFlow_PreviewsBeforeApplying()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson(
            ScryfallSearchPath(
                "(o:add or o:treasure or o:\"search your library for a land\") legal:commander usd<=10"
            ),
            """
            {
              "has_more": false,
              "data": [
                { "id": "arcane-signet", "name": "Arcane Signet", "type_line": "Artifact" }
              ]
            }
            """);
        scryfall.GetJson(
            ScryfallSearchPath("is:game-changer"),
            """
            {
              "has_more": false,
              "data": []
            }
            """);
        scryfall.PostJson(
            "cards/collection",
            """
            {
              "data": [
                {
                  "id": "arcane-signet",
                  "oracle_id": "oracle-arcane-signet",
                  "name": "Arcane Signet",
                  "mana_cost": "{2}",
                  "cmc": 2,
                  "type_line": "Artifact",
                  "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
                  "produced_mana": ["W", "U", "B", "R", "G"],
                  "legalities": { "commander": "legal" },
                  "prices": { "usd": "1.00" },
                  "edhrec_rank": 5
                }
              ]
            }
            """);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        archidekt.GetJson("api/decks/123/", RemoteSwampsDeckJson);
        archidekt.GetJson(
            "api/cards/v2/?name=Arcane%20Signet&pageSize=25",
            """
            {
              "results": [
                { "id": 555, "oracleCard": { "name": "Arcane Signet" } }
              ]
            }
            """);
        archidekt.PatchJson("api/decks/123/modifyCards/v2/", "{}");

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            });
        string workspaceId = GetString(workspace, "id");
        string beforePreviewExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        JsonElement planResult = await CallJsonAsync(
            session.Client,
            "find_consistency_improvements",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["focus"] = "ramp",
                ["maxPrice"] = 10,
                ["limit"] = 1
            });
        string planId = GetString(GetObject(planResult, "plan"), "planId");
        JsonElement preview = await CallJsonAsync(
            session.Client,
            "preview_deck_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["resolveAddedCards"] = true
            });
        string afterPreviewExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetInt32(GetObject(GetObject(preview, "before"), "consistency"), "rampCount").Should().Be(0);
        GetInt32(GetObject(GetObject(preview, "after"), "consistency"), "rampCount").Should().Be(1);
        beforePreviewExport.Should().NotContain("Arcane Signet");
        afterPreviewExport.Should().Be(beforePreviewExport);
        archidekt.Requests.Should().NotContain(request => request.Method == "PATCH");

        JsonElement apply = await CallJsonAsync(
            session.Client,
            "apply_deck_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["createCheckpoint"] = false
            });
        string afterApplyExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetInt32(apply, "appliedOperations").Should().Be(1);
        afterApplyExport.Should().Contain("1 Arcane Signet");
        FakeHttpRequest patch = archidekt.Requests.Single(request => request.Method == "PATCH");
        patch.PathAndQuery.Should().Be("api/decks/123/modifyCards/v2/");
        using JsonDocument payload = JsonDocument.Parse(patch.Body);
        JsonElement card = payload.RootElement.GetProperty("cards")[0];
        card.GetProperty("action").GetString().Should().Be("add");
        card.GetProperty("cardid").GetInt32().Should().Be(555);
        card.GetProperty("categories")[0].GetString().Should().Be("Ramp");
        card.TryGetProperty("deckRelationId", out _).Should().BeFalse();
        card.GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that read-only mode rejects Archidekt workspace mutation before any HTTP call.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ReadOnlyMode_BlocksArchidektWorkspaceBeforeHttp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "read-only",
            TestContext.Current.CancellationToken);

        CallToolResult result = await session.Client.CallToolAsync(
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
        ReadText(result).Should().Contain("start_deck_workspace");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Calls a tool and parses the single text result as JSON.
    /// </summary>
    private static async Task<JsonElement> CallJsonAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        string text = await CallTextAsync(client, toolName, arguments);
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Calls a tool and returns the single text result after asserting success.
    /// </summary>
    private static async Task<string> CallTextAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().NotBeTrue(ReadText(result));
        return ReadText(result);
    }

    /// <summary>
    /// Adds a card to a workspace through the public MCP tool surface.
    /// </summary>
    private static async Task AddCardAsync(
        McpClient client,
        string workspaceId,
        string cardName,
        int quantity,
        string category)
    {
        await CallJsonAsync(
            client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = cardName,
                ["quantity"] = quantity,
                ["category"] = category
            });
    }

    /// <summary>
    /// Reads the single text content block returned by a tool call.
    /// </summary>
    private static string ReadText(CallToolResult result)
    {
        TextContentBlock block = result.Content.OfType<TextContentBlock>().Single();
        return block.Text;
    }

    /// <summary>
    /// Reads a JSON string property using camelCase or PascalCase naming.
    /// </summary>
    private static string GetString(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName).GetString() ?? "";
    }

    /// <summary>
    /// Reads a JSON integer property using camelCase or PascalCase naming.
    /// </summary>
    private static int GetInt32(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName).GetInt32();
    }

    /// <summary>
    /// Reads a JSON object property using camelCase or PascalCase naming.
    /// </summary>
    private static JsonElement GetObject(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName);
    }

    /// <summary>
    /// Reads one odds row by target name.
    /// </summary>
    private static JsonElement GetOddsRow(JsonElement odds, string target)
    {
        return GetProperty(odds, "rows")
            .EnumerateArray()
            .Single(row => string.Equals(GetString(row, "target"), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a JSON property while tolerating serializer casing differences.
    /// </summary>
    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property))
        {
            return property;
        }

        string pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (element.TryGetProperty(pascalCase, out property))
        {
            return property;
        }

        throw new InvalidOperationException($"JSON property '{propertyName}' was not found in {element}.");
    }

    /// <summary>
    /// Builds the Scryfall search path for fake HTTP routes.
    /// </summary>
    private static string ScryfallSearchPath(string query)
    {
        return $"cards/search?q={Uri.EscapeDataString(query)}&unique=cards&order=edhrec";
    }

    /// <summary>
    /// Decodes request targets until they stabilize across platform-specific escaping.
    /// </summary>
    private static string DecodeRepeatedly(string value)
    {
        string current = value;
        for (int index = 0; index < 3; index++)
        {
            string decoded = WebUtility.UrlDecode(current);
            if (decoded == current)
            {
                return decoded;
            }

            current = decoded;
        }

        return current;
    }

    /// <summary>
    /// Provides a Scryfall commander payload for local analysis E2E tests.
    /// </summary>
    private const string TinybonesJson = """
    {
      "id": "tinybones",
      "oracle_id": "oracle-tinybones",
      "name": "Tinybones, Trinket Thief",
      "mana_cost": "{1}{B}",
      "cmc": 2,
      "type_line": "Legendary Creature - Skeleton Rogue",
      "oracle_text": "At the beginning of each end step, if an opponent discarded a card this turn, you draw a card and you lose 1 life.",
      "legalities": { "commander": "legal" },
      "prices": { "usd": "2.00" },
      "color_identity": ["B"]
    }
    """;

    /// <summary>
    /// Provides a Scryfall basic land payload for local analysis E2E tests.
    /// </summary>
    private const string SwampJson = """
    {
      "id": "swamp",
      "oracle_id": "oracle-swamp",
      "name": "Swamp",
      "mana_cost": "",
      "cmc": 0,
      "type_line": "Basic Land - Swamp",
      "oracle_text": "{T}: Add {B}.",
      "produced_mana": ["B"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.05" },
      "color_identity": []
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload shared by E2E flows that add Lightning Bolt.
    /// </summary>
    private const string LightningBoltJson = """
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "oracle_id": "00000000-0000-0000-0000-000000000002",
      "name": "Lightning Bolt",
      "mana_cost": "{R}",
      "cmc": 1,
      "type_line": "Instant",
      "oracle_text": "Lightning Bolt deals 3 damage to any target.",
      "set": "clu",
      "collector_number": "141",
      "scryfall_uri": "https://scryfall.example/card/clu/141",
      "color_identity": ["R"]
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload for local analysis E2E tests.
    /// </summary>
    private const string PhyrexianArenaJson = """
    {
      "id": "phyrexian-arena",
      "oracle_id": "oracle-phyrexian-arena",
      "name": "Phyrexian Arena",
      "mana_cost": "{1}{B}{B}",
      "cmc": 3,
      "type_line": "Enchantment",
      "oracle_text": "At the beginning of your upkeep, you draw a card and you lose 1 life.",
      "legalities": { "commander": "legal" },
      "prices": { "usd": "6.00" },
      "color_identity": ["B"]
    }
    """;

    /// <summary>
    /// Provides an empty Scryfall search payload.
    /// </summary>
    private const string EmptySearchJson = """
    {
      "has_more": false,
      "data": []
    }
    """;

    /// <summary>
    /// Provides a Scryfall search payload for one-stop deckbuilding E2E tests.
    /// </summary>
    private const string TableEdictSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "table-edict",
          "name": "Table Edict",
          "type_line": "Sorcery",
          "oracle_text": "Each opponent sacrifices a creature. You draw a card.",
          "released_at": "2026-02-01",
          "set": "tst",
          "prices": { "usd": "0.50" },
          "edhrec_rank": 2500
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall collection payload for one-stop deckbuilding E2E tests.
    /// </summary>
    private const string TableEdictCollectionJson = """
    {
      "data": [
        {
          "id": "table-edict",
          "oracle_id": "oracle-table-edict",
          "name": "Table Edict",
          "mana_cost": "{3}{B}",
          "cmc": 4,
          "type_line": "Sorcery",
          "oracle_text": "Each opponent sacrifices a creature. You draw a card.",
          "released_at": "2026-02-01",
          "set": "tst",
          "color_identity": ["B"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.50" },
          "edhrec_rank": 2500
        }
      ]
    }
    """;

    /// <summary>
    /// Provides an empty Commander Spellbook payload.
    /// </summary>
    private const string EmptySpellbookJson = """
    {
      "results": {
        "included": [],
        "almostIncluded": []
      }
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload for corpus budget replacement E2E tests.
    /// </summary>
    private const string ManaCryptJson = """
    {
      "id": "mana-crypt",
      "oracle_id": "oracle-mana-crypt",
      "name": "Mana Crypt",
      "mana_cost": "{0}",
      "cmc": 0,
      "type_line": "Artifact",
      "oracle_text": "{T}: Add two colorless mana.",
      "legalities": { "commander": "legal" },
      "prices": { "usd": "180.00" },
      "edhrec_rank": 20
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload for consistency workflow E2E tests.
    /// </summary>
    private const string ArcaneSignetJson = """
    {
      "id": "arcane-signet",
      "oracle_id": "oracle-arcane-signet",
      "name": "Arcane Signet",
      "mana_cost": "{2}",
      "cmc": 2,
      "type_line": "Artifact",
      "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
      "produced_mana": ["W", "U", "B", "R", "G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "1.00" },
      "edhrec_rank": 5
    }
    """;

    /// <summary>
    /// Provides a Scryfall search payload for Arcane Signet.
    /// </summary>
    private const string ArcaneSignetSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "arcane-signet",
          "name": "Arcane Signet",
          "type_line": "Artifact",
          "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
          "prices": { "usd": "1.00" },
          "edhrec_rank": 5
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall collection payload for Arcane Signet.
    /// </summary>
    private const string ArcaneSignetCollectionJson = """
    {
      "data": [
        {
          "id": "arcane-signet",
          "oracle_id": "oracle-arcane-signet",
          "name": "Arcane Signet",
          "mana_cost": "{2}",
          "cmc": 2,
          "type_line": "Artifact",
          "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
          "produced_mana": ["W", "U", "B", "R", "G"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "1.00" },
          "edhrec_rank": 5
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall search payload for corpus refresh E2E tests.
    /// </summary>
    private const string HiddenGemSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "hidden-gem",
          "name": "Hidden Gem of Discard",
          "type_line": "Enchantment",
          "oracle_text": "Whenever an opponent discards a card, draw a card.",
          "prices": { "usd": "0.50" },
          "edhrec_rank": 15000
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall collection payload for corpus refresh E2E tests.
    /// </summary>
    private const string HiddenGemCollectionJson = """
    {
      "data": [
        {
          "id": "hidden-gem",
          "oracle_id": "oracle-hidden-gem",
          "name": "Hidden Gem of Discard",
          "mana_cost": "{2}{B}",
          "cmc": 3,
          "type_line": "Enchantment",
          "oracle_text": "Whenever an opponent discards a card, draw a card.",
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.50" },
          "edhrec_rank": 15000
        }
      ]
    }
    """;

    /// <summary>
    /// Provides an Archidekt deck payload with enough real shape to drive workflow primitive E2E tests.
    /// </summary>
    private const string RemoteSwampsDeckJson = """
    {
      "id": 123,
      "name": "Remote Swamps",
      "deckFormat": 3,
      "edhBracket": null,
      "categories": [
        { "id": 1, "name": "Lands", "includedInDeck": true, "includedInPrice": true }
      ],
      "cards": [
        {
          "id": 44,
          "quantity": 36,
          "categories": ["Lands"],
          "card": {
            "id": 99,
            "uid": "swamp-print",
            "prices": { "tcg": 0.05 },
            "oracleCard": {
              "uid": "swamp-oracle",
              "name": "Swamp",
              "typeLine": "Basic Land - Swamp",
              "manaValue": 0,
              "colorIdentity": [],
              "text": "{T}: Add {B}."
            }
          }
        }
      ]
    }
    """;
}
