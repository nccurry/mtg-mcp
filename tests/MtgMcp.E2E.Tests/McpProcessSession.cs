using ModelContextProtocol.Client;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Owns a running MCP stdio client and its temporary E2E data directory.
/// </summary>
internal sealed class McpProcessSession : IAsyncDisposable
{
    /// <summary>
    /// Points to the isolated app data directory removed when the session ends.
    /// </summary>
    private readonly string dataDirectory;

    /// <summary>
    /// Creates a session around an initialized MCP client.
    /// </summary>
    private McpProcessSession(McpClient client, string dataDirectory)
    {
        Client = client;
        this.dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Gets the MCP client connected to the app process.
    /// </summary>
    public McpClient Client { get; }

    /// <summary>
    /// Starts the MCP app with fake HTTP endpoints and isolated data storage.
    /// </summary>
    public static async Task<McpProcessSession> StartAsync(
        Uri scryfallBaseAddress,
        Uri archidektBaseAddress,
        string operationMode,
        CancellationToken cancellationToken,
        Uri? commanderSpellbookBaseAddress = null)
    {
        string repoRoot = FindRepoRoot();
        string dataDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        StdioClientTransportOptions options = CreateTransportOptions(
            repoRoot,
            scryfallBaseAddress,
            archidektBaseAddress,
            commanderSpellbookBaseAddress,
            dataDirectory,
            operationMode);

        StdioClientTransport transport = new(options);
        McpClient client = await McpClient
            .CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new McpProcessSession(client, dataDirectory);
    }

    /// <summary>
    /// Builds stdio transport options that point the app at fake services.
    /// </summary>
    private static StdioClientTransportOptions CreateTransportOptions(
        string repoRoot,
        Uri scryfallBaseAddress,
        Uri archidektBaseAddress,
        Uri? commanderSpellbookBaseAddress,
        string dataDirectory,
        string operationMode)
    {
        string? installedCommand = Environment.GetEnvironmentVariable("MTGMCP_E2E_COMMAND");
        string[] commandArguments = [];
        string command = string.IsNullOrWhiteSpace(installedCommand)
            ? ResolveDotnetCommand(repoRoot)
            : installedCommand.Trim();
        if (string.IsNullOrWhiteSpace(installedCommand))
        {
            string configuration = GetCurrentConfiguration();
            string appProjectPath = Path.Combine(
                repoRoot,
                "src",
                "MtgMcp.App",
                "MtgMcp.App.csproj");
            commandArguments = ["run", "--project", appProjectPath, "--configuration", configuration, "--no-build"];
        }

        return new StdioClientTransportOptions
        {
            Name = "mtg-mcp-e2e",
            Command = command,
            Arguments = commandArguments,
            WorkingDirectory = repoRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["MTGMCP__DATA_DIR"] = dataDirectory,
                ["MTGMCP__OPERATION_MODE"] = operationMode,
                ["MTGMCP__SCRYFALL__BASE_ADDRESS"] = scryfallBaseAddress.ToString(),
                ["MTGMCP__SCRYFALL__USER_AGENT"] = "mtg-mcp-e2e/1.0",
                ["MTGMCP__ARCHIDEKT__BASE_ADDRESS"] = archidektBaseAddress.ToString(),
                ["MTGMCP__ARCHIDEKT__USERNAME"] = "test-user",
                ["MTGMCP__ARCHIDEKT__PASSWORD"] = "test-password",
                ["MTGMCP__COMMANDERSPELLBOOK__BASE_ADDRESS"] = (commanderSpellbookBaseAddress ?? new Uri("http://127.0.0.1:9/")).ToString()
            }
        };
    }

    /// <summary>
    /// Finds the same repo-local dotnet host used by bootstrap and Taskfile workflows.
    /// </summary>
    private static string ResolveDotnetCommand(string repoRoot)
    {
        string localDotnetFileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string localDotnet = Path.Combine(
            repoRoot,
            ".dotnet",
            localDotnetFileName);
        if (File.Exists(localDotnet))
        {
            return OperatingSystem.IsWindows()
                ? Path.Combine(".dotnet", localDotnetFileName)
                : localDotnet;
        }

        return "dotnet";
    }

    /// <summary>
    /// Reads the build configuration from the current test output path.
    /// </summary>
    private static string GetCurrentConfiguration()
    {
        DirectoryInfo outputDirectory = new(AppContext.BaseDirectory);
        return outputDirectory.Parent?.Name ?? "Release";
    }

    /// <summary>
    /// Locates the repository root so the E2E process can run the app project.
    /// </summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "mtg-mcp.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    /// <summary>
    /// Disposes the MCP client and removes the temporary data directory.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);

        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
