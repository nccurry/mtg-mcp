using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using MtgMcp.Archidekt;
using MtgMcp.CommanderSpellbook;
using MtgMcp.Core;
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
        services.GetRequiredService<ICardCatalog>();
        services.GetRequiredService<IArchidektGateway>();
        services.GetRequiredService<ICardTrendProvider>();
        services.GetRequiredService<ICommanderMetaProvider>();
        services.GetRequiredService<IComboCatalog>();
        _ = services.GetRequiredService<OperationModeGuard>().EffectiveMode;
        services.GetRequiredService<ServerInfoService>();
    }

    /// <summary>
    /// Creates the builder.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder
            .Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("mtg-mcp.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
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
        builder.Services.AddTransient<DeckWorkspaceService>();
        builder.Services.AddSingleton<OperationModeGuard>();
        builder.Services.AddSingleton<ServerInfoService>();
        builder.Services.AddScryfall(builder.Configuration);
        builder.Services.AddArchidekt(builder.Configuration);
        builder.Services.AddCommanderSpellbook(builder.Configuration);

        builder
            .Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly();

        return builder;
    }
}
