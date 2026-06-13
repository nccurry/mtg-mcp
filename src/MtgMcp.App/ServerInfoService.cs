using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Builds redacted runtime identity information for the MCP server.
/// </summary>
public sealed class ServerInfoService
{
    /// <summary>
    /// Limits git probes so version checks cannot stall MCP tool calls.
    /// </summary>
    private const int GitCommandTimeoutMilliseconds = 2000;

    /// <summary>
    /// Stores non-secret mtg-mcp configuration values.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Stores the resolved operation mode guard.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates a service that reads assembly metadata, operation mode, and local git metadata.
    /// </summary>
    public ServerInfoService(
        IOptions<MtgMcpOptions> options,
        OperationModeGuard operationMode)
    {
        this.options = options.Value;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Gets version and runtime details for the current server process.
    /// </summary>
    public ServerInfo GetInfo()
    {
        Assembly assembly = typeof(MtgMcpHost).Assembly;
        AssemblyName assemblyName = assembly.GetName();
        string informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assemblyName.Version?.ToString()
            ?? "";
        string fileVersion =
            assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
            ?? "";
        DirectoryInfo? repositoryRoot = FindRepositoryRoot();

        return new ServerInfo
        {
            AssemblyName = assemblyName.Name ?? "",
            AssemblyPath = assembly.Location,
            SemVer = ExtractSemVer(informationalVersion),
            AssemblyVersion = assemblyName.Version?.ToString() ?? "",
            FileVersion = fileVersion,
            InformationalVersion = informationalVersion,
            GitCommit = DetectGitCommit(informationalVersion, repositoryRoot),
            GitBranch = ReadGit(repositoryRoot, "rev-parse", "--abbrev-ref", "HEAD"),
            GitDirty = DetectGitDirty(repositoryRoot),
            GitRepositoryRoot = repositoryRoot?.FullName,
            OperationMode = operationMode.EffectiveMode,
            DataDirectory = options.DataDir,
            BaseDirectory = AppContext.BaseDirectory,
            CurrentDirectory = Environment.CurrentDirectory,
        };
    }

    /// <summary>
    /// Removes build metadata from the informational version.
    /// </summary>
    private static string ExtractSemVer(string informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "";
        }

        int metadataStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataStart >= 0
            ? informationalVersion[..metadataStart]
            : informationalVersion;
    }

    /// <summary>
    /// Finds the best available source revision from assembly, environment, or git.
    /// </summary>
    private static string? DetectGitCommit(string informationalVersion, DirectoryInfo? repositoryRoot)
    {
        string? metadataCommit = ExtractGitCommitFromInformationalVersion(informationalVersion);
        if (!string.IsNullOrWhiteSpace(metadataCommit))
        {
            return metadataCommit;
        }

        string? environmentCommit =
            Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? Environment.GetEnvironmentVariable("GIT_COMMIT")
            ?? Environment.GetEnvironmentVariable("BUILD_SOURCEVERSION");
        if (!string.IsNullOrWhiteSpace(environmentCommit))
        {
            return environmentCommit;
        }

        return ReadGit(repositoryRoot, "rev-parse", "HEAD");
    }

    /// <summary>
    /// Reads a commit-like build metadata segment from the informational version.
    /// </summary>
    private static string? ExtractGitCommitFromInformationalVersion(string informationalVersion)
    {
        int metadataStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (metadataStart < 0 || metadataStart == informationalVersion.Length - 1)
        {
            return null;
        }

        string metadata = informationalVersion[(metadataStart + 1)..];
        string candidate = metadata
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.All(Uri.IsHexDigit) && part.Length >= 7)
            ?? "";
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    /// <summary>
    /// Checks whether the detected repository has uncommitted tracked or untracked files.
    /// </summary>
    private static bool? DetectGitDirty(DirectoryInfo? repositoryRoot)
    {
        string? status = ReadGit(repositoryRoot, "status", "--short");
        return status is null ? null : status.Length > 0;
    }

    /// <summary>
    /// Finds a git repository root near the server binary or current process directory.
    /// </summary>
    private static DirectoryInfo? FindRepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct())
        {
            DirectoryInfo? current = Directory.Exists(start)
                ? new DirectoryInfo(start)
                : new FileInfo(start).Directory;
            while (current is not null)
            {
                string gitPath = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return current;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs a bounded git command against the detected repository.
    /// </summary>
    private static string? ReadGit(DirectoryInfo? repositoryRoot, params string[] arguments)
    {
        if (repositoryRoot is null)
        {
            return null;
        }

        try
        {
            ProcessStartInfo startInfo = new("git")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(repositoryRoot.FullName);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start git process.");
            if (!process.WaitForExit(GitCommandTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
