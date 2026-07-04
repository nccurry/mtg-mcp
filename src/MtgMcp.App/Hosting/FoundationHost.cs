using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Decks;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Composes and runs the explicitly registered stdio MCP server surface.
/// </summary>
internal static class FoundationHost
{
    /// <summary>
    /// Runs one stdio session until the client disconnects or cancellation is requested.
    /// </summary>
    internal static async Task RunAsync(
        FoundationConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        using SqliteDeckStore deckStore = new(
            configuration.DataRoot,
            FoundationServerIdentity.PackageVersion);
        DeckReadTools readTools = new(deckStore);
        bool writesVisible = OperationModeGuard.Allows(
            configuration.Mode,
            OperationRequirement.LocalWrite);
        int toolCount = writesVisible ? 19 : 4;
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        IMcpServerBuilder mcpBuilder = builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = FoundationServerIdentity.Name,
                    Title = FoundationServerIdentity.Title,
                    Version = FoundationServerIdentity.PackageVersion,
                };
            })
            .WithStdioServerTransport()
            .WithMessageFilters(filters =>
                filters.AddOutgoingFilter(FoundationProtocolPolicy.OmitImplicitLoggingCapability()))
            .WithResources(new FoundationResources(configuration, toolCount))
            .WithTools(readTools);
        if (writesVisible)
        {
            mcpBuilder.WithTools(new DeckWriteTools(deckStore, configuration.Mode));
        }

        using IHost host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
