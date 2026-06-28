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
using MtgMcp.Moxfield;
using MtgMcp.Playgroup;
using MtgMcp.Scryfall;

namespace MtgMcp.App;

/// <summary>
/// Provides mtg mcp host behavior.
/// </summary>
public static class MtgMcpHost
{
    /// <summary>
    /// Guides MCP clients to use Scryfall links included on recommendation rows.
    /// </summary>
    public const string RecommendationPresentationInstructions =
        "When presenting card recommendations, link card names to their Scryfall URI when available.";

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
        services.GetRequiredService<DeckBatchTuningService>();
        services.GetRequiredService<DeckBrainstormingService>();
        services.GetRequiredService<DeckQueryService>();
        services.GetRequiredService<DeckGoalPackageService>();
        services.GetRequiredService<DeckReplacementService>();
        services.GetRequiredService<DeckCategorySuggestionService>();
        services.GetRequiredService<DeckCardEvaluationService>();
        services.GetRequiredService<DeckCorpusRecommendationService>();
        services.GetRequiredService<DeckNewCardService>();
        services.GetRequiredService<DeckNewCardSwapReviewService>();
        services.GetRequiredService<DeckWinconPayoffSearchService>();
        services.GetRequiredService<DeckCommanderEvidenceService>();
        services.GetRequiredService<DeckCommanderCandidateSearchService>();
        services.GetRequiredService<DeckCommanderMetaService>();
        services.GetRequiredService<DeckPlaygroupMetaScoringService>();
        services.GetRequiredService<CommanderThemeResolver>();
        services.GetRequiredService<DeckRecommendationService>();
        services.GetRequiredService<DeckPlanService>();
        services.GetRequiredService<DeckSimulationService>();
        services.GetRequiredService<SimulationProfileCatalog>();
        services.GetRequiredService<CardFacetService>();
        services.GetRequiredService<ICardCatalog>();
        services.GetRequiredService<IArchidektGateway>();
        services.GetRequiredService<IMoxfieldGateway>();
        services.GetRequiredService<IPlaygroupGateway>();
        services.GetRequiredService<PlaygroupService>();
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
        builder.Services.AddTransient<DeckAnalysisMetrics>();
        builder.Services.AddTransient<DeckAnalysisService>();
        builder.Services.AddTransient<DeckBatchTuningService>();
        builder.Services.AddTransient<DeckBrainstormingService>();
        builder.Services.AddTransient<DeckQueryService>();
        builder.Services.AddTransient<DeckGoalPackageService>();
        builder.Services.AddTransient<DeckReplacementService>();
        builder.Services.AddTransient<DeckCategorySuggestionService>();
        builder.Services.AddTransient<DeckCardEvaluationService>();
        builder.Services.AddTransient<DeckCorpusRecommendationService>();
        builder.Services.AddTransient<DeckNewCardService>();
        builder.Services.AddTransient<DeckNewCardSwapReviewService>();
        builder.Services.AddTransient<DeckWinconPayoffSearchService>();
        builder.Services.AddTransient<DeckCommanderEvidenceService>();
        builder.Services.AddTransient<DeckCommanderCandidateSearchService>();
        builder.Services.AddTransient<DeckCommanderMetaService>();
        builder.Services.AddTransient<DeckPlaygroupMetaScoringService>();
        builder.Services.AddTransient<CommanderThemeResolver>();
        builder.Services.AddTransient<DeckRecommendationService>();
        builder.Services.AddTransient<DeckPlanService>();
        builder.Services.AddTransient<DeckSimulationService>();
        builder.Services.AddTransient<CardFacetService>();
        builder.Services.AddTransient<PlaygroupService>();
        builder.Services.AddSingleton<SimulationProfileLoader>();
        builder.Services.AddSingleton(serviceProvider => serviceProvider
            .GetRequiredService<SimulationProfileLoader>()
            .Load());
        builder.Services.AddSingleton<OperationModeGuard>();
        builder.Services.AddSingleton<ServerInfoService>();
        builder.Services.AddScryfall(builder.Configuration);
        builder.Services.AddArchidekt(builder.Configuration);
        builder.Services.AddMoxfield(builder.Configuration);
        builder.Services.AddPlaygroup(builder.Configuration);
        builder.Services.AddCommanderSpellbook(builder.Configuration);
        builder.Services.AddDecklistCorpusSources(builder.Configuration);
        MtgMcpOptions startupOptions = builder
            .Configuration.GetSection("MtgMcp")
            .Get<MtgMcpOptions>() ?? new MtgMcpOptions();

        // Fall back to the well-known files the auth helpers write to, so
        // `mtg-mcp auth archidekt|playgroup` takes effect with no extra MCP config.
        builder.Services.PostConfigure<ArchidektOptions>(options =>
            ApplyDefaultCredentialsFile(
                options.CredentialsFile,
                ArchidektAuthCommand.GetDefaultCredentialsFile(),
                value => options.CredentialsFile = value
            )
        );
        builder.Services.PostConfigure<PlaygroupOptions>(options =>
            ApplyDefaultCredentialsFile(
                options.CredentialsFile,
                PlaygroupAuthCommand.GetDefaultCredentialsFile(),
                value => options.CredentialsFile = value
            )
        );
        builder
            .Services.AddMcpServer(options =>
            {
                options.ServerInstructions = RecommendationPresentationInstructions;
            })
            .WithStdioServerTransport()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpErrorMapping.CreateCallToolFilter()))
            .WithTools(ToolRegistry.CreateTools(startupOptions))
            .WithResourcesFromAssembly()
            .WithPromptsFromAssembly();

        return builder;
    }

    /// <summary>
    /// Applies the per-user default credentials file when none was configured and
    /// the default file exists, so auth helpers take effect without extra config.
    /// </summary>
    private static void ApplyDefaultCredentialsFile(
        string? configured,
        string defaultFile,
        Action<string> setCredentialsFile
    )
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        if (File.Exists(defaultFile))
        {
            setCredentialsFile(defaultFile);
        }
    }
}
