using System.Net;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Verifies how Archidekt rate limits and a failed or lagging final read show up in reads and in a push: a read waits
/// and retries, a write is never retried and says it was not sent, and the check after the writes keeps the statuses
/// of the writes that were sent. Every wait goes through a recorded delay, so no test sleeps.
/// </summary>
public sealed class ArchidektRateLimitAndVerificationTests
{
    /// <summary>
    /// Holds the deck route every test reads.
    /// </summary>
    private const string DeckRoute = "api/decks/42/";

    /// <summary>
    /// Verifies a read that gets a 429 waits the time Retry-After names and then succeeds.
    /// </summary>
    [Fact]
    public async Task Read_WithARateLimitThenSuccess_WaitsAndReturnsTheDeck()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, DeckRoute, "{}", HttpStatusCode.TooManyRequests, retryAfter: "3");
        handler.Add(HttpMethod.Get, DeckRoute, ArchidektTestPayloads.Deck);
        List<TimeSpan> waits = [];
        using ArchidektService service = new(CreateSession(handler, waits), 150);

        OperationResult<RemoteDeckSnapshot> result = await service.GetDeckAsync(
            "42",
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<RemoteDeckSnapshot>>(result.Value);
        Assert.Equal([TimeSpan.FromSeconds(3)], waits);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies a read that keeps getting 429 caps each wait at ten seconds, retries twice, and then fails with the
    /// rate-limit reason.
    /// </summary>
    [Fact]
    public async Task Read_WithEveryAnswerRateLimited_RetriesTwiceWithCappedWaitsAndFails()
    {
        ArchidektTestHttpHandler handler = new();
        for (int index = 0; index < 3; index++)
        {
            handler.Add(HttpMethod.Get, DeckRoute, "{}", HttpStatusCode.TooManyRequests, retryAfter: "30");
        }

        List<TimeSpan> waits = [];
        using ArchidektService service = new(CreateSession(handler, waits), 150);

        OperationResult<RemoteDeckSnapshot> result = await service.GetDeckAsync(
            "42",
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("provider-rate-limited", unavailable.ReasonCode);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)], waits);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies a 429 without a usable Retry-After waits the default two seconds.
    /// </summary>
    [Fact]
    public async Task Read_WithARateLimitAndNoRetryAfter_WaitsTheDefault()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, DeckRoute, "{}", HttpStatusCode.TooManyRequests);
        handler.Add(HttpMethod.Get, DeckRoute, ArchidektTestPayloads.Deck);
        List<TimeSpan> waits = [];
        using ArchidektService service = new(CreateSession(handler, waits), 150);

        _ = await service.GetDeckAsync("42", TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    /// <summary>
    /// Verifies a write that gets a 429 is reported as not sent, is not retried, and leaves the rest not attempted;
    /// after an earlier write was applied the push is partial.
    /// </summary>
    [Theory]
    [InlineData(false, "not-applied", "not-sent", "not-attempted")]
    [InlineData(true, "partial", "applied", "not-sent")]
    public async Task Write_WithARateLimit_IsNotSentAndNotRetried(
        bool metadataWriteFirst,
        string outcome,
        string firstStatus,
        string secondStatus)
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"t\"}");
        if (metadataWriteFirst)
        {
            handler.Add(HttpMethod.Patch, DeckRoute, "{}");
            handler.Add(HttpMethod.Post, "api/decks/createCategory/", "{}", HttpStatusCode.TooManyRequests, "1");
        }
        else
        {
            handler.Add(HttpMethod.Patch, DeckRoute, "{}", HttpStatusCode.TooManyRequests, "1");
        }

        List<TimeSpan> waits = [];
        ArchidektApplyResult result = await ApplyAsync(handler, waits, renamedAndExtended: true);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(["metadata-update", "category-create"], result.Operations.Select(value => value.Kind));
        Assert.Equal(firstStatus, result.Operations[0].Status);
        Assert.Equal(secondStatus, result.Operations[1].Status);
        Assert.Contains(
            "Nothing was sent to Archidekt for this operation.",
            result.Operations[metadataWriteFirst ? 1 : 0].Message,
            StringComparison.Ordinal);
        Assert.Empty(waits);
        Assert.Equal(
            metadataWriteFirst ? 2 : 1,
            handler.Requests.Count(value => value.Method != HttpMethod.Post || value.Path != "api/rest-auth/login/"));
    }

    /// <summary>
    /// Verifies a final read that keeps failing after the writes keeps every applied status and reports the push as
    /// applied but not verified.
    /// </summary>
    [Fact]
    public async Task FinalRead_ThatFailsAfterTheWrites_KeepsTheStatusesAndIsAppliedUnverified()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"t\"}");
        handler.Add(HttpMethod.Patch, DeckRoute, "{}");
        for (int index = 0; index < 3; index++)
        {
            handler.Add(HttpMethod.Get, DeckRoute, "{}", HttpStatusCode.TooManyRequests, "1");
        }

        List<TimeSpan> waits = [];
        ArchidektApplyResult result = await ApplyAsync(handler, waits, renamedAndExtended: false);

        Assert.Equal("applied-unverified", result.Outcome);
        Assert.Equal("applied", Assert.Single(result.Operations).Status);
        Assert.Null(result.FinalRemoteFingerprint);
        Assert.Equal(ArchidektApplyResult.UnverifiedNotice, result.Notice);
    }

    /// <summary>
    /// Verifies a push plan counts every read the apply makes, with room to retry each, so a push that could not finish
    /// its reads is blocked when previewed and the same plan made for the apply needs one read less.
    /// </summary>
    [Theory]
    [InlineData(15, true, true)]
    [InlineData(16, true, false)]
    [InlineData(12, false, true)]
    [InlineData(13, false, false)]
    public async Task PlanPush_CountsTheReadsTheApplyMakesAndTheirRetries(int maximum, bool preview, bool blocked)
    {
        ArchidektTestHttpHandler handler = new();
        using ArchidektSession session = CreateSession(handler, []);
        ArchidektDeckOperations operations = new(new ArchidektDeckTransport(session), 150);
        RemoteDeckSnapshot current = Parse(ArchidektTestPayloads.Deck);
        RemoteDeckSnapshot target = Parse(RenamedDeckJson);

        OperationResult<ArchidektPushPlan> result = await operations.PlanPushAsync(
            current,
            target,
            preview ? null : new ArchidektPrintingChoices(),
            new ArchidektOperationScope(maximum),
            TestContext.Current.CancellationToken);

        ArchidektPushPlan plan = Assert.IsType<OperationSuccess<ArchidektPushPlan>>(result.Value).Data;
        Assert.Equal(blocked, plan.BlockedNotice is not null);
    }

    /// <summary>
    /// Verifies a verification read that finds the request limit used up after the writes keeps every applied status
    /// and reports the push as applied but not verified, instead of failing and losing the statuses.
    /// </summary>
    [Fact]
    public async Task VerificationRead_ThatHitsTheRequestLimitAfterTheWrites_KeepsTheStatuses()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"t\"}");
        handler.Add(HttpMethod.Patch, DeckRoute, "{}");

        ArchidektApplyResult result = await ApplyAsync(handler, [], renamedAndExtended: false, maximumRequests: 2);

        Assert.Equal("applied-unverified", result.Outcome);
        Assert.Equal("applied", Assert.Single(result.Operations).Status);
        Assert.Null(result.FinalRemoteFingerprint);
        Assert.Equal(ArchidektApplyResult.UnverifiedNotice, result.Notice);
        Assert.DoesNotContain(handler.Requests, value => value.Method == HttpMethod.Get);
    }

    /// <summary>
    /// Verifies a read that lags behind the writes is read once more after a pause, and the push counts as applied
    /// when the second read matches.
    /// </summary>
    [Fact]
    public async Task FinalRead_ThatLagsThenSettles_IsApplied()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"t\"}");
        handler.Add(HttpMethod.Patch, DeckRoute, "{}");
        handler.Add(HttpMethod.Get, DeckRoute, ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, DeckRoute, RenamedDeckJson);
        List<TimeSpan> waits = [];

        ArchidektApplyResult result = await ApplyAsync(handler, waits, renamedAndExtended: false);

        Assert.Equal("applied", result.Outcome);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
        Assert.Null(result.Differences);
    }

    /// <summary>
    /// Verifies a difference that is still there on the second read is reported with the rows that differ.
    /// </summary>
    [Fact]
    public async Task FinalRead_WithARealDifference_ListsTheRowsThatDiffer()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"t\"}");
        handler.Add(HttpMethod.Patch, DeckRoute, "{}");
        handler.Add(HttpMethod.Get, DeckRoute, ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, DeckRoute, ArchidektTestPayloads.Deck);
        List<TimeSpan> waits = [];

        ArchidektApplyResult result = await ApplyAsync(handler, waits, renamedAndExtended: false);

        Assert.Equal("verification-mismatch", result.Outcome);
        Assert.Equal("applied", Assert.Single(result.Operations).Status);
        ArchidektVerificationDifference difference = Assert.Single(result.Differences!);
        Assert.Equal("metadata-update", difference.Kind);
        Assert.Contains("1 rows", result.Notice, StringComparison.Ordinal);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
        Assert.Equal(2, handler.Requests.Count(value => value.Method == HttpMethod.Get));
    }

    /// <summary>
    /// Gets the deck payload after a rename.
    /// </summary>
    private static string RenamedDeckJson { get; } = ArchidektTestPayloads.Deck.Replace(
        "Rate Safe Weenies",
        "Renamed Deck",
        StringComparison.Ordinal);

    /// <summary>
    /// Applies a rename (and, when asked, a new category) to the canonical deck the way the apply tool does.
    /// </summary>
    private static async Task<ArchidektApplyResult> ApplyAsync(
        ArchidektTestHttpHandler handler,
        List<TimeSpan> waits,
        bool renamedAndExtended,
        int maximumRequests = 150)
    {
        using ArchidektSession session = CreateSession(handler, waits);
        ArchidektDeckOperations operations = new(new ArchidektDeckTransport(session), 150);
        RemoteDeckSnapshot current = Parse(ArchidektTestPayloads.Deck);
        RemoteDeckSnapshot target = Parse(RenamedDeckJson);
        if (renamedAndExtended)
        {
            target = target with
            {
                Categories =
                [
                    .. target.Categories,
                    new RemoteDeckCategory(string.Empty, "Extra", IncludedInDeck: true, IncludedInPrice: true, IsPremier: false, SortOrder: 5),
                ],
            };
        }

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
        return await operations.ApplyPlanAsync(
            current,
            target,
            plan,
            new ArchidektOperationBudget(maximumRequests),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Parses one deck payload.
    /// </summary>
    private static RemoteDeckSnapshot Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ArchidektDeckContractMapper.MapDeck(document.RootElement, json, DateTimeOffset.UtcNow, "GET");
    }

    /// <summary>
    /// Creates a session whose waits are recorded and skipped.
    /// </summary>
    private static ArchidektSession CreateSession(ArchidektTestHttpHandler handler, List<TimeSpan> waits)
    {
        ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
        return new ArchidektSession(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            ownsHttpClient: true,
            options,
            delay: (duration, _) =>
            {
                waits.Add(duration);
                return Task.CompletedTask;
            });
    }
}
