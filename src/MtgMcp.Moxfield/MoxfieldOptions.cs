using MtgMcp.Core;

namespace MtgMcp.Moxfield;

/// <summary>
/// Configures Moxfield import endpoint settings.
/// </summary>
public sealed class MoxfieldOptions
{
    /// <summary>
    /// Gets or sets the Moxfield API base address.
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://api2.moxfield.com/");

    /// <summary>
    /// Gets or sets the user agent used for anonymous Moxfield imports.
    /// </summary>
    public string UserAgent { get; set; } = MtgMcpHttpDefaults.UserAgent;

    /// <summary>
    /// Gets or sets whether blocked anonymous requests may retry through curl when available.
    /// </summary>
    public bool EnableCurlFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the curl executable used for the blocked-request fallback.
    /// </summary>
    public string CurlPath { get; set; } = "curl";
}
