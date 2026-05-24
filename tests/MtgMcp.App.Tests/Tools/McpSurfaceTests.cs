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
        typeof(FacetTools),
        typeof(PlaygroupTools),
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
            "get_playgroup_auth_status",
            "get_playgroup",
            "get_playgroup_deck",
            "list_playgroup_decks",
            "list_playgroup_users",
            "list_playgroup_user_decks",
            "rank_playgroup_decks",
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
            "analyze_deck_best_practices",
            "compare_to_commander_meta",
            "find_new_cards_for_deck",
            "query_cards_for_deck",
            "find_deck_combos",
            "find_near_miss_combos",
            "estimate_combo_pressure",
            "simulate_goldfish",
            "project_board_state",
            "estimate_win_turn",
            "analyze_commander_trends",
            "find_lesser_known_cards",
            "find_top_exemplar_decks",
            "explain_card_corpus_signal",
            "search_corpus_evidence",
            "search_reddit_discussions",
            "list_corpus_sources",
            "create_deck_plan_from_explicit_changes",
            "list_deck_plans",
            "get_deck_plan",
            "delete_deck_plan",
            "apply_deck_plan",
            "get_deck_intent",
            "suggest_deck_intent",
            "set_deck_intent",
            "clear_deck_intent",
            "get_card_facets",
            "get_deck_facets",
            "count_deck_cards_matching",
            "explain_card_match",
            "set_card_facet_annotations",
            "get_server_info",
        ];

        ToolTypes
            .SelectMany(type => GetNamedAttributeValues(type, "McpServerToolAttribute", "Name"))
            .Should()
            .BeEquivalentTo(expected);
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
            "mtg://playgroup/auth-status",
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
            "research_budget_replacements",
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
            ["MtgMcp:Playgroup:ApiKey"] = "playgroup-secret",
            ["MtgMcp:DataDir"] = "C:/data",
        };

        Dictionary<string, object?> redacted = SecretRedactor.Redact(values);

        redacted["MtgMcp:Archidekt:Jwt"].Should().Be("***REDACTED***");
        redacted["MtgMcp:Playgroup:ApiKey"].Should().Be("***REDACTED***");
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
        CustomAttributeData queryCards = GetToolAttribute(nameof(RecommendationTools.QueryCardsForDeckAsync));
        CustomAttributeData createExplicitPlan = GetToolAttribute(nameof(PlanTools.CreateDeckPlanFromExplicitChangesAsync));
        CustomAttributeData listPlaygroupDecks = GetToolAttribute(nameof(PlaygroupTools.ListPlaygroupDecksAsync));

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
        GetNamedBool(queryCards, "ReadOnly").Should().BeTrue();
        GetNamedBool(queryCards, "OpenWorld").Should().BeTrue();
        GetNamedBool(createExplicitPlan, "ReadOnly").Should().BeFalse();
        GetNamedBool(createExplicitPlan, "OpenWorld").Should().BeFalse();
        GetNamedBool(listPlaygroupDecks, "ReadOnly").Should().BeTrue();
        GetNamedBool(listPlaygroupDecks, "OpenWorld").Should().BeTrue();
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

        planMode.Invoking(guard => guard.EnsureCanWritePlanningState("create_deck_plan_from_explicit_changes"))
            .Should()
            .NotThrow();
        readOnlyMode.Invoking(guard => guard.EnsureCanWritePlanningState("create_deck_plan_from_explicit_changes"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*read-only mode*create_deck_plan_from_explicit_changes*");
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
    /// Verifies that open deck summaries follow primary category inclusion rules.
    /// </summary>
    [Fact]
    public async Task OpenArchidektDeck_UsesPrimaryCategoryForIncludedCount()
    {
        FakeArchidektGateway archidektGateway = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote-workspace",
                Name = "Remote",
                Format = "commander",
                Mode = WorkspaceMode.Archidekt,
                ArchidektDeckId = "123",
                Categories =
                [
                    new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                    new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                    new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
                    new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
                ],
                Cards =
                [
                    new DeckCard
                    {
                        Name = "Main Ramp",
                        Quantity = 1,
                        PrimaryCategory = DeckRoles.Ramp,
                        Categories = [DeckRoles.Ramp],
                    },
                    new DeckCard
                    {
                        Name = "Maybe Draw",
                        Quantity = 1,
                        PrimaryCategory = DeckDefaults.Maybeboard,
                        Categories = [DeckDefaults.Maybeboard, DeckRoles.Draw],
                    },
                    new DeckCard
                    {
                        Name = "Maybe Ramp",
                        Quantity = 2,
                        PrimaryCategory = DeckDefaults.Maybeboard,
                        Categories = [DeckDefaults.Maybeboard, DeckRoles.Ramp],
                    },
                    new DeckCard
                    {
                        Name = "Sideboard Test",
                        Quantity = 1,
                        PrimaryCategory = DeckDefaults.Sideboard,
                        Categories = [DeckDefaults.Sideboard],
                    },
                ],
            },
        };
        DeckWorkspaceService deckService = new(
            new InMemoryRepository(),
            new EmptyCardCatalog(),
            archidektGateway);
        OperationModeGuard operationMode = new(
            Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })
        );
        WorkspaceTools tools = new(deckService, operationMode);

        DeckOpenResult result = await tools.OpenArchidektDeckAsync(
            "https://archidekt.com/decks/123/remote",
            true,
            TestContext.Current.CancellationToken);

        result.TotalCards.Should().Be(5);
        result.IncludedCards.Should().Be(1);
        result.MaybeboardCards.Should().Be(4);
        result.Categories.Single(category => category.Name == DeckRoles.Ramp).CardCount.Should().Be(1);
        result.Categories.Single(category => category.Name == DeckRoles.Draw).CardCount.Should().Be(0);
        result.Categories.Single(category => category.Name == DeckDefaults.Maybeboard).CardCount.Should().Be(3);
        result.Categories.Single(category => category.Name == DeckDefaults.Sideboard).CardCount.Should().Be(1);
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
            ["INTELLIGENCE:SOURCES:SCRYFALL_TAGGER:ENABLED"] = "false",
            ["INTELLIGENCE:SOURCES:COMMANDERSPELLBOOK:ENABLED"] = "false",
            ["INTELLIGENCE:SOURCES:TOPDECK:API_KEY"] = "topdeck-key",
            ["INTELLIGENCE:SOURCES:TOPDECK:BASE_ADDRESS"] = "https://topdeck.test/api/",
            ["INTELLIGENCE:SOURCES:EDHTOP16:ENABLED"] = "true",
            ["INTELLIGENCE:SOURCES:EDHTOP16:ALLOW_UNOFFICIAL_API"] = "true",
            ["INTELLIGENCE:SOURCES:EDHTOP16:BASE_ADDRESS"] = "https://edhtop16.test/",
            ["INTELLIGENCE:SOURCES:REDDIT:ENABLED"] = "true",
            ["INTELLIGENCE:SOURCES:REDDIT:API_KEY"] = "reddit-token",
            ["INTELLIGENCE:SOURCES:REDDIT:ALLOW_UNOFFICIAL_API"] = "true",
            ["INTELLIGENCE:SOURCES:REDDIT:BASE_ADDRESS"] = "https://reddit.test/",
            ["ARCHIDEKT:JWT"] = "jwt-token",
            ["ARCHIDEKT:REFRESH_TOKEN"] = "refresh-token",
            ["ARCHIDEKT:USER_ID"] = "278245",
            ["ARCHIDEKT:EMAIL"] = "archidekt@example.com",
            ["ARCHIDEKT:CREDENTIALS_FILE"] = "C:/creds.json",
            ["PLAYGROUP:BASE_ADDRESS"] = "https://playgroup.test/api/public/v1/",
            ["PLAYGROUP:API_KEY"] = "playgroup-key",
            ["PLAYGROUP:CREDENTIALS_FILE"] = "C:/playgroup-creds.json",
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
        aliases["MtgMcp:Intelligence:Sources:ScryfallTagger:Enabled"].Should().Be("false");
        aliases["MtgMcp:Intelligence:Sources:CommanderSpellbook:Enabled"].Should().Be("false");
        aliases["MtgMcp:Intelligence:Sources:TopDeck:ApiKey"].Should().Be("topdeck-key");
        aliases["MtgMcp:Intelligence:Sources:TopDeck:BaseAddress"].Should().Be("https://topdeck.test/api/");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:Enabled"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:AllowUnofficialApi"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:BaseAddress"].Should().Be("https://edhtop16.test/");
        aliases["MtgMcp:Intelligence:Sources:Reddit:Enabled"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:Reddit:ApiKey"].Should().Be("reddit-token");
        aliases["MtgMcp:Intelligence:Sources:Reddit:AllowUnofficialApi"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:Reddit:BaseAddress"].Should().Be("https://reddit.test/");
        aliases["MtgMcp:Archidekt:Jwt"].Should().Be("jwt-token");
        aliases["MtgMcp:Archidekt:RefreshToken"].Should().Be("refresh-token");
        aliases["MtgMcp:Archidekt:UserId"].Should().Be("278245");
        aliases["MtgMcp:Archidekt:Email"].Should().Be("archidekt@example.com");
        aliases["MtgMcp:Archidekt:CredentialsFile"].Should().Be("C:/creds.json");
        aliases["MtgMcp:Playgroup:BaseAddress"].Should().Be("https://playgroup.test/api/public/v1/");
        aliases["MtgMcp:Playgroup:ApiKey"].Should().Be("playgroup-key");
        aliases["MtgMcp:Playgroup:CredentialsFile"].Should().Be("C:/playgroup-creds.json");
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
        host.Services.GetRequiredService<IPlaygroupGateway>().Should().NotBeNull();
        host.Services.GetRequiredService<PlaygroupService>().Should().NotBeNull();
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
    /// Provides Archidekt gateway behavior for app tool tests.
    /// </summary>
    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        /// <summary>
        /// Gets or sets the workspace returned from import.
        /// </summary>
        public DeckWorkspace ImportedDeck { get; set; } = new();

        /// <summary>
        /// Returns a configured authenticated status.
        /// </summary>
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasJwt = true });
        }

        /// <summary>
        /// Returns no deck summaries.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>([]);
        }

        /// <summary>
        /// Imports the configured fake workspace.
        /// </summary>
        public Task<DeckWorkspace> ImportDeckAsync(
            string deckIdOrUrl,
            bool writeBack,
            CancellationToken cancellationToken
        )
        {
            DeckWorkspace workspace = new()
            {
                Id = ImportedDeck.Id,
                Name = ImportedDeck.Name,
                Format = ImportedDeck.Format,
                Description = ImportedDeck.Description,
                Mode = ImportedDeck.Mode,
                WriteBack = writeBack,
                ArchidektDeckId = ImportedDeck.ArchidektDeckId,
                ArchidektDeckFormatId = ImportedDeck.ArchidektDeckFormatId,
                Categories = ImportedDeck.Categories.ToList(),
                Cards = ImportedDeck.Cards.ToList(),
            };
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Ignores card persistence requests.
        /// </summary>
        public Task PersistCardsAsync(
            DeckWorkspace workspace,
            IReadOnlyList<DeckCard> upsertedCards,
            IReadOnlyList<DeckCard> removedCards,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ignores category persistence requests.
        /// </summary>
        public Task PersistCategoryAsync(
            DeckWorkspace workspace,
            DeckCategory category,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ignores category deletion requests.
        /// </summary>
        public Task DeleteCategoryAsync(
            DeckWorkspace workspace,
            DeckCategory category,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Ignores metadata persistence requests.
        /// </summary>
        public Task PersistMetadataAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a fake checkpoint response.
        /// </summary>
        public Task<DeckCheckpoint> CreateCheckpointAsync(
            DeckWorkspace workspace,
            string name,
            string? description,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new DeckCheckpoint
            {
                Id = "checkpoint",
                DeckId = workspace.ArchidektDeckId ?? "",
                Name = name,
                Description = description,
            });
        }

        /// <summary>
        /// Returns no fake checkpoints.
        /// </summary>
        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([]);
        }

        /// <summary>
        /// Returns a fake checkpoint by id.
        /// </summary>
        public Task<DeckCheckpoint> GetCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new DeckCheckpoint
            {
                Id = checkpointId,
                DeckId = workspace.ArchidektDeckId ?? "",
                Name = "Checkpoint",
            });
        }

        /// <summary>
        /// Renames a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> RenameCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            string name,
            string? description,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new DeckCheckpoint
            {
                Id = checkpointId,
                DeckId = workspace.ArchidektDeckId ?? "",
                Name = name,
                Description = description,
            });
        }

        /// <summary>
        /// Ignores checkpoint deletion requests.
        /// </summary>
        public Task DeleteCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }
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

}
