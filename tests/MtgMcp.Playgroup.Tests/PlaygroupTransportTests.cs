using System.Net;
using MtgMcp.Core.Results;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Proves bearer authentication, terminal failure mapping, bounded read retry, and single-attempt writes.
/// </summary>
public sealed class PlaygroupTransportTests
{
    /// <summary>Verifies required authentication fails locally while public reads remain available.</summary>
    [Fact]
    public async Task AuthenticationBoundary_FailsBeforeRequiredHttpButAllowsPublicRead()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddJson("{\"id\":1}");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler, apiKey: null);

        OperationResult<PlaygroupEvidence> required = await service.GetCurrentUserAsync(
            TestContext.Current.CancellationToken);
        OperationResult<PlaygroupEvidence> publicRead = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal("playgroup-auth-required", Assert.IsType<OperationUnavailable>(required.Value).ReasonCode);
        Assert.IsType<OperationSuccess<PlaygroupEvidence>>(publicRead.Value);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Null(request.AuthScheme);
        Assert.Null(request.AuthParameter);
    }

    /// <summary>Verifies every expected terminal status maps to a sanitized shared result.</summary>
    [Theory]
    [InlineData(400, "invalid-input", "provider-request-rejected")]
    [InlineData(401, "unavailable", "provider-unauthorized")]
    [InlineData(403, "unavailable", "provider-forbidden")]
    [InlineData(404, "not-found", "provider-entity-not-found")]
    [InlineData(418, "unavailable", "provider-unavailable")]
    public async Task ReadStatuses_MapWithoutProviderBodies(int status, string expectedKind, string expectedReason)
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddStatus((HttpStatusCode)status);
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        (string kind, string reason, string message) = result.Value switch
        {
            OperationInvalidInput value => (value.Kind, value.ReasonCode, value.Message),
            OperationNotFound value => (value.Kind, value.ReasonCode, value.Message),
            OperationUnavailable value => (value.Kind, value.ReasonCode, value.Message),
            _ => throw new Xunit.Sdk.XunitException("Expected a structured provider failure."),
        };
        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedReason, reason);
        Assert.DoesNotContain("private", message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    /// <summary>Verifies idempotent reads retry at most twice for transient response failures.</summary>
    [Theory]
    [InlineData(500)]
    [InlineData(408)]
    public async Task ReadRetries_AreBoundedForTransientStatuses(int status)
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddStatus((HttpStatusCode)status);
        handler.AddStatus((HttpStatusCode)status);
        handler.AddJson("{\"id\":1}");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<PlaygroupEvidence>>(result.Value);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>Verifies transient transport failures receive the same bounded GET retry policy.</summary>
    [Fact]
    public async Task ReadRetries_AreBoundedForTransportFailures()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddFailure(new HttpRequestException("private failure"));
        handler.AddFailure(new IOException("private path"));
        handler.AddFailure(new TimeoutException("private timeout"));
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("provider-unavailable", unavailable.ReasonCode);
        Assert.DoesNotContain("private", unavailable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>Verifies a bounded Retry-After permits one GET replay and never a second.</summary>
    [Fact]
    public async Task RateLimit_RetriesOneGetOnlyWhenRetryAfterIsBounded()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddStatus(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(1));
        handler.AddJson("{\"id\":1}");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<PlaygroupEvidence>>(result.Value);
        Assert.Equal(2, handler.Requests.Count);

        PlaygroupTestHttpHandler noHeader = new();
        noHeader.AddStatus(HttpStatusCode.TooManyRequests);
        using PlaygroupService noHeaderService = PlaygroupTestFactory.CreateService(noHeader);
        OperationResult<PlaygroupEvidence> unavailable = await noHeaderService.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "provider-rate-limited",
            Assert.IsType<OperationUnavailable>(unavailable.Value).ReasonCode);
        Assert.Single(noHeader.Requests);
    }

    /// <summary>Verifies writes do not retry after status or ambiguous transport failure.</summary>
    [Fact]
    public async Task Writes_NeverRetryAndReportUnknownAcceptance()
    {
        PlaygroupTestHttpHandler statusHandler = new();
        statusHandler.AddStatus(HttpStatusCode.InternalServerError);
        statusHandler.AddJson();
        using PlaygroupService statusService = PlaygroupTestFactory.CreateService(statusHandler);
        OperationResult<PlaygroupEvidence> statusResult = await statusService.CreateLiveSessionAsync(
            new PlaygroupLiveSessionCreateRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "provider-write-acceptance-unknown",
            Assert.IsType<OperationUnavailable>(statusResult.Value).ReasonCode);
        Assert.Single(statusHandler.Requests);

        PlaygroupTestHttpHandler failureHandler = new();
        failureHandler.AddFailure(new HttpRequestException("secret"));
        failureHandler.AddJson();
        using PlaygroupService failureService = PlaygroupTestFactory.CreateService(failureHandler);
        OperationResult<PlaygroupEvidence> failureResult = await failureService.CreateLiveSessionAsync(
            new PlaygroupLiveSessionCreateRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "provider-write-acceptance-unknown",
            Assert.IsType<OperationUnavailable>(failureResult.Value).ReasonCode);
        Assert.Single(failureHandler.Requests);
    }

    /// <summary>Verifies terminal throttling and validation responses never replay a write.</summary>
    [Theory]
    [InlineData(429, "provider-rate-limited")]
    [InlineData(422, "provider-request-rejected")]
    public async Task Writes_TerminalResponsesNeverRetry(int status, string expectedReason)
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddStatus((HttpStatusCode)status, status == 429 ? TimeSpan.FromSeconds(1) : null);
        handler.AddJson();
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.CreateLiveSessionAsync(
            new PlaygroupLiveSessionCreateRequest(),
            TestContext.Current.CancellationToken);

        string reason = result.Value switch
        {
            OperationInvalidInput value => value.ReasonCode,
            OperationUnavailable value => value.ReasonCode,
            _ => throw new Xunit.Sdk.XunitException("Expected a terminal write failure."),
        };
        Assert.Equal(expectedReason, reason);
        Assert.Single(handler.Requests);
    }

    /// <summary>Verifies malformed success JSON reports contract drift rather than guessed data.</summary>
    [Fact]
    public async Task MalformedSuccess_ReturnsContractUnsupported()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddJson("not-json");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "provider-contract-unsupported",
            Assert.IsType<OperationUnsupported>(result.Value).ReasonCode);
    }

    /// <summary>Verifies provider arrays cannot create an unbounded MCP response.</summary>
    [Fact]
    public async Task OversizedSuccess_ReturnsUnavailableWithoutParsingPayload()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddJson(new string('x', PlaygroupTransport.MaximumTurnDamageResponseBytes + 1));
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderTurnDamageAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "provider-response-too-large",
            Assert.IsType<OperationUnavailable>(result.Value).ReasonCode);
        Assert.Single(handler.Requests);
    }

    /// <summary>Verifies body-stream transport failures receive bounded read retry and sanitized terminal handling.</summary>
    [Fact]
    public async Task ResponseBodyFailures_UseTransportRetryBoundary()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingReadStream()),
        });
        handler.AddJson("{\"id\":1}");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<PlaygroupEvidence>>(result.Value);
        Assert.Equal(2, handler.Requests.Count);
    }
}
