using ModelContextProtocol.Client;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Owns one initialized MCP client process and its isolated filesystem boundary.
/// </summary>
internal sealed class McpProcessSession : IAsyncDisposable
{
    /// <summary>
    /// Identifies the only protocol revision supported by the test server and client.
    /// </summary>
    private const string CurrentProtocolVersion = "2026-07-28";

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
        return await StartAsync(mode, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the built or installed server with an explicit static toolset selection.
    /// </summary>
    internal static async Task<McpProcessSession> StartAsync(
        string? mode,
        string? toolsets,
        CancellationToken cancellationToken)
    {
        return await StartIsolatedAsync(
            mode,
            toolsets,
            null,
            CurrentProtocolVersion,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the server after optionally seeding its otherwise isolated application-data root.
    /// </summary>
    internal static async Task<McpProcessSession> StartAsync(
        string? mode,
        string? toolsets,
        Func<string, CancellationToken, Task>? seedDataRoot,
        CancellationToken cancellationToken)
    {
        return await StartIsolatedAsync(
            mode,
            toolsets,
            seedDataRoot,
            CurrentProtocolVersion,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts an isolated server session with an explicit protocol revision for a protocol-boundary test.
    /// </summary>
    internal static async Task<McpProcessSession> StartWithProtocolAsync(
        string? mode,
        string? toolsets,
        string protocolVersion,
        CancellationToken cancellationToken)
    {
        return await StartIsolatedAsync(
            mode,
            toolsets,
            null,
            protocolVersion,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts an isolated server process and connects an official client at the requested protocol revision.
    /// </summary>
    private static async Task<McpProcessSession> StartIsolatedAsync(
        string? mode,
        string? toolsets,
        Func<string, CancellationToken, Task>? seedDataRoot,
        string protocolVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);

        string repositoryRoot = FindRepositoryRoot();
        DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("mtg-mcp-e2e-");
        string dataRoot = Path.Combine(workingDirectory.FullName, "private-data");
        StdioClientTransportOptions options = CreateTransportOptions(
            repositoryRoot,
            workingDirectory,
            dataRoot,
            mode,
            toolsets,
            environmentOverrides: null,
            requireInstalledCommand: false);

        try
        {
            if (seedDataRoot is not null)
            {
                await seedDataRoot(dataRoot, cancellationToken).ConfigureAwait(false);
            }

            McpClient client = await CreateClientAsync(
                options,
                protocolVersion,
                cancellationToken).ConfigureAwait(false);
            return new McpProcessSession(client, workingDirectory, dataRoot);
        }
        catch
        {
            workingDirectory.Delete(recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Starts an installed package against a caller-owned persistent live-acceptance data root.
    /// </summary>
    internal static async Task<McpProcessSession> StartLiveAsync(
        string dataRoot,
        string mode,
        string toolsets,
        IReadOnlyDictionary<string, string?> environmentOverrides,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(environmentOverrides);

        string repositoryRoot = FindRepositoryRoot();
        DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("mtg-mcp-live-e2e-");
        StdioClientTransportOptions options = CreateTransportOptions(
            repositoryRoot,
            workingDirectory,
            Path.GetFullPath(dataRoot),
            mode,
            toolsets,
            environmentOverrides,
            requireInstalledCommand: true);

        try
        {
            McpClient client = await CreateClientAsync(
                options,
                CurrentProtocolVersion,
                cancellationToken).ConfigureAwait(false);
            return new McpProcessSession(client, workingDirectory, Path.GetFullPath(dataRoot));
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
    /// Connects one official client that explicitly requires the requested protocol revision.
    /// </summary>
    private static async Task<McpClient> CreateClientAsync(
        StdioClientTransportOptions options,
        string protocolVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);

        StdioClientTransport transport = new(options);
        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = protocolVersion,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds transport options for either the repository binary or an installed package command.
    /// </summary>
    private static StdioClientTransportOptions CreateTransportOptions(
        string repositoryRoot,
        DirectoryInfo workingDirectory,
        string dataRoot,
        string? mode,
        string? toolsets,
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        bool requireInstalledCommand)
    {
        string? installedCommand = Environment.GetEnvironmentVariable("MTGMCP_E2E_COMMAND");
        string command;
        string[] arguments;
        if (string.IsNullOrWhiteSpace(installedCommand))
        {
            if (requireInstalledCommand)
            {
                throw new InvalidOperationException(
                    "MTGMCP_E2E_COMMAND must identify the installed package command for live acceptance.");
            }

            command = ResolveDotnetHost();
            arguments = [ResolveApplicationPath(repositoryRoot)];
        }
        else
        {
            command = installedCommand.Trim();
            arguments = [];
        }

        (string launchCommand, string[] launchArguments, string launchWorkingDirectory) =
            OperatingSystem.IsWindows()
                ? CreateWindowsLaunch(workingDirectory, command, arguments)
                : (command, arguments, repositoryRoot);

        Dictionary<string, string?> environment = new(StringComparer.Ordinal)
        {
            ["MTGMCP__DATA_DIR"] = dataRoot,
            ["MTGMCP__MODE"] = mode,
            ["MTGMCP__TOOLSETS"] = toolsets,
            ["MTGMCP__PLAYGROUP__API_KEY"] = null,
        };
        if (!requireInstalledCommand)
        {
            environment["MTGMCP__PLAYGROUP__CREDENTIALS_FILE"] = Path.Combine(
                repositoryRoot,
                "tests",
                "MtgMcp.E2E.Tests",
                "Fixtures",
                "empty-playgroup-credentials.json");
        }

        if (environmentOverrides is not null)
        {
            foreach ((string key, string? value) in environmentOverrides)
            {
                environment[key] = value;
            }
        }

        return new StdioClientTransportOptions
        {
            Name = "mtg-mcp-foundation-e2e",
            Command = launchCommand,
            Arguments = launchArguments,
            WorkingDirectory = launchWorkingDirectory,
            EnvironmentVariables = environment,
            ShutdownTimeout = requireInstalledCommand
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromMilliseconds(500),
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
    /// Resolves the .NET host supplied by the test runner or the Mise environment.
    /// </summary>
    private static string ResolveDotnetHost()
    {
        string? testHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(testHost))
        {
            return testHost;
        }

        return "dotnet";
    }

    /// <summary>
    /// Writes a short Windows batch launcher so the MCP transport can start commands whose paths contain spaces.
    /// </summary>
    private static (string Command, string[] Arguments, string WorkingDirectory) CreateWindowsLaunch(
        DirectoryInfo workingDirectory,
        string command,
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);

        const string launcherName = "mtg-mcp-server.cmd";
        string launcherPath = Path.Combine(workingDirectory.FullName, launcherName);
        List<string> quotedParts = [QuoteForBatch(command)];
        foreach (string argument in arguments)
        {
            quotedParts.Add(QuoteForBatch(argument));
        }

        string commandLine = string.Join(" ", quotedParts);
        File.WriteAllLines(launcherPath, ["@echo off", commandLine]);
        return ("cmd.exe", ["/d", "/c", launcherName], workingDirectory.FullName);
    }

    /// <summary>
    /// Quotes one command or argument for the private batch launcher.
    /// </summary>
    private static string QuoteForBatch(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException("A process path or argument contains unsupported characters.", nameof(value));
        }

        return $"\"{value}\"";
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
