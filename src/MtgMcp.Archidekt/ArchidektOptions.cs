namespace MtgMcp.Archidekt;

/// <summary>
/// Configures the isolated Archidekt transport and its conservative client safety limits.
/// </summary>
public sealed record ArchidektOptions(
    Uri BaseAddress,
    string? Username,
    string? Password,
    string? CredentialsFile,
    TimeSpan MinimumRequestInterval,
    int MaximumRequestsPerWindow,
    TimeSpan RequestWindow,
    int MaximumRequestsPerOperation)
{
    /// <summary>
    /// Creates the production defaults that stay below the currently observed provider threshold.
    /// </summary>
    public static ArchidektOptions CreateDefault(
        string? username = null,
        string? password = null,
        string? credentialsFile = null)
    {
        return new ArchidektOptions(
            new Uri("https://archidekt.com/", UriKind.Absolute),
            username,
            password,
            credentialsFile,
            TimeSpan.FromSeconds(2),
            30,
            TimeSpan.FromMinutes(1),
            150);
    }

    /// <summary>
    /// Validates values before any provider session or request is created.
    /// </summary>
    public void Validate()
    {
        bool secureProvider = BaseAddress.IsAbsoluteUri && BaseAddress.Scheme == Uri.UriSchemeHttps;
        bool loopbackTestProvider = BaseAddress.IsAbsoluteUri &&
            BaseAddress.Scheme == Uri.UriSchemeHttp &&
            BaseAddress.IsLoopback;
        if (!secureProvider && !loopbackTestProvider)
        {
            throw new ArgumentException(
                "Archidekt base address must use HTTPS unless it is loopback.",
                nameof(BaseAddress));
        }

        if (MinimumRequestInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumRequestInterval),
                MinimumRequestInterval,
                "Minimum request interval cannot be negative.");
        }

        if (MaximumRequestsPerWindow <= 0 || MaximumRequestsPerOperation <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRequestsPerWindow),
                MaximumRequestsPerWindow,
                "Request ceilings must be positive.");
        }

        if (RequestWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestWindow),
                RequestWindow,
                "Request window must be positive.");
        }
    }
}
