using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MtgMcp.E2E.Tests;

internal sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentDictionary<(string Method, string Path), Func<FakeHttpRequest, FakeHttpResponse>> routes = new();
    private readonly List<FakeHttpRequest> requests = [];
    private readonly object requestsLock = new();
    private readonly Task acceptLoop;

    public FakeHttpServer()
    {
        listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        acceptLoop = Task.Run(() => AcceptAsync(cancellation.Token));
    }

    public Uri BaseAddress { get; }

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

    public void GetJson(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        RouteJson(HttpMethod.Get, pathAndQuery, json, statusCode);
    }

    public void PostJson(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        RouteJson(HttpMethod.Post, pathAndQuery, json, statusCode);
    }

    public void PatchJson(string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        RouteJson(HttpMethod.Patch, pathAndQuery, json, statusCode);
    }

    public void RouteJson(HttpMethod method, string pathAndQuery, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        routes[(method.Method, NormalizePath(pathAndQuery))] = _ => FakeHttpResponse.Json(json, statusCode);
    }

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
                await WriteResponseAsync(stream, FakeHttpResponse.Json("""{ "error": "bad request" }""", HttpStatusCode.BadRequest), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
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

            FakeHttpRequest request = new(
                requestParts[0],
                NormalizePath(requestParts[1]),
                headers,
                body);

            lock (requestsLock)
            {
                requests.Add(request);
            }

            FakeHttpResponse response = routes.TryGetValue((request.Method, request.PathAndQuery), out Func<FakeHttpRequest, FakeHttpResponse>? route)
                ? route(request)
                : FakeHttpResponse.Json($$"""{ "error": "No fake route for {{request.Method}} {{request.PathAndQuery}}" }""", HttpStatusCode.NotFound);

            await WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
        }
    }

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
            byte[] bodyBytes = await ReadExactBytesAsync(stream, contentLength, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(bodyBytes);
        }

        return "";
    }

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

    private static async Task<byte[]> ReadExactBytesAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == count ? bytes : bytes[..offset];
    }

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

    private static async Task WriteResponseAsync(
        Stream stream,
        FakeHttpResponse response,
        CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(response.Body);
        string headers = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"HTTP/1.1 {(int)response.StatusCode} {response.StatusCode}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizePath(string pathAndQuery)
    {
        string value = pathAndQuery.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absoluteUri))
        {
            value = absoluteUri.PathAndQuery;
        }

        return value.TrimStart('/');
    }

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

internal sealed record FakeHttpRequest(
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

internal sealed record FakeHttpResponse(
    HttpStatusCode StatusCode,
    string Body,
    string ContentType)
{
    public static FakeHttpResponse Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new FakeHttpResponse(statusCode, body, "application/json");
    }
}
