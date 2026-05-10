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
        if (ArchidektAuthCommand.IsCommand(args))
        {
            return ArchidektAuthCommand.Run(args, output, error);
        }

        bool smoke = args.Any(arg => arg.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
        using IHost host = (buildHost ?? MtgMcpHost.Build)(args);

        if (smoke)
        {
            MtgMcpHost.ValidateServices(host.Services);
            error.WriteLine("mtg-mcp host build ok");
            return 0;
        }

        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
