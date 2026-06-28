using System.Runtime.InteropServices;

namespace MtgMcp.App;

/// <summary>
/// Describes the running MCP server build and runtime environment.
/// </summary>
public sealed class ServerInfo
{
    /// <summary>
    /// Gets or sets the MCP server package id.
    /// </summary>
    public string PackageId { get; set; } = "Nccurry.MtgMcp";

    /// <summary>
    /// Gets or sets the executable assembly name.
    /// </summary>
    public string AssemblyName { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved path to the server assembly.
    /// </summary>
    public string AssemblyPath { get; set; } = "";

    /// <summary>
    /// Gets or sets the semantic package version without build metadata.
    /// </summary>
    public string SemVer { get; set; } = "";

    /// <summary>
    /// Gets or sets the assembly version.
    /// </summary>
    public string AssemblyVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the file version reported by the assembly.
    /// </summary>
    public string FileVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the informational version reported by the assembly.
    /// </summary>
    public string InformationalVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the source control commit when it can be detected.
    /// </summary>
    public string? GitCommit { get; set; }

    /// <summary>
    /// Gets or sets the source control branch when it can be detected.
    /// </summary>
    public string? GitBranch { get; set; }

    /// <summary>
    /// Gets or sets whether the detected repository has uncommitted changes.
    /// </summary>
    public bool? GitDirty { get; set; }

    /// <summary>
    /// Gets or sets the detected repository root.
    /// </summary>
    public string? GitRepositoryRoot { get; set; }

    /// <summary>
    /// Gets or sets the effective mtg-mcp operation mode.
    /// </summary>
    public string OperationMode { get; set; } = "";

    /// <summary>
    /// Gets or sets the current MCP host-boundary logging threshold.
    /// </summary>
    public string McpLoggingLevel { get; set; } = "";

    /// <summary>
    /// Gets or sets the configured data directory.
    /// </summary>
    public string DataDirectory { get; set; } = "";

    /// <summary>
    /// Gets or sets the assembly base directory used to launch the server.
    /// </summary>
    public string BaseDirectory { get; set; } = "";

    /// <summary>
    /// Gets or sets the current process working directory.
    /// </summary>
    public string CurrentDirectory { get; set; } = "";

    /// <summary>
    /// Gets or sets the .NET runtime framework description.
    /// </summary>
    public string FrameworkDescription { get; set; } = RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// Gets or sets the operating system description.
    /// </summary>
    public string OSDescription { get; set; } = RuntimeInformation.OSDescription;

    /// <summary>
    /// Gets or sets the process architecture.
    /// </summary>
    public string ProcessArchitecture { get; set; } = RuntimeInformation.ProcessArchitecture.ToString();
}
