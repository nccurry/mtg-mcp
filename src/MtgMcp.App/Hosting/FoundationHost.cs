using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Playgroup;
using MtgMcp.App.Scryfall;
using MtgMcp.App.Statistics;
using MtgMcp.Archidekt;
using MtgMcp.Decks;
using MtgMcp.Playgroup;
using MtgMcp.Scryfall;
using MtgMcp.Statistics;

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

        bool decksEnabled = configuration.Toolsets.Includes(CapabilityToolset.Decks);
        bool archidektEnabled = configuration.Toolsets.Includes(CapabilityToolset.Archidekt);
        bool statisticsEnabled = configuration.Toolsets.Includes(CapabilityToolset.Stats);
        using SqliteDeckStore? deckStore = decksEnabled || archidektEnabled || statisticsEnabled
            ? new SqliteDeckStore(configuration.DataRoot, FoundationServerIdentity.PackageVersion)
            : null;
        bool scryfallEnabled = configuration.Toolsets.Includes(CapabilityToolset.Scryfall);
        using ScryfallService? scryfallService = scryfallEnabled || decksEnabled
            ? new ScryfallService(
                configuration.DataRoot,
                OperationModeGuard.Allows(configuration.Mode, OperationRequirement.LocalWrite),
                FoundationServerIdentity.PackageVersion,
                freshnessTtl: configuration.ScryfallFreshnessTtl)
            : null;
        using ArchidektService? archidektService = archidektEnabled
            ? new ArchidektService(configuration.Archidekt, FoundationServerIdentity.PackageVersion)
            : null;
        bool playgroupEnabled = configuration.Toolsets.Includes(CapabilityToolset.Playgroup);
        using PlaygroupService? playgroupService = playgroupEnabled
            ? new PlaygroupService(configuration.Playgroup, FoundationServerIdentity.PackageVersion)
            : null;
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
                filters.AddOutgoingFilter(FoundationProtocolPolicy.OmitUnsupportedImplicitCapabilities()))
            .WithResources(new FoundationResources(configuration));
        if (decksEnabled && deckStore is not null && scryfallService is not null)
        {
            DeckToolsetManifest.Register(mcpBuilder, deckStore, scryfallService, configuration.Mode);
        }

        if (scryfallEnabled && scryfallService is not null)
        {
            ScryfallToolsetManifest.Register(mcpBuilder, scryfallService, configuration.Mode);
        }

        if (statisticsEnabled && deckStore is not null)
        {
            StatisticsToolsetManifest.Register(
                mcpBuilder,
                deckStore,
                new ExactStatisticsCalculator(FoundationServerIdentity.PackageVersion));
        }

        if (archidektService is not null && deckStore is not null)
        {
            ArchidektToolsetManifest.Register(
                mcpBuilder,
                archidektService,
                deckStore,
                configuration.Mode);
        }

        if (playgroupService is not null)
        {
            PlaygroupToolsetManifest.Register(mcpBuilder, playgroupService, configuration.Mode);
        }

        using IHost host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
