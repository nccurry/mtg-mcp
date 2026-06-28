using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using MtgMcp.App;
using MtgMcp.Core;

namespace MtgMcp.App.Tests;

/// <summary>
/// Contains tests for mcp surface.
/// </summary>
public sealed class McpSurfaceTests
{
    /// <summary>
    /// Maps single-byte IL opcodes for method-body inspection.
    /// </summary>
    private static readonly OpCode[] SingleByteOpCodes = CreateOpCodeLookup(multiByte: false);

    /// <summary>
    /// Maps two-byte IL opcodes for method-body inspection.
    /// </summary>
    private static readonly OpCode[] MultiByteOpCodes = CreateOpCodeLookup(multiByte: true);

    /// <summary>
    /// Serializes direct tool results with the same naming shape the MCP surface uses.
    /// </summary>
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies explicit null safety fields survive hosts that omit null object properties.
    /// </summary>
    private static readonly JsonSerializerOptions NullIgnoringWebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Lists all tool wrapper types that contribute to the MCP surface.
    /// </summary>
    private static readonly Type[] ToolTypes =
    [
        typeof(CardTools),
        typeof(WorkspaceTools),
        typeof(DeckMutationTools),
        typeof(CategoryTools),
        typeof(CheckpointTools),
        typeof(AnalysisTools),
        typeof(DeckReEvaluationTools),
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
            "archidekt_checkpoint_create",
            "archidekt_checkpoint_delete",
            "archidekt_checkpoint_get",
            "archidekt_checkpoint_list",
            "archidekt_checkpoint_rename",
            "archidekt_compare_goldfish",
            "archidekt_copy_workspace",
            "archidekt_create_deck",
            "archidekt_create_folder",
            "archidekt_list_decks",
            "archidekt_list_folders",
            "archidekt_move_decks",
            "card_facets_explain_match",
            "card_facets_get",
            "card_facets_set_annotations",
            "card_get",
            "card_get_prints",
            "card_get_rulings",
            "card_search",
            "card_classify_win_routes",
            "commander_get_aggregate_cards",
            "commander_get_tags",
            "commander_get_win_condition_evidence",
            "commander_search_candidates",
            "combo_get_details",
            "combo_search_by_card",
            "deck_add_card",
            "deck_add_card_category",
            "deck_add_cards_bulk",
            "deck_analyze_best_practices",
            "deck_analyze_combos",
            "deck_analyze_commander_trends",
            "deck_analyze_consistency",
            "deck_analyze_cost",
            "deck_analyze_draw_odds",
            "deck_analyze_land_drop_odds",
            "deck_analyze_mana",
            "deck_analyze_performance",
            "deck_analyze_structure",
            "deck_batch_tuning_report",
            "deck_compare_goldfish",
            "deck_compare_workspaces_analysis",
            "deck_create_category",
            "deck_delete_category",
            "deck_estimate_commander_bracket",
            "deck_estimate_win_turn",
            "deck_evaluate_card",
            "deck_explain_role_counts",
            "deck_facets_count",
            "deck_facets_get",
            "deck_find_exemplar_decks",
            "deck_find_lesser_known_cards",
            "deck_intent_clear",
            "deck_intent_get",
            "deck_intent_set",
            "deck_intent_suggest",
            "deck_list_cards_by_category",
            "deck_list_cards_by_zone",
            "deck_move_card",
            "deck_move_cards_bulk",
            "deck_plan_apply",
            "deck_plan_clone",
            "deck_plan_compare_performance",
            "deck_plan_create",
            "deck_plan_delete",
            "deck_plan_get",
            "deck_plan_list",
            "deck_plan_preview",
            "deck_preview_card_package",
            "deck_project_board_state",
            "deck_query_cards",
            "deck_re_evaluate",
            "deck_refresh_card_metadata",
            "deck_remove_card",
            "deck_remove_card_category",
            "deck_rename_category",
            "deck_review_weak_spots",
            "deck_review_new_card_swaps",
            "deck_score_cards_for_playgroup_meta",
            "deck_set_card_quantity",
            "deck_set_primary_card_category",
            "deck_update_card_categories_bulk",
            "deck_simulate_goldfish",
            "deck_summarize",
            "deck_update_metadata",
            "playgroup_get",
            "playgroup_get_auth_status",
            "playgroup_get_deck",
            "playgroup_list_observed_decks",
            "playgroup_list_observed_users",
            "playgroup_list_user_decks",
            "playgroup_rank_decks",
            "server_get_info",
            "source_explain_card_signal",
            "source_list",
            "source_search_evidence",
            "workspace_checkpoint_create",
            "workspace_checkpoint_delete",
            "workspace_checkpoint_get",
            "workspace_checkpoint_list",
            "workspace_checkpoint_restore",
            "workspace_export",
            "workspace_diff",
            "workspace_diff_last_import",
            "workspace_list",
            "workspace_open",
            "workspace_parse_decklist",
            "workspace_refresh_from_source",
            "workspace_reopen_with_writeback",
            "workspace_start",
            "workspace_validate",
            "workspace_validate_legality",
            "wincon_find_payoffs",
        ];

