using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Composes and runs the minimal resources-only stdio MCP server.
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

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services
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
            .WithResources(new FoundationResources(configuration));

        using IHost host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
