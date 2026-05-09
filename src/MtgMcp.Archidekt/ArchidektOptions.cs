namespace MtgMcp.Archidekt;

public sealed class ArchidektOptions
{
    public Uri BaseAddress { get; set; } = new("https://archidekt.com/");
    public string AuthScheme { get; set; } = "JWT";
    public string? Jwt { get; set; }
    public string? RefreshToken { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? CredentialsFile { get; set; }
    public bool EnableUsernamePasswordLogin { get; set; } = true;
}

public sealed class ArchidektCredentials
{
    public string? Jwt { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
