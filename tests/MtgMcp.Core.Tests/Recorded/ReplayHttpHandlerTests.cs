using System.Net;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Verifies the replay handler and JSON shape helper that adapter tests share.
/// </summary>
public sealed class ReplayHttpHandlerTests
{
    /// <summary>
    /// Builds an entry that names a URL; its body is supplied to the handler directly.
    /// </summary>
    private static RecordedEntry Entry(string url)
    {
        return new RecordedEntry("test", "GET", url, "/test", 200, "none.json", "2026-11-01", "", "none");
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
    /// Verifies shapes bucket arrays as empty, one, and many and compare as subsets.
    /// </summary>
    [Fact]
    public void Missing_RecordedPathsAbsentFromActual_AreListed()
    {
        const string recorded = "{\"a\":[1,2],\"b\":\"x\",\"c\":[]}";

        Assert.Empty(JsonShape.Missing(recorded, "{\"a\":[1,2,3],\"b\":\"y\",\"c\":[],\"d\":1}"));
        Assert.Equal(
            ["$.a:array:many", "$.b:string"],
            JsonShape.Missing(recorded, "{\"a\":[1],\"c\":[]}"));
    }
}
