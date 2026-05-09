using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway : IArchidektGateway, IDisposable
{
    /// <summary>
    /// Handles serializer options.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>
    /// Stores the http client.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores the options.
    /// </summary>
    private readonly ArchidektOptions options;

    /// <summary>
    /// Handles auth lock.
    /// </summary>
    private readonly SemaphoreSlim authLock = new(1, 1);

    /// <summary>
    /// Stores the credentials.
    /// </summary>
    private ArchidektCredentials? credentials;

    /// <summary>
    /// Handles archidekt gateway.
    /// </summary>
    public ArchidektGateway(HttpClient httpClient, IOptions<ArchidektOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= this.options.BaseAddress;
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    /// <summary>
    /// Releases resources held by the instance.
    /// </summary>
    public void Dispose()
    {
        authLock.Dispose();
    }
}
