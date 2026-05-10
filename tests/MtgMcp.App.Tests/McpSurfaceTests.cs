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
        typeof(IntelligenceTools),
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
            "normalize_deck_cards",
            "summarize_deck_plan",
            "analyze_draw_odds",
            "analyze_deck_cost",
            "preview_deck_plan",
            "estimate_commander_bracket",
            "analyze_mana_base",
            "analyze_deck_consistency",
            "find_budget_replacements",
            "find_card_upgrades",
            "find_power_upgrades",
            "find_bracket_reduction_candidates",
            "find_power_reduction_candidates",
            "find_mana_base_improvements",
            "find_consistency_improvements",
            "suggest_deck_categories",
            "list_deck_plans",
            "get_deck_plan",
            "delete_deck_plan",
            "apply_deck_plan",
        ];

        ToolTypes
            .SelectMany(type => GetNamedAttributeValues(type, "McpServerToolAttribute", "Name"))
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>
    /// Verifies that power upgrade weights are true optional overrides.
    /// </summary>
    [Fact]
    public void FindPowerUpgrades_UsesNullableWeightOverrides()
    {
        MethodInfo method = typeof(IntelligenceTools).GetMethod(nameof(IntelligenceTools.FindPowerUpgradesAsync))
            ?? throw new InvalidOperationException("find_power_upgrades method not found.");

        ParameterInfo[] parameters = method.GetParameters();
        parameters.Single(parameter => parameter.Name == "roleWeight").ParameterType.Should().Be<double?>();
        parameters.Single(parameter => parameter.Name == "roleWeight").DefaultValue.Should().BeNull();
        parameters.Single(parameter => parameter.Name == "powerWeight").ParameterType.Should().Be<double?>();
        parameters.Single(parameter => parameter.Name == "powerWeight").DefaultValue.Should().BeNull();
        parameters.Single(parameter => parameter.Name == "priceWeight").ParameterType.Should().Be<double?>();
        parameters.Single(parameter => parameter.Name == "priceWeight").DefaultValue.Should().BeNull();
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
            "mtg://scryfall/syntax-cheatsheet",
            "mtg://formats/{format}/deck-rules",
            "mtg://usage/workspace-selection",
            "mtg://usage/operation-modes",
            "mtg://config/effective",
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
        CustomAttributeData previewPlan = GetToolAttribute(nameof(IntelligenceTools.PreviewDeckPlanAsync));
        CustomAttributeData bracket = GetToolAttribute(nameof(IntelligenceTools.EstimateCommanderBracketAsync));

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
    /// Verifies that configuration aliases map documented environment keys.
    /// </summary>
    [Fact]
    public void ConfigurationAliases_MapDocumentedEnvironmentKeys()
    {
        Dictionary<string, string?> rawConfig = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DATA_DIR"] = "C:/mtg-mcp",
            ["OPERATION_MODE"] = "plan",
            ["ARCHIDEKT:JWT"] = "jwt-token",
            ["ARCHIDEKT:REFRESH_TOKEN"] = "refresh-token",
            ["ARCHIDEKT:CREDENTIALS_FILE"] = "C:/creds.json",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(rawConfig)
            .Build();

        IReadOnlyDictionary<string, string?> aliases = MtgMcpConfigurationAliases.Create(
            configuration
        );

        aliases["MtgMcp:DataDir"].Should().Be("C:/mtg-mcp");
        aliases["MtgMcp:OperationMode"].Should().Be("plan");
        aliases["MtgMcp:Archidekt:Jwt"].Should().Be("jwt-token");
        aliases["MtgMcp:Archidekt:RefreshToken"].Should().Be("refresh-token");
        aliases["MtgMcp:Archidekt:CredentialsFile"].Should().Be("C:/creds.json");
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
        /// Saves a workspace in the fake repository.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
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
            return Task.FromResult<DeckWorkspace?>(null);
        }

        /// <summary>
        /// Verifies that list.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>([]);
        }
    }
}
