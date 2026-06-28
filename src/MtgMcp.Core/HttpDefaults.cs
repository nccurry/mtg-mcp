using System.Reflection;

namespace MtgMcp.Core;

/// <summary>
/// Provides shared HTTP defaults for external service adapters.
/// </summary>
public static class MtgMcpHttpDefaults
{
    /// <summary>
    /// Gets the mtg-mcp repository URL advertised to public APIs.
    /// </summary>
    public const string ProjectUrl = "https://github.com/nccurry/mtg-mcp";

    /// <summary>
    /// Gets the default User-Agent value for adapter requests.
    /// </summary>
    public static string UserAgent => $"mtg-mcp/{GetProductVersion()} (+{ProjectUrl})";

    /// <summary>
    /// Applies a configured or default User-Agent to an HTTP client.
    /// </summary>
    public static void ApplyUserAgent(HttpClient httpClient, string? userAgent = null)
    {
        string resolved = string.IsNullOrWhiteSpace(userAgent) ? UserAgent : userAgent.Trim();
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(resolved);
    }

    /// <summary>
    /// Reads the running MCP host version when available.
    /// </summary>
    private static string GetProductVersion()
    {
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly?.GetName().Name?.Equals("MtgMcp.App", StringComparison.Ordinal) == true)
        {
            string? informationalVersion = entryAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return TrimBuildMetadata(informationalVersion);
            }

            Version? assemblyVersion = entryAssembly.GetName().Version;
            if (assemblyVersion is not null)
            {
                return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(0, assemblyVersion.Build)}";
            }
        }

        return "0.0.0";
    }

    /// <summary>
    /// Removes SemVer build metadata that is not valid inside an HTTP product version token.
    /// </summary>
    private static string TrimBuildMetadata(string version)
    {
        int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex < 0 ? version.Trim() : version[..metadataIndex].Trim();
    }
}
