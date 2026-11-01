using System.Net;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Verifies the replay handler that adapter tests share.
/// </summary>
public sealed class ReplayHttpHandlerTests
{
    /// <summary>
    /// Builds an entry that names a URL; its body is supplied to the handler directly.
    /// </summary>
    private static RecordedEntry Entry(string url)
    {
        return new RecordedEntry("test", "GET", url, "/test", 200, "none.json", "");
    }

    /// <summary>
    /// Sends one GET through the handler.
    /// </summary>
    private static async Task<HttpResponseMessage> GetAsync(ReplayHttpHandler handler, string url)
    {
        using HttpClient client = new(handler, disposeHandler: false);
        return await client.GetAsync(new Uri(url), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies a request matching a recording by path and query gets the recorded status and body for any host.
    /// </summary>
    [Fact]
    public async Task Send_MatchingRequest_ReturnsRecordedResponseForAnyHost()
    {
        using ReplayHttpHandler handler = new();
        handler.Add(Entry("https://api.example.test/a?q=1"), HttpStatusCode.Accepted, "{\"ok\":true}");

        using HttpResponseMessage response = await GetAsync(handler, "https://other.test/a?q=1");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"ok\":true}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies an unmatched request throws a message naming the request and every known route.
    /// </summary>
    [Fact]
    public async Task Send_UnmatchedRequest_ThrowsListingKnownRoutes()
    {
        using ReplayHttpHandler handler = new();
        handler.Add(Entry("https://api.example.test/a?q=1"), HttpStatusCode.OK, "{}");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GetAsync(handler, "https://api.example.test/b?q=2"));

        Assert.Contains("GET /b?q=2", error.Message, StringComparison.Ordinal);
        Assert.Contains("GET /a?q=1", error.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies query parameter order and encoding do not change the match.
    /// </summary>
    [Fact]
    public async Task Send_ReorderedEncodedQuery_StillMatches()
    {
        using ReplayHttpHandler handler = new();
        handler.Add(Entry("https://api.example.test/a?q=t%3Aangel&page=2"), HttpStatusCode.OK, "{}");

        using HttpResponseMessage response = await GetAsync(handler, "https://api.example.test/a?page=2&q=t:angel");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Verifies a per-route override replaces the response and an unknown route is rejected.
    /// </summary>
    [Fact]
    public async Task Override_KnownRoute_ReturnsSyntheticStatus()
    {
        using ReplayHttpHandler handler = new();
        RecordedEntry entry = Entry("https://api.example.test/a");
        handler.Add(entry, HttpStatusCode.OK, "{}").Override(entry, HttpStatusCode.TooManyRequests);

        using HttpResponseMessage response = await GetAsync(handler, "https://api.example.test/a");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Throws<InvalidOperationException>(
            () => handler.Override(Entry("https://api.example.test/zzz"), HttpStatusCode.OK));
    }

    /// <summary>
    /// Verifies an encoded ampersand inside a value is not the same query as two separate pairs.
    /// </summary>
    [Fact]
    public void Key_EncodedAmpersandInValue_DiffersFromSeparatePairs()
    {
        Assert.NotEqual(
            ReplayHttpHandler.Key(HttpMethod.Get, new Uri("https://api.example.test/a?q=a%26b=c")),
            ReplayHttpHandler.Key(HttpMethod.Get, new Uri("https://api.example.test/a?q=a&b=c")));
    }

    /// <summary>
    /// Verifies the handler keeps the body of a POST for the test to assert on.
    /// </summary>
    [Fact]
    public async Task Send_PostWithBody_RecordsBody()
    {
        using ReplayHttpHandler handler = new();
        handler.Add(new RecordedEntry("test", "POST", "https://api.example.test/a", "/a", 200, "none.json", ""), HttpStatusCode.OK, "{}");
        using HttpClient client = new(handler, disposeHandler: false);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("https://api.example.test/a"),
            new StringContent("{\"x\":1}"),
            TestContext.Current.CancellationToken);

        Assert.Equal("{\"x\":1}", Assert.Single(handler.Requests).Body);
    }
}
