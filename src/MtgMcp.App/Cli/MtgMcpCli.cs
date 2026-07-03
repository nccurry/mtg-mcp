using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MtgMcp.App;

/// <summary>
/// Routes command-line invocations before starting the MCP host.
/// </summary>
public static class MtgMcpCli
{
    /// <summary>
    /// Runs a non-MCP command or starts the stdio MCP host.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string[], IHost>? buildHost = null
    )
    {
        if (IsMainHelp(args))
        {
            WriteMainHelp(output);
            return 0;
        }

        if (IsAuthHelp(args))
        {
            WriteAuthHelp(output);
            return 0;
        }

        if (IsVersion(args))
        {
            WriteVersion(output);
            return 0;
        }

        if (ArchidektAuthCommand.IsCommand(args))
        {
            return ArchidektAuthCommand.Run(args, output, error);
        }

        if (PlaygroupAuthCommand.IsCommand(args))
        {
            return PlaygroupAuthCommand.Run(args, output, error);
        }

        if (args.Length > 0 && args[0].Equals("auth", StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine($"Unknown auth provider '{(args.Length > 1 ? args[1] : "")}'.");
            WriteAuthHelp(error);
            return 1;
        }

        bool smoke = args.Any(arg => arg.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
        using IHost host = (buildHost ?? MtgMcpHost.Build)(args);

        if (smoke)
        {
            MtgMcpHost.ValidateServices(host.Services);
            WriteSmokeInfo(output, host.Services.GetRequiredService<ServerInfoService>().GetInfo());
            return 0;
        }

        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Determines whether arguments request top-level command help.
    /// </summary>
    private static bool IsMainHelp(IReadOnlyList<string> args)
    {
        return args.Count == 1
            && (args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)
                || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase)
                || args[0].Equals("help", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether arguments request auth command help.
    /// </summary>
    private static bool IsAuthHelp(IReadOnlyList<string> args)
    {
        return args.Count >= 1
            && args[0].Equals("auth", StringComparison.OrdinalIgnoreCase)
            && (
                args.Count == 1
                || (args.Count == 2
                    && (
                        args[1].Equals("--help", StringComparison.OrdinalIgnoreCase)
                        || args[1].Equals("-h", StringComparison.OrdinalIgnoreCase)
                        || args[1].Equals("help", StringComparison.OrdinalIgnoreCase)
                    ))
            );
    }

    /// <summary>
    /// Determines whether arguments request package version output.
    /// </summary>
    private static bool IsVersion(IReadOnlyList<string> args)
    {
        return args.Count == 1
            && (
                args[0].Equals("--version", StringComparison.OrdinalIgnoreCase)
                || args[0].Equals("-v", StringComparison.OrdinalIgnoreCase)
                || args[0].Equals("version", StringComparison.OrdinalIgnoreCase)
            );
    }

    /// <summary>
    /// Writes top-level CLI usage.
    /// </summary>
    private static void WriteMainHelp(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  mtg-mcp [--smoke|--version]");
        output.WriteLine("  mtg-mcp auth <provider> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  auth archidekt  Write an Archidekt credentials file.");
        output.WriteLine("  auth playgroup  Write a Playgroup.gg credentials file.");
        output.WriteLine();
        output.WriteLine("Safety:");
        output.WriteLine("  The default operation mode is plan. Set MTGMCP__OPERATION_MODE=apply to enable mutation tools.");
        output.WriteLine();
        output.WriteLine("Run 'mtg-mcp auth --help' for credential helper usage.");
    }

    /// <summary>
    /// Writes the package version without starting the MCP stdio host.
    /// </summary>
    private static void WriteVersion(TextWriter output)
    {
        Assembly assembly = typeof(MtgMcpCli).Assembly;
        string informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        output.WriteLine($"mtg-mcp {ExtractSemVer(informationalVersion)}");
    }

    /// <summary>
    /// Writes build and runtime identity after validating the host can start.
    /// </summary>
    private static void WriteSmokeInfo(TextWriter output, ServerInfo info)
    {
        output.WriteLine("mtg-mcp host build ok");
        output.WriteLine($"serverVersion: {info.SemVer}");
        output.WriteLine($"serverInformationalVersion: {info.InformationalVersion}");
        output.WriteLine($"serverAssemblyPath: {info.AssemblyPath}");
        output.WriteLine($"gitCommit: {info.GitCommit ?? "unknown"}");
        output.WriteLine($"gitBranch: {info.GitBranch ?? "unknown"}");
        output.WriteLine($"gitDirty: {FormatNullableBoolean(info.GitDirty)}");
        output.WriteLine($"operationMode: {info.OperationMode}");
        output.WriteLine($"mcpLoggingLevel: {info.McpLoggingLevel}");
    }

    /// <summary>
    /// Formats an optional boolean for smoke output.
    /// </summary>
    private static string FormatNullableBoolean(bool? value)
    {
        return value.HasValue
            ? value.Value.ToString().ToLowerInvariant()
            : "unknown";
    }

    /// <summary>
    /// Removes build metadata from an assembly informational version.
    /// </summary>
    private static string ExtractSemVer(string informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "unknown";
        }

        int metadataStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataStart >= 0
            ? informationalVersion[..metadataStart]
            : informationalVersion;
    }

    /// <summary>
    /// Writes auth helper usage.
    /// </summary>
    private static void WriteAuthHelp(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine(
            "  mtg-mcp auth archidekt [--credentials-file <path>] "
                + "--username <name-or-email> --password <password> [--force]"
        );
        output.WriteLine(
            "  mtg-mcp auth playgroup [--credentials-file <path>] "
                + "--api-key <key> [--force]"
        );
    }
}
