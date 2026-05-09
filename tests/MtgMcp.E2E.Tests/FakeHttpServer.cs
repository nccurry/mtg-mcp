using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Provides an in-process HTTP server for deterministic Scryfall and Archidekt E2E routes.
/// </summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    /// <summary>
    /// Accepts loopback TCP connections for the fake HTTP endpoint.
    /// </summary>
    private readonly TcpListener listener;

    /// <summary>
    /// Signals the accept loop and request handlers to stop.
    /// </summary>
    private readonly CancellationTokenSource cancellation = new();

    /// <summary>
    /// Maps HTTP method and normalized path pairs to canned responses.
    /// </summary>
    private readonly ConcurrentDictionary<
        (string Method, string Path),
        Func<FakeHttpRequest, FakeHttpResponse>
    > routes = new();

    /// <summary>
    /// Records requests received by the fake server for assertions.
    /// </summary>
    private readonly List<FakeHttpRequest> requests = [];

    /// <summary>
    /// Protects request recording while client handlers run concurrently.
    /// </summary>
    private readonly object requestsLock = new();

    /// <summary>
    /// Tracks the background TCP accept loop so disposal can await shutdown.
    /// </summary>
    private readonly Task acceptLoop;

    /// <summary>
    /// Starts a loopback server on an available port.
    /// </summary>
    public FakeHttpServer()
    {
        listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        acceptLoop = Task.Run(() => AcceptAsync(cancellation.Token));
    }

    /// <summary>
    /// Gets the loopback base address that MCP clients should call during tests.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// Gets a snapshot of requests captured by the fake server.
    /// </summary>
    public IReadOnlyList<FakeHttpRequest> Requests
    {
        get
        {
            lock (requestsLock)
            {
                return requests.ToList();
            }
        }
    }

    /// <summary>
    /// Registers a JSON response for a GET route.
    /// </summary>
    public void GetJson(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        RouteJson(HttpMethod.Get, pathAndQuery, json, statusCode);
    }

    /// <summary>
    /// Registers a JSON response for a POST route.
    /// </summary>
    public void PostJson(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        RouteJson(HttpMethod.Post, pathAndQuery, json, statusCode);
    }

    /// <summary>
    /// Registers a JSON response for a PATCH route.
    /// </summary>
    public void PatchJson(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        RouteJson(HttpMethod.Patch, pathAndQuery, json, statusCode);
    }

    /// <summary>
    /// Registers a JSON response for an arbitrary HTTP method and normalized path.
    /// </summary>
    public void RouteJson(
        HttpMethod method,
        string pathAndQuery,
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        routes[(method.Method, NormalizeRouteKey(pathAndQuery))] = _ => FakeHttpResponse.Json(json, statusCode);
    }

    /// <summary>
    /// Accepts loopback TCP clients until the server is disposed.
    /// </summary>
    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    /// <summary>
    /// Parses one HTTP request, records it, and writes the matching fake response.
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using NetworkStream stream = client.GetStream();
        using (client)
        {
            byte[] headerBytes = await ReadHeaderBytesAsync(stream, cancellationToken).ConfigureAwait(false);
            string headerText = Encoding.ASCII.GetString(headerBytes);
            string[] headerLines = headerText.Split("\r\n", StringSplitOptions.None);
            string? requestLine = headerLines.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            string[] requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2)
            {
                FakeHttpResponse badRequest = FakeHttpResponse.Json(
                    """{ "error": "bad request" }""",
                    HttpStatusCode.BadRequest
                );
                await WriteResponseAsync(stream, badRequest, cancellationToken).ConfigureAwait(false);
                return;
            }

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

            // The fake server reads just enough HTTP to support the .NET clients used by the MCP E2E tests.
            foreach (string headerLine in headerLines.Skip(1))
            {
                if (string.IsNullOrEmpty(headerLine))
                {
                    continue;
                }

                int separator = headerLine.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                {
                    headers[headerLine[..separator].Trim()] = headerLine[(separator + 1)..].Trim();
                }
            }

            string body = await ReadBodyAsync(stream, headers, cancellationToken).ConfigureAwait(false);

            string pathAndQuery = NormalizePath(requestParts[1]);
            FakeHttpRequest request = new(requestParts[0], pathAndQuery, headers, body);

            lock (requestsLock)
            {
                requests.Add(request);
            }

            FakeHttpResponse response;
            if (
                routes.TryGetValue(
                    (request.Method, NormalizeRouteKey(request.PathAndQuery)),
                    out Func<FakeHttpRequest, FakeHttpResponse>? route
                )
            )
            {
                response = route(request);
            }
            else
            {
                response = FakeHttpResponse.Json(
                    $$"""{ "error": "No fake route for {{request.Method}} {{request.PathAndQuery}}" }""",
                    HttpStatusCode.NotFound
                );
            }

            await WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads bytes through the CRLF CRLF sequence that ends HTTP headers.
    /// </summary>
    private static async Task<byte[]> ReadHeaderBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<byte> bytes = [];
        byte[] buffer = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            bytes.Add(buffer[0]);
            if (bytes.Count > 4
                && bytes[^4] == '\r'
                && bytes[^3] == '\n'
                && bytes[^2] == '\r'
                && bytes[^1] == '\n')
            {
                break;
            }
        }

        return bytes.ToArray();
    }

    /// <summary>
    /// Reads either a content-length body, a chunked body, or an empty body.
    /// </summary>
    private static async Task<string> ReadBodyAsync(
        Stream stream,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (headers.TryGetValue("Transfer-Encoding", out string? transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadChunkedBodyAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if (headers.TryGetValue("Content-Length", out string? lengthText)
            && int.TryParse(lengthText, System.Globalization.CultureInfo.InvariantCulture, out int contentLength)
            && contentLength > 0)
        {
            byte[] bodyBytes = await ReadExactBytesAsync(
                stream,
                contentLength,
                cancellationToken
            ).ConfigureAwait(false);
            return Encoding.UTF8.GetString(bodyBytes);
        }

        return "";
    }

    /// <summary>
    /// Reads an HTTP chunked transfer body used by some client requests.
    /// </summary>
    private static async Task<string> ReadChunkedBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using MemoryStream body = new();
        while (true)
        {
            string? chunkHeader = await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (chunkHeader is null)
            {
                break;
            }

            int extensionStart = chunkHeader.IndexOf(';', StringComparison.Ordinal);
            string chunkSizeText = extensionStart >= 0 ? chunkHeader[..extensionStart] : chunkHeader;
            if (!int.TryParse(
                chunkSizeText.Trim(),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out int chunkSize))
            {
                break;
            }

            if (chunkSize == 0)
            {
                // Consume trailer headers after the terminating chunk so the stream can close cleanly.
                while (!string.IsNullOrEmpty(await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false)))
                {
                }

                break;
            }

            byte[] chunk = await ReadExactBytesAsync(stream, chunkSize, cancellationToken).ConfigureAwait(false);
            await body.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            await ReadAsciiLineAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    /// <summary>
    /// Reads up to the requested number of bytes from a stream.
    /// </summary>
    private static async Task<byte[]> ReadExactBytesAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream
                .ReadAsync(bytes.AsMemory(offset, count - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == count ? bytes : bytes[..offset];
    }

    /// <summary>
    /// Reads a CRLF-terminated ASCII line from a stream.
    /// </summary>
    private static async Task<string?> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<byte> bytes = [];
        byte[] buffer = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (buffer[0] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (buffer[0] != '\r')
            {
                bytes.Add(buffer[0]);
            }
        }
    }

    /// <summary>
    /// Writes a minimal HTTP/1.1 JSON response to the client stream.
    /// </summary>
    private static async Task WriteResponseAsync(
        Stream stream,
        FakeHttpResponse response,
        CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(response.Body);
        string statusLine = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"HTTP/1.1 {(int)response.StatusCode} {response.StatusCode}"
        );
        string headers = string.Join(
            "\r\n",
            [
                statusLine,
                $"Content-Type: {response.ContentType}",
                $"Content-Length: {body.Length}",
                "Connection: close",
                "",
                "",
            ]
        );

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Normalizes relative and absolute request targets to route dictionary keys.
    /// </summary>
    private static string NormalizePath(string pathAndQuery)
    {
        string value = pathAndQuery.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absoluteUri))
        {
            value = absoluteUri.PathAndQuery;
        }

        return value.TrimStart('/');
    }

    /// <summary>
    /// Creates a stable route key across platform-specific query encoding differences.
    /// </summary>
    private static string NormalizeRouteKey(string pathAndQuery)
    {
        return WebUtility.UrlDecode(NormalizePath(pathAndQuery));
    }

    /// <summary>
    /// Stops the server, waits for the accept loop, and releases owned resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync().ConfigureAwait(false);
        listener.Stop();

        try
        {
            await acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        cancellation.Dispose();
    }
}

/// <summary>
/// Captures the request data needed by E2E assertions.
/// </summary>
internal sealed record FakeHttpRequest(
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

/// <summary>
/// Describes the fake HTTP response written to an E2E client.
/// </summary>
internal sealed record FakeHttpResponse(
    HttpStatusCode StatusCode,
    string Body,
    string ContentType)
{
    /// <summary>
    /// Creates an application/json response with the supplied status code.
    /// </summary>
    public static FakeHttpResponse Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new FakeHttpResponse(statusCode, body, "application/json");
    }
}