        ToolTypes
            .SelectMany(type => GetNamedAttributeValues(type, "McpServerToolAttribute", "Name"))
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>
    /// Verifies that the breaking cleanup exposes a coherent domain-prefixed tool surface.
    /// </summary>
    [Fact]
    public void ToolNames_UseDomainPrefixesAndRemoveLegacyNames()
    {
        string[] names = ToolTypes
            .SelectMany(type => GetNamedAttributeValues(type, "McpServerToolAttribute", "Name"))
            .ToArray();
        string[] approvedPrefixes =
        [
            "archidekt_",
            "card_",
            "commander_",
            "combo_",
            "deck_",
            "playgroup_",
            "server_",
            "source_",
            "wincon_",
            "workspace_",
        ];
        string[] legacyNames =
        [
            "search_cards",
            "get_card",
            "suggest_cards",
            "start_deck_workspace",
            "create_local_deck",
            "open_archidekt_deck",
            "open_moxfield_deck",
            "copy_workspace_to_archidekt",
            "list_corpus_sources",
            "search_corpus_evidence",
            "score_cards_for_playgroup_meta",
            "make_deck_do_goal_better",
            "deck_compare_commander_meta",
            "deck_estimate_combo_pressure",
            "deck_find_combos",
            "deck_find_near_miss_combos",
            "deck_find_new_cards",
        ];

        names.Should().OnlyContain(name =>
            approvedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
        names.Should().OnlyContain(name => !name.Contains("corpus", StringComparison.OrdinalIgnoreCase));
        names.Should().NotIntersectWith(legacyNames);
    }

    /// <summary>
    /// Verifies that resource templates cover planned surface.
    /// </summary>
    [Fact]
    public void ResourceTemplates_CoverPlannedSurface()
    {
        string[] expected =
        [
            "mtg://workspaces",
            "mtg://workspace/{workspaceId}",
            "mtg://workspace/{workspaceId}/summary",
            "mtg://workspace/{workspaceId}/intent",
            "mtg://workspace/{workspaceId}/state",
            "mtg://workspace/{workspaceId}/assistant-context",
            "mtg://scryfall/syntax-cheatsheet",
            "mtg://formats/{format}/deck-rules",
            "mtg://usage/workspace-selection",
            "mtg://usage/simulation-tool-selection",
            "mtg://usage/operation-modes",
            "mtg://usage/deck-intent",
            "mtg://config/effective",
            "mtg://server/info",
            "mtg://sources/status",
            "mtg://providers/{provider}/auth-status",
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
            "iterative_deck_review",
            "research_commander_common_cards",
            "research_commander_win_conditions",
            "reduce_deck_cost",
            "upgrade_deck_power",
            "reduce_deck_power",
            "lower_commander_bracket",
            "optimize_mana_base",
            "improve_deck_consistency",
            "tune_for_local_meta",
            "review_new_card_swaps",
            "check_land_drop_risk",
            "find_missing_combo_pieces",
            "goldfish_deck",
            "improve_deck_for_goal",
            "rules_and_rulings_check",
        ];

        GetNamedAttributeValues(typeof(MtgPrompts), "McpServerPromptAttribute", "Name")
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>
    /// Verifies that built-in prompt bodies do not reference removed public tools.
    /// </summary>
    [Fact]
    public void PromptBodies_DoNotReferenceRemovedToolNames()
    {
        MtgPrompts prompts = new();
        string[] bodies =
        [
            prompts.BrewCommanderDeck("Tinybones"),
            prompts.TuneExistingDeck("workspace-1"),
            prompts.IterativeDeckReview("workspace-1", "workspace-0"),
            prompts.ResearchCommanderCommonCards("Tinybones"),
            prompts.ResearchCommanderWinConditions("Tinybones"),
            prompts.ReduceDeckCost("workspace-1"),
            prompts.UpgradeDeckPower("workspace-1"),
            prompts.ReduceDeckPower("workspace-1"),
            prompts.LowerCommanderBracket("workspace-1"),
            prompts.OptimizeManaBase("workspace-1"),
            prompts.ImproveDeckConsistency("workspace-1"),
            prompts.TuneForLocalMeta("workspace-1", "tokens"),
            prompts.ReviewNewCardSwaps("workspace-1"),
            prompts.CheckLandDropRisk("workspace-1"),
            prompts.FindMissingComboPieces("workspace-1"),
            prompts.GoldfishDeck("workspace-1"),
            prompts.MakeDeckDoGoalBetter("workspace-1", "draw more cards"),
            prompts.RulesAndRulingsCheck("Sol Ring", "Can I tap it immediately?")
        ];
        string[] removedToolNames =
        [
            "deck_compare_commander_meta",
            "deck_estimate_combo_pressure",
            "deck_find_combos",
            "deck_find_near_miss_combos",
            "deck_find_new_cards",
            "research_budget_replacements",
            "review_new_releases_for_deck"
        ];

        bodies.Should().OnlyContain(body =>
            !removedToolNames.Any(tool => body.Contains(tool, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Verifies that important magic-string parameters include public schema guidance.
    /// </summary>
    [Fact]
    public void PublicParameters_DescribeImportantMagicStringValues()
    {
        GetParameterDescription(typeof(WorkspaceTools), nameof(WorkspaceTools.StartDeckWorkspaceAsync), "mode")
            .Should()
            .Contain("local")
            .And.Contain("archidekt")
            .And.Contain("moxfield");
        GetParameterDescription(typeof(WorkspaceTools), nameof(WorkspaceTools.StartDeckWorkspaceAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(WorkspaceTools), nameof(WorkspaceTools.ExportDeckAsync), "format")
            .Should()
            .Contain("text")
            .And.Contain("markdown")
            .And.Contain("markdown-links");
        GetParameterDescription(typeof(WorkspaceTools), nameof(WorkspaceTools.DiffWorkspacesAsync), "previousWorkspaceId")
            .Should()
            .Contain("Explicit baseline");
        GetParameterDescription(typeof(WorkspaceTools), nameof(WorkspaceTools.ListCardsByZoneAsync), "zone")
            .Should()
            .Contain("active")
            .And.Contain("sideboard")
            .And.Contain("maybeboard")
            .And.Contain("excluded")
            .And.Contain("all");
        GetParameterDescription(typeof(AnalysisTools), nameof(AnalysisTools.RefreshDeckCardSnapshotsAsync), "scope")
            .Should()
            .Contain("included")
            .And.Contain("maybeboard")
            .And.Contain("missing");
        GetParameterDescription(typeof(AnalysisTools), nameof(AnalysisTools.RefreshDeckCardSnapshotsAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(DeckReEvaluationTools), nameof(DeckReEvaluationTools.ReEvaluateDeckAsync), "analysisProfile")
            .Should()
            .Contain("auto");
        GetParameterDescription(typeof(DeckReEvaluationTools), nameof(DeckReEvaluationTools.ReEvaluateDeckAsync), "limit")
            .Should()
            .Contain("clamped");
        GetParameterDescription(typeof(FacetTools), nameof(FacetTools.GetCardFacetsAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(CorpusTools), nameof(CorpusTools.SearchCorpusEvidenceAsync), "sourceKey")
            .Should()
            .Contain("topdeck")
            .And.Contain("edhtop16");
        GetParameterDescription(typeof(CorpusTools), nameof(CorpusTools.SearchCorpusEvidenceAsync), "analysisDepth")
            .Should()
            .Contain("minimal")
            .And.Contain("balanced")
            .And.Contain("best");
        GetParameterDescription(typeof(SimulationTools), nameof(SimulationTools.AnalyzeDeckPerformanceAsync), "simulationProfile")
            .Should()
            .Contain("auto")
            .And.Contain("neutral")
            .And.Contain("stax");
        GetParameterDescription(typeof(SimulationTools), nameof(SimulationTools.AnalyzeDeckPerformanceAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(SimulationTools), nameof(SimulationTools.ComparePlanPerformanceAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(SimulationTools), nameof(SimulationTools.CompareGoldfishAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(SimulationTools), nameof(SimulationTools.CompareGoldfishAsync), "simulationProfile")
            .Should()
            .Contain("auto")
            .And.Contain("neutral")
            .And.Contain("stax");
        GetParameterDescription(typeof(SimulationTools), nameof(SimulationTools.CompareGoldfishAsync), "model")
            .Should()
            .Contain("optimistic-goldfish-model")
            .And.Contain("rules-backed-goldfish-race-v1");
        GetParameterDescription(typeof(RecommendationTools), nameof(RecommendationTools.BuildBatchTuningReportAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(RecommendationTools), nameof(RecommendationTools.BuildBatchTuningReportAsync), "simulationProfile")
            .Should()
            .Contain("auto")
            .And.Contain("neutral")
            .And.Contain("stax");
        GetParameterDescription(typeof(PlanTools), nameof(PlanTools.PreviewDeckPlanAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(PlanTools), nameof(PlanTools.PreviewCardPackageAsync), "detailLevel")
            .Should()
            .Contain("summary")
            .And.Contain("normal")
            .And.Contain("full");
        GetParameterDescription(typeof(PlanTools), nameof(PlanTools.PreviewCardPackageAsync), "sourceSupportDepth")
            .Should()
            .Contain("none")
            .And.Contain("minimal")
            .And.Contain("balanced");
        GetParameterDescription(typeof(PlanTools), nameof(PlanTools.PreviewCardPackageAsync), "analysisMode")
            .Should()
            .Contain("none")
            .And.Contain("summary")
            .And.Contain("full");
        GetParameterDescription(typeof(PlanTools), nameof(PlanTools.PreviewCardPackageAsync), "simulationProfile")
            .Should()
            .Contain("auto")
            .And.Contain("neutral")
            .And.Contain("stax");
        GetParameterDescription(typeof(PlaygroupTools), nameof(PlaygroupTools.RankPlaygroupDecksAsync), "metric")
            .Should()
            .Contain("estimated_power")
            .And.Contain("average_win_turn");
        GetParameterDescription(typeof(RecommendationTools), nameof(RecommendationTools.ScoreCardsForPlaygroupMetaAsync), "candidateSource")
            .Should()
            .Contain("explicit-cards")
            .And.Contain("excluded-workspace-cards");
        GetParameterDescription(typeof(AnalysisTools), nameof(AnalysisTools.ClassifyWinRoutesAsync), "producedFeatures")
            .Should()
            .Contain("combat")
            .And.Contain("infinite-mana")
            .And.Contain("draw-deck");
        GetParameterDescription(typeof(RecommendationTools), nameof(RecommendationTools.FindWinconPayoffsAsync), "route")
            .Should()
            .Contain("combat")
            .And.Contain("infinite-mana")
            .And.Contain("draw-deck");
        GetParameterDescription(typeof(MtgResources), nameof(MtgResources.GetProviderAuthStatusAsync), "provider")
            .Should()
            .Contain("archidekt")
            .And.Contain("playgroup")
            .And.NotContain("reddit");
    }

    /// <summary>
    /// Verifies that performance tool descriptions explain scorecards and replay metadata.
    /// </summary>
    [Fact]
    public void PerformanceToolDescriptions_DescribeScorecardsAsMetricEvidence()
    {
        string analysisDescription = GetMethodDescription(
            typeof(SimulationTools),
            nameof(SimulationTools.AnalyzeDeckPerformanceAsync));
        string comparisonDescription = GetMethodDescription(
            typeof(SimulationTools),
            nameof(SimulationTools.ComparePlanPerformanceAsync));

        analysisDescription.Should()
            .Contain("modelVersion")
            .And.Contain("fingerprints")
            .And.Contain("scorecard")
            .And.Contain("not a power ranking")
            .And.Contain("traceSummary");
        comparisonDescription.Should()
            .Contain("modelVersion")
            .And.Contain("fingerprints")
            .And.Contain("scorecard")
            .And.Contain("not a universal deck power score")
            .And.Contain("traceSummary");
    }

    /// <summary>
    /// Verifies card evaluation compact output does not include full operational evidence.
    /// </summary>
    [Fact]
    public void CardEvaluationCompactOutput_OmitsFullFactsAndEvidence()
    {
        RampContextEvaluation evaluation = new()
        {
            WorkspaceId = "workspace",
            CardName = "Wayfarer's Bauble",
            Role = DeckRoles.Ramp,
            EvaluatedRole = CardEvaluationRoles.Ramp,
            DetectedRoles = [CardEvaluationRoles.Ramp],
            RampKind = "activatedLandRamp",
            Score = 54,
            TopIssues = ["requires 2 future activation mana"],
            TopStrengths = ["supports deck color requirements"],
            Facts = new CardOperationalFacts
            {
                CardName = "Wayfarer's Bauble",
                Role = DeckRoles.Ramp,
                Evidence =
                [
                    new CardFactEvidence
                    {
                        Source = "oracle-parser",
                        Kind = "parserDerived",
                        Label = "activated-land-ramp",
                        Detail = "Matched activated ramp text.",
                    }
                ],
            },
            CandidateEvaluations =
            [
                new RampContextEvaluation
                {
                    CardName = "Nature's Lore",
                    Role = DeckRoles.Ramp,
                    EvaluatedRole = CardEvaluationRoles.Ramp,
                    RampKind = "spellLandRampUntapped",
                    Score = 90,
                    Facts = new CardOperationalFacts
                    {
                        Evidence = [new CardFactEvidence { Source = "oracle-parser", Kind = "parserDerived" }]
                    },
                }
            ],
        };
        MethodInfo method = typeof(RecommendationTools)
            .GetMethod("ToCompactEvaluation", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing compact card-evaluation presenter.");

        JsonElement compact = JsonSerializer.SerializeToElement(method.Invoke(null, [evaluation]));

        compact.GetProperty("Evaluator").GetString().Should().Be("card-operational");
        compact.GetProperty("Applicable").GetBoolean().Should().BeTrue();
        compact.GetProperty("EvaluationStatus").GetString().Should().Be("evaluated");
        compact.GetProperty("EvaluatedRole").GetString().Should().Be(CardEvaluationRoles.Ramp);
        compact.GetProperty("EvaluatedRoles").GetArrayLength().Should().Be(3);
        compact.GetProperty("DetectedRoles").GetArrayLength().Should().Be(1);
        compact.GetProperty("UnsupportedRole").GetBoolean().Should().BeFalse();
        compact.TryGetProperty("TopCandidates", out JsonElement topCandidates).Should().BeTrue();
        topCandidates.GetArrayLength().Should().Be(1);
        topCandidates[0].GetProperty("EvaluatedRole").GetString().Should().Be(CardEvaluationRoles.Ramp);
        compact.TryGetProperty("Facts", out _).Should().BeFalse();
        compact.TryGetProperty("SubScores", out _).Should().BeFalse();
        compact.TryGetProperty("CandidateEvaluations", out _).Should().BeFalse();
        topCandidates[0].TryGetProperty("Facts", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that workspace resources expose workspaceId rather than legacy deckId.
    /// </summary>
    [Fact]
    public void WorkspaceResources_UseWorkspaceIdParameter()
    {
        MethodInfo[] workspaceResourceMethods = typeof(MtgResources)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.CustomAttributes.Any(attribute =>
                attribute.AttributeType.Name == "McpServerResourceAttribute"
                && GetNamedString(attribute, "UriTemplate")?.StartsWith("mtg://workspace/", StringComparison.Ordinal) == true))
            .ToArray();

        workspaceResourceMethods.Should().NotBeEmpty();
        workspaceResourceMethods.Should().OnlyContain(method =>
            method.GetParameters().Any(parameter => parameter.Name == "workspaceId"));
        workspaceResourceMethods.Should().OnlyContain(method =>
            method.GetParameters().All(parameter => parameter.Name != "deckId"));
    }

    /// <summary>
    /// Verifies that public tool schemas use normalized parameter names from the API cleanup.
    /// </summary>
    [Fact]
    public void ToolParameters_UseNormalizedPublicNames()
    {
        string[] legacyParameterNames =
        [
            "archidektDeckUrl1",
            "archidektDeckUrl2",
            "archidektDeckUrl3",
            "count",
            "deckId",
            "nameOrId",
            "profile",
            "refresh",
        ];

        string[] publicParameterNames = ToolTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Where(method => TryGetToolAttribute(method) is not null)
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Select(parameter => parameter.Name ?? "")
            .ToArray();

        publicParameterNames.Should().NotIntersectWith(legacyParameterNames);
    }

    /// <summary>
    /// Verifies that secret redactor redacts known secret keys.
    /// </summary>
    [Fact]
    public void SecretRedactor_RedactsKnownSecretKeys()
    {
        Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MtgMcp:Archidekt:Password"] = "secret",
            ["MtgMcp:Playgroup:ApiKey"] = "playgroup-secret",
            ["MtgMcp:DataDir"] = "C:/data",
        };

        Dictionary<string, object?> redacted = SecretRedactor.Redact(values);

        redacted["MtgMcp:Archidekt:Password"].Should().Be("***REDACTED***");
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
        info.AssemblyPath.Should().NotBeNullOrWhiteSpace();
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
        CustomAttributeData bulkMoveCards = GetToolAttribute(nameof(DeckMutationTools.MoveCardsBulkAsync));
        CustomAttributeData removeCard = GetToolAttribute(
            nameof(DeckMutationTools.RemoveCardAsync)
        );
        CustomAttributeData openLocal = GetToolAttribute(nameof(WorkspaceTools.OpenLocalDeckAsync));
        CustomAttributeData listCardsByZone = GetToolAttribute(nameof(WorkspaceTools.ListCardsByZoneAsync));
        CustomAttributeData previewPlan = GetToolAttribute(nameof(PlanTools.PreviewDeckPlanAsync));
        CustomAttributeData previewPackage = GetToolAttribute(nameof(PlanTools.PreviewCardPackageAsync));
        CustomAttributeData bracket = GetToolAttribute(nameof(AnalysisTools.EstimateCommanderBracketAsync));
        CustomAttributeData roleCounts = GetToolAttribute(nameof(AnalysisTools.ExplainRoleCountsAsync));
        CustomAttributeData weakSpots = GetToolAttribute(nameof(AnalysisTools.ReviewWeakSpotsAsync));
        CustomAttributeData reEvaluate = GetToolAttribute(nameof(DeckReEvaluationTools.ReEvaluateDeckAsync));
        CustomAttributeData workspaceDiff = GetToolAttribute(nameof(WorkspaceTools.DiffWorkspacesAsync));
        CustomAttributeData lastImportDiff = GetToolAttribute(nameof(WorkspaceTools.DiffLastImportAsync));
        CustomAttributeData reopenWriteback = GetToolAttribute(nameof(WorkspaceTools.ReopenWorkspaceWithWritebackAsync));
        CustomAttributeData performance = GetToolAttribute(nameof(SimulationTools.AnalyzeDeckPerformanceAsync));
        CustomAttributeData comparePerformance = GetToolAttribute(nameof(SimulationTools.ComparePlanPerformanceAsync));
        CustomAttributeData compareGoldfish = GetToolAttribute(nameof(SimulationTools.CompareArchidektGoldfishAsync));
        CustomAttributeData deckCompareGoldfish = GetToolAttribute(nameof(SimulationTools.CompareGoldfishAsync));
        CustomAttributeData commanderCandidates = GetToolAttribute(nameof(RecommendationTools.SearchCommanderCandidatesAsync));
        CustomAttributeData batchTuning = GetToolAttribute(nameof(RecommendationTools.BuildBatchTuningReportAsync));
        CustomAttributeData queryCards = GetToolAttribute(nameof(RecommendationTools.QueryCardsForDeckAsync));
        CustomAttributeData scoreMeta = GetToolAttribute(nameof(RecommendationTools.ScoreCardsForPlaygroupMetaAsync));
        CustomAttributeData createExplicitPlan = GetToolAttribute(nameof(PlanTools.CreateDeckPlanFromExplicitChangesAsync));
        CustomAttributeData clonePlan = GetToolAttribute(nameof(PlanTools.CloneDeckPlanAsync));
        CustomAttributeData listPlaygroupDecks = GetToolAttribute(nameof(PlaygroupTools.ListPlaygroupDecksAsync));

        GetNamedBool(searchCards, "ReadOnly").Should().BeTrue();
        GetNamedBool(searchCards, "OpenWorld").Should().BeTrue();
        GetNamedBool(addCard, "ReadOnly").Should().BeFalse();
        GetNamedBool(addCard, "Destructive").Should().BeFalse();
        GetNamedBool(bulkMoveCards, "ReadOnly").Should().BeFalse();
        GetNamedBool(bulkMoveCards, "Destructive").Should().BeTrue();
        GetNamedBool(removeCard, "ReadOnly").Should().BeFalse();
        GetNamedBool(removeCard, "Destructive").Should().BeTrue();
        GetNamedBool(openLocal, "ReadOnly").Should().BeTrue();
        GetNamedBool(openLocal, "OpenWorld").Should().BeFalse();
        GetNamedBool(listCardsByZone, "ReadOnly").Should().BeTrue();
        GetNamedBool(listCardsByZone, "OpenWorld").Should().BeFalse();
        GetNamedBool(previewPlan, "ReadOnly").Should().BeTrue();
        GetNamedBool(previewPlan, "OpenWorld").Should().BeTrue();
        GetNamedBool(previewPackage, "ReadOnly").Should().BeTrue();
        GetNamedBool(previewPackage, "OpenWorld").Should().BeTrue();
        GetNamedBool(bracket, "ReadOnly").Should().BeTrue();
        GetNamedBool(bracket, "OpenWorld").Should().BeTrue();
        GetNamedBool(roleCounts, "ReadOnly").Should().BeTrue();
        GetNamedBool(roleCounts, "OpenWorld").Should().BeFalse();
        GetNamedBool(weakSpots, "ReadOnly").Should().BeTrue();
        GetNamedBool(weakSpots, "OpenWorld").Should().BeFalse();
        GetNamedBool(reEvaluate, "ReadOnly").Should().BeTrue();
        GetNamedBool(reEvaluate, "OpenWorld").Should().BeFalse();
        GetNamedBool(workspaceDiff, "ReadOnly").Should().BeTrue();
        GetNamedBool(workspaceDiff, "OpenWorld").Should().BeFalse();
        GetNamedBool(lastImportDiff, "ReadOnly").Should().BeTrue();
        GetNamedBool(lastImportDiff, "OpenWorld").Should().BeFalse();
        GetNamedBool(reopenWriteback, "ReadOnly").Should().BeFalse();
        GetNamedBool(reopenWriteback, "OpenWorld").Should().BeTrue();
        GetNamedBool(performance, "ReadOnly").Should().BeTrue();
        GetNamedBool(performance, "OpenWorld").Should().BeFalse();
        GetNamedBool(comparePerformance, "ReadOnly").Should().BeTrue();
        GetNamedBool(comparePerformance, "OpenWorld").Should().BeTrue();
        GetNamedBool(compareGoldfish, "ReadOnly").Should().BeTrue();
        GetNamedBool(compareGoldfish, "OpenWorld").Should().BeTrue();
        GetNamedBool(deckCompareGoldfish, "ReadOnly").Should().BeTrue();
        GetNamedBool(deckCompareGoldfish, "OpenWorld").Should().BeTrue();
        GetNamedBool(commanderCandidates, "ReadOnly").Should().BeTrue();
        GetNamedBool(commanderCandidates, "OpenWorld").Should().BeTrue();
        GetNamedBool(batchTuning, "ReadOnly").Should().BeTrue();
        GetNamedBool(batchTuning, "OpenWorld").Should().BeTrue();
        GetNamedBool(queryCards, "ReadOnly").Should().BeTrue();
        GetNamedBool(queryCards, "OpenWorld").Should().BeTrue();
        GetNamedBool(scoreMeta, "ReadOnly").Should().BeTrue();
        GetNamedBool(scoreMeta, "OpenWorld").Should().BeTrue();
        GetNamedBool(createExplicitPlan, "ReadOnly").Should().BeFalse();
        GetNamedBool(createExplicitPlan, "OpenWorld").Should().BeFalse();
        GetNamedBool(clonePlan, "ReadOnly").Should().BeFalse();
        GetNamedBool(clonePlan, "OpenWorld").Should().BeFalse();
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

        planMode.Invoking(guard => guard.EnsureCanWritePlanningState("deck_plan_create"))
            .Should()
            .NotThrow();
        readOnlyMode.Invoking(guard => guard.EnsureCanWritePlanningState("deck_plan_create"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*read-only mode*deck_plan_create*");
    }

    /// <summary>
    /// Verifies that all public detail-level spellings route through one parser.
    /// </summary>
    [Fact]
    public void DetailLevelParser_NormalizesSharedVocabulary()
    {
        DetailLevelParser.Parse(null).Should().Be(DetailLevel.Summary);
        DetailLevelParser.Normalize(" NORMAL ").Should().Be(DetailLevelParser.Normal);
        DetailLevelParser.Normalize(null, DetailLevel.Full).Should().Be(DetailLevelParser.Full);
        DetailLevelParser.Normalize("compact", allowCompactAlias: true).Should().Be(DetailLevelParser.Summary);

        Action act = () => DetailLevelParser.Parse("verbose");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*summary, normal, or full*");
    }

    /// <summary>
    /// Verifies that presenters do not reintroduce local detail-level normalizers.
    /// </summary>
    [Fact]
    public void DetailLevelPresenters_UseSharedParser()
    {
        string[] presenterTypeNames =
        [
            "CompactMutationPresenter",
            "PlanPreviewPresenter",
            "GoldfishOutputPresenter",
            "PerformanceOutputPresenter",
            "DeckNormalizationPresenter",
            "CardFacetOutputPresenter",
        ];
        List<string> localHelpers = [];
        Assembly assembly = typeof(MtgMcpHost).Assembly;
        foreach (string typeName in presenterTypeNames)
        {
            Type type = assembly.GetType($"MtgMcp.App.{typeName}")
                ?? throw new InvalidOperationException($"{typeName} was not found.");
            if (type.GetMethod(
                    "NormalizeDetailLevel",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is not null)
            {
                localHelpers.Add($"{typeName}.NormalizeDetailLevel");
            }

            foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nestedType.Name.Contains("DetailLevels", StringComparison.Ordinal))
                {
                    localHelpers.Add($"{typeName}.{nestedType.Name}");
                }
            }
        }

        localHelpers.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies MCP detail-level parameters use the shared public vocabulary.
    /// </summary>
    [Fact]
    public void DetailLevelParameters_UseSharedVocabulary()
    {
        List<string> invalidParameters = [];
        foreach (ToolRegistryEntry entry in ToolRegistry.Entries)
        {
            ParameterInfo? parameter = entry.Method
                .GetParameters()
                .FirstOrDefault(parameter => parameter.Name == "detailLevel");
            if (parameter is null)
            {
                continue;
            }

            if (parameter.ParameterType != typeof(string))
            {
                invalidParameters.Add($"{entry.Name}: detailLevel must be string.");
                continue;
            }

            string? defaultValue = parameter.DefaultValue as string;
            try
            {
                DetailLevelParser.Parse(defaultValue, allowCompactAlias: true);
            }
            catch (ArgumentException exception)
            {
                invalidParameters.Add($"{entry.Name}: {exception.Message}");
            }
        }

        invalidParameters.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies registry-created tools have client-facing titles and schemas for typed results.
    /// </summary>
    [Fact]
    public void ToolRegistry_CreatesTitlesAndStructuredSchemasForTypedTools()
    {
        IReadOnlyList<McpServerTool> tools = ToolRegistry.CreateTools(new MtgMcpOptions
        {
            OperationMode = OperationModeGuard.Apply
        });
        Dictionary<string, McpServerTool> byName = tools.ToDictionary(
            tool => tool.ProtocolTool.Name,
            StringComparer.Ordinal);

        tools.Should().OnlyContain(tool => !string.IsNullOrWhiteSpace(tool.ProtocolTool.Title));
        byName["server_get_info"].ProtocolTool.Title.Should().Be("Server Get Info");
        byName["server_get_info"].ProtocolTool.OutputSchema.Should().NotBeNull();
        byName["workspace_list"].ProtocolTool.OutputSchema.Should().NotBeNull();
        byName["deck_plan_list"].ProtocolTool.OutputSchema.Should().NotBeNull();
        byName["workspace_start"].ProtocolTool.OutputSchema.Should().BeNull();
        byName["workspace_checkpoint_delete"].ProtocolTool.OutputSchema.Should().BeNull();
    }

    /// <summary>
    /// Verifies the shared cursor envelope pages list-style tool output.
    /// </summary>
    [Fact]
    public void ToolPagination_PagesWithOpaqueCursor()
    {
        PagedToolResult<int> first = ToolPagination.Page([1, 2, 3], limit: 2, cursor: null);
        PagedToolResult<int> second = ToolPagination.Page([1, 2, 3], limit: 2, first.NextCursor);

        first.Items.Should().Equal([1, 2]);
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        first.Limit.Should().Be(2);
        first.TotalCount.Should().Be(3);
        second.Items.Should().Equal([3]);
        second.NextCursor.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the method-level tool registry covers every attributed tool.
    /// </summary>
    [Fact]
    public void ToolRegistry_CoversEveryRegisteredTool()
    {
        string[] attributedToolNames = ToolTypes
            .SelectMany(type => GetNamedAttributeValues(type, "McpServerToolAttribute", "Name"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] registryToolNames = ToolRegistry.Entries
            .Select(entry => entry.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        registryToolNames.Should().BeEquivalentTo(attributedToolNames);
    }

    /// <summary>
    /// Verifies that operation mode affects advertised tools before invocation.
    /// </summary>
    [Fact]
    public void ToolRegistry_FiltersToolsByOperationMode()
    {
        IReadOnlyList<ToolRegistryEntry> readOnly = ToolRegistry.SelectEntries(
            new MtgMcpOptions { OperationMode = OperationModeGuard.ReadOnly });
        IReadOnlyList<ToolRegistryEntry> plan = ToolRegistry.SelectEntries(
            new MtgMcpOptions { OperationMode = OperationModeGuard.Plan });
        IReadOnlyList<ToolRegistryEntry> apply = ToolRegistry.SelectEntries(
            new MtgMcpOptions { OperationMode = OperationModeGuard.Apply });

        readOnly.Should().OnlyContain(entry => entry.Capability == ToolCapability.Read);
        plan.Should().OnlyContain(entry =>
            entry.Capability == ToolCapability.Read || entry.Capability == ToolCapability.Plan);
        apply.Should().HaveSameCount(ToolRegistry.Entries);
        readOnly.Count.Should().BeLessThan(plan.Count);
        plan.Count.Should().BeLessThan(apply.Count);
    }

    /// <summary>
    /// Verifies that toolset selection intersects with operation mode.
    /// </summary>
    [Fact]
    public void ToolRegistry_FiltersToolsByToolsetAndOperationMode()
    {
        string[] names = ToolRegistry
            .SelectEntries(new MtgMcpOptions
            {
                OperationMode = OperationModeGuard.Apply,
                Toolsets = "cards, server"
            })
            .Select(entry => entry.Name)
            .ToArray();
        string[] readOnlyEditingNames = ToolRegistry
            .SelectEntries(new MtgMcpOptions
            {
                OperationMode = OperationModeGuard.ReadOnly,
                Toolsets = "editing"
            })
            .Select(entry => entry.Name)
            .ToArray();

        names.Should().Contain(["card_search", "server_get_info"]);
        names.Should().NotContain("workspace_start");
        readOnlyEditingNames.Should().Contain("deck_list_cards_by_category");
        readOnlyEditingNames.Should().NotContain("deck_add_card");
    }

    /// <summary>
    /// Verifies that non-read registry entries are not advertised as read-only MCP tools.
    /// </summary>
    [Fact]
    public void ToolRegistry_CapabilitiesMatchMcpReadOnlyAnnotations()
    {
        ToolRegistry.Entries
            .Where(entry => entry.Capability != ToolCapability.Read)
            .Should()
            .OnlyContain(entry => !entry.ReadOnly);
    }

    /// <summary>
    /// Verifies that registry capability tags stay aligned with call-time operation guards.
    /// </summary>
    [Fact]
    public void ToolRegistry_CapabilitiesMatchOperationModeGuardCalls()
    {
        ToolRegistry.Entries
            .Where(entry => entry.Capability != ToolCapability.Read)
            .Should()
            .OnlyContain(entry => CallsOperationModeGuard(entry.Method));
        ToolRegistry.Entries
            .Where(entry => CallsOperationModeGuard(entry.Method))
            .Should()
            .OnlyContain(entry => entry.Capability != ToolCapability.Read);
    }

    /// <summary>
    /// Verifies that every mutating tool wrapper calls the operation mode guard before doing work.
    /// </summary>
    [Fact]
    public void MutatingTools_CallOperationModeGuard()
    {
        List<string> unguardedTools = [];
        foreach (MethodInfo method in ToolTypes.SelectMany(type =>
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public)))
        {
            CustomAttributeData? toolAttribute = TryGetToolAttribute(method);
            if (toolAttribute is null || GetNamedBool(toolAttribute, "ReadOnly") != false)
            {
                continue;
            }

            if (!CallsOperationModeGuard(method))
            {
                string toolName = GetNamedString(toolAttribute, "Name") ?? method.Name;
                unguardedTools.Add($"{method.DeclaringType?.Name}.{method.Name} ({toolName})");
            }
        }

        unguardedTools.Should().BeEmpty(
            "mutating tools must fail fast when mtg-mcp is in read-only or plan mode");
    }

    /// <summary>
    /// Verifies that operation mode guard blocks an invoked mutating tool when read only.
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
            tools.StartDeckWorkspaceAsync(
                mode: "local",
                name: "Blocked",
                cancellationToken: TestContext.Current.CancellationToken
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only mode*workspace_start*");
    }

    /// <summary>
    /// Verifies that the bulk move tool is guarded in read-only mode.
    /// </summary>
    [Fact]
    public async Task OperationModeGuard_BlocksBulkMoveWhenReadOnly()
    {
        DeckWorkspaceService deckService = new(new InMemoryRepository(), new EmptyCardCatalog());
        DeckMutationTools tools = new(
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = "read-only" })));

        Func<Task> act = () => tools.MoveCardsBulkAsync(
            "workspace",
            [new BulkDeckCardMove { CardName = "Sol Ring", ToCategory = DeckDefaults.Maybeboard }],
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only mode*deck_move_cards_bulk*");
    }

    /// <summary>
    /// Verifies that workspace_start defaults to compact output and keeps full raw workspace output opt-in.
    /// </summary>
    [Fact]
    public async Task WorkspaceStart_DefaultsToCompactSummaryAndFullEscapeHatch()
    {
        DeckWorkspaceService deckService = new(new InMemoryRepository(), new EmptyCardCatalog());
        WorkspaceTools tools = new(
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        JsonElement summary = JsonSerializer.SerializeToElement(await tools.StartDeckWorkspaceAsync(
            mode: "local",
            name: "Compact Start",
            decklist: "1 Sol Ring",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);
        JsonElement normal = JsonSerializer.SerializeToElement(await tools.StartDeckWorkspaceAsync(
            mode: "local",
            name: "Normal Start",
            decklist: "1 Sol Ring",
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);
        JsonElement full = JsonSerializer.SerializeToElement(await tools.StartDeckWorkspaceAsync(
            mode: "local",
            name: "Full Start",
            decklist: "1 Sol Ring",
            detailLevel: "full",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        summary.GetProperty("detailLevel").GetString().Should().Be("summary");
        summary.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
        summary.GetProperty("workspaceId").GetString().Should().Be(summary.GetProperty("id").GetString());
        summary.GetProperty("totalCards").GetInt32().Should().Be(1);
        summary.GetProperty("cards").GetArrayLength().Should().Be(0);
        normal.GetProperty("detailLevel").GetString().Should().Be("normal");
        normal.GetProperty("cards").EnumerateArray()
            .Should()
            .Contain(card => card.GetProperty("cardName").GetString() == "Sol Ring");
        normal.GetProperty("cards").EnumerateArray().First().TryGetProperty("snapshot", out _).Should().BeFalse();
        full.GetProperty("name").GetString().Should().Be("Full Start");
        full.GetProperty("cards").EnumerateArray()
            .Should()
            .Contain(card => card.GetProperty("name").GetString() == "Sol Ring");
    }

    /// <summary>
    /// Verifies that intent mutation tools default to compact output and keep full output opt-in.
    /// </summary>
    [Fact]
    public async Task IntentMutationTools_DefaultCompactAndFullEscapeHatch()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Intent Compact",
            Description = "Primer text."
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService deckService = new(repository, new EmptyCardCatalog());
        IntentTools tools = new(
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        object compact = await tools.SetDeckIntentAsync(
            workspace.Id,
            """
            Commander: Kenessos, Priest of Thassa
            Archetype: sea monsters
            Heuristic Profile: sea monsters
            """,
            cancellationToken: TestContext.Current.CancellationToken);
        CompactDeckIntentChangeResult compactResult = compact
            .Should()
            .BeOfType<CompactDeckIntentChangeResult>()
            .Subject;

        compactResult.WorkspaceId.Should().Be(workspace.Id);
        compactResult.Changed.Should().BeTrue();
        compactResult.DescriptionUpdated.Should().BeTrue();
        compactResult.IntentSummary.Commander.Should().Be("Kenessos, Priest of Thassa");
        compactResult.IntentSummary.HeuristicProfile.Should().Be("archetype-sea-monsters");

        object full = await tools.ClearDeckIntentAsync(
            workspace.Id,
            includeWorkspace: true,
            cancellationToken: TestContext.Current.CancellationToken);

        full.Should().BeOfType<DeckIntentChangeResult>()
            .Which.Workspace.Description.Should().Be("Primer text.");
    }

    /// <summary>
    /// Verifies that Archidekt copy infers existing-deck mode when a destination is supplied.
    /// </summary>
    [Fact]
    public async Task ArchidektCopyWorkspace_InfersExistingDestinationMode()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Copy Source"
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService deckService = new(
            repository,
            new EmptyCardCatalog(),
            new FakeArchidektGateway
            {
                ImportedDeck = new DeckWorkspace
                {
                    Name = "Existing",
                    Mode = WorkspaceMode.Archidekt,
                    WriteBack = true,
                    ArchidektDeckId = "123",
                }
            });
        WorkspaceTools tools = new(
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        ArchidektCopyResult result = await tools.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: true,
            destinationDeckIdOrUrl: "123",
            cancellationToken: TestContext.Current.CancellationToken);

        result.CreatedNewDeck.Should().BeFalse();
        result.DestinationArchidektDeckId.Should().Be("123");
        result.CopyPhase.Should().Be("dry-run");
    }

    /// <summary>
    /// Verifies that metadata refresh defaults to bounded output and keeps full workspace output opt-in.
    /// </summary>
    [Fact]
    public async Task RefreshCardMetadata_DefaultSummaryAndFullEscapeHatch()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = new()
        {
            Name = "Refresh Compact"
        };
        for (int index = 0; index < 100; index++)
        {
            workspace.Cards.Add(new DeckCard
            {
                Name = $"Missing Card {index}",
                Quantity = 1
            });
        }

        workspace = await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        DeckAnalysisService service = new(repository, new EmptyCardCatalog());
        AnalysisTools tools = new(
            service,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Plan })));

        JsonElement summary = JsonSerializer.SerializeToElement(await tools.RefreshDeckCardSnapshotsAsync(
            workspace.Id,
            scope: "all",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);
        object full = await tools.RefreshDeckCardSnapshotsAsync(
            workspace.Id,
            scope: "all",
            detailLevel: "full",
            cancellationToken: TestContext.Current.CancellationToken);

        summary.GetProperty("requestedCardCount").GetInt32().Should().Be(100);
        summary.GetProperty("missingCardCount").GetInt32().Should().Be(100);
        summary.GetProperty("failedCardCount").GetInt32().Should().Be(0);
        summary.TryGetProperty("workspace", out _).Should().BeFalse();
        summary.TryGetProperty("missingCards", out _).Should().BeFalse();
        summary.TryGetProperty("failedCards", out _).Should().BeFalse();
        summary.GetProperty("snapshotQualityBefore").GetProperty("cardCount").GetInt32().Should().Be(100);
        full.Should().BeOfType<DeckNormalizationResult>()
            .Which.Workspace.Cards.Should().HaveCount(100);
    }

    /// <summary>
    /// Verifies that deck re-evaluation composes existing analyses into bounded compact output.
    /// </summary>
    [Fact]
    public async Task DeckReEvaluate_ReturnsCompactBoundedHealthSnapshot()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(
            CreateReEvaluationWorkspace(),
            TestContext.Current.CancellationToken);
        EmptyCardCatalog cardCatalog = new();
        DeckWorkspaceService deckService = new(repository, cardCatalog);
        DeckAnalysisService analysisService = new(repository, cardCatalog);
        DeckReEvaluationTools tools = new(deckService, analysisService);

        JsonElement result = JsonSerializer.SerializeToElement(await tools.ReEvaluateDeckAsync(
            workspace.Id,
            limit: 2,
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        result.GetProperty("detailLevel").GetString().Should().Be("summary");
        result.GetProperty("validation").GetProperty("isValid").GetBoolean().Should().BeTrue();
        result.GetProperty("mana").GetProperty("landCount").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("consistency").GetProperty("deckSize").GetInt32().Should().Be(100);
        result.GetProperty("roleBalance").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        result.GetProperty("topRisks").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        result.GetProperty("topSuspectedCuts").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        result.GetProperty("bestExcludedUpgrades").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        result.GetProperty("bestExcludedUpgrades")
            .EnumerateArray()
            .Should()
            .Contain(row => row.GetProperty("cardName").GetString() == "Read the Bones"
                && row.GetProperty("sourceCategory").GetString() == DeckDefaults.Maybeboard);
        result.GetProperty("sourceRecommendations").GetProperty("status").GetString().Should().Be("notQueried");
    }

    /// <summary>
    /// Verifies that source evidence stays default-off and becomes an explicit bounded query.
    /// </summary>
    [Fact]
    public async Task DeckReEvaluate_SourceEvidenceIsOptInAndBounded()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(
            CreateReEvaluationWorkspace(),
            TestContext.Current.CancellationToken);
        EmptyCardCatalog cardCatalog = new();
        DeckWorkspaceService deckService = new(repository, cardCatalog);
        DeckAnalysisService analysisService = new(repository, cardCatalog);
        DeckSimulationService simulationService = new(repository, cardCatalog);
        DeckRecommendationService recommendationService = new(
            repository,
            cardCatalog,
            analysisService,
            simulationService);
        DeckReEvaluationTools tools = new(deckService, analysisService, recommendationService);

        JsonElement result = JsonSerializer.SerializeToElement(await tools.ReEvaluateDeckAsync(
            workspace.Id,
            limit: 20,
            includeSourceEvidence: true,
            sourceAnalysisDepth: "minimal",
            sourceLimit: 30,
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        JsonElement sourceRecommendations = result.GetProperty("sourceRecommendations");
        sourceRecommendations.GetProperty("status").GetString().Should().Be("queried");
        sourceRecommendations.GetProperty("analysisDepth").GetString().Should().Be(AnalysisDepths.Minimal);
        sourceRecommendations.GetProperty("limit").GetInt32().Should().Be(10);
        sourceRecommendations.GetProperty("recommendations").GetArrayLength().Should().Be(0);
        sourceRecommendations.GetProperty("notes")
            .EnumerateArray()
            .Should()
            .Contain(note => note.GetString()!.Contains("No API-backed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that explicit workspace analysis comparison returns bounded deltas and diff rows.
    /// </summary>
    [Fact]
    public async Task DeckCompareWorkspacesAnalysis_ExplicitBaselineReturnsCompactDeltas()
    {
        InMemoryRepository repository = new();
        DeckWorkspace baseline = await repository.SaveAsync(
            CreateReEvaluationWorkspace(),
            TestContext.Current.CancellationToken);
        DeckWorkspace current = await repository.SaveAsync(
            CreateReEvaluationCurrentWorkspace(),
            TestContext.Current.CancellationToken);
        EmptyCardCatalog cardCatalog = new();
        DeckWorkspaceService deckService = new(repository, cardCatalog);
        DeckAnalysisService analysisService = new(repository, cardCatalog);
        DeckReEvaluationTools tools = new(deckService, analysisService);

        JsonElement result = JsonSerializer.SerializeToElement(await tools.CompareWorkspacesAnalysisAsync(
            current.Id,
            baselineMode: "explicit",
            baselineWorkspaceId: baseline.Id,
            detailLevel: "summary",
            limit: 1,
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        result.GetProperty("status").GetString().Should().Be("compared");
        result.GetProperty("detailLevel").GetString().Should().Be("summary");
        result.GetProperty("baseline").GetProperty("validation").GetProperty("isValid").GetBoolean().Should().BeTrue();
        result.GetProperty("current").GetProperty("cost").GetProperty("includedTotal").GetDecimal().Should().Be(0);
        result.GetProperty("deltas").GetProperty("includedCountDelta").GetInt32().Should().Be(0);
        result.GetProperty("workspaceDiff").GetProperty("counts").GetProperty("addedCards").GetInt32().Should().Be(1);
        result.GetProperty("workspaceDiff").GetProperty("addedCards").GetArrayLength().Should().Be(1);
        result.GetProperty("performance").GetProperty("status").GetString().Should().Be("notRequested");
    }

    /// <summary>
    /// Verifies that last-import analysis comparison uses import history baselines.
    /// </summary>
    [Fact]
    public async Task DeckCompareWorkspacesAnalysis_LastImportUsesImportHistory()
    {
        InMemoryRepository repository = new();
        DeckWorkspace baseline = CreateReEvaluationWorkspace();
        DeckWorkspace current = CreateReEvaluationCurrentWorkspace();
        current.SourceReferences.Add(new DeckSourceReference
        {
            Provider = DeckImportProviders.Moxfield,
            ExternalId = "abc123"
        });
        current.ImportHistory.Add(new DeckImportHistoryEntry
        {
            Provider = DeckImportProviders.Moxfield,
            ExternalId = "abc123",
            LocalWorkspaceId = current.Id,
            ImportedAt = DateTimeOffset.UtcNow,
            BaselineWorkspace = baseline
        });
        current = await repository.SaveAsync(current, TestContext.Current.CancellationToken);
        EmptyCardCatalog cardCatalog = new();
        DeckWorkspaceService deckService = new(repository, cardCatalog);
        DeckAnalysisService analysisService = new(repository, cardCatalog);
        DeckReEvaluationTools tools = new(deckService, analysisService);

        JsonElement result = JsonSerializer.SerializeToElement(await tools.CompareWorkspacesAnalysisAsync(
            current.Id,
            baselineMode: " LAST-IMPORT ",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        result.GetProperty("status").GetString().Should().Be("compared");
        result.GetProperty("baselineMode").GetString().Should().Be("last-import");
        result.GetProperty("workspaceDiff").GetProperty("counts").GetProperty("addedCards").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// Verifies that last-import analysis comparison exposes the no-baseline status.
    /// </summary>
    [Fact]
    public async Task DeckCompareWorkspacesAnalysis_LastImportReturnsNoBaselineStatus()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = CreateReEvaluationWorkspace();
        workspace.SourceReferences.Add(new DeckSourceReference
        {
            Provider = DeckImportProviders.Moxfield,
            ExternalId = "abc123"
        });
        workspace = await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        EmptyCardCatalog cardCatalog = new();
        DeckWorkspaceService deckService = new(repository, cardCatalog);
        DeckAnalysisService analysisService = new(repository, cardCatalog);
        DeckReEvaluationTools tools = new(deckService, analysisService);

        JsonElement result = JsonSerializer.SerializeToElement(await tools.CompareWorkspacesAnalysisAsync(
            workspace.Id,
            baselineMode: "last-import",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        result.GetProperty("status").GetString().Should().Be("noPriorBaseline");
        result.TryGetProperty("current", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that performance comparison is opt-in and uses bounded summary output.
    /// </summary>
    [Fact]
    public async Task DeckCompareWorkspacesAnalysis_PerformanceOptInReturnsBoundedSummary()
    {
        InMemoryRepository repository = new();
        DeckWorkspace baseline = await repository.SaveAsync(
            CreateReEvaluationWorkspace(),
            TestContext.Current.CancellationToken);
        DeckWorkspace current = await repository.SaveAsync(
            CreateReEvaluationCurrentWorkspace(),
            TestContext.Current.CancellationToken);
        EmptyCardCatalog cardCatalog = new();
        DeckWorkspaceService deckService = new(repository, cardCatalog);
        DeckAnalysisService analysisService = new(repository, cardCatalog);
        DeckSimulationService simulationService = new(repository, cardCatalog);
        DeckReEvaluationTools tools = new(deckService, analysisService, simulation: simulationService);

        JsonElement result = JsonSerializer.SerializeToElement(await tools.CompareWorkspacesAnalysisAsync(
            current.Id,
            baselineMode: "explicit",
            baselineWorkspaceId: baseline.Id,
            includePerformance: true,
            detailLevel: "summary",
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);

        JsonElement performance = result.GetProperty("performance");
        performance.GetProperty("status").GetString().Should().Be("compared");
        performance.GetProperty("settings").GetProperty("simulations").GetInt32().Should().Be(1000);
        performance.GetProperty("before").GetProperty("detailLevel").GetString().Should().Be("summary");
        performance.GetProperty("after").GetProperty("topStrandedCards").GetArrayLength().Should().BeLessThanOrEqualTo(5);
    }

    /// <summary>
    /// Verifies mutation tools default to summary and still honor explicit full output.
    /// </summary>
    [Fact]
    public async Task MutationTools_DefaultSummaryAndFullEscapeHatch()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Mutation Compact",
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp]
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService deckService = new(repository, new EmptyCardCatalog());
        CategoryTools tools = new(
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        CompactMutationSummaryResult summary = (CompactMutationSummaryResult)await tools.AddCardCategoryAsync(
            workspace.Id,
            "Sol Ring",
            "Anthem",
            cancellationToken: TestContext.Current.CancellationToken);
        DeckChangeResult full = (DeckChangeResult)await tools.AddCardCategoryAsync(
            workspace.Id,
            "Sol Ring",
            "Finisher",
            includeWorkspace: true,
            cancellationToken: TestContext.Current.CancellationToken);
        CompactMutationResult normal = (CompactMutationResult)await tools.AddCardCategoryAsync(
            workspace.Id,
            "Sol Ring",
            "Wincon",
            includeWorkspace: true,
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken);

        summary.Success.Should().BeTrue();
        summary.WorkspaceId.Should().Be(workspace.Id);
        summary.ChangedCards.Should().Equal("Sol Ring");
        summary.ValidationSummary.IsValid.Should().BeTrue();
        full.Workspace.Cards.Single().Categories.Should().Contain("Finisher");
        normal.ChangedCards.Should().Equal("Sol Ring");
        normal.CategoryCountsAfter.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies card facets default to compact key data and miss with a structured result.
    /// </summary>
    [Fact]
    public async Task CardFacetsGet_DefaultSummaryFullEscapeHatchAndStructuredMiss()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Facet Compact",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true }
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Phyrexian Arena",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    ScryfallId = "phyrexian-arena",
                    ScryfallOracleId = "oracle-phyrexian-arena",
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you draw a card and you lose 1 life.",
                        ManaValue = 3,
                        ScryfallUri = "https://scryfall.com/card/test/1/phyrexian-arena",
                        Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["commander"] = "legal",
                            ["modern"] = "legal"
                        },
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["usd"] = "2.50"
                        },
                        ImageUris = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["normal"] = "https://cards.scryfall.io/normal/front/test.jpg"
                        }
                    },
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CardFacetNames.UserTags] = "card-advantage"
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        FacetTools tools = new(
            new CardFacetService(repository),
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        CardFacetSummaryResult summary = (CardFacetSummaryResult)await tools.GetCardFacetsAsync(
            workspace.Id,
            "Phyrexian Arena",
            cancellationToken: TestContext.Current.CancellationToken);
        CardFacetNormalResult normal = (CardFacetNormalResult)await tools.GetCardFacetsAsync(
            workspace.Id,
            "Phyrexian Arena",
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken);
        CardFacetSnapshot full = (CardFacetSnapshot)await tools.GetCardFacetsAsync(
            workspace.Id,
            "Phyrexian Arena",
            detailLevel: "full",
            cancellationToken: TestContext.Current.CancellationToken);
        CardFacetNotFoundResult missing = (CardFacetNotFoundResult)await tools.GetCardFacetsAsync(
            workspace.Id,
            "Skyhunter Strike Force",
            cancellationToken: TestContext.Current.CancellationToken);

        summary.Status.Should().Be("ok");
        summary.Role.Should().Be(DeckRoles.Draw);
        summary.UserTags.Should().Contain("card-advantage");
        summary.CommanderLegality.Should().Be("legal");
        summary.PriceUsd.Should().Be("2.50");
        normal.Facets.Should().ContainKey("scryfall.legalities.commander");
        normal.Facets.Should().NotContainKey("scryfall.legalities.modern");
        normal.Facets.Should().NotContainKey("scryfall.image_uris.normal");
        full.Facets.Should().ContainKey("scryfall.legalities.modern");
        full.Facets.Should().ContainKey("scryfall.image_uris.normal");
        missing.Status.Should().Be("not-found-in-workspace");
        missing.Suggestion.Should().Contain("card_get");
    }

    /// <summary>
    /// Verifies the opt-in rules-backed goldfish race model returns bounded MCP output.
    /// </summary>
    [Fact]
    public async Task CompareGoldfish_RulesBackedRaceModelReturnsBoundedSummary()
    {
        InMemoryRepository repository = new();
        DeckWorkspace fast = await repository.SaveAsync(CreateRaceWorkspace("fast", "Fast", power: 20), TestContext.Current.CancellationToken);
        DeckWorkspace slow = await repository.SaveAsync(CreateRaceWorkspace("slow", "Slow", power: 1), TestContext.Current.CancellationToken);
        SimulationTools tools = new(new DeckSimulationService(repository, new EmptyCardCatalog()));

        object summaryResult = await tools.CompareGoldfishAsync(
            [fast.Id, slow.Id],
            detailLevel: "summary",
            targetTurn: 5,
            simulations: 2,
            seed: 4,
            mulligan: false,
            model: RulesGoldfishRaceConstants.ModelName,
            cancellationToken: TestContext.Current.CancellationToken);
        object normalResult = await tools.CompareGoldfishAsync(
            [fast.Id, slow.Id],
            detailLevel: "normal",
            targetTurn: 5,
            simulations: 2,
            seed: 4,
            mulligan: false,
            model: RulesGoldfishRaceConstants.ModelName,
            cancellationToken: TestContext.Current.CancellationToken);

        JsonElement summary = JsonSerializer.SerializeToElement(summaryResult, WebJsonOptions);
        JsonElement normal = JsonSerializer.SerializeToElement(normalResult, WebJsonOptions);
        summary.GetProperty("modelName").GetString().Should().Be(RulesGoldfishRaceConstants.ModelName);
        summary.GetProperty("modelDescription").GetString().Should().Contain("not a full Magic rules engine");
        summary.GetProperty("decks")[0].GetProperty("wins").GetInt32().Should().Be(2);
        summary.GetProperty("decks")[0].TryGetProperty("representativeTrace", out JsonElement summaryTrace)
            .Should()
            .BeTrue();
        summaryTrace.ValueKind.Should().Be(JsonValueKind.Null);
        normal.GetProperty("decks")[0].GetProperty("representativeTrace").GetArrayLength().Should().BeGreaterThan(0);
        normal.GetProperty("sampleOutcomes").GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// Verifies that performance analysis defaults to raw output and offers compact detail levels.
    /// </summary>
    [Fact]
    public async Task AnalyzePerformance_DefaultsToFullAndSupportsCompactDetailLevels()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreatePerformanceWorkspace(), TestContext.Current.CancellationToken);
        SimulationTools tools = new(new DeckSimulationService(repository, new EmptyCardCatalog()));

        object fullResult = await tools.AnalyzeDeckPerformanceAsync(
            workspace.Id,
            simulations: 100,
            maxTurn: 4,
            seed: 9,
            cancellationToken: TestContext.Current.CancellationToken);
        object summaryResult = await tools.AnalyzeDeckPerformanceAsync(
            workspace.Id,
            detailLevel: "summary",
            simulations: 100,
            maxTurn: 4,
            seed: 9,
            cancellationToken: TestContext.Current.CancellationToken);
        object normalResult = await tools.AnalyzeDeckPerformanceAsync(
            workspace.Id,
            detailLevel: "normal",
            simulations: 100,
            maxTurn: 4,
            seed: 9,
            cancellationToken: TestContext.Current.CancellationToken);

        fullResult.Should().BeOfType<DeckPerformanceAnalysis>();
        JsonElement summary = JsonSerializer.SerializeToElement(summaryResult, WebJsonOptions);
        JsonElement normal = JsonSerializer.SerializeToElement(normalResult, WebJsonOptions);
        summary.GetProperty("detailLevel").GetString().Should().Be("summary");
        summary.TryGetProperty("turnProbabilities", out _).Should().BeFalse();
        summary.GetProperty("keyMetrics").GetProperty("sevenCardKeepRate").GetDouble().Should().BeInRange(0, 1);
        summary.GetProperty("failedScenarios").GetArrayLength().Should().BeLessThanOrEqualTo(5);
        summary.GetProperty("topStrandedCards").GetArrayLength().Should().BeLessThanOrEqualTo(5);
        summary.GetProperty("commanderContext")
            .GetProperty("commanderNames")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .Contain("Test Commander");
        summary.GetProperty("traceSummary").ValueKind.Should().Be(JsonValueKind.Null);
        normal.GetProperty("detailLevel").GetString().Should().Be("normal");
        normal.GetProperty("traceSummary").GetProperty("aggregateCounters")
            .GetProperty("total-runs")
            .GetInt32()
            .Should()
            .Be(100);
    }

    /// <summary>
    /// Verifies that performance comparison defaults to raw output and offers compact detail levels.
    /// </summary>
    [Fact]
    public async Task ComparePlanPerformance_DefaultsToFullAndSupportsCompactDetailLevels()
    {
        InMemoryRepository repository = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreatePerformanceWorkspace(), TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Swap curve slots",
            Operations =
            [
                DeckEditOperation.SetCardQuantity("Ramp Stone", 10, DeckRoles.Ramp),
                DeckEditOperation.SetCardQuantity("Heavy Spell", 54, DeckRoles.Utility)
            ]
        }, TestContext.Current.CancellationToken);
        SimulationTools tools = new(new DeckSimulationService(
            repository,
            new EmptyCardCatalog(),
            planRepository: plans));

        object fullResult = await tools.ComparePlanPerformanceAsync(
            plan.PlanId,
            simulations: 100,
            maxTurn: 4,
            seed: 9,
            cancellationToken: TestContext.Current.CancellationToken);
        object summaryResult = await tools.ComparePlanPerformanceAsync(
            plan.PlanId,
            detailLevel: "summary",
            simulations: 100,
            maxTurn: 4,
            seed: 9,
            cancellationToken: TestContext.Current.CancellationToken);

        fullResult.Should().BeOfType<DeckPerformanceComparison>();
        JsonElement summary = JsonSerializer.SerializeToElement(summaryResult, WebJsonOptions);
        summary.GetProperty("detailLevel").GetString().Should().Be("summary");
        summary.GetProperty("before").GetProperty("detailLevel").GetString().Should().Be("summary");
        summary.GetProperty("after").GetProperty("detailLevel").GetString().Should().Be("summary");
        summary.GetProperty("deltas").GetArrayLength().Should().BeLessThanOrEqualTo(8);
        summary.TryGetProperty("traceSummary", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that unsupported performance detail levels are rejected.
    /// </summary>
    [Fact]
    public async Task PerformanceTools_RejectInvalidDetailLevels()
    {
        InMemoryRepository repository = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreatePerformanceWorkspace(), TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "No-op",
            Operations = []
        }, TestContext.Current.CancellationToken);
        SimulationTools tools = new(new DeckSimulationService(
            repository,
            new EmptyCardCatalog(),
            planRepository: plans));

        Func<Task> analyze = () => tools.AnalyzeDeckPerformanceAsync(
            workspace.Id,
            detailLevel: "verbose",
            simulations: 10,
            maxTurn: 2,
            cancellationToken: TestContext.Current.CancellationToken);
        Func<Task> compare = () => tools.ComparePlanPerformanceAsync(
            plan.PlanId,
            detailLevel: "verbose",
            simulations: 10,
            maxTurn: 2,
            cancellationToken: TestContext.Current.CancellationToken);

        await analyze.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*detailLevel must be summary, normal, or full*");
        await compare.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*detailLevel must be summary, normal, or full*");
    }

    /// <summary>
    /// Verifies that compact direct mutation output reports actual workspace deltas.
    /// </summary>
    [Fact]
    public async Task CompactMutationResult_ReportsActualDirectMutationDeltas()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Compact",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard { Name = "Ramp Growth", Quantity = 2, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Short Stay", Quantity = 1, PrimaryCategory = DeckRoles.Draw, Categories = [DeckRoles.Draw] },
                new DeckCard { Name = "Maybe Later", Quantity = 1, PrimaryCategory = DeckRoles.Draw, Categories = [DeckRoles.Draw] },
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService deckService = new(repository, new EmptyCardCatalog());
        DeckMutationTools tools = new(
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        CompactMutationResult quantity = (CompactMutationResult)await tools.SetCardQuantityAsync(
            workspace.Id,
            "Ramp Growth",
            5,
            DeckRoles.Ramp,
            includeWorkspace: false,
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken);
        CompactMutationResult remove = (CompactMutationResult)await tools.RemoveCardAsync(
            workspace.Id,
            "Short Stay",
            quantity: 5,
            category: DeckRoles.Draw,
            includeWorkspace: false,
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken);
        CompactMutationResult move = (CompactMutationResult)await tools.MoveCardAsync(
            workspace.Id,
            "Maybe Later",
            DeckDefaults.Maybeboard,
            fromCategory: DeckRoles.Draw,
            includeWorkspace: false,
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken);

        quantity.Added.Should().Be(3);
        quantity.Removed.Should().Be(0);
        quantity.Moved.Should().Be(0);
        quantity.ChangedCards.Should().Equal("Ramp Growth");
        remove.Added.Should().Be(0);
        remove.Removed.Should().Be(1);
        remove.Moved.Should().Be(0);
        remove.ChangedCards.Should().Equal("Short Stay");
        move.Added.Should().Be(0);
        move.Removed.Should().Be(0);
        move.Moved.Should().Be(1);
        move.ChangedCards.Should().Equal("Maybe Later");
    }

    /// <summary>
    /// Verifies that compact plan apply output only reports changes that actually applied.
    /// </summary>
    [Fact]
    public async Task CompactMutationResult_PartialPlanApplyReportsActualAppliedChanges()
    {
        InMemoryRepository repository = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Partial Compact",
            Cards =
            [
                new DeckCard { Name = "Sol Ring", Quantity = 1, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
            ]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Partial",
            Operations =
            [
                DeckEditOperation.SetCardQuantity("Sol Ring", 3, DeckRoles.Ramp),
                DeckEditOperation.RenameCategory("Missing Category", "New Category")
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService deckService = new(repository, new EmptyCardCatalog(), planRepository: plans);
        DeckPlanService planService = new(
            repository,
            new EmptyCardCatalog(),
            deckService,
            planRepository: plans);
        PlanTools tools = new(
            planService,
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Apply })));

        CompactMutationResult compact = (CompactMutationResult)await tools.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: false,
            checkpointName: null,
            includeWorkspace: false,
            detailLevel: "normal",
            cancellationToken: TestContext.Current.CancellationToken);

        compact.Success.Should().BeFalse();
        compact.Status.Should().Be(DeckEditPlanStatus.PartiallyApplied);
        compact.Added.Should().Be(2);
        compact.Removed.Should().Be(0);
        compact.Moved.Should().Be(0);
        compact.ChangedCards.Should().Equal("Sol Ring");
        compact.Notes.Should().Contain(note => note.Contains("Missing Category", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that preview tools default to compact summaries while preserving a full raw escape hatch.
    /// </summary>
    [Fact]
    public async Task PlanPreviewTools_DefaultToCompactSummariesAndFullEscapeHatch()
    {
        InMemoryRepository repository = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await repository.SaveAsync(new DeckWorkspace
        {
            Name = "Preview Compact",
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        OracleText = "{T}: Add {C}{C}.",
                        ScryfallUri = "https://scryfall.test/card/Sol%20Ring"
                    }
                },
            ]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Quantity preview",
            Operations =
            [
                DeckEditOperation.SetCardQuantity("Sol Ring", 2, DeckRoles.Ramp)
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService deckService = new(repository, new EmptyCardCatalog(), planRepository: plans);
        DeckPlanService planService = new(
            repository,
            new EmptyCardCatalog(),
            deckService,
            planRepository: plans);
        PlanTools tools = new(
            planService,
            deckService,
            new OperationModeGuard(Options.Create(new MtgMcpOptions { OperationMode = OperationModeGuard.Plan })));

        JsonElement compactPlan = JsonSerializer.SerializeToElement(await tools.PreviewDeckPlanAsync(
            plan.PlanId,
            cancellationToken: TestContext.Current.CancellationToken), WebJsonOptions);
        JsonElement compactPackage = JsonSerializer.SerializeToElement(await tools.PreviewCardPackageAsync(
            workspace.Id,
            removeCards:
            [
                new ExplicitDeckPlanCardChange
                {
                    CardName = "Sol Ring",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ],
            simulations: 10,
            maxTurn: 2,
            cancellationToken: TestContext.Current.CancellationToken), NullIgnoringWebJsonOptions);
        JsonElement fullPackage = JsonSerializer.SerializeToElement(await tools.PreviewCardPackageAsync(
            workspace.Id,
            removeCards:
            [
                new ExplicitDeckPlanCardChange
                {
                    CardName = "Sol Ring",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ],
            detailLevel: "full",
            sourceSupportDepth: PreviewSourceSupportDepths.None,
            simulations: 10,
            maxTurn: 2,
            cancellationToken: TestContext.Current.CancellationToken), NullIgnoringWebJsonOptions);

        compactPlan.GetProperty("detailLevel").GetString().Should().Be("summary");
        compactPlan.GetProperty("summary").GetProperty("includedCards").GetProperty("delta").GetInt32()
            .Should()
            .Be(1);
        compactPlan.GetProperty("before").ValueKind.Should().Be(JsonValueKind.Null);
        compactPackage.GetProperty("previewOnly").GetBoolean().Should().BeTrue();
        compactPackage.GetProperty("canApply").GetBoolean().Should().BeFalse();
        compactPackage.GetProperty("applyPlanId").ValueKind.Should().Be(JsonValueKind.Null);
        compactPackage.GetProperty("analysisMode").GetString().Should().Be(PreviewAnalysisModes.Summary);
        compactPackage.GetProperty("partialDeck").GetBoolean().Should().BeTrue();
        compactPackage.GetProperty("performanceSkipped").GetBoolean().Should().BeTrue();
        compactPackage.GetProperty("performanceSkipReason").GetString().Should().Contain("partial Commander decks");
        compactPackage.GetProperty("sourceSupportDepth").GetString().Should().Be(PreviewSourceSupportDepths.Minimal);
        compactPackage.GetProperty("performance").GetProperty("skipped").GetBoolean().Should().BeTrue();
        compactPackage.GetProperty("sourceSupport").EnumerateArray()
            .Should()
            .Contain(row => row.GetProperty("status").GetString() == "source-backed-metadata");
        compactPackage.TryGetProperty("preview", out _).Should().BeFalse();
        fullPackage.GetProperty("previewOnly").GetBoolean().Should().BeTrue();
        fullPackage.GetProperty("canApply").GetBoolean().Should().BeFalse();
        fullPackage.GetProperty("applyPlanId").ValueKind.Should().Be(JsonValueKind.Null);
        fullPackage.GetProperty("analysisMode").GetString().Should().Be(PreviewAnalysisModes.Summary);
        fullPackage.GetProperty("performanceSkipped").GetBoolean().Should().BeTrue();
        fullPackage.GetProperty("sourceSupportDepth").GetString().Should().Be(PreviewSourceSupportDepths.None);
        fullPackage.GetProperty("sourceSupport").GetArrayLength().Should().Be(0);
        fullPackage.TryGetProperty("preview", out _).Should().BeTrue();
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
            ["INTELLIGENCE:SOURCES:TOPDECK:USER_AGENT"] = "topdeck-agent",
            ["INTELLIGENCE:SOURCES:SPICERACK:API_KEY"] = "ignored-spicerack-key",
            ["INTELLIGENCE:SOURCES:EDHREC:ENABLED"] = "true",
            ["INTELLIGENCE:SOURCES:EDHREC:ALLOW_UNOFFICIAL_API"] = "true",
            ["INTELLIGENCE:SOURCES:EDHREC:BASE_ADDRESS"] = "https://edhrec.test/pages/",
            ["INTELLIGENCE:SOURCES:EDHREC:USER_AGENT"] = "edhrec-agent",
            ["INTELLIGENCE:SOURCES:EDHTOP16:ENABLED"] = "true",
            ["INTELLIGENCE:SOURCES:EDHTOP16:ALLOW_UNOFFICIAL_API"] = "true",
            ["INTELLIGENCE:SOURCES:EDHTOP16:BASE_ADDRESS"] = "https://edhtop16.test/",
            ["INTELLIGENCE:SOURCES:EDHTOP16:USER_AGENT"] = "edhtop16-agent",
            ["INTELLIGENCE:SOURCES:REDDIT:ENABLED"] = "ignored",
            ["REDDIT:CLIENT_SECRET"] = "ignored-reddit-secret",
            ["COMMANDERSPELLBOOK:USER_AGENT"] = "spellbook-agent",
            ["ARCHIDEKT:USER_AGENT"] = "archidekt-agent",
            ["ARCHIDEKT:USERNAME"] = "archidekt-user",
            ["ARCHIDEKT:PASSWORD"] = "archidekt-password",
            ["ARCHIDEKT:CREDENTIALS_FILE"] = "C:/creds.json",
            ["ARCHIDEKT:CARD_ID_CACHE_FILE"] = "C:/archidekt-card-ids.json",
            ["ARCHIDEKT:RATE_LIMIT:MAX_REQUESTS"] = "30",
            ["ARCHIDEKT:RATE_LIMIT:WINDOW_SECONDS"] = "60",
            ["MOXFIELD:BASE_ADDRESS"] = "https://moxfield.test/",
            ["MOXFIELD:USER_AGENT"] = "mtg-mcp-test",
            ["MOXFIELD:CURL_FALLBACK_ENABLED"] = "false",
            ["MOXFIELD:CURL_PATH"] = "custom-curl",
            ["PLAYGROUP:BASE_ADDRESS"] = "https://playgroup.test/api/public/v1/",
            ["PLAYGROUP:USER_AGENT"] = "playgroup-agent",
            ["PLAYGROUP:API_KEY"] = "playgroup-key",
            ["PLAYGROUP:CREDENTIALS_FILE"] = "C:/playgroup-creds.json",
            ["SIMULATION:PROFILE_PATHS:0"] = "profiles/simulation/*.json",
            ["SIMULATION:PROFILE_PATHS:1"] = "C:/mtg-mcp/custom-profile.json",
            ["SIMULATION:ALLOW_EXTERNAL_PROFILE_OVERRIDES"] = "false",
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
        aliases["MtgMcp:Intelligence:Sources:TopDeck:UserAgent"].Should().Be("topdeck-agent");
        aliases.Should().NotContainKey("MtgMcp:Intelligence:Sources:Spicerack:ApiKey");
        aliases["MtgMcp:Intelligence:Sources:Edhrec:Enabled"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:Edhrec:AllowUnofficialApi"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:Edhrec:BaseAddress"].Should().Be("https://edhrec.test/pages/");
        aliases["MtgMcp:Intelligence:Sources:Edhrec:UserAgent"].Should().Be("edhrec-agent");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:Enabled"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:AllowUnofficialApi"].Should().Be("true");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:BaseAddress"].Should().Be("https://edhtop16.test/");
        aliases["MtgMcp:Intelligence:Sources:EdhTop16:UserAgent"].Should().Be("edhtop16-agent");
        aliases.Should().NotContainKey("MtgMcp:Intelligence:Sources:Reddit:Enabled");
        aliases.Should().NotContainKey("MtgMcp:Reddit:ClientSecret");
        aliases["MtgMcp:CommanderSpellbook:UserAgent"].Should().Be("spellbook-agent");
        aliases["MtgMcp:Archidekt:UserAgent"].Should().Be("archidekt-agent");
        aliases["MtgMcp:Archidekt:Username"].Should().Be("archidekt-user");
        aliases["MtgMcp:Archidekt:Password"].Should().Be("archidekt-password");
        aliases["MtgMcp:Archidekt:CredentialsFile"].Should().Be("C:/creds.json");
        aliases["MtgMcp:Archidekt:CardIdCacheFile"].Should().Be("C:/archidekt-card-ids.json");
        aliases["MtgMcp:Archidekt:RateLimit:MaxRequests"].Should().Be("30");
        aliases["MtgMcp:Archidekt:RateLimit:WindowSeconds"].Should().Be("60");
        aliases["MtgMcp:Moxfield:BaseAddress"].Should().Be("https://moxfield.test/");
        aliases["MtgMcp:Moxfield:UserAgent"].Should().Be("mtg-mcp-test");
        aliases["MtgMcp:Moxfield:EnableCurlFallback"].Should().Be("false");
        aliases["MtgMcp:Moxfield:CurlPath"].Should().Be("custom-curl");
        aliases["MtgMcp:Playgroup:BaseAddress"].Should().Be("https://playgroup.test/api/public/v1/");
        aliases["MtgMcp:Playgroup:UserAgent"].Should().Be("playgroup-agent");
        aliases["MtgMcp:Playgroup:ApiKey"].Should().Be("playgroup-key");
        aliases["MtgMcp:Playgroup:CredentialsFile"].Should().Be("C:/playgroup-creds.json");
        aliases["MtgMcp:Simulation:ProfilePaths:0"].Should().Be("profiles/simulation/*.json");
        aliases["MtgMcp:Simulation:ProfilePaths:1"].Should().Be("C:/mtg-mcp/custom-profile.json");
        aliases["MtgMcp:Simulation:AllowExternalProfileOverrides"].Should().Be("false");
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
        host.Services.GetRequiredService<IMoxfieldGateway>().Should().NotBeNull();
        host.Services.GetRequiredService<IPlaygroupGateway>().Should().NotBeNull();
        host.Services.GetRequiredService<PlaygroupService>().Should().NotBeNull();
        host.Services.GetRequiredService<ServerInfoService>().Should().NotBeNull();
        host.Services.GetRequiredService<ICorpusCache>().Should().NotBeNull();
        host.Services.GetServices<ICorpusSignalProvider>().Should().NotBeEmpty();
        host.Services.GetRequiredService<DeckRecommendationService>().ListCorpusSources().Sources.Should().Contain(source =>
            source.Key == "topdeck"
            && source.Status == CorpusSourceStatusKind.MissingConfig
            && source.RequiresKey);
        host.Services.GetRequiredService<DeckRecommendationService>().ListCorpusSources().Sources.Should().Contain(source =>
            source.Key == "edhrec"
            && source.Status == CorpusSourceStatusKind.Available
            && source.UnofficialApi
            && source.PermissionSensitive);
        host.Services.GetRequiredService<DeckRecommendationService>().ListCorpusSources().Sources.Should().Contain(source =>
            source.Key == "edhtop16"
            && source.Status == CorpusSourceStatusKind.Disabled
            && source.UnofficialApi
            && source.PermissionSensitive);
        host.Services.GetRequiredService<DeckRecommendationService>().ListCorpusSources().Sources.Should().NotContain(source =>
            source.Key == "reddit-discussions");
        host.Services.GetRequiredService<IOptions<MtgMcpOptions>>()
            .Value.DataDir.Should()
            .NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies that MCP initialization includes recommendation presentation guidance.
    /// </summary>
    [Fact]
    public void HostBuild_ConfiguresRecommendationPresentationInstructions()
    {
        using IHost host = MtgMcpHost.Build(["--smoke"]);

        McpServerOptions options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        options.ServerInstructions.Should().Be(MtgMcpHost.RecommendationPresentationInstructions);
        options.ServerInstructions.Should().Contain("Scryfall URI");
    }

    /// <summary>
    /// Reads a named string value from custom attribute metadata.
    /// </summary>
    private static string? GetNamedString(CustomAttributeData attribute, string propertyName)
    {
        CustomAttributeNamedArgument? argument = attribute.NamedArguments.FirstOrDefault(value =>
            value.MemberName.Equals(propertyName, StringComparison.Ordinal)
        );
        return argument.HasValue ? (string?)argument.Value.TypedValue.Value : null;
    }

    /// <summary>
    /// Reads named string values from method attributes on a type.
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
    /// Gets the MCP tool attribute for the named tool method.
    /// </summary>
    private static CustomAttributeData GetToolAttribute(string methodName)
    {
        MethodInfo method =
            ToolTypes
                .Select(type => type.GetMethod(methodName))
                .SingleOrDefault(method => method is not null)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return GetToolAttribute(method);
    }

    /// <summary>
    /// Gets the MCP tool attribute for a reflected tool method.
    /// </summary>
    private static CustomAttributeData GetToolAttribute(MethodInfo method)
    {
        return TryGetToolAttribute(method)
            ?? throw new InvalidOperationException($"{method.Name} is not an MCP tool method.");
    }

    /// <summary>
    /// Gets a parameter description for public schema guidance assertions.
    /// </summary>
    private static string GetParameterDescription(
        Type type,
        string methodName,
        string parameterName
    )
    {
        MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == methodName);
        ParameterInfo parameter = method.GetParameters()
            .Single(parameter => parameter.Name == parameterName);
        return parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException(
                $"{type.Name}.{methodName} parameter {parameterName} does not have a description."
            );
    }

    /// <summary>
    /// Gets a method description for public tool presentation assertions.
    /// </summary>
    private static string GetMethodDescription(Type type, string methodName)
    {
        MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == methodName);
        return method.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"{type.Name}.{methodName} does not have a description.");
    }

    /// <summary>
    /// Gets the MCP tool attribute when a method exposes one.
    /// </summary>
    private static CustomAttributeData? TryGetToolAttribute(MethodInfo method)
    {
        return method.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.Name == "McpServerToolAttribute"
        );
    }

    /// <summary>
    /// Reads a named boolean value from custom attribute metadata.
    /// </summary>
    private static bool? GetNamedBool(CustomAttributeData attribute, string propertyName)
    {
        CustomAttributeNamedArgument? argument = attribute.NamedArguments.FirstOrDefault(value =>
            value.MemberName.Equals(propertyName, StringComparison.Ordinal)
        );
        return argument.HasValue ? (bool?)argument.Value.TypedValue.Value : null;
    }

    /// <summary>
    /// Checks the wrapper method and its async state machine for operation guard calls.
    /// </summary>
    private static bool CallsOperationModeGuard(MethodInfo method)
    {
        if (CallsOperationModeGuardInMethod(method))
        {
            return true;
        }

        AsyncStateMachineAttribute? stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        MethodInfo? moveNext = stateMachine?.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return moveNext is not null && CallsOperationModeGuardInMethod(moveNext);
    }

    /// <summary>
    /// Scans one IL method body for calls to operation mode guard methods.
    /// </summary>
    private static bool CallsOperationModeGuardInMethod(MethodInfo method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        int index = 0;
        while (index < il.Length)
        {
            OpCode opCode = ReadOpCode(il, ref index);
            int operandStart = index;
            int operandSize = GetOperandSize(opCode.OperandType, il, operandStart);
            if ((opCode == OpCodes.Call || opCode == OpCodes.Callvirt) && operandSize == 4)
            {
                int token = BitConverter.ToInt32(il, operandStart);
                if (IsOperationModeGuardCall(method.Module, token))
                {
                    return true;
                }
            }

            index += operandSize;
        }

        return false;
    }

    /// <summary>
    /// Resolves a call target and checks whether it is one of the guard methods.
    /// </summary>
    private static bool IsOperationModeGuardCall(Module module, int metadataToken)
    {
        try
        {
            MethodBase? target = module.ResolveMethod(metadataToken);
            return target?.DeclaringType == typeof(OperationModeGuard)
                && target.Name is nameof(OperationModeGuard.EnsureCanMutate)
                    or nameof(OperationModeGuard.EnsureCanWritePlanningState);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads one opcode and advances the IL cursor.
    /// </summary>
    private static OpCode ReadOpCode(byte[] il, ref int index)
    {
        byte value = il[index++];
        return value == 0xFE
            ? MultiByteOpCodes[il[index++]]
            : SingleByteOpCodes[value];
    }

    /// <summary>
    /// Returns the byte width of an opcode operand.
    /// </summary>
    private static int GetOperandSize(OperandType operandType, byte[] il, int operandStart)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget
                or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget
                or OperandType.InlineField
                or OperandType.InlineI
                or OperandType.InlineMethod
                or OperandType.InlineSig
                or OperandType.InlineString
                or OperandType.InlineTok
                or OperandType.InlineType
                or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, operandStart) * 4,
            _ => throw new InvalidOperationException($"Unsupported IL operand type {operandType}.")
        };
    }

    /// <summary>
    /// Builds an opcode lookup table from the runtime opcode definitions.
    /// </summary>
    private static OpCode[] CreateOpCodeLookup(bool multiByte)
    {
        OpCode[] opCodes = new OpCode[256];
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            ushort value = (ushort)opCode.Value;
            bool isMultiByte = (value & 0xFF00) == 0xFE00;
            if (isMultiByte == multiByte)
            {
                opCodes[value & 0xFF] = opCode;
            }
        }

        return opCodes;
    }

    /// <summary>
    /// Creates a compact rules-backed race fixture workspace.
    /// </summary>
    private static DeckWorkspace CreateRaceWorkspace(string id, string name, int power)
    {
        return new DeckWorkspace
        {
            Id = id,
            Name = name,
            Cards =
            [
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 3,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Forest",
                        ProducedMana = ["G"],
                    }
                },
                new DeckCard
                {
                    Name = $"{name} Attacker",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Wincons,
                    Categories = [DeckRoles.Wincons],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature - Cat",
                        ManaValue = 1,
                        Power = power.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Toughness = "1",
                    }
                },
            ],
        };
    }

    /// <summary>
    /// Creates a deterministic Commander workspace for compact performance output tests.
    /// </summary>
    private static DeckWorkspace CreatePerformanceWorkspace()
    {
        return new DeckWorkspace
        {
            Name = "Performance Compact",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Utility, IncludedInDeck = true },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Test Commander",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature - Advisor",
                        ManaCost = "{2}{G}",
                        ManaValue = 3,
                        OracleText = "Whenever you cast your second spell each turn, draw a card.",
                        ColorIdentity = ["G"]
                    }
                },
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 35,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Forest",
                        OracleText = "{T}: Add {G}.",
                        ProducedMana = ["G"],
                    }
                },
                new DeckCard
                {
                    Name = "Ramp Stone",
                    Quantity = 8,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        ManaCost = "{2}",
                        ManaValue = 2,
                        OracleText = "{T}: Add {G}.",
                    }
                },
                new DeckCard
                {
                    Name = "Heavy Spell",
                    Quantity = 56,
                    PrimaryCategory = DeckRoles.Utility,
                    Categories = [DeckRoles.Utility],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        ManaCost = "{5}{G}",
                        ManaValue = 6,
                        OracleText = "Draw two cards.",
                        ColorIdentity = ["G"]
                    }
                },
            ],
        };
    }

    /// <summary>
    /// Creates a Commander workspace with an excluded draw candidate for re-evaluation tests.
    /// </summary>
    private static DeckWorkspace CreateReEvaluationWorkspace()
    {
        return new DeckWorkspace
        {
            Name = "Re-evaluate Compact",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Utility, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Test Commander",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature - Advisor",
                        ManaCost = "{2}{B}",
                        ManaValue = 3,
                        OracleText = "Whenever you gain life, each opponent loses 1 life.",
                        ColorIdentity = ["B"]
                    }
                },
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 98,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Swamp",
                        OracleText = "{T}: Add {B}.",
                        ProducedMana = ["B"],
                    }
                },
                new DeckCard
                {
                    Name = "Expensive Filler",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Utility,
                    Categories = [DeckRoles.Utility],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        ManaCost = "{5}{B}",
                        ManaValue = 6,
                        OracleText = "Target opponent loses 2 life.",
                        ColorIdentity = ["B"]
                    }
                },
                new DeckCard
                {
                    Name = "Read the Bones",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckDefaults.Maybeboard],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        ManaCost = "{2}{B}",
                        ManaValue = 3,
                        OracleText = "Scry 2, then draw two cards. You lose 2 life.",
                        ColorIdentity = ["B"],
                        ScryfallUri = "https://scryfall.com/card/test/2/read-the-bones"
                    }
                },
            ],
        };
    }

    /// <summary>
    /// Creates a deterministic follow-up workspace with one card swap for analysis comparison tests.
    /// </summary>
    private static DeckWorkspace CreateReEvaluationCurrentWorkspace()
    {
        DeckWorkspace workspace = CreateReEvaluationWorkspace();
        workspace.Name = "Re-evaluate Compact Updated";
        DeckCard swamp = workspace.Cards.Single(card => card.Name == "Swamp");
        swamp.Quantity = 97;
        workspace.Cards.Add(new DeckCard
        {
            Name = "Sol Ring",
            Quantity = 1,
            PrimaryCategory = DeckRoles.Ramp,
            Categories = [DeckRoles.Ramp],
            Snapshot = new CardSnapshot
            {
                TypeLine = "Artifact",
                ManaCost = "{1}",
                ManaValue = 1,
                OracleText = "{T}: Add {C}{C}.",
                ProducedMana = ["C"],
                ColorIdentity = [],
                ScryfallUri = "https://scryfall.com/card/test/3/sol-ring"
            }
        });

        return workspace;
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
            return Task.FromResult(new AuthStatus { HasUsernamePassword = true });
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
        /// Returns no deck summaries for filtered fake list requests.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(
            ArchidektDeckListRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>([]);
        }

        /// <summary>
        /// Returns no fake folders by default.
        /// </summary>
        public Task<IReadOnlyList<ArchidektFolder>> ListFoldersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektFolder>>([]);
        }

        /// <summary>
        /// Creates a deterministic fake folder for tool shape tests.
        /// </summary>
        public Task<ArchidektFolder> CreateFolderAsync(
            string name,
            string? parentFolderId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArchidektFolder
            {
                Id = "folder",
                Name = name,
                ParentFolderId = parentFolderId,
            });
        }

