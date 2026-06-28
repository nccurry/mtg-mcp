using MtgMcp.Core;

namespace MtgMcp.Playgroup;

/// <summary>
/// Configures Playgroup.gg public API access.
/// </summary>
public sealed class PlaygroupOptions
{
    /// <summary>
    /// Gets or sets the Playgroup public API base address.
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://playgroup.gg/api/public/v1/");

    /// <summary>
    /// Gets or sets the authorization scheme for API-key requests.
    /// </summary>
    public string AuthScheme { get; set; } = "Bearer";

    /// <summary>
    /// Gets or sets the User-Agent used for Playgroup requests.
    /// </summary>
    public string UserAgent { get; set; } = MtgMcpHttpDefaults.UserAgent;

    /// <summary>
    /// Gets or sets an optional Playgroup API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a credentials file containing an API key.
    /// </summary>
    public string? CredentialsFile { get; set; }
}

/// <summary>
/// Stores Playgroup credentials loaded from a file.
/// </summary>
public sealed class PlaygroupCredentials
{
    /// <summary>
    /// Gets or sets a Playgroup API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets an access-token alias for API-key credentials.
    /// </summary>
    public string? AccessToken { get; set; }
}
