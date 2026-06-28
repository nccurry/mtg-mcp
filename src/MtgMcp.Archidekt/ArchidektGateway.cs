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
    /// Shares Archidekt JSON casing rules across request bodies and response mapping.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>
    /// Sends all Archidekt API requests for this gateway instance.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Holds the configured Archidekt endpoint and authentication settings.
    /// </summary>
    private readonly ArchidektOptions options;

    /// <summary>
    /// Serializes login attempts so concurrent requests share one token update.
    /// </summary>
    private readonly SemaphoreSlim authLock = new(1, 1);

    /// <summary>
    /// Serializes access to the persistent Archidekt card-id cache.
    /// </summary>
    private readonly SemaphoreSlim cardIdCacheLock = new(1, 1);

    /// <summary>
    /// Caches credentials loaded from configuration or the credentials file.
    /// </summary>
    private ArchidektCredentials? credentials;

    /// <summary>
    /// Caches the access token returned by Archidekt login for this process.
    /// </summary>
    private string? sessionJwt;

    /// <summary>
    /// Caches the decoded JWT expiration when Archidekt returns a compact JWT.
    /// </summary>
    private DateTimeOffset? sessionJwtExpiresAt;

    /// <summary>
    /// Caches the logged-in Archidekt user id when the login response includes it.
    /// </summary>
    private string? sessionUserId;

    /// <summary>
    /// Keeps a sanitized credentials-file parse error for auth status reporting.
    /// </summary>
    private string? credentialsFileError;

    /// <summary>
    /// Caches Archidekt mutation card ids by provider-neutral print keys.
    /// </summary>
    private Dictionary<string, ArchidektCardIdCacheEntry>? cardIdCache;

    /// <summary>
    /// Tracks whether legacy cache values should be rewritten in the structured cache format.
    /// </summary>
    private bool cardIdCacheNeedsSave;

    /// <summary>
    /// Creates a gateway that sends JSON requests to Archidekt.
    /// </summary>
    public ArchidektGateway(HttpClient httpClient, IOptions<ArchidektOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= this.options.BaseAddress;
        MtgMcpHttpDefaults.ApplyUserAgent(this.httpClient, this.options.UserAgent);
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
        cardIdCacheLock.Dispose();
    }
}