        /// <summary>
        /// Echoes fake deck move requests.
        /// </summary>
        public Task<ArchidektMoveDecksResult> MoveDecksAsync(
            IReadOnlyList<string> deckIds,
            string? folderId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArchidektMoveDecksResult
            {
                FolderId = folderId,
                DeckIds = deckIds.ToList(),
                Moved = deckIds.Count,
            });
        }

        /// <summary>
        /// Creates a fake Archidekt deck.
        /// </summary>
        public Task<DeckWorkspace> CreateDeckAsync(
            ArchidektDeckCreateRequest request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new DeckWorkspace
            {
                Name = request.Name,
                Format = request.Format,
                Description = request.Description,
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "created",
            });
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
        /// Leaves fake Archidekt card ids unchanged.
        /// </summary>
        public Task ResolveCardIdsAsync(IReadOnlyList<DeckCard> cards, CancellationToken cancellationToken)
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

    /// <summary>
    /// Provides in memory plan repository behavior.
    /// </summary>
    private sealed class InMemoryPlanRepository : IDeckPlanRepository
    {
        /// <summary>
        /// Stores fake plans by id.
        /// </summary>
        private readonly Dictionary<string, DeckEditPlan> plans = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a plan in the fake repository.
        /// </summary>
        public Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
        {
            plans[plan.PlanId] = plan;
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Gets a plan from the fake repository.
        /// </summary>
        public Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
        {
            plans.TryGetValue(planId, out DeckEditPlan? plan);
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Lists fake plans.
        /// </summary>
        public Task<IReadOnlyList<DeckEditPlan>> ListAsync(string? workspaceId, CancellationToken cancellationToken)
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
        public Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken)
        {
            return Task.FromResult(plans.Remove(planId));
        }
    }

}
