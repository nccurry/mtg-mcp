using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Verifies the three bounded Commander Spellbook service workflows through fake HTTP.
/// </summary>
public sealed class SpellbookServiceTests
{
    /// <summary>
    /// Verifies a raw source query is encoded once, defaults are explicit, and a fresh result is cached.
    /// </summary>
    [Fact]
    public async Task VariantSearch_PreservesSourceQueryAndUsesCachedEvidence()
    {
        const string sourceQuery = "card:\"Thassa's Oracle\" (blue white)";
        SpellbookTestHttpHandler handler = new();
        handler.AddJson("{\"count\":1,\"next\":\"https://backend.commanderspellbook.com/variants/?q=card%3A%22Thassa%27s%20Oracle%22\",\"results\":[{\"unknown\":true}]}");
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);

        OperationResult<SpellbookEvidence> first = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(sourceQuery),
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookEvidence> second = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(sourceQuery),
            TestContext.Current.CancellationToken);

        SpellbookEvidence network = Success(first);
        SpellbookEvidence cached = Success(second);
        CapturedSpellbookRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://backend.commanderspellbook.com", request.Uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal(
            "/variants/?q=card%3A%22Thassa%27s%20Oracle%22%20%28blue%20white%29&limit=20&offset=0&groupByCombo=true",
            request.Uri.PathAndQuery);
        Assert.Contains("mtg-mcp/", request.UserAgent, StringComparison.Ordinal);
        Assert.Contains("github.com/nccurry/mtg-mcp", request.UserAgent, StringComparison.Ordinal);
        Assert.Equal("application/json", request.Accept);
        Assert.Equal("network", network.CacheStatus);
        Assert.Equal("cached", cached.CacheStatus);
        Assert.Equal(sourceQuery, network.Request.SourceQuery);
        Assert.Equal(new SpellbookPageRequest(20, 0, true), network.Request.Page);
        Assert.Equal(network.RetrievedAtUtc, cached.RetrievedAtUtc);
        Assert.True(cached.Data.GetProperty("results")[0].GetProperty("unknown").GetBoolean());
        Assert.Contains(network.Limitations, value => value.Contains("not proof", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies invalid request values stop before any source request or cache lookup.
    /// </summary>
    [Fact]
    public async Task Validation_RejectsInvalidInputsBeforeHttp()
    {
        SpellbookTestHttpHandler handler = new();
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationResult<SpellbookEvidence> blank = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(" "),
            token);
        OperationResult<SpellbookEvidence> page = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest("Sol Ring", new SpellbookPageOptions(Limit: 26)),
            token);
        OperationResult<SpellbookEvidence> id = await service.GetVariantAsync(" ", token);
        OperationResult<SpellbookEvidence> deck = await service.FindDeckCombosAsync(
            new SpellbookDeckComboRequest(new SpellbookDeckRequest([], [])),
            token);

        Assert.IsType<OperationInvalidInput>(blank.Value);
        Assert.IsType<OperationInvalidInput>(page.Value);
        Assert.IsType<OperationInvalidInput>(id.Value);
        Assert.IsType<OperationInvalidInput>(deck.Value);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Verifies an exact variant identifier stays one escaped path segment and source fields survive.
    /// </summary>
    [Fact]
    public async Task VariantGet_UsesOneEscapedPathSegment()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson("{\"id\":\"742 /1295\",\"extension\":42}");
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);

        SpellbookEvidence evidence = Success(await service.GetVariantAsync(
            "742 /1295",
            TestContext.Current.CancellationToken));

        CapturedSpellbookRequest request = Assert.Single(handler.Requests);
        Assert.Equal("/variants/742%20%2F1295/", request.Uri.PathAndQuery);
        Assert.Equal("742 /1295", evidence.Request.VariantId);
        Assert.Equal("GET /variants/742%20%2F1295/", evidence.Endpoint);
        Assert.Equal(42, evidence.Data.GetProperty("extension").GetInt32());
    }

    /// <summary>
    /// Verifies the deck route preserves its paginated object and sends only normalized source deck rows.
    /// </summary>
    [Fact]
    public async Task DeckCombos_SendsNormalizedDeckAndPreservesEverySourceGroup()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson("""
            {
              "count": null,
              "next": null,
              "previous": null,
              "results": {
                "identity": ["U", "B"],
                "included": [],
                "includedByChangingCommanders": [],
                "almostIncluded": [],
                "almostIncludedByAddingColors": [],
                "almostIncludedByChangingCommanders": [],
                "almostIncludedByAddingColorsAndChangingCommanders": [],
                "unknown": true
              }
            }
            """);
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);

        SpellbookEvidence evidence = Success(await service.FindDeckCombosAsync(
            new SpellbookDeckComboRequest(new SpellbookDeckRequest(
                [new SpellbookDeckEntry("Kess, Dissident Mage", 1)],
                [new SpellbookDeckEntry("Sol Ring", 1), new SpellbookDeckEntry("Sol Ring", 2), new SpellbookDeckEntry("Thassa's Oracle", 1)])),
            TestContext.Current.CancellationToken));

        CapturedSpellbookRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/find-my-combos?limit=20&offset=0&groupByCombo=true", request.Uri.PathAndQuery);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        JsonElement main = body.RootElement.GetProperty("main");
        Assert.Equal("Sol Ring", main[0].GetProperty("card").GetString());
        Assert.Equal(3, main[0].GetProperty("quantity").GetInt32());
        Assert.Equal("Thassa's Oracle", main[1].GetProperty("card").GetString());
        Assert.Equal("Kess, Dissident Mage", body.RootElement.GetProperty("commanders")[0].GetProperty("card").GetString());
        JsonElement results = evidence.Data.GetProperty("results");
        Assert.Equal(JsonValueKind.Object, results.ValueKind);
        Assert.True(results.GetProperty("unknown").GetBoolean());
        Assert.Equal(JsonValueKind.Array, results.GetProperty("almostIncludedByAddingColorsAndChangingCommanders").ValueKind);
        Assert.Null(evidence.Request.SourceQuery);
    }

    /// <summary>
    /// Verifies terminal source statuses map to safe typed results without exposing a provider body.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid-input")]
    [InlineData(HttpStatusCode.NotFound, "not-found")]
    [InlineData(HttpStatusCode.InternalServerError, "unavailable")]
    public async Task SourceStatus_MapsToSafeTypedResult(HttpStatusCode statusCode, string expectedKind)
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddStatus(statusCode);
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);

        OperationResult<SpellbookEvidence> result = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest("Sol Ring"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedKind, ResultKind(result));
        Assert.DoesNotContain("hidden", ResultMessage(result), StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies a source 429 records a cooldown and the next call stops before HTTP without retrying.
    /// </summary>
    [Fact]
    public async Task RateLimit_RecordsCooldownAndStopsLaterRequestBeforeHttp()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddStatus(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(4));
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);
        SpellbookVariantSearchRequest request = new("Sol Ring");

        OperationResult<SpellbookEvidence> first = await service.SearchVariantsAsync(request, TestContext.Current.CancellationToken);
        OperationResult<SpellbookEvidence> second = await service.SearchVariantsAsync(request, TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnavailable>(first.Value);
        Assert.IsType<OperationUnavailable>(second.Value);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies malformed and oversized successful responses never become cached source evidence.
    /// </summary>
    [Fact]
    public async Task InvalidOrOversizedResponse_MapsToExpectedFailure()
    {
        SpellbookTestHttpHandler malformedHandler = new();
        malformedHandler.AddJson("not-json");
        using TemporarySpellbookDirectory malformedDirectory = new();
        MutableTimeProvider malformedClock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService malformedService = SpellbookTestFactory.CreateService(
            malformedHandler,
            malformedDirectory,
            malformedClock);
        OperationResult<SpellbookEvidence> malformed = await malformedService.SearchVariantsAsync(
            new SpellbookVariantSearchRequest("Sol Ring"),
            TestContext.Current.CancellationToken);

        SpellbookTestHttpHandler oversizedHandler = new();
        oversizedHandler.AddJson($"{{\"padding\":\"{new string('x', SpellbookTransport.MaximumResponseBytes)}\"}}");
        using TemporarySpellbookDirectory oversizedDirectory = new();
        MutableTimeProvider oversizedClock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService oversizedService = SpellbookTestFactory.CreateService(
            oversizedHandler,
            oversizedDirectory,
            oversizedClock);
        OperationResult<SpellbookEvidence> oversized = await oversizedService.SearchVariantsAsync(
            new SpellbookVariantSearchRequest("Sol Ring"),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnsupported>(malformed.Value);
        Assert.IsType<OperationUnavailable>(oversized.Value);
    }

    /// <summary>
    /// Verifies a corrupt local cache returns a safe result without a second source request.
    /// </summary>
    [Fact]
    public async Task CorruptCache_ReturnsUnavailableWithoutCallingSourceAgain()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson("{\"results\":[]}");
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);
        SpellbookVariantSearchRequest request = new("Sol Ring");

        Assert.IsType<OperationSuccess<SpellbookEvidence>>((await service.SearchVariantsAsync(
            request,
            TestContext.Current.CancellationToken).ConfigureAwait(false)).Value);
        using SpellbookDatabase database = new(directory.Path);
        await using (SqliteConnection connection = await database.OpenWriteAsync(TestContext.Current.CancellationToken))
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE response_cache SET response_checksum = 'corrupt';";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        OperationResult<SpellbookEvidence> result = await service.SearchVariantsAsync(
            request,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        OperationUnavailable failure = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("spellbook-cache-unavailable", failure.ReasonCode);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Extracts one successful source evidence result from the native union wrapper.
    /// </summary>
    private static SpellbookEvidence Success(OperationResult<SpellbookEvidence> result)
    {
        return result.Value switch
        {
            OperationSuccess<SpellbookEvidence> success => success.Data,
            _ => throw new Xunit.Sdk.XunitException("Expected a successful Commander Spellbook result."),
        };
    }

    /// <summary>
    /// Reads the stable serialized union case discriminator for an expected failure result.
    /// </summary>
    private static string ResultKind(OperationResult<SpellbookEvidence> result)
    {
        return result.Value switch
        {
            OperationInvalidInput => "invalid-input",
            OperationNotFound => "not-found",
            OperationUnavailable => "unavailable",
            _ => throw new Xunit.Sdk.XunitException("Expected a mapped Commander Spellbook failure."),
        };
    }

    /// <summary>
    /// Reads the safe human-readable message from an expected failure result.
    /// </summary>
    private static string ResultMessage(OperationResult<SpellbookEvidence> result)
    {
        return result.Value switch
        {
            OperationInvalidInput failure => failure.Message,
            OperationNotFound failure => failure.Message,
            OperationUnavailable failure => failure.Message,
            _ => throw new Xunit.Sdk.XunitException("Expected a mapped Commander Spellbook failure."),
        };
    }
}
