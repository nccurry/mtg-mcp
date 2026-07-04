using System.Reflection;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Provides the stable MCP identity and evaluated package version for this process.
/// </summary>
internal static class FoundationServerIdentity
{
    /// <summary>
    /// Gets the registry-compatible MCP server name.
    /// </summary>
    internal const string Name = "io.github.nccurry/mtg-mcp";

    /// <summary>
    /// Gets the concise human-facing server title.
    /// </summary>
    internal const string Title = "mtg-mcp";

    /// <summary>
    /// Gets the package version without build metadata.
    /// </summary>
    internal static string PackageVersion
    {
        get
        {
            Assembly assembly = typeof(FoundationServerIdentity).Assembly;
            string informationalVersion =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            int metadataStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadataStart >= 0
                ? informationalVersion[..metadataStart]
                : informationalVersion;
        }
    }
}
