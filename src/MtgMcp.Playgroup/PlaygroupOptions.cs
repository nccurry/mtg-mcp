namespace MtgMcp.Playgroup;

/// <summary>
/// Holds private Playgroup authentication and conservative transport settings.
/// </summary>
public sealed record PlaygroupOptions(
    string? ApiKey,
    string? CredentialsFile,
    TimeSpan MinimumRequestInterval,
    TimeSpan MaximumRetryAfter)
{
    /// <summary>
    /// Creates the supported production configuration.
    /// </summary>
    public static PlaygroupOptions CreateDefault(string? apiKey, string? credentialsFile = null)
    {
        return new PlaygroupOptions(
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            string.IsNullOrWhiteSpace(credentialsFile) ? null : Path.GetFullPath(credentialsFile),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Rejects transport settings that could disable pacing or create unbounded waits.
    /// </summary>
    public void Validate()
    {
        if (ApiKey is not null && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ArgumentException("The Playgroup API key cannot be blank.", nameof(ApiKey));
        }

        if (CredentialsFile is not null && string.IsNullOrWhiteSpace(CredentialsFile))
        {
            throw new ArgumentException("The Playgroup credentials file cannot be blank.", nameof(CredentialsFile));
        }

        if (MinimumRequestInterval < TimeSpan.FromMilliseconds(250))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumRequestInterval),
                "Playgroup requests must be spaced by at least 250 milliseconds.");
        }

        if (MaximumRetryAfter <= TimeSpan.Zero || MaximumRetryAfter > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRetryAfter),
                "The bounded Retry-After limit must be positive and no greater than five minutes.");
        }
    }
}
