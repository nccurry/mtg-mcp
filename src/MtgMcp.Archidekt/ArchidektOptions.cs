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
    /// Gets or sets the jwt.
    /// </summary>
    public string? Jwt { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt user id for user-scoped deck listing.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the email used for Archidekt login.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the username.
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
}

/// <summary>
/// Provides archidekt credentials behavior.
/// </summary>
public sealed class ArchidektCredentials
{
    /// <summary>
    /// Gets or sets the jwt.
    /// </summary>
    public string? Jwt { get; set; }

    /// <summary>
    /// Gets or sets an access token alias loaded from credentials.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the Archidekt user id.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the email used for Archidekt login.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string? Password { get; set; }
}
