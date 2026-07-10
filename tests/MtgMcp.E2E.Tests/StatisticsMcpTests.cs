using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the complete exact statistics surface through the official MCP client.
/// </summary>
public sealed class StatisticsMcpTests
{
    /// <summary>
    /// Lists the exact eight statistics tools in canonical discovery order.
    /// </summary>
    private static readonly string[] ToolNames =
    [
        "stats_deck_summary",
        "stats_hypergeometric",
        "stats_mana_availability",
        "stats_minimum_count",
        "stats_mulligan",
        "stats_multivariate",
        "stats_package_assembly",
        "stats_turn_table",
    ];

    /// <summary>
    /// Reuses immutable serialized string vectors in the realistic workflow.
    /// </summary>
    private static readonly string[] WhiteCapability = ["W"];

    /// <summary>
    /// Reuses the red source capability vector.
    /// </summary>
    private static readonly string[] RedCapability = ["R"];

    /// <summary>
    /// Reuses the red-white source capability vector.
    /// </summary>
    private static readonly string[] RedWhiteCapabilities = ["R", "W"];

    /// <summary>
    /// Reuses the package groups eligible for the threat slot.
    /// </summary>
    private static readonly string[] ThreatEligibleGroups = ["threat", "flex"];

    /// <summary>
    /// Reuses the package groups eligible for the answer slot.
    /// </summary>
    private static readonly string[] AnswerEligibleGroups = ["interaction", "flex"];

    /// <summary>
    /// Reuses the explicit bottom-priority vector.
    /// </summary>
    private static readonly string[] BottomPriority = ["other"];

    /// <summary>
    /// Reuses the single exact group used by structured-failure fixtures.
    /// </summary>
    private static readonly string[] HitGroup = ["hits"];

    /// <summary>
    /// Reuses the exact main-zone selector value.
    /// </summary>
    private static readonly string[] MainZone = ["main"];

    /// <summary>
    /// Reuses the requested median percentile.
    /// </summary>
    private static readonly int[] MedianPercentile = [50];

    /// <summary>
    /// Verifies exact read-only annotations and useful descriptions throughout each input schema.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StatisticsTools_EveryMode_PublishExactDescribedReadSurface()
    {
        foreach (string mode in new[] { "read-only", "local", "remote" })
        {
            await using McpProcessSession session = await McpProcessSession.StartAsync(
                mode,
                "stats",
                TestContext.Current.CancellationToken).ConfigureAwait(false);
            IList<McpClientTool> tools = await session.Client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

            Assert.Equal(ToolNames, tools.Select(value => value.Name));
            foreach (McpClientTool tool in tools)
            {
                Assert.Equal(["request"], tool.ProtocolTool.InputSchema
                    .GetProperty("properties")
                    .EnumerateObject()
                    .Select(value => value.Name));
                Assert.NotNull(tool.ProtocolTool.OutputSchema);
                Assert.NotNull(tool.ProtocolTool.Annotations);
                Assert.True(tool.ProtocolTool.Annotations.ReadOnlyHint);
                Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
                Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
                Assert.False(tool.ProtocolTool.Annotations.OpenWorldHint);
                List<string> missing = [];
                CollectMissingDescriptions(tool.ProtocolTool.InputSchema, tool.Name, missing);
                Assert.True(
                    missing.Count == 0,
                    $"Missing statistics input descriptions: {string.Join(", ", missing)}");
            }

            Assert.False(Directory.Exists(session.DataRoot));
        }
    }

