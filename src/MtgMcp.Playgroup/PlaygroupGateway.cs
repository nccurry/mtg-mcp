using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Playgroup;

/// <summary>
/// Sends Playgroup.gg public API requests and maps responses to Core models.
/// </summary>
public sealed partial class PlaygroupGateway : IPlaygroupGateway
{
    /// <summary>
    /// Sends HTTP requests to the Playgroup public API.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores configured endpoint and authentication settings.
    /// </summary>
    private readonly PlaygroupOptions options;

    /// <summary>
    /// Caches credentials loaded from configuration, environment, or file.
    /// </summary>
    private PlaygroupCredentials? credentials;

    /// <summary>
    /// Stores a sanitized credentials-file error for status reporting.
    /// </summary>
    private string? credentialsFileError;

    /// <summary>
    /// Creates a gateway for Playgroup.gg public API requests.
    /// </summary>
    public PlaygroupGateway(HttpClient httpClient, IOptions<PlaygroupOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= GetConfiguredBaseAddress(this.options);
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    /// <summary>
    /// Selects the API base address from direct environment fallback or options.
    /// </summary>
    private static Uri GetConfiguredBaseAddress(PlaygroupOptions options)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("PLAYGROUP_BASE_ADDRESS");
        if (
            !string.IsNullOrWhiteSpace(fromEnvironment)
            && Uri.TryCreate(fromEnvironment, UriKind.Absolute, out Uri? uri)
        )
        {
            return EnsureTrailingSlash(uri);
        }

        return EnsureTrailingSlash(options.BaseAddress);
    }

    /// <summary>
    /// Ensures relative endpoint paths resolve beneath the API base path.
    /// </summary>
    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string text = uri.ToString();
        return text.EndsWith('/') ? uri : new Uri($"{text}/");
    }
}
