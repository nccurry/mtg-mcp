using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MtgMcp.Core.Results;
using MtgMcp.Spellbook;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the opt-in Commander Spellbook surface through cached source evidence only.
/// </summary>
public sealed class SpellbookMcpTests
{
    /// <summary>
    /// Verifies every Spellbook tool publishes its exact schema and read-only annotations.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task SpellbookTools_EachMode_PublishExactSchemasAndAnnotations()
    {
        foreach (string mode in new[] { "read-only", "local", "remote" })
        {
            await using McpProcessSession session = await McpProcessSession.StartAsync(
                mode,
                "spellbook",
                TestContext.Current.CancellationToken).ConfigureAwait(false);
            IList<McpClientTool> tools = await session.Client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            Dictionary<string, string[]> expectedProperties = new(StringComparer.Ordinal)
            {
                ["spellbook_deck_combos_find"] =
                    ["deckId", "expectedRevision", "groupByCombo", "limit", "offset", "sourceQuery"],
                ["spellbook_variant_get"] = ["variantId"],
                ["spellbook_variant_search"] = ["groupByCombo", "limit", "offset", "sourceQuery"],
            };

            Assert.Equal(expectedProperties.Keys.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name));
            foreach (McpClientTool tool in tools)
            {
                Assert.Equal(
                    expectedProperties[tool.Name],
                    tool.ProtocolTool.InputSchema.GetProperty("properties")
                        .EnumerateObject()
                        .Select(value => value.Name)
                        .Order(StringComparer.Ordinal));
                Assert.NotNull(tool.ProtocolTool.OutputSchema);
                Assert.NotNull(tool.ProtocolTool.Annotations);
                Assert.True(tool.ProtocolTool.Annotations.ReadOnlyHint);
                Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
                Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
                Assert.True(tool.ProtocolTool.Annotations.OpenWorldHint);
            }
        }
    }

    /// <summary>
    /// Verifies cached source responses remain separate from the local deck-selection evidence.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task SpellbookTools_CachedEvidence_UsesExactLocalDeckRevision()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,spellbook",
            SeedAsync,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Spellbook fixture deck",
                    format = "commander",
                    entries = new[]
                    {
                        new { quantity = 1, cardName = "Fixture Commander", zone = "commander" },
                        new { quantity = 2, cardName = "Fixture Main", zone = "main" },
                        new { quantity = 1, cardName = "Fixture Sideboard", zone = "sideboard" },
                        new { quantity = 1, cardName = "Fixture Maybe", zone = "maybeboard" },
                    },
                },
            }).ConfigureAwait(false);
        Guid deckId = deck.GetProperty("deckId").GetGuid();
        long revision = deck.GetProperty("revision").GetInt64();

        JsonElement search = await CallSuccessAsync(
            session,
            "spellbook_variant_search",
            new Dictionary<string, object?>
            {
                ["sourceQuery"] = "type:combo",
                ["limit"] = 20,
                ["offset"] = 0,
                ["groupByCombo"] = true,
            }).ConfigureAwait(false);
        JsonElement variant = await CallSuccessAsync(
            session,
            "spellbook_variant_get",
            new Dictionary<string, object?> { ["variantId"] = "fixture-variant" }).ConfigureAwait(false);
        JsonElement combos = await CallSuccessAsync(
            session,
            "spellbook_deck_combos_find",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["sourceQuery"] = "type:combo",
                ["limit"] = 20,
                ["offset"] = 0,
                ["groupByCombo"] = true,
            }).ConfigureAwait(false);

        Assert.Equal("cached", search.GetProperty("cacheStatus").GetString());
        Assert.Equal("type:combo", search.GetProperty("request").GetProperty("sourceQuery").GetString());
        Assert.Equal("cached", variant.GetProperty("cacheStatus").GetString());
        Assert.Equal("fixture-variant", variant.GetProperty("request").GetProperty("variantId").GetString());
        JsonElement selectedDeck = combos.GetProperty("deck");
        Assert.Equal(deckId, selectedDeck.GetProperty("deckId").GetGuid());
        Assert.Equal(revision, selectedDeck.GetProperty("revision").GetInt64());
        Assert.Equal(2, selectedDeck.GetProperty("sentEntries").GetArrayLength());
        Assert.Equal("Fixture Commander", selectedDeck.GetProperty("sentEntries")[0].GetProperty("card").GetString());
        Assert.Equal("Fixture Main", selectedDeck.GetProperty("sentEntries")[1].GetProperty("card").GetString());
        Assert.Equal(2, selectedDeck.GetProperty("sentEntries")[1].GetProperty("quantity").GetInt32());
        Assert.Equal(2, selectedDeck.GetProperty("skippedEntries").GetArrayLength());
        Assert.Equal("Fixture Maybe", selectedDeck.GetProperty("skippedEntries")[0].GetProperty("cardName").GetString());
        Assert.Equal("Fixture Sideboard", selectedDeck.GetProperty("skippedEntries")[1].GetProperty("cardName").GetString());
        Assert.Equal("cached", combos.GetProperty("source").GetProperty("cacheStatus").GetString());
        Assert.Equal(
            "fixture-combo",
            combos.GetProperty("source").GetProperty("data").GetProperty("included")[0].GetProperty("id").GetString());
    }

    /// <summary>
    /// Stores the three exact source requests that the child process will replay from its cache.
    /// </summary>
    private static async Task SeedAsync(string dataRoot, CancellationToken cancellationToken)
    {
        FixtureHandler handler = new(
            """{"count":1,"results":[{"id":"fixture-search"}]}""",
            """{"id":"fixture-variant","uses":[{"card":"Fixture Commander"}]}""",
            """{"identity":{"colors":["U"]},"included":[{"id":"fixture-combo"}]}""");
        SpellbookOptions options = SpellbookOptions.CreateDefault(dataRoot);
        SpellbookDatabase database = new(dataRoot);
        SpellbookTransport transport = new(
            new HttpClient(handler),
            ownsHttpClient: true,
            TimeProvider.System);
        using SpellbookService service = new(options, database, transport, TimeProvider.System);

        Assert.IsType<OperationSuccess<SpellbookEvidence>>((await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(
                "type:combo",
                new SpellbookPageOptions(20, 0, true)),
            cancellationToken).ConfigureAwait(false)).Value);
        Assert.IsType<OperationSuccess<SpellbookEvidence>>((await service.GetVariantAsync(
            "fixture-variant",
            cancellationToken).ConfigureAwait(false)).Value);
        Assert.IsType<OperationSuccess<SpellbookEvidence>>((await service.FindDeckCombosAsync(
            new SpellbookDeckComboRequest(
                new SpellbookDeckRequest(
                    [new SpellbookDeckEntry("Fixture Commander", 1)],
                    [new SpellbookDeckEntry("Fixture Main", 2)]),
                "type:combo",
                new SpellbookPageOptions(20, 0, true)),
            cancellationToken).ConfigureAwait(false)).Value);
    }

    /// <summary>
    /// Calls one tool and extracts its successful structured result payload.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult call = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, call.IsError);
        JsonElement result = Assert.IsType<JsonElement>(call.StructuredContent);
        Assert.Equal("success", result.GetProperty("kind").GetString());
        return result.GetProperty("data");
    }

    /// <summary>
    /// Supplies queued JSON objects to the production source adapter while the child process stays offline.
    /// </summary>
    private sealed class FixtureHandler : HttpMessageHandler
    {
        /// <summary>
        /// Stores one JSON object for each expected seed request.
        /// </summary>
        private readonly Queue<string> responses;

        /// <summary>
        /// Creates a handler with source responses in call order.
        /// </summary>
        internal FixtureHandler(params string[] responses)
        {
            this.responses = new Queue<string>(responses);
        }

        /// <summary>
        /// Returns the next fixture object without allowing an unplanned source call.
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No Commander Spellbook fixture response remains.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue()),
            });
        }
    }
}
