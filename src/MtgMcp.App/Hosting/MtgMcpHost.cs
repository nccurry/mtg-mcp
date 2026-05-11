using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using MtgMcp.Archidekt;
using MtgMcp.CommanderSpellbook;
using MtgMcp.Core;
using MtgMcp.Decklists;
using MtgMcp.Scryfall;

namespace MtgMcp.App;

/// <summary>
/// Provides mtg mcp host behavior.
/// </summary>
public static class MtgMcpHost
{
    /// <summary>
    /// Builds the args.
    /// </summary>
    public static IHost Build(string[] args)
    {
        return CreateBuilder(args).Build();
    }

    /// <summary>
    /// Validates the services.
    /// </summary>
    public static void ValidateServices(IServiceProvider services)
    {
        services.GetRequiredService<DeckWorkspaceService>();
        services.GetRequiredService<DeckAnalysisService>();
        services.GetRequiredService<DeckRecommendationService>();
        services.GetRequiredService<DeckPlanService>();
        services.GetRequiredService<DeckSimulationService>();
        services.GetRequiredService<ICardCatalog>();
        services.GetRequiredService<IArchidektGateway>();
        services.GetRequiredService<ICardTrendProvider>();
        services.GetRequiredService<ICommanderMetaProvider>();
        services.GetRequiredService<IComboCatalog>();
        services.GetRequiredService<ICorpusCache>();
        _ = services.GetServices<ICorpusSignalProvider>().ToList();
        _ = services.GetRequiredService<OperationModeGuard>().EffectiveMode;
        services.GetRequiredService<ServerInfoService>();
    }

    /// <summary>
    /// Creates the builder.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.Sources.Clear();

        builder
            .Configuration.AddJsonFile("mtg-mcp.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "MTGMCP__")
            .AddCommandLine(args);

        builder.Configuration.AddInMemoryCollection(
            MtgMcpConfigurationAliases.Create(builder.Configuration)
        );

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.Configure<MtgMcpOptions>(builder.Configuration.GetSection("MtgMcp"));
        builder.Services.AddSingleton<IDeckWorkspaceRepository>(serviceProvider =>
        {
            MtgMcpOptions options = serviceProvider
                .GetRequiredService<IOptions<MtgMcpOptions>>()
                .Value;
            return new JsonDeckWorkspaceRepository(options.DataDir);
        });
        builder.Services.AddSingleton<IDeckPlanRepository>(serviceProvider =>
        {
            MtgMcpOptions options = serviceProvider.GetRequiredService<IOptions<MtgMcpOptions>>().Value;
            return new JsonDeckPlanRepository(options.DataDir);
        });
        builder.Services.AddSingleton<ICorpusCache>(serviceProvider =>
        {
            MtgMcpOptions options = serviceProvider.GetRequiredService<IOptions<MtgMcpOptions>>().Value;
            return CorpusCacheFactory.Create(options.DataDir, options.Intelligence.Cache);
        });
        builder.Services.AddTransient<DeckWorkspaceService>();
        builder.Services.AddTransient<DeckAnalysisService>();
        builder.Services.AddTransient<DeckRecommendationService>();
        builder.Services.AddTransient<DeckPlanService>();
        builder.Services.AddTransient<DeckSimulationService>();
        builder.Services.AddSingleton<OperationModeGuard>();
        builder.Services.AddSingleton<ServerInfoService>();
        builder.Services.AddScryfall(builder.Configuration);
        builder.Services.AddArchidekt(builder.Configuration);
        builder.Services.AddCommanderSpellbook(builder.Configuration);
        builder.Services.AddDecklistCorpusSources();

        builder
            .Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly();

        return builder;
    }
}
