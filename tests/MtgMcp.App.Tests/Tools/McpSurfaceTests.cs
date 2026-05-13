using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MtgMcp.App;
using MtgMcp.Core;

namespace MtgMcp.App.Tests;

/// <summary>
/// Contains tests for mcp surface.
/// </summary>
public sealed class McpSurfaceTests
{
    /// <summary>
    /// Stores the tool types.
    /// </summary>
    private static readonly Type[] ToolTypes =
    [
        typeof(CardTools),
        typeof(WorkspaceTools),
        typeof(DeckMutationTools),
        typeof(CategoryTools),
        typeof(CheckpointTools),
        typeof(AnalysisTools),
        typeof(RecommendationTools),
        typeof(CorpusTools),
        typeof(PlanTools),
        typeof(SimulationTools),
        typeof(IntentTools),
        typeof(ServerTools),
    ];

    /// <summary>
    /// Verifies that tool names cover planned surface.
    /// </summary>
    [Fact]
    public void ToolNames_CoverPlannedSurface()
    {
        string[] expected =
        [
            "search_cards",
            "get_card",
            "get_rulings",
            "get_prints",
            "suggest_cards",
            "create_local_deck",
            "start_deck_workspace",
            "list_local_decks",
            "open_local_deck",
            "open_archidekt_deck",
            "list_archidekt_decks",
            "import_decklist",
            "export_deck",
            "add_card",
            "remove_card",
            "set_card_quantity",
            "move_card",
            "add_card_category",
            "remove_card_category",
            "set_primary_card_category",
            "create_category",
            "rename_category",
            "delete_category",
            "update_deck_metadata",
            "checkpoint_deck",
            "list_deck_checkpoints",
            "get_deck_checkpoint",
            "rename_deck_checkpoint",
            "delete_deck_checkpoint",
            "parse_decklist",
            "validate_deck",
            "analyze_deck",
            "refresh_deck_card_snapshots",
            "summarize_deck_workspace",
            "analyze_draw_odds",
            "analyze_deck_cost",
            "preview_deck_plan",
            "estimate_commander_bracket",
            "analyze_mana_base",
            "analyze_deck_consistency",
            "analyze_deck_performance",
            "compare_plan_performance",
            "find_budget_replacements",
            "find_card_upgrades",
            "find_bracket_reduction_candidates",
            "find_power_reduction_candidates",
            "find_mana_base_improvements",
            "find_consistency_improvements",
            "suggest_deck_categories",
            "analyze_deck_best_practices",
            "compare_to_commander_meta",
            "find_missing_popular_cards",
            "find_new_cards_for_deck",
            "rank_cards_for_deck_query",
            "create_deck_plan_from_query",
            "find_cards_for_deck_goal",
            "find_deck_combos",
            "find_near_miss_combos",
            "estimate_combo_pressure",
            "simulate_goldfish",
            "project_board_state",
            "estimate_win_turn",
            "brainstorm_deck_improvements",
            "analyze_commander_trends",
            "find_lesser_known_cards",
            "find_corpus_budget_replacements",
            "find_top_exemplar_decks",
            "explain_card_corpus_signal",
            "list_corpus_sources",
            "list_deck_plans",
            "get_deck_plan",
            "delete_deck_plan",
            "apply_deck_plan",
            "get_deck_intent",
            "suggest_deck_intent",
            "set_deck_intent",
            "clear_deck_intent",
            "get_server_info",
        ];

        ToolTypes
            .SelectMany(type => GetNamedAttributeValues(type, "McpServerToolAttribute", "Name"))
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>
    /// Verifies that card upgrade weights are true optional overrides.
    /// </summary>
    [Fact]
    public void FindCardUpgrades_UsesNullableWeightOverrides()
    {
        MethodInfo method = typeof(RecommendationTools).GetMethod(nameof(RecommendationTools.FindCardUpgradesAsync))
            ?? throw new InvalidOperationException("find_card_upgrades method not found.");

        ParameterInfo[] parameters = method.GetParameters();
        parameters.Single(parameter => parameter.Name == "focus").ParameterType.Should().Be<string>();
        parameters.Single(parameter => parameter.Name == "maxPrice").ParameterType.Should().Be<decimal?>();
        parameters.Single(parameter => parameter.Name == "roleWeight").ParameterType.Should().Be<double?>();
        parameters.Single(parameter => parameter.Name == "roleWeight").DefaultValue.Should().BeNull();
        parameters.Single(parameter => parameter.Name == "powerWeight").ParameterType.Should().Be<double?>();
        parameters.Single(parameter => parameter.Name == "powerWeight").DefaultValue.Should().BeNull();
        parameters.Single(parameter => parameter.Name == "priceWeight").ParameterType.Should().Be<double?>();
        parameters.Single(parameter => parameter.Name == "priceWeight").DefaultValue.Should().BeNull();
    }

    /// <summary>
    /// Verifies that find_card_upgrades creates card-upgrade plans when focus options are supplied.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_ForwardsFocusOptionsToCardUpgradePlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        UpgradeCardCatalog catalog = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "App Upgrade Surface",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        EdhrecRank = 20_000,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService analysis = new(workspaces, catalog, planRepository: plans);
        DeckSimulationService simulation = new(workspaces, catalog, planRepository: plans);
        DeckRecommendationService recommendations = new(
            workspaces,
            catalog,
            analysis,
            simulation,
            planRepository: plans);
        RecommendationTools tools = new(
            recommendations,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = "plan" })));

        RecommendationPlanResult result = await tools.FindCardUpgradesAsync(
            workspace.Id,
            focus: "speed",
            maxPrice: null,
            limit: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Plan.Kind.Should().Be("card-upgrades");
        result.Plan.Name.Should().Be("Card upgrade plan");
        result.Plan.Rationale.Should().Contain("power=0.5");
        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Phyrexian Arena");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that resource templates cover planned surface.
    /// </summary>
    [Fact]
    public void ResourceTemplates_CoverPlannedSurface()
    {
        string[] expected =
        [
            "mtg://deck/{deckId}",
            "mtg://deck/{deckId}/summary",
            "mtg://deck/{deckId}/intent",
            "mtg://scryfall/syntax-cheatsheet",
            "mtg://formats/{format}/deck-rules",
            "mtg://usage/workspace-selection",
            "mtg://usage/operation-modes",
            "mtg://usage/deck-intent",
            "mtg://config/effective",
            "mtg://server/info",
            "mtg://corpus/sources",
            "mtg://archidekt/auth-status",
        ];

        GetNamedAttributeValues(typeof(MtgResources), "McpServerResourceAttribute", "UriTemplate")
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>
    /// Verifies that prompt names cover planned surface.
    /// </summary>
    [Fact]
    public void PromptNames_CoverPlannedSurface()
    {
        string[] expected =
        [
            "brew_commander_deck",
            "tune_existing_deck",
            "find_budget_replacements",
            "reduce_deck_cost",
            "upgrade_deck_power",
            "reduce_deck_power",
            "lower_commander_bracket",
            "optimize_mana_base",
            "improve_deck_consistency",
            "tune_for_local_meta",
            "review_new_releases_for_deck",
            "goldfish_deck",
            "make_deck_do_goal_better",
            "rules_and_rulings_check",
        ];

        GetNamedAttributeValues(typeof(MtgPrompts), "McpServerPromptAttribute", "Name")
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>
    /// Verifies that secret redactor redacts known secret keys.
    /// </summary>
    [Fact]
    public void SecretRedactor_RedactsKnownSecretKeys()
    {
        Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MtgMcp:Archidekt:Jwt"] = "secret",
            ["MtgMcp:DataDir"] = "C:/data",
        };

        Dictionary<string, object?> redacted = SecretRedactor.Redact(values);

        redacted["MtgMcp:Archidekt:Jwt"].Should().Be("***REDACTED***");
        redacted["MtgMcp:DataDir"].Should().Be("C:/data");
    }

    /// <summary>
    /// Verifies that server info exposes build identity without requiring deck dependencies.
    /// </summary>
    [Fact]
    public void ServerInfoService_ReturnsVersionAndRuntimeIdentity()
    {
        ServerInfoService service = new(
            Options.Create(new MtgMcpOptions
            {
                DataDir = "C:/mtg-mcp-test-data",
                OperationMode = "plan",
            }),
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = "plan" })));

        ServerInfo info = service.GetInfo();

        info.PackageId.Should().Be("Nccurry.MtgMcp");
        info.AssemblyName.Should().Be("MtgMcp.App");
        info.SemVer.Should().NotBeNullOrWhiteSpace();
        info.InformationalVersion.Should().NotBeNullOrWhiteSpace();
        info.OperationMode.Should().Be(OperationModeGuard.Plan);
        info.DataDirectory.Should().Be("C:/mtg-mcp-test-data");
        info.FrameworkDescription.Should().Contain(".NET");
    }

    /// <summary>
    /// Verifies that tool annotations mark read only and mutating tools.
    /// </summary>
    [Fact]
    public void ToolAnnotations_MarkReadOnlyAndMutatingTools()
    {
        CustomAttributeData searchCards = GetToolAttribute(nameof(CardTools.SearchCardsAsync));
        CustomAttributeData addCard = GetToolAttribute(nameof(DeckMutationTools.AddCardAsync));
        CustomAttributeData removeCard = GetToolAttribute(
            nameof(DeckMutationTools.RemoveCardAsync)
        );
        CustomAttributeData openLocal = GetToolAttribute(nameof(WorkspaceTools.OpenLocalDeckAsync));
        CustomAttributeData previewPlan = GetToolAttribute(nameof(PlanTools.PreviewDeckPlanAsync));
        CustomAttributeData bracket = GetToolAttribute(nameof(AnalysisTools.EstimateCommanderBracketAsync));
        CustomAttributeData performance = GetToolAttribute(nameof(SimulationTools.AnalyzeDeckPerformanceAsync));
        CustomAttributeData comparePerformance = GetToolAttribute(nameof(SimulationTools.ComparePlanPerformanceAsync));

        GetNamedBool(searchCards, "ReadOnly").Should().BeTrue();
        GetNamedBool(searchCards, "OpenWorld").Should().BeTrue();
        GetNamedBool(addCard, "ReadOnly").Should().BeFalse();
        GetNamedBool(addCard, "Destructive").Should().BeFalse();
        GetNamedBool(removeCard, "ReadOnly").Should().BeFalse();
        GetNamedBool(removeCard, "Destructive").Should().BeTrue();
        GetNamedBool(openLocal, "ReadOnly").Should().BeTrue();
        GetNamedBool(openLocal, "OpenWorld").Should().BeFalse();
        GetNamedBool(previewPlan, "ReadOnly").Should().BeTrue();
        GetNamedBool(previewPlan, "OpenWorld").Should().BeTrue();
        GetNamedBool(bracket, "ReadOnly").Should().BeTrue();
        GetNamedBool(bracket, "OpenWorld").Should().BeTrue();
        GetNamedBool(performance, "ReadOnly").Should().BeTrue();
        GetNamedBool(performance, "OpenWorld").Should().BeFalse();
        GetNamedBool(comparePerformance, "ReadOnly").Should().BeTrue();
        GetNamedBool(comparePerformance, "OpenWorld").Should().BeTrue();
    }

    /// <summary>
    /// Verifies that operation mode guard normalizes client mode names.
    /// </summary>
    [Fact]
    public void OperationModeGuard_NormalizesClientModeNames()
    {
        new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = "ask" }))
            .EffectiveMode.Should()
            .Be(OperationModeGuard.ReadOnly);
        new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = "plan" }))
            .EffectiveMode.Should()
            .Be(OperationModeGuard.Plan);
        new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = "act" }))
            .EffectiveMode.Should()
            .Be(OperationModeGuard.Apply);
    }

    /// <summary>
    /// Verifies that operation mode guard blocks mutating tools when read only.
    /// </summary>
    [Fact]
    public void OperationModeGuard_AllowsPlanningStateInPlanModeOnly()
    {
        OperationModeGuard planMode = new(Options.Create(new MtgMcpOptions { OperationMode = "plan" }));
        OperationModeGuard readOnlyMode = new(Options.Create(new MtgMcpOptions { OperationMode = "read-only" }));

        planMode.Invoking(guard => guard.EnsureCanWritePlanningState("find_budget_replacements"))
            .Should()
            .NotThrow();
        readOnlyMode.Invoking(guard => guard.EnsureCanWritePlanningState("find_budget_replacements"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*read-only mode*find_budget_replacements*");
    }

    /// <summary>
    /// Verifies that operation mode guard blocks mutating tools when read only.
    /// </summary>
    [Fact]
    public async Task OperationModeGuard_BlocksMutatingToolsWhenReadOnly()
    {
        DeckWorkspaceService deckService = new(new InMemoryRepository(), new EmptyCardCatalog());
        OperationModeGuard operationMode = new(
            Options.Create(new MtgMcpOptions { OperationMode = "read-only" })
        );
        WorkspaceTools tools = new(deckService, operationMode);

        Func<Task> act = () =>
            tools.CreateLocalDeckAsync(
                "Blocked",
                cancellationToken: TestContext.Current.CancellationToken
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only mode*create_local_deck*");
    }

    /// <summary>
    /// Verifies that read-only mode blocks corpus tools that write local planning state.
    /// </summary>
    [Fact]
    public async Task OperationModeGuard_BlocksCorpusBudgetPlanWhenReadOnly()
    {
        InMemoryRepository workspaces = new();
        EmptyCardCatalog catalog = new();
        DeckAnalysisService analysis = new(workspaces, catalog);
        DeckSimulationService simulation = new(workspaces, catalog);
        DeckRecommendationService recommendations = new(workspaces, catalog, analysis, simulation);
        IOptions<MtgMcpOptions> options = Options.Create(new MtgMcpOptions
        {
            OperationMode = "read-only"
        });
        OperationModeGuard operationMode = new(options);
        CorpusTools tools = new(recommendations, operationMode, options);

        Func<Task> act = () => tools.FindCorpusBudgetReplacementsAsync(
            "workspace-1",
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only mode*find_corpus_budget_replacements*");
    }

    /// <summary>
    /// Verifies that configuration aliases map the single documented prefixed environment shape.
    /// </summary>
    [Fact]
    public void ConfigurationAliases_MapDocumentedEnvironmentKeys()
    {
        Dictionary<string, string?> rawConfig = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DATA_DIR"] = "C:/mtg-mcp",
            ["OPERATION_MODE"] = "plan",
            ["INTELLIGENCE:ANALYSIS_DEPTH"] = "best",
            ["INTELLIGENCE:CACHE:MODE"] = "memory",
            ["INTELLIGENCE:CACHE:MAX_BYTES"] = "1024",
            ["INTELLIGENCE:CACHE:TTLS:SCRYFALL_SEARCH"] = "12h",
            ["INTELLIGENCE:CACHE:TTLS:COMMANDERSPELLBOOK"] = "18h",
            ["INTELLIGENCE:SOURCES:SCRYFALL:ENABLED"] = "false",
            ["INTELLIGENCE:SOURCES:COMMANDERSPELLBOOK:ENABLED"] = "false",
            ["INTELLIGENCE:SOURCES:TOPDECK:API_KEY"] = "topdeck-key",
            ["INTELLIGENCE:SOURCES:TOPDECK:BASE_ADDRESS"] = "https://topdeck.test/api/",
            ["ARCHIDEKT:JWT"] = "jwt-token",
            ["ARCHIDEKT:REFRESH_TOKEN"] = "refresh-token",
            ["ARCHIDEKT:USER_ID"] = "278245",
            ["ARCHIDEKT:EMAIL"] = "archidekt@example.com",
            ["ARCHIDEKT:CREDENTIALS_FILE"] = "C:/creds.json",
            ["SCRYFALL:USER_AGENT"] = "mtg-mcp-test",
            ["SCRYFALL:MAX_RATE_LIMIT_RETRIES"] = "5",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(rawConfig)
            .Build();

        IReadOnlyDictionary<string, string?> aliases = MtgMcpConfigurationAliases.Create(
            configuration
        );

        aliases["MtgMcp:DataDir"].Should().Be("C:/mtg-mcp");
        aliases["MtgMcp:OperationMode"].Should().Be("plan");
        aliases["MtgMcp:Intelligence:AnalysisDepth"].Should().Be("best");
        aliases["MtgMcp:Intelligence:Cache:Mode"].Should().Be("memory");
        aliases["MtgMcp:Intelligence:Cache:MaxBytes"].Should().Be("1024");
        aliases["MtgMcp:Intelligence:Cache:Ttls:ScryfallSearch"].Should().Be("12h");
        aliases["MtgMcp:Intelligence:Cache:Ttls:CommanderSpellbook"].Should().Be("18h");
        aliases["MtgMcp:Intelligence:Sources:Scryfall:Enabled"].Should().Be("false");
        aliases["MtgMcp:Intelligence:Sources:CommanderSpellbook:Enabled"].Should().Be("false");
        aliases["MtgMcp:Intelligence:Sources:TopDeck:ApiKey"].Should().Be("topdeck-key");
        aliases["MtgMcp:Intelligence:Sources:TopDeck:BaseAddress"].Should().Be("https://topdeck.test/api/");
        aliases["MtgMcp:Archidekt:Jwt"].Should().Be("jwt-token");
        aliases["MtgMcp:Archidekt:RefreshToken"].Should().Be("refresh-token");
        aliases["MtgMcp:Archidekt:UserId"].Should().Be("278245");
        aliases["MtgMcp:Archidekt:Email"].Should().Be("archidekt@example.com");
        aliases["MtgMcp:Archidekt:CredentialsFile"].Should().Be("C:/creds.json");
        aliases["MtgMcp:Scryfall:UserAgent"].Should().Be("mtg-mcp-test");
        aliases["MtgMcp:Scryfall:MaxRateLimitRetries"].Should().Be("5");
    }

    /// <summary>
    /// Verifies that removed duplicate environment aliases no longer map to config.
    /// </summary>
    [Fact]
    public void ConfigurationAliases_DoNotMapRemovedDuplicateEnvironmentKeys()
    {
        Dictionary<string, string?> rawConfig = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MODE"] = "plan",
            ["ANALYSIS_DEPTH"] = "best",
            ["ARCHIDEKT_USER_ID"] = "278245",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(rawConfig)
            .Build();

        IReadOnlyDictionary<string, string?> aliases = MtgMcpConfigurationAliases.Create(
            configuration
        );

        aliases.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that appsettings.json is not a supported mtg-mcp config file.
    /// </summary>
    [Fact]
    public void HostConfiguration_LoadsMtgMcpJsonButNotAppsettingsJson()
    {
        string originalDirectory = Directory.GetCurrentDirectory();
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"mtg-mcp-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """{ "MtgMcp": { "DataDir": "from-appsettings" } }""");
            File.WriteAllText(
                Path.Combine(tempDirectory, "mtg-mcp.json"),
                """{ "MtgMcp": { "OperationMode": "plan" } }""");
            Directory.SetCurrentDirectory(tempDirectory);

            using IHost host = MtgMcpHost.Build([]);
            MtgMcpOptions options = host.Services.GetRequiredService<IOptions<MtgMcpOptions>>().Value;

            options.DataDir.Should().NotBe("from-appsettings");
            options.OperationMode.Should().Be("plan");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that host build constructs registered services.
    /// </summary>
    [Fact]
    public void HostBuild_ConstructsRegisteredServices()
    {
        using IHost host = MtgMcpHost.Build(["--smoke"]);
        MtgMcpHost.ValidateServices(host.Services);

        DeckWorkspaceService firstWorkspaceService =
            host.Services.GetRequiredService<DeckWorkspaceService>();
        DeckWorkspaceService secondWorkspaceService =
            host.Services.GetRequiredService<DeckWorkspaceService>();

        firstWorkspaceService.Should().NotBeNull();
        secondWorkspaceService
            .Should()
            .NotBeSameAs(
                firstWorkspaceService,
                because: "DeckWorkspaceService must not capture typed HttpClient dependencies as a singleton"
            );
        host.Services.GetRequiredService<ICardCatalog>().Should().NotBeNull();
        host.Services.GetRequiredService<IDeckPlanRepository>().Should().NotBeNull();
        host.Services.GetRequiredService<IArchidektGateway>().Should().NotBeNull();
        host.Services.GetRequiredService<ServerInfoService>().Should().NotBeNull();
        host.Services.GetRequiredService<ICorpusCache>().Should().NotBeNull();
        host.Services.GetServices<ICorpusSignalProvider>().Should().NotBeEmpty();
        host.Services.GetRequiredService<DeckRecommendationService>().ListCorpusSources().Sources.Should().Contain(source =>
            source.Key == "topdeck"
            && source.Status == CorpusSourceStatuses.MissingConfig
            && source.RequiresKey);
        host.Services.GetRequiredService<IOptions<MtgMcpOptions>>()
            .Value.DataDir.Should()
            .NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies that get named attribute values.
    /// </summary>
    private static IReadOnlyList<string> GetNamedAttributeValues(
        Type type,
        string attributeName,
        string propertyName
    )
    {
        List<string> values = [];
        foreach (
            MethodInfo method in type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            )
        )
        {
            foreach (CustomAttributeData attribute in method.CustomAttributes)
            {
                if (!attribute.AttributeType.Name.Equals(attributeName, StringComparison.Ordinal))
                {
                    continue;
                }

                CustomAttributeNamedArgument? namedArgument =
                    attribute.NamedArguments.FirstOrDefault(argument =>
                        argument.MemberName.Equals(propertyName, StringComparison.Ordinal)
                    );
                if (namedArgument.HasValue && namedArgument.Value.TypedValue.Value is string value)
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Verifies that get tool attribute.
    /// </summary>
    private static CustomAttributeData GetToolAttribute(string methodName)
    {
        MethodInfo method =
            ToolTypes
                .Select(type => type.GetMethod(methodName))
                .SingleOrDefault(method => method is not null)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return method.CustomAttributes.Single(attribute =>
            attribute.AttributeType.Name == "McpServerToolAttribute"
        );
    }

    /// <summary>
    /// Verifies that get named bool.
    /// </summary>
    private static bool? GetNamedBool(CustomAttributeData attribute, string propertyName)
    {
        CustomAttributeNamedArgument? argument = attribute.NamedArguments.FirstOrDefault(value =>
            value.MemberName.Equals(propertyName, StringComparison.Ordinal)
        );
        return argument.HasValue ? (bool?)argument.Value.TypedValue.Value : null;
    }

    /// <summary>
    /// Provides empty card catalog behavior.
    /// </summary>
    private sealed class EmptyCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Verifies that search cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Verifies that semantic search cards returns empty.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Verifies that get card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(null);
        }

        /// <summary>
        /// Verifies that get cards by names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(
                new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// Verifies that get rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Verifies that get prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Verifies that suggest cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }

    /// <summary>
    /// Provides enough card data for the card-upgrade MCP surface test.
    /// </summary>
    private sealed class UpgradeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Searches upgrade candidates by role query.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results = query.Contains("draw", StringComparison.OrdinalIgnoreCase)
                ? [new CardSearchResult { Name = "Phyrexian Arena" }]
                : [];
            return Task.FromResult(results);
        }

        /// <summary>
        /// Searches upgrade candidates by semantic role request.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            string role = request.Role ?? "";
            IReadOnlyList<CardSearchResult> results = request.Preset == CardSearchPreset.Role
                && role.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase)
                    ? [new CardSearchResult { Name = "Phyrexian Arena" }]
                    : [];
            return Task.FromResult(results);
        }

        /// <summary>
        /// Gets one fake upgrade candidate.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(nameOrId.Equals("Phyrexian Arena", StringComparison.OrdinalIgnoreCase)
                ? new CardInfo
                {
                    Id = "phyrexian-arena",
                    OracleId = "oracle-phyrexian-arena",
                    Name = "Phyrexian Arena",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "At the beginning of your upkeep, you draw a card and you lose 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 250,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["usd"] = "3.00",
                    },
                }
                : null);
        }

        /// <summary>
        /// Gets fake cards by name.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            Dictionary<string, CardInfo> cards = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                CardInfo? card = await GetCardAsync(name, cancellationToken).ConfigureAwait(false);
                if (card is not null)
                {
                    cards[name] = card;
                }
            }

            return cards;
        }

        /// <summary>
        /// Gets no fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets no fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests no fake prompt cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }

    /// <summary>
    /// Provides in memory repository behavior.
    /// </summary>
    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Stores fake workspaces by id.
        /// </summary>
        private readonly Dictionary<string, DeckWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a workspace in the fake repository.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
            workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets a workspace from the fake repository.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Lists fake workspaces.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(workspaces.Values.ToList());
        }
    }

    /// <summary>
    /// Provides in-memory plan persistence for MCP surface behavior tests.
    /// </summary>
    private sealed class InMemoryPlanRepository : IDeckPlanRepository
    {
        /// <summary>
        /// Stores fake plans by id.
        /// </summary>
        private readonly Dictionary<string, DeckEditPlan> plans = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a fake plan.
        /// </summary>
        public Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
        {
            plans[plan.PlanId] = plan;
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Gets a fake plan by id.
        /// </summary>
        public Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
        {
            plans.TryGetValue(planId, out DeckEditPlan? plan);
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Lists fake plans.
        /// </summary>
        public Task<IReadOnlyList<DeckEditPlan>> ListAsync(
            string? workspaceId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DeckEditPlan> result = plans.Values
                .Where(plan => string.IsNullOrWhiteSpace(workspaceId)
                    || plan.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(result);
        }

        /// <summary>
        /// Deletes a fake plan.
        /// </summary>
        public Task DeleteAsync(string planId, CancellationToken cancellationToken)
        {
            plans.Remove(planId);
            return Task.CompletedTask;
        }
    }
}