    /// <summary>
    /// Exercises every statistics method on an explicit 99-card library and proves format neutrality.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StatisticsTools_RealisticNinetyNineCardDeck_ReturnExactEvidence()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,stats",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement commanderDeck = await CreateDeckAsync(
            session,
            "Exact Statistics Commander Shape",
            "commander",
            includeCommander: true).ConfigureAwait(false);
        Guid deckId = commanderDeck.GetProperty("deckId").GetGuid();
        long revision = commanderDeck.GetProperty("revision").GetInt64();
        Dictionary<string, Guid> entries = EntryIds(commanderDeck);

        object population = DeckPopulation(deckId, revision, entries);
        JsonElement hypergeometric = await CallExactAsync(
            session,
            "stats_hypergeometric",
            new
            {
                population,
                successGroup = "lands",
                drawCount = 7,
                @event = new { kind = "at-least", count = 3 },
            }).ConfigureAwait(false);
        Assert.Equal(99, hypergeometric.GetProperty("population").GetProperty("totalCount").GetInt32());
        Assert.Equal(36, hypergeometric.GetProperty("successCount").GetInt32());
        AssertExactOpeningHandProbability(hypergeometric.GetProperty("probability"));

        JsonElement multivariate = await CallExactAsync(
            session,
            "stats_multivariate",
            new
            {
                population,
                drawCount = 7,
                conditions = new object[]
                {
                    new { group = "lands", minimum = 2, maximum = 5 },
                    new { group = "interaction", minimum = 1, maximum = (int?)null },
                },
            }).ConfigureAwait(false);
        AssertProbabilityNormalized(multivariate);

        JsonElement turnTable = await CallExactAsync(
            session,
            "stats_turn_table",
            new
            {
                population,
                successGroup = "lands",
                openingHandSize = 7,
                drawsByTurn = new object[]
                {
                    new { turn = 1, draws = 0 },
                    new { turn = 2, draws = 1 },
                    new { turn = 3, draws = 1 },
                    new { turn = 4, draws = 1 },
                },
                @event = new { kind = "at-least", count = 4 },
            }).ConfigureAwait(false);
        Assert.Equal([7, 8, 9, 10], turnTable.GetProperty("rows")
            .EnumerateArray()
            .Select(row => row.GetProperty("cardsSeen").GetInt32()));

        JsonElement mana = await CallExactAsync(
            session,
            "stats_mana_availability",
            new
            {
                population,
                drawCount = 10,
                sources = new object[]
                {
                    new { group = "white-only", capabilities = WhiteCapability },
                    new { group = "red-only", capabilities = RedCapability },
                    new { group = "red-white", capabilities = RedWhiteCapabilities },
                },
                requirement = new { white = 1, red = 1, generic = 1 },
                maximumUsableSources = 3,
            }).ConfigureAwait(false);
        AssertProbabilityNormalized(mana);

        JsonElement package = await CallExactAsync(
            session,
            "stats_package_assembly",
            new
            {
                population,
                drawCount = 10,
                requirements = new object[]
                {
                    new { name = "threat", count = 1, eligibleGroups = ThreatEligibleGroups },
                    new { name = "answer", count = 1, eligibleGroups = AnswerEligibleGroups },
                },
            }).ConfigureAwait(false);
        AssertProbabilityNormalized(package);

        JsonElement mulligan = await CallExactAsync(
            session,
            "stats_mulligan",
            new
            {
                population,
                attempts = new object[]
                {
                    new { drawCount = 7, bottomCount = 0, forced = false },
                    new { drawCount = 7, bottomCount = 1, forced = false },
                    new { drawCount = 7, bottomCount = 2, forced = true },
                },
                keepConditions = new object[]
                {
                    new { group = "lands", minimum = 2, maximum = 5 },
                },
                bottomPriority = BottomPriority,
                finalConditions = new object[]
                {
                    new { group = "lands", minimum = 2, maximum = (int?)null },
                },
            }).ConfigureAwait(false);
        Assert.Equal(3, mulligan.GetProperty("attempts").GetArrayLength());
        Assert.Equal("1", mulligan.GetProperty("attempts")[2]
            .GetProperty("conditionalKeepProbability")
            .GetProperty("numerator")
            .GetString());

        JsonElement minimum = await CallExactAsync(
            session,
            "stats_minimum_count",
            new
            {
                @event = new
                {
                    kind = "hypergeometric-at-least",
                    populationSize = 99,
                    drawCount = 7,
                    requiredSuccesses = 3,
                },
                targetNumerator = "1",
                targetDenominator = "2",
                minimumCount = 0,
                maximumCount = 99,
            }).ConfigureAwait(false);
        Assert.True(minimum.GetProperty("found").GetBoolean());
        Assert.InRange(minimum.GetProperty("count").GetInt32(), 1, 99);

        JsonElement summary = await CallExactAsync(
            session,
            "stats_deck_summary",
            SummaryRequest(deckId, revision, entries)).ConfigureAwait(false);
        Assert.Equal(99, summary.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(10, summary.GetProperty("entryCount").GetInt32());
        Assert.Equal("nearest-rank", summary.GetProperty("numericSeries")[0]
            .GetProperty("percentileMethod")
            .GetString());
        Assert.Equal(99, summary.GetProperty("zonePartition").GetProperty("includedQuantity").GetInt32());
        Assert.Equal(1, summary.GetProperty("excludedEntries").GetArrayLength());

        JsonElement customDeck = await CreateDeckAsync(
            session,
            "Exact Statistics Custom Shape",
            "kitchen-table",
            includeCommander: false).ConfigureAwait(false);
        Guid customDeckId = customDeck.GetProperty("deckId").GetGuid();
        long customRevision = customDeck.GetProperty("revision").GetInt64();
        JsonElement customProbability = await CallExactAsync(
            session,
            "stats_hypergeometric",
            new
            {
                population = DeckPopulation(customDeckId, customRevision, EntryIds(customDeck)),
                successGroup = "lands",
                drawCount = 7,
                @event = new { kind = "at-least", count = 3 },
            }).ConfigureAwait(false);
        Assert.Equal(
            hypergeometric.GetProperty("probability").GetProperty("numerator").GetString(),
            customProbability.GetProperty("probability").GetProperty("numerator").GetString());
        Assert.Equal(
            hypergeometric.GetProperty("probability").GetProperty("denominator").GetString(),
            customProbability.GetProperty("probability").GetProperty("denominator").GetString());

        await DeleteDeckAsync(session, deckId, revision).ConfigureAwait(false);
        await DeleteDeckAsync(session, customDeckId, customRevision).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies invalid, missing, stale, and bounded outcomes retain their closed structured shapes.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task StatisticsTools_FailuresRemainStructuredAndContainNoPartialProbability()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,stats",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CreateDeckAsync(
            session,
            "Statistics Failure Fixture",
            "unrestricted-custom",
            includeCommander: false).ConfigureAwait(false);
        Guid deckId = deck.GetProperty("deckId").GetGuid();
        long revision = deck.GetProperty("revision").GetInt64();
        JsonElement stale = await CallResultAsync(
            session,
            "stats_deck_summary",
            new { request = FailureSummaryRequest(deckId, revision + 1) }).ConfigureAwait(false);
        Assert.Equal("conflict", stale.GetProperty("kind").GetString());

        JsonElement missing = await CallResultAsync(
            session,
            "stats_deck_summary",
            new { request = FailureSummaryRequest(Guid.NewGuid(), 1) }).ConfigureAwait(false);
        Assert.Equal("not-found", missing.GetProperty("kind").GetString());

        object population = new
        {
            kind = "raw",
            buckets = new object[] { new { count = 10, groups = HitGroup } },
            declaredGroups = HitGroup,
        };
        JsonElement invalid = await CallResultAsync(
            session,
            "stats_hypergeometric",
            new
            {
                request = new
                {
                    population,
                    successGroup = "hits",
                    drawCount = 11,
                    @event = new { kind = "at-least", count = 1 },
                },
            }).ConfigureAwait(false);
        Assert.Equal("invalid-input", invalid.GetProperty("kind").GetString());

        JsonElement boundedOperation = await CallSuccessAsync(
            session,
            "stats_hypergeometric",
            new
            {
                request = new
                {
                    population = new
                    {
                        kind = "raw",
                        buckets = new object[] { new { count = 1001, groups = HitGroup } },
                        declaredGroups = HitGroup,
                    },
                    successGroup = "hits",
                    drawCount = 7,
                    @event = new { kind = "at-least", count = 1 },
                },
            }).ConfigureAwait(false);
        Assert.Equal("bounded-unsupported", boundedOperation.GetProperty("kind").GetString());
        Assert.False(boundedOperation.TryGetProperty("data", out _));
        Assert.Equal(
            "population",
            boundedOperation.GetProperty("limit").GetProperty("limitKind").GetString());

        await DeleteDeckAsync(session, deckId, revision).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the deterministic 99-card library used by the complete workflow.
    /// </summary>
    private static async Task<JsonElement> CreateDeckAsync(
        McpProcessSession session,
        string name,
        string format,
        bool includeCommander)
    {
        List<object> entries =
        [
            new { quantity = 15, cardName = "Plains", zone = "main" },
            new { quantity = 15, cardName = "Mountain", zone = "main" },
            new { quantity = 6, cardName = "Sacred Foundry", zone = "main" },
            new { quantity = 4, cardName = "Boros Signet", zone = "main" },
            new { quantity = 8, cardName = "Recruitment Officer", zone = "main" },
            new { quantity = 12, cardName = "Resolute Reinforcements", zone = "main" },
            new { quantity = 12, cardName = "Adeline, Resplendent Cathar", zone = "main" },
            new { quantity = 10, cardName = "Hero of Bladehold", zone = "main" },
            new { quantity = 9, cardName = "Swords to Plowshares", zone = "main" },
            new { quantity = 8, cardName = "Shared Animosity", zone = "main" },
        ];
        if (includeCommander)
        {
            entries.Add(new { quantity = 1, cardName = "Winota, Joiner of Forces", zone = "commander" });
        }

        return await CallSuccessAsync(
            session,
            "deck_create",
            new
            {
                request = new
                {
                    name,
                    description = "Disposable exact-statistics MCP acceptance deck",
                    format,
                    entries,
                },
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds explicit deck selection and caller-owned group definitions.
    /// </summary>
    private static object DeckPopulation(
        Guid deckId,
        long revision,
        IReadOnlyDictionary<string, Guid> entries)
    {
        object EntrySelector(params string[] names)
        {
            return new
            {
                kind = "entry-ids",
                entryIds = names.Select(name => entries[name]).ToArray(),
            };
        }

        return new
        {
            kind = "deck",
            deckId,
            expectedRevision = revision,
            populationSelectors = new object[]
            {
                new { kind = "zone-names", zoneNames = new[] { "main" } },
            },
            groups = new object[]
            {
                new { name = "lands", selectors = new[] { EntrySelector("Plains", "Mountain", "Sacred Foundry") } },
                new { name = "white-only", selectors = new[] { EntrySelector("Plains") } },
                new { name = "red-only", selectors = new[] { EntrySelector("Mountain") } },
                new { name = "red-white", selectors = new[] { EntrySelector("Sacred Foundry") } },
                new { name = "interaction", selectors = new[] { EntrySelector("Swords to Plowshares") } },
                new { name = "threat", selectors = new[] { EntrySelector("Recruitment Officer", "Resolute Reinforcements", "Adeline, Resplendent Cathar", "Hero of Bladehold") } },
                new { name = "flex", selectors = new[] { EntrySelector("Shared Animosity") } },
                new { name = "other", selectors = new[] { EntrySelector("Boros Signet", "Shared Animosity") } },
            },
        };
    }

    /// <summary>
    /// Builds a deck-summary request with caller-supplied exact mana values.
    /// </summary>
    private static object SummaryRequest(
        Guid deckId,
        long revision,
        IReadOnlyDictionary<string, Guid> entries)
    {
        (string Name, string Value)[] values =
        [
            ("Plains", "0"),
            ("Mountain", "0"),
            ("Sacred Foundry", "0"),
            ("Boros Signet", "2"),
            ("Recruitment Officer", "1"),
            ("Resolute Reinforcements", "2"),
            ("Adeline, Resplendent Cathar", "3"),
            ("Hero of Bladehold", "4"),
            ("Swords to Plowshares", "1"),
            ("Shared Animosity", "3"),
        ];
        return new
        {
            deckId,
            expectedRevision = revision,
            populationSelectors = new object[]
            {
                new { kind = "zone-names", zoneNames = new[] { "main" } },
            },
            numericSeries = new object[]
            {
                new
                {
                    name = "mana-value",
                    values = values.Select(value => new
                    {
                        entryId = entries[value.Name],
                        value = value.Value,
                    }).ToArray(),
                },
            },
            percentiles = new[] { 25, 50, 75, 100 },
            zonePartition = new
            {
                includedZones = new[] { "main" },
                excludedZones = new[] { "sideboard", "commander" },
            },
        };
    }

    /// <summary>
    /// Builds the minimal deterministic summary request used by failure cases.
    /// </summary>
    private static object FailureSummaryRequest(Guid deckId, long expectedRevision)
    {
        return new
        {
            deckId,
            expectedRevision,
            populationSelectors = new object[]
            {
                new { kind = "zone-names", zoneNames = MainZone },
            },
            numericSeries = Array.Empty<object>(),
            percentiles = MedianPercentile,
            zonePartition = (object?)null,
        };
    }

    /// <summary>
    /// Maps stored entry names to stable IDs for explicit selector construction.
    /// </summary>
    private static Dictionary<string, Guid> EntryIds(JsonElement deck)
    {
        return deck.GetProperty("entries")
            .EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("cardName").GetString()!,
                value => value.GetProperty("entryId").GetGuid(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Deletes one acceptance-created local deck at its unchanged revision.
    /// </summary>
    private static async Task DeleteDeckAsync(McpProcessSession session, Guid deckId, long revision)
    {
        _ = await CallSuccessAsync(
            session,
            "deck_delete",
            new { deckId, expectedRevision = revision }).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls one statistics tool and extracts its exact nested payload.
    /// </summary>
    private static async Task<JsonElement> CallExactAsync(
        McpProcessSession session,
        string toolName,
        object request)
    {
        JsonElement calculation = await CallSuccessAsync(
            session,
            toolName,
            new { request }).ConfigureAwait(false);
        Assert.Equal("exact", calculation.GetProperty("kind").GetString());
        return calculation.GetProperty("data");
    }

    /// <summary>
    /// Calls one MCP tool and extracts its successful operation payload.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        object rawArguments)
    {
        JsonElement result = await CallResultAsync(session, toolName, rawArguments).ConfigureAwait(false);
        Assert.Equal("success", result.GetProperty("kind").GetString());
        return result.GetProperty("data");
    }

    /// <summary>
    /// Calls one MCP tool and extracts its closed operation result.
    /// </summary>
    private static async Task<JsonElement> CallResultAsync(
        McpProcessSession session,
        string toolName,
        object rawArguments)
    {
        Dictionary<string, object?> arguments = JsonSerializer.SerializeToElement(rawArguments)
            .EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => (object?)value.Value.Clone(),
                StringComparer.Ordinal);
        CallToolResult call = await session.Client.CallToolAsync(
            toolName,
            (IReadOnlyDictionary<string, object?>?)arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, call.IsError);
        JsonElement content = Assert.IsType<JsonElement>(call.StructuredContent);
        return content.GetProperty("result");
    }

    /// <summary>
    /// Compares the MCP opening-hand result to an independently assembled direct formula.
    /// </summary>
    private static void AssertExactOpeningHandProbability(JsonElement probability)
    {
        BigInteger numerator = BigInteger.Zero;
        for (int lands = 3; lands <= 7; lands++)
        {
            numerator += Choose(36, lands) * Choose(63, 7 - lands);
        }

        BigInteger denominator = Choose(99, 7);
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Assert.Equal(
            (numerator / divisor).ToString(CultureInfo.InvariantCulture),
            probability.GetProperty("numerator").GetString());
        Assert.Equal(
            (denominator / divisor).ToString(CultureInfo.InvariantCulture),
            probability.GetProperty("denominator").GetString());
    }

    /// <summary>
    /// Verifies one result returns a reduced probability and exact complement summing to one.
    /// </summary>
    private static void AssertProbabilityNormalized(JsonElement result)
    {
        JsonElement probability = result.GetProperty("probability");
        JsonElement complement = result.GetProperty("complement");
        BigInteger numerator = BigInteger.Parse(probability.GetProperty("numerator").GetString()!, CultureInfo.InvariantCulture);
        BigInteger denominator = BigInteger.Parse(probability.GetProperty("denominator").GetString()!, CultureInfo.InvariantCulture);
        BigInteger complementNumerator = BigInteger.Parse(complement.GetProperty("numerator").GetString()!, CultureInfo.InvariantCulture);
        BigInteger complementDenominator = BigInteger.Parse(complement.GetProperty("denominator").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(denominator * complementDenominator, numerator * complementDenominator + complementNumerator * denominator);
    }

    /// <summary>
    /// Calculates a binomial coefficient independently for the realistic probability oracle.
    /// </summary>
    private static BigInteger Choose(int population, int count)
    {
        count = Math.Min(count, population - count);
        BigInteger result = BigInteger.One;
        for (int index = 1; index <= count; index++)
        {
            result = result * (population - count + index) / index;
        }

        return result;
    }

    /// <summary>
    /// Recursively reports generated object fields or discriminators lacking explanatory text.
    /// </summary>
    private static void CollectMissingDescriptions(
        JsonElement schema,
        string path,
        ICollection<string> missing)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("properties", out JsonElement properties))
        {
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("description", out JsonElement description) ||
                    string.IsNullOrWhiteSpace(description.GetString()))
                {
                    missing.Add($"{path}.{property.Name}");
                }

                CollectMissingDescriptions(property.Value, $"{path}.{property.Name}", missing);
            }
        }

        foreach (string branch in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (schema.TryGetProperty(branch, out JsonElement variants))
            {
                int index = 0;
                foreach (JsonElement variant in variants.EnumerateArray())
                {
                    CollectMissingDescriptions(variant, $"{path}.{branch}[{index}]", missing);
                    index++;
                }
            }
        }

        if (schema.TryGetProperty("items", out JsonElement items))
        {
            CollectMissingDescriptions(items, $"{path}[]", missing);
        }
    }
}
