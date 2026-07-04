using ModelContextProtocol.Client;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Owns one initialized MCP client process and its isolated filesystem boundary.
/// </summary>
internal sealed class McpProcessSession : IAsyncDisposable
{
    /// <summary>
    /// Stores the isolated working directory removed when the session closes.
    /// </summary>
    private readonly DirectoryInfo workingDirectory;

    /// <summary>
    /// Creates an initialized session with its isolated paths.
    /// </summary>
    private McpProcessSession(
        McpClient client,
        DirectoryInfo workingDirectory,
        string dataRoot)
    {
        Client = client;
        this.workingDirectory = workingDirectory;
        DataRoot = dataRoot;
    }

    /// <summary>
    /// Gets the connected official MCP client.
    /// </summary>
    internal McpClient Client { get; }

    /// <summary>
    /// Gets the intentionally absent configured data root.
    /// </summary>
    internal string DataRoot { get; }

    /// <summary>
    /// Starts the built or installed server with isolated configuration.
    /// </summary>
    internal static async Task<McpProcessSession> StartAsync(
        string? mode,
        CancellationToken cancellationToken)
    {
        string repositoryRoot = FindRepositoryRoot();
        DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("mtg-mcp-e2e-");
        string dataRoot = Path.Combine(workingDirectory.FullName, "private-data");
        StdioClientTransportOptions options = CreateTransportOptions(
            repositoryRoot,
            dataRoot,
            mode);

        try
        {
            StdioClientTransport transport = new(options);
            McpClient client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new McpProcessSession(client, workingDirectory, dataRoot);
        }
        catch
        {
            workingDirectory.Delete(recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Closes the stdio session and removes all isolated test paths.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);

        workingDirectory.Refresh();
        if (workingDirectory.Exists)
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Builds transport options for either the repository binary or an installed package command.
    /// </summary>
    private static StdioClientTransportOptions CreateTransportOptions(
        string repositoryRoot,
        string dataRoot,
        string? mode)
    {
        string? installedCommand = Environment.GetEnvironmentVariable("MTGMCP_E2E_COMMAND");
        string command;
        string[] arguments;
        if (string.IsNullOrWhiteSpace(installedCommand))
        {
            command = ResolveDotnetHost(repositoryRoot);
            arguments = [ResolveApplicationPath(repositoryRoot)];
        }
        else
        {
            command = ResolveInstalledCommand(repositoryRoot, installedCommand.Trim());
            arguments = [];
        }

        return new StdioClientTransportOptions
        {
            Name = "mtg-mcp-foundation-e2e",
            Command = command,
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["MTGMCP__DATA_DIR"] = dataRoot,
                ["MTGMCP__MODE"] = mode,
            },
            ShutdownTimeout = TimeSpan.FromMilliseconds(500),
        };
    }

    /// <summary>
    /// Resolves the built application used by repository E2E tests.
    /// </summary>
    private static string ResolveApplicationPath(string repositoryRoot)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        string appPath = Path.Combine(
            repositoryRoot,
            "src",
            "MtgMcp.App",
            "bin",
            configuration,
            "net11.0",
            "MtgMcp.App.dll");
        return File.Exists(appPath)
            ? appPath
            : throw new FileNotFoundException("The built MCP application was not found.", appPath);
    }

    /// <summary>
    /// Resolves the repository-local .NET host used by the build.
    /// </summary>
    private static string ResolveDotnetHost(string repositoryRoot)
    {
        string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string repositoryHost = Path.Combine(repositoryRoot, ".dotnet", executableName);
        if (!File.Exists(repositoryHost))
        {
            return "dotnet";
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(".dotnet", executableName)
            : repositoryHost;
    }

    /// <summary>
    /// Uses a repository-relative Windows command so the SDK launcher preserves paths containing spaces.
    /// </summary>
    private static string ResolveInstalledCommand(string repositoryRoot, string command)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathRooted(command))
        {
            return command;
        }

        string relative = Path.GetRelativePath(repositoryRoot, command);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? command
            : relative;
    }

    /// <summary>
    /// Finds the repository root from the E2E output directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mtg-mcp repository root.");
    }
}
