using System.Net;
using System.Text;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Serves recorded provider responses by method, path, and sorted decoded query, ignoring the host, and keeps every
/// request it saw. An unmatched request throws and names the request and the known routes.
/// </summary>
internal sealed class ReplayHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// Maps a request key to the response served for it.
    /// </summary>
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> routes = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds the requests received so far, in order.
    /// </summary>
    private readonly List<(HttpMethod Method, Uri Uri)> requests = [];

    /// <summary>
    /// Gets the requests received so far, in order.
    /// </summary>
    internal IReadOnlyList<(HttpMethod Method, Uri Uri)> Requests => requests;

    /// <summary>
    /// Serves a recorded entry at its recorded method and URL.
    /// </summary>
    internal ReplayHttpHandler Add(RecordedEntry entry)
    {
        return Add(entry, (HttpStatusCode)entry.Status, entry.ReadText());
    }

    /// <summary>
    /// Serves a response of the caller's choosing at an entry's method and URL.
    /// </summary>
    internal ReplayHttpHandler Add(RecordedEntry entry, HttpStatusCode status, string body)
    {
        routes[Key(new HttpMethod(entry.Method), new Uri(entry.Url))] = (status, body);
        return this;
    }

    /// <summary>
    /// Replaces the response of an already added route with a synthetic one, such as a 429.
    /// </summary>
    internal ReplayHttpHandler Override(RecordedEntry entry, HttpStatusCode status, string body = "{}")
    {
        string key = Key(new HttpMethod(entry.Method), new Uri(entry.Url));
        if (!routes.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot override unknown route {key}.");
        }

        routes[key] = (status, body);
        return this;
    }

    /// <summary>
    /// Builds the match key from the method, path, and sorted decoded query pairs.
    /// </summary>
    internal static string Key(HttpMethod method, Uri uri)
    {
        List<string> pairs = [];
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            pairs.Add(Uri.UnescapeDataString(pair.Replace('+', ' ')));
        }

        pairs.Sort(StringComparer.Ordinal);
        return $"{method.Method} {uri.AbsolutePath}?{string.Join('&', pairs)}";
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requests.Add((request.Method, request.RequestUri!));
        string key = Key(request.Method, request.RequestUri!);
        if (!routes.TryGetValue(key, out (HttpStatusCode Status, string Body) route))
        {
            throw new InvalidOperationException(
                $"No recording matches {key}. Known routes:{Environment.NewLine}" +
                string.Join(Environment.NewLine, routes.Keys.Order(StringComparer.Ordinal)));
        }

        return Task.FromResult(new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body, Encoding.UTF8, "application/json"),
        });
    }
}
