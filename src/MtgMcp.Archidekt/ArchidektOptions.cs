namespace MtgMcp.Archidekt;

/// <summary>
/// Configures archidekt options settings.
/// </summary>
public sealed class ArchidektOptions
{
    /// <summary>
    /// Gets or sets the base address.
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://archidekt.com/");

    /// <summary>
    /// Gets or sets the auth scheme.
    /// </summary>
    public string AuthScheme { get; set; } = "JWT";

    /// <summary>
    /// Gets or sets the Archidekt username or account email used for login.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the credentials file.
    /// </summary>
    public string? CredentialsFile { get; set; }

    /// <summary>
    /// Gets or sets the enable username password login.
    /// </summary>
    public bool EnableUsernamePasswordLogin { get; set; } = true;

    /// <summary>
    /// Gets or sets optional client-side pacing for Archidekt requests.
    /// </summary>
    public ArchidektRateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>
/// Configures optional client-side Archidekt request pacing.
/// </summary>
public sealed class ArchidektRateLimitOptions
{
    /// <summary>
    /// Gets or sets the maximum requests allowed in one window; zero disables proactive pacing.
    /// </summary>
    public int MaxRequests { get; set; }

    /// <summary>
    /// Gets or sets the request window length in seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Provides archidekt credentials behavior.
/// </summary>
public sealed class ArchidektCredentials
{
    /// <summary>
    /// Gets or sets the Archidekt username or account email used for login.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string? Password { get; set; }
}
