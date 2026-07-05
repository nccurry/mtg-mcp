using System.Net;
using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Exercises exact printing resolution, format mapping, and sanitized transport failure boundaries.
/// </summary>
public sealed class ArchidektTransportTests
{
    /// <summary>
    /// Verifies exact printing resolution accepts numeric IDs and rejects ambiguous name-only candidates.
    /// </summary>
    [Fact]
    public async Task ResolveCardId_RequiresOneExactPrinting()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"access_token\":\"test-token\"}");
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Island&pageSize=25",
            "{\"results\":[{\"id\":501,\"uid\":\"33333333-3333-3333-3333-333333333333\",\"setCode\":\"dmu\",\"collectorNumber\":\"278\",\"oracleCard\":{\"name\":\"Island\"}}]}");
        using ArchidektTransport transport = CreateTransport(handler);
        RemoteDeckEntry exact = new(
            "",
            "",
            1,
            "Island",
            OracleId: null,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "DMU",
            "278",
            "en",
            "nonfoil",
            "main",
            [],
            PrimaryCategoryName: null,
            SortOrder: 0);

        string cardId = await transport.ResolveCardIdAsync(
            exact,
            new ArchidektOperationBudget(5),
            TestContext.Current.CancellationToken);

        Assert.Equal("501", cardId);

        ArchidektTestHttpHandler ambiguousHandler = new();
        ambiguousHandler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"jwt\":\"test-token\"}");
        ambiguousHandler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Island&pageSize=25",
            "[{\"id\":1,\"oracleCard\":{\"name\":\"Island\"}},{\"id\":2,\"oracleCard\":{\"name\":\"Island\"}}]");
        using ArchidektTransport ambiguousTransport = CreateTransport(ambiguousHandler);
        RemoteDeckEntry ambiguous = exact with { PrintingId = null, SetCode = null, CollectorNumber = null };

        ArchidektProviderException exception = await Assert.ThrowsAsync<ArchidektProviderException>(
            () => ambiguousTransport.ResolveCardIdAsync(
                ambiguous,
                new ArchidektOperationBudget(5),
                TestContext.Current.CancellationToken));
        Assert.Equal("printing-resolution-unavailable", exception.ReasonCode);
    }

    /// <summary>
    /// Verifies every supported format alias and provider-ID representation.
    /// </summary>
    [Theory]
    [InlineData("standard", 1)]
    [InlineData("modern", 2)]
    [InlineData("commander", 3)]
    [InlineData("edh", 3)]
    [InlineData("legacy", 4)]
    [InlineData("vintage", 5)]
    [InlineData("pauper", 6)]
    [InlineData("pioneer", 7)]
    [InlineData("brawl", 8)]
    [InlineData("historic", 9)]
    [InlineData("oathbreaker", 10)]
    public void MappingHelpers_PreserveSupportedProviderVocabulary(string format, int expectedId)
    {
        Assert.Equal(expectedId, ArchidektTransport.MapFormatId(format));
        Assert.Equal(42L, ArchidektTransport.ParseProviderId("42"));
        Assert.Equal("opaque", ArchidektTransport.ParseProviderId("opaque"));
        Assert.Null(ArchidektTransport.ParseProviderId(" "));
    }

    /// <summary>
    /// Verifies unsupported formats fail before provider I/O.
    /// </summary>
    [Fact]
    public void MappingHelpers_RejectUnsupportedFormats()
    {
        ArchidektProviderException exception = Assert.Throws<ArchidektProviderException>(
            () => ArchidektTransport.MapFormatId("custom"));
        Assert.Equal("unsupported-deck-format", exception.ReasonCode);
    }

    /// <summary>
    /// Verifies status classes, missing credentials, and malformed login remain structured.
    /// </summary>
    [Theory]
    [InlineData(404, "provider-entity-not-found")]
    [InlineData(400, "provider-request-rejected")]
    [InlineData(418, "provider-unavailable")]
    public async Task ListDecks_MapsProviderFailuresWithoutBodies(int status, string expectedReason)
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"key\":\"test-token\"}");
        handler.Add(
            HttpMethod.Get,
            "api/decks/v3/?ownerUsername=user",
            "{\"secret\":\"hidden\"}",
            (HttpStatusCode)status);
        using ArchidektService service = CreateService(handler);

        OperationResult<RemoteDeckPage> result = await service.ListDecksAsync(
            null,
            10,
            TestContext.Current.CancellationToken);

        (string reasonCode, string message) = result.Value switch
        {
            OperationNotFound value => (value.ReasonCode, value.Message),
            OperationUnavailable value => (value.ReasonCode, value.Message),
            _ => throw new Xunit.Sdk.XunitException("Expected a structured provider failure."),
        };
        Assert.Equal(expectedReason, reasonCode);
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies absent credentials and a login response without a token fail closed.
    /// </summary>
    [Fact]
    public async Task AuthenticationFailures_DoNotStartAuthenticatedProviderReads()
    {
        ArchidektTestHttpHandler noCredentialsHandler = new();
        using ArchidektService noCredentials = CreateService(
            noCredentialsHandler,
            username: null,
            password: null);
        OperationResult<RemoteDeckPage> missing = await noCredentials.ListDecksAsync(
            null,
            50,
            TestContext.Current.CancellationToken);
        Assert.Equal("credentials-unavailable", Assert.IsType<OperationUnavailable>(missing.Value).ReasonCode);
        Assert.Empty(noCredentialsHandler.Requests);

        ArchidektTestHttpHandler malformedHandler = new();
        malformedHandler.Add(HttpMethod.Post, "api/rest-auth/login/", "{}");
        using ArchidektService malformed = CreateService(malformedHandler);
        OperationResult<RemoteDeckPage> invalidToken = await malformed.ListDecksAsync(
            null,
            50,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "provider-contract-unsupported",
            Assert.IsType<OperationUnsupported>(invalidToken.Value).ReasonCode);
        Assert.Single(malformedHandler.Requests);
    }

    /// <summary>
    /// Verifies the production constructor validates and owns its HTTP client without performing network I/O.
    /// </summary>
    [Fact]
    public void ProductionServiceConstructor_ValidatesOptions()
    {
        using ArchidektService service = new(ArchidektOptions.CreateDefault(), "0.9.0-preview.1");
        Assert.IsType<OperationSuccess<ArchidektAuthStatus>>(service.GetAuthStatus().Value);
    }

    /// <summary>
    /// Creates an injected transport with zero-delay safety bounds.
    /// </summary>
    private static ArchidektTransport CreateTransport(
        ArchidektTestHttpHandler handler,
        string? username = "user",
        string? password = "secret")
    {
        ArchidektOptions options = ArchidektOptions.CreateDefault(username, password) with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
        return new ArchidektTransport(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            ownsHttpClient: true,
            options);
    }

    /// <summary>
    /// Creates a provider service over an injected transport.
    /// </summary>
    private static ArchidektService CreateService(
        ArchidektTestHttpHandler handler,
        string? username = "user",
        string? password = "secret")
    {
        ArchidektTransport transport = CreateTransport(handler, username, password);
        return new ArchidektService(transport, 150);
    }
}
