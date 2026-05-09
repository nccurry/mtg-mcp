using ModelContextProtocol.Client;

namespace MtgMcp.E2E.Tests;

internal sealed class McpProcessSession : IAsyncDisposable
{
    private readonly string dataDirectory;

    private McpProcessSession(McpClient client, string dataDirectory)
    {
        Client = client;
        this.dataDirectory = dataDirectory;
    }

    public McpClient Client { get; }

    public static async Task<McpProcessSession> StartAsync(
        Uri scryfallBaseAddress,
        Uri archidektBaseAddress,
        string operationMode,
        CancellationToken cancellationToken)
    {
        string repoRoot = FindRepoRoot();
        string dataDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        StdioClientTransportOptions options = CreateTransportOptions(
            repoRoot,
            scryfallBaseAddress,
            archidektBaseAddress,
            dataDirectory,
            operationMode);

        StdioClientTransport transport = new(options);
        McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new McpProcessSession(client, dataDirectory);
    }

    private static StdioClientTransportOptions CreateTransportOptions(
        string repoRoot,
        Uri scryfallBaseAddress,
        Uri archidektBaseAddress,
        string dataDirectory,
        string operationMode)
    {
        string configuration = GetCurrentConfiguration();
        string appProjectPath = Path.Combine(
            repoRoot,
            "src",
            "MtgMcp.App",
            "MtgMcp.App.csproj");

        return new StdioClientTransportOptions
        {
            Name = "mtg-mcp-e2e",
            Command = "dotnet",
            Arguments = ["run", "--project", appProjectPath, "--configuration", configuration, "--no-build"],
            WorkingDirectory = repoRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["MTGMCP__DATA_DIR"] = dataDirectory,
                ["MTGMCP__OPERATION_MODE"] = operationMode,
                ["MTGMCP__SCRYFALL__BASE_ADDRESS"] = scryfallBaseAddress.ToString(),
                ["MTGMCP__SCRYFALL__USER_AGENT"] = "mtg-mcp-e2e/1.0",
                ["MTGMCP__ARCHIDEKT__BASE_ADDRESS"] = archidektBaseAddress.ToString(),
                ["MTGMCP__ARCHIDEKT__JWT"] = "test-jwt"
            }
        };
    }

    private static string GetCurrentConfiguration()
    {
        DirectoryInfo outputDirectory = new(AppContext.BaseDirectory);
        return outputDirectory.Parent?.Name ?? "Release";
    }

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

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);

        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
