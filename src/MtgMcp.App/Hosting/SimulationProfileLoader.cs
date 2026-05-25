using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Loads optional simulation profile files for the host while Core owns schema validation.
/// </summary>
public sealed class SimulationProfileLoader
{
    /// <summary>
    /// Stores bound host options used to locate optional profile files.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a loader from bound MCP options.
    /// </summary>
    public SimulationProfileLoader(IOptions<MtgMcpOptions> options)
    {
        this.options = options.Value;
    }

    /// <summary>
    /// Builds the active simulation profile catalog.
    /// </summary>
    public SimulationProfileCatalog Load()
    {
        List<string> paths = ExpandPaths(options.Simulation.ProfilePaths);
        (List<SimulationProfile> profiles, List<string> warnings) = SimulationProfileCatalog.ReadProfileFiles(paths);
        return new SimulationProfileCatalog(
            profiles,
            options.Simulation.AllowExternalProfileOverrides,
            warnings);
    }

    /// <summary>
    /// Expands configured file paths and simple glob patterns into concrete files.
    /// </summary>
    private static List<string> ExpandPaths(IEnumerable<string> configuredPaths)
    {
        List<string> paths = [];
        foreach (string configuredPath in configuredPaths)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                continue;
            }

            string path = Environment.ExpandEnvironmentVariables(configuredPath);
            if (!path.Contains('*', StringComparison.Ordinal))
            {
                paths.Add(Path.GetFullPath(path));
                continue;
            }

            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            string pattern = Path.GetFileName(path);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            paths.AddRange(Directory.GetFiles(directory, pattern));
        }

        return paths;
    }
}
