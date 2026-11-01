using System.Net;
using System.Text;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Serves recorded provider responses by method, path, and sorted decoded query, ignoring the host, and keeps every
/// request it saw with its body. An unmatched request throws and names the request and the known routes.
/// </summary>
internal sealed class ReplayHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// Guards the route map and the request list, which a service may touch from several tasks.
    /// </summary>
    private readonly object gate = new();

    /// <summary>
    /// Maps a request key to the response served for it.
    /// </summary>
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> routes = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds the requests received so far, in order.
    /// </summary>
    private readonly List<(HttpMethod Method, Uri Uri, string Body)> requests = [];

    /// <summary>
    /// Gets a snapshot of the requests received so far, in order; the body is empty for a request without content.
    /// </summary>
    internal IReadOnlyList<(HttpMethod Method, Uri Uri, string Body)> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

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
        lock (gate)
        {
            routes[Key(new HttpMethod(entry.Method), new Uri(entry.Url))] = (status, body);
        }

        return this;
    }

    /// <summary>
    /// Replaces the response of an already added route with a synthetic one, such as a 429.
    /// </summary>
    internal ReplayHttpHandler Override(RecordedEntry entry, HttpStatusCode status, string body = "{}")
    {
        string key = Key(new HttpMethod(entry.Method), new Uri(entry.Url));
        lock (gate)
        {
            if (!routes.ContainsKey(key))
            {
                throw new InvalidOperationException($"Cannot override unknown route {key}.");
            }

            routes[key] = (status, body);
        }

        return this;
    }

    /// <summary>
    /// Builds the match key from the method, path, and query pairs. Each pair is split on its first equals sign and
    /// its name and value are unescaped separately, with a plus read as a space, so an encoded ampersand or equals
    /// sign inside a value stays part of that value.
    /// </summary>
    internal static string Key(HttpMethod method, Uri uri)
    {
        List<(string Name, string Value)> pairs = [];
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string name = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            pairs.Add((Unescape(name), Unescape(value)));
        }

        pairs.Sort(static (left, right) =>
        {
            int byName = string.CompareOrdinal(left.Name, right.Name);
            return byName != 0 ? byName : string.CompareOrdinal(left.Value, right.Value);
        });
        StringBuilder query = new();
        foreach ((string name, string value) in pairs)
        {
            query.Append(query.Length == 0 ? "" : "&").Append(name).Append('=').Append(value);
        }

        return $"{method.Method} {uri.AbsolutePath}?{query}";
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string key = Key(request.Method, request.RequestUri!);
        (HttpStatusCode Status, string Body) route;
        lock (gate)
        {
            requests.Add((request.Method, request.RequestUri!, body));
            if (!routes.TryGetValue(key, out route))
            {
                throw new InvalidOperationException(
                    $"No recording matches {key}. Known routes:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, routes.Keys.Order(StringComparer.Ordinal)));
            }
        }

        return new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Decodes one query name or value, reading a plus as a space.
    /// </summary>
    private static string Unescape(string text)
    {
        return Uri.UnescapeDataString(text.Replace('+', ' '));
    }
}
