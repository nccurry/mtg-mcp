using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the card list, deck rule, duplicate, castability, bracket, and game statistics tools through the real
/// server over standard input and output. These runs are offline: no Scryfall card data is installed, so the tests
/// check each tool's real binding, result shape, and failure kinds, and the toolset and credential rules of the
/// list sources (PLC phase 4).
/// </summary>
public sealed class CardListAndRuleToolsMcpTests
{
    /// <summary>
    /// Groups a query by mana value.
    /// </summary>
    private static readonly string[] ManaValueGroup = ["manaValue"];

    /// <summary>
    /// Asks a query for the copy count.
    /// </summary>
    private static readonly string[] QuantityMetric = ["quantity"];

    /// <summary>
    /// Names colorless mana.
    /// </summary>
    private static readonly string[] ColorlessMana = ["C"];

    /// <summary>
    /// Names green mana.
    /// </summary>
    private static readonly string[] GreenMana = ["G"];

    /// <summary>
    /// Verifies the duplicate preview finds a card held twice by name and plans the removal, and its result matches
    /// the published output schema.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task DuplicatesPreview_ByName_PlansTheRemovalAndMatchesTheOutputSchema()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, _) = await CreateDeckAsync(session).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "local_deck_duplicates_preview",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["exempt"] = "none",
                ["keepPriority"] = new[] { new { zone = "main" }, new { zone = "sideboard" } },
                ["matchBy"] = "name",
            }).ConfigureAwait(false);

        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal("success", structured.GetProperty("kind").GetString());
        JsonElement data = structured.GetProperty("data");
        Assert.Equal("Sol Ring", Assert.Single(data.GetProperty("duplicates").EnumerateArray())
            .GetProperty("card").GetProperty("name").GetString());
        Assert.Equal("remove-entry", Assert.Single(data.GetProperty("changes").EnumerateArray())
            .GetProperty("kind").GetString());
        await AssertDataMatchesSchemaAsync(session, "local_deck_duplicates_preview", data).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies local_deck_validate without rules keeps its plain structural result, and with rules commander and no
    /// card data returns not-cached, marked as an error, naming the sync tool.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task DeckValidate_WithAndWithoutRules_ReturnsFactsOrNotCached()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, _) = await CreateDeckAsync(session).ConfigureAwait(false);

        CallToolResult plain = await CallAsync(
            session,
            "local_deck_validate",
            new Dictionary<string, object?> { ["deckId"] = deckId }).ConfigureAwait(false);
        CallToolResult withRules = await CallAsync(
            session,
            "local_deck_validate",
            new Dictionary<string, object?> { ["deckId"] = deckId, ["rules"] = "commander" }).ConfigureAwait(false);
        CallToolResult unknown = await CallAsync(
            session,
            "local_deck_validate",
            new Dictionary<string, object?> { ["deckId"] = deckId, ["rules"] = "modern" }).ConfigureAwait(false);

        JsonElement plainData = Assert.IsType<JsonElement>(plain.StructuredContent).GetProperty("data");
        Assert.False(plainData.TryGetProperty("rules", out _));
        await AssertDataMatchesSchemaAsync(session, "local_deck_validate", plainData).ConfigureAwait(false);
        AssertFailure(withRules, "not-cached", "card-data-not-installed");
        Assert.Contains("scryfall_card_data_sync", FailureMessage(withRules), StringComparison.Ordinal);
        AssertBindingFailure(unknown);
    }

    /// <summary>
    /// Verifies card_list_query and card_lists_compare bind their sources and answer with not-cached when no card
    /// data is installed, and a nested comparison binds through its kind.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task CardListTools_DeckSources_BindAndNeedInstalledCardData()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, _) = await CreateDeckAsync(session).ConfigureAwait(false);
        object deck = new { kind = "deck", deckId };

        CallToolResult query = await CallAsync(
            session,
            "card_list_query",
            new Dictionary<string, object?>
            {
                ["source"] = deck,
                ["groupBy"] = ManaValueGroup,
                ["metrics"] = QuantityMetric,
            }).ConfigureAwait(false);
        CallToolResult compare = await CallAsync(
            session,
            "card_lists_compare",
            new Dictionary<string, object?>
            {
                ["left"] = new
                {
                    kind = "compare",
                    left = deck,
                    right = deck,
                    operation = "union",
                },
                ["right"] = deck,
                ["operation"] = "difference",
            }).ConfigureAwait(false);

        AssertFailure(query, "not-cached", "card-data-not-installed");
        AssertFailure(compare, "not-cached", "card-data-not-installed");
    }

    /// <summary>
    /// Verifies stats_castability binds its source, group, and turn unions, with the kind last, and answers with
    /// not-cached when no card data is installed.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StatsCastability_BindsItsUnionsAndNeedsInstalledCardData()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, _) = await CreateDeckAsync(session).ConfigureAwait(false);
        Guid entry = await FirstEntryAsync(session, deckId).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "stats_castability",
            new Dictionary<string, object?>
            {
                ["source"] = new { deckId, kind = "deck" },
                ["sources"] = new[]
                {
                    new
                    {
                        id = "rocks",
                        select = new { ids = new[] { entry }, kind = "entry-ids" },
                        produces = new { @default = ColorlessMana },
                    },
                },
                ["turn"] = new { turn = 3, kind = "fixed" },
                ["onThePlay"] = true,
                ["requireLandDrops"] = false,
            }).ConfigureAwait(false);

        AssertFailure(result, "not-cached", "card-data-not-installed");
    }

    /// <summary>
    /// Verifies stats_castability works when only the stats toolset is selected: it reaches the deck store and fails
    /// for the deck it cannot find, not with a message that the decks toolset is off.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StatsCastability_WithOnlyTheStatsToolset_ReadsTheDeckStore()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "stats",
            McpCredentials.None,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "stats_castability",
            new Dictionary<string, object?>
            {
                ["source"] = new { kind = "deck", deckId = Guid.NewGuid() },
                ["sources"] = new[]
                {
                    new
                    {
                        id = "lands",
                        select = new { kind = "entry-ids", ids = new[] { Guid.NewGuid() } },
                        produces = new { @default = GreenMana },
                    },
                },
                ["turn"] = new { kind = "mana-value" },
                ["onThePlay"] = true,
                ["requireLandDrops"] = true,
            }).ConfigureAwait(false);

        AssertFailure(result, "not-found", null);
    }

    /// <summary>
    /// Verifies the bracket facts tool refuses a stale revision as a conflict before any request leaves the machine.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task BracketFacts_StaleRevision_IsAConflict()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, long revision) = await CreateDeckAsync(session).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "spellbook_deck_bracket_facts_get",
            new Dictionary<string, object?> { ["deckId"] = deckId, ["expectedRevision"] = revision + 5 })
            .ConfigureAwait(false);

        AssertFailure(result, "conflict", "deck-revision-conflict");
    }

    /// <summary>
    /// Verifies the Playgroup game statistics tool binds and, with the empty test credential, reports the service as
    /// unavailable with the error flag, and a bad grouping is refused.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task PlaygroupGameStats_BindsAndReportsAnUnavailableService()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        CallToolResult unavailable = await CallAsync(
            session,
            "playgroup_games_stats_get",
            new Dictionary<string, object?> { ["playgroupId"] = 1, ["groupBy"] = "deck", ["maxGames"] = 5 })
            .ConfigureAwait(false);
        CallToolResult badGroup = await CallAsync(
            session,
            "playgroup_games_stats_get",
            new Dictionary<string, object?> { ["playgroupId"] = 1, ["groupBy"] = "commander", ["maxGames"] = 5 })
            .ConfigureAwait(false);

        AssertFailure(unavailable, "unavailable", null);
        AssertBindingFailure(badGroup);
    }

    /// <summary>
    /// Verifies an Archidekt list source with no Archidekt credentials is refused naming the credential problem and
    /// the source, and that a deck source is refused naming --toolsets when the decks toolset is not selected.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ListSources_WithoutCredentialsOrToolsets_AreRefusedWithTheReason()
    {
        await using McpProcessSession noCredentials = await McpProcessSession.StartAsync(
            "local",
            null,
            McpCredentials.None,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        await using McpProcessSession scryfallOnly = await McpProcessSession.StartAsync(
            "local",
            "scryfall",
            McpCredentials.None,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        object cards = new { kind = "cards", ids = new[] { Guid.NewGuid() } };

        CallToolResult archidekt = await CallAsync(
            noCredentials,
            "card_lists_compare",
            new Dictionary<string, object?>
            {
                ["left"] = new { kind = "archidekt-deck", archidektDeckId = "42" },
                ["right"] = cards,
                ["operation"] = "union",
            }).ConfigureAwait(false);
        CallToolResult deck = await CallAsync(
            scryfallOnly,
            "card_lists_compare",
            new Dictionary<string, object?>
            {
                ["left"] = new { kind = "deck", deckId = Guid.NewGuid() },
                ["right"] = cards,
                ["operation"] = "union",
            }).ConfigureAwait(false);

        JsonElement archidektBody = AssertFailure(archidekt, "invalid-input", "credentials-missing");
        JsonElement deckBody = AssertFailure(deck, "invalid-input", "toolset-not-selected");
        Assert.Equal("left", archidektBody.GetProperty("path").GetString());
        Assert.Equal("left", deckBody.GetProperty("path").GetString());
        Assert.Contains("--toolsets", deckBody.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a deck with Sol Ring in the main zone and in the sideboard.
    /// </summary>
    private static async Task<(Guid DeckId, long Revision)> CreateDeckAsync(McpProcessSession session)
    {
        CallToolResult created = await CallAsync(
            session,
            "local_deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Phase four fixture",
                    format = "commander",
                    entries = new[]
                    {
                        new { quantity = 1, cardName = "Sol Ring", zone = "main" },
                        new { quantity = 1, cardName = "Sol Ring", zone = "sideboard" },
                    },
                },
            }).ConfigureAwait(false);
        Assert.NotEqual(true, created.IsError);
        JsonElement data = Assert.IsType<JsonElement>(created.StructuredContent).GetProperty("data");
        return (data.GetProperty("deckId").GetGuid(), data.GetProperty("revision").GetInt64());
    }

    /// <summary>
    /// Reads the ID of the first entry of a deck.
    /// </summary>
    private static async Task<Guid> FirstEntryAsync(McpProcessSession session, Guid deckId)
    {
        CallToolResult deck = await CallAsync(
            session,
            "local_deck_get",
            new Dictionary<string, object?> { ["deckId"] = deckId }).ConfigureAwait(false);
        JsonElement data = Assert.IsType<JsonElement>(deck.StructuredContent).GetProperty("data");
        return data.GetProperty("entries")[0].GetProperty("entryId").GetGuid();
    }

    /// <summary>
    /// Calls one tool through the official MCP client and returns the raw protocol result.
    /// </summary>
    private static async Task<CallToolResult> CallAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        return await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the result kind of a structured result.
    /// </summary>
    private static string? Kind(CallToolResult result)
    {
        return Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("kind").GetString();
    }

    /// <summary>
    /// Reads the message of a failure, or an empty text.
    /// </summary>
    private static string FailureMessage(CallToolResult result)
    {
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        return structured.TryGetProperty("message", out JsonElement message)
            ? message.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Asserts a failure with the error flag set, the given kind, and the given reason code when one is named.
    /// </summary>
    private static JsonElement AssertFailure(CallToolResult result, string kind, string? reasonCode)
    {
        string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.IsError, text);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(kind, structured.GetProperty("kind").GetString());
        if (reasonCode is not null)
        {
            Assert.Equal(reasonCode, structured.GetProperty("reasonCode").GetString());
        }

        return structured;
    }

    /// <summary>
    /// Asserts a refusal of the arguments: either the binding failure or an invalid-input failure.
    /// </summary>
    private static void AssertBindingFailure(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Equal("invalid-input", Kind(result));
    }

    /// <summary>
    /// Checks that the tool publishes an output schema and that the result data is a JSON object. The server publishes
    /// the open schema (<c>true</c>) for result unions, so when a schema describes properties, every top-level name
    /// of the data must be one of them.
    /// </summary>
    private static async Task AssertDataMatchesSchemaAsync(McpProcessSession session, string toolName, JsonElement data)
    {
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        McpClientTool tool = tools.Single(candidate => candidate.Name == toolName);
        JsonElement schema = Assert.IsType<JsonElement>(tool.ProtocolTool.OutputSchema);
        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out JsonElement properties))
        {
            foreach (JsonProperty property in data.EnumerateObject())
            {
                Assert.True(properties.TryGetProperty(property.Name, out _), property.Name);
            }
        }
    }
}
