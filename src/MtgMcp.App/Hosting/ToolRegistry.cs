using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Builds the method-level MCP tool registry used for toolset and operation-mode advertising.
/// </summary>
public static class ToolRegistry
{
    /// <summary>
    /// Lists every tool wrapper type that contributes to the public MCP tool surface.
    /// </summary>
    public static readonly Type[] ToolTypes =
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
    /// Gets every registered tool method with its public grouping and write capability.
    /// </summary>
    public static IReadOnlyList<ToolRegistryEntry> Entries { get; } = BuildEntries();

    /// <summary>
    /// Creates SDK tool instances for the configured toolsets and operation mode.
    /// </summary>
    public static IReadOnlyList<McpServerTool> CreateTools(MtgMcpOptions options)
    {
        List<McpServerTool> tools = [];
        foreach (ToolRegistryEntry entry in SelectEntries(options))
        {
            tools.Add(McpServerTool.Create(
                entry.Method,
                request => CreateTarget(request, entry.OwnerType),
                new McpServerToolCreateOptions()));
        }

        return tools;
    }

    /// <summary>
    /// Selects registry entries advertised for the configured toolsets and operation mode.
    /// </summary>
    public static IReadOnlyList<ToolRegistryEntry> SelectEntries(MtgMcpOptions options)
    {
        HashSet<string> enabledToolsets = ParseToolsets(options.Toolsets);
        string operationMode = OperationModeGuard.Normalize(options.OperationMode);
        List<ToolRegistryEntry> entries = [];

        foreach (ToolRegistryEntry entry in Entries)
        {
            if (enabledToolsets.Count > 0 && !enabledToolsets.Contains(entry.Toolset))
            {
                continue;
            }

            if (!IsAllowedInMode(entry.Capability, operationMode))
            {
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Returns whether the capability can be advertised in the supplied operation mode.
    /// </summary>
    public static bool IsAllowedInMode(ToolCapability capability, string operationMode)
    {
        return operationMode switch
        {
            OperationModeGuard.ReadOnly => capability == ToolCapability.Read,
            OperationModeGuard.Plan => capability is ToolCapability.Read or ToolCapability.Plan,
            OperationModeGuard.Apply => true,
            _ => false,
        };
    }

    /// <summary>
    /// Parses comma, semicolon, or whitespace separated toolset names.
    /// </summary>
    public static HashSet<string> ParseToolsets(string? toolsets)
    {
        HashSet<string> enabled = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(toolsets))
        {
            return enabled;
        }

        foreach (string toolset in toolsets.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            enabled.Add(toolset.Trim());
        }

        return enabled;
    }

    /// <summary>
    /// Builds registry entries from MCP tool attributes.
    /// </summary>
    private static IReadOnlyList<ToolRegistryEntry> BuildEntries()
    {
        List<ToolRegistryEntry> entries = [];
        foreach (Type type in ToolTypes)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                McpServerToolAttribute? attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute?.Name is null)
                {
                    continue;
                }

                entries.Add(new ToolRegistryEntry(
                    attribute.Name,
                    type,
                    method,
                    ResolveToolset(attribute.Name),
                    ResolveCapability(attribute.Name, attribute.ReadOnly),
                    attribute.ReadOnly,
                    attribute.Destructive,
                    attribute.Idempotent,
                    attribute.OpenWorld,
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""));
            }
        }

        entries.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return entries;
    }

    /// <summary>
    /// Creates a tool wrapper instance from the request service provider.
    /// </summary>
    private static object CreateTarget(RequestContext<CallToolRequestParams> request, Type ownerType)
    {
        IServiceProvider services = request.Services
            ?? request.Server.Services
            ?? throw new InvalidOperationException("MCP request did not include a service provider.");
        return ActivatorUtilities.CreateInstance(services, ownerType);
    }

    /// <summary>
    /// Maps one tool name to the smallest useful toolset group.
    /// </summary>
    private static string ResolveToolset(string name)
    {
        if (name.StartsWith("archidekt_", StringComparison.Ordinal))
        {
            return "archidekt";
        }

        if (name.StartsWith("card_facets_", StringComparison.Ordinal)
            || name.StartsWith("deck_facets_", StringComparison.Ordinal))
        {
            return "facets";
        }

        if (name.StartsWith("card_", StringComparison.Ordinal))
        {
            return "cards";
        }

        if (name.StartsWith("combo_", StringComparison.Ordinal)
            || name == "card_classify_win_routes")
        {
            return "combos";
        }

        if (name.StartsWith("playgroup_", StringComparison.Ordinal))
        {
            return "playgroup";
        }

        if (name.StartsWith("server_", StringComparison.Ordinal))
        {
            return "server";
        }

        if (name.StartsWith("source_", StringComparison.Ordinal)
            || name is "deck_analyze_commander_trends" or "deck_find_lesser_known_cards" or "deck_find_exemplar_decks")
        {
            return "sources";
        }

        if (name.StartsWith("workspace_checkpoint_", StringComparison.Ordinal)
            || name.StartsWith("workspace_", StringComparison.Ordinal))
        {
            return "workspace";
        }

        if (name.StartsWith("deck_plan_", StringComparison.Ordinal)
            || name == "deck_preview_card_package")
        {
            return "plans";
        }

        if (name.StartsWith("deck_intent_", StringComparison.Ordinal))
        {
            return "intent";
        }

        if (name is "deck_simulate_goldfish"
            or "deck_compare_goldfish"
            or "deck_project_board_state"
            or "deck_estimate_win_turn"
            or "deck_analyze_performance"
            or "deck_plan_compare_performance")
        {
            return "simulation";
        }

        if (name.StartsWith("deck_analyze_", StringComparison.Ordinal)
            || name is "deck_estimate_commander_bracket" or "deck_explain_role_counts" or "deck_review_weak_spots"
                or "deck_re_evaluate" or "deck_compare_workspaces_analysis" or "deck_summarize")
        {
            return "analysis";
        }

        if (name.StartsWith("commander_", StringComparison.Ordinal)
            || name.StartsWith("wincon_", StringComparison.Ordinal)
            || name is "deck_query_cards" or "deck_review_new_card_swaps" or "deck_evaluate_card"
                or "deck_batch_tuning_report" or "deck_score_cards_for_playgroup_meta")
        {
            return "recommendations";
        }

        return "editing";
    }

    /// <summary>
    /// Maps one tool name and MCP read-only annotation to the advertised write capability.
    /// </summary>
    private static ToolCapability ResolveCapability(string name, bool readOnly)
    {
        if (readOnly)
        {
            return ToolCapability.Read;
        }

        return name is "deck_refresh_card_metadata"
            or "deck_plan_create"
            or "deck_plan_clone"
            or "deck_plan_delete"
            or "card_facets_set_annotations"
            or "archidekt_copy_workspace"
            ? ToolCapability.Plan
            : ToolCapability.Mutate;
    }
}

/// <summary>
/// Describes whether a tool is read-only, writes local planning state, or can mutate deck state.
/// </summary>
public enum ToolCapability
{
    /// <summary>
    /// The tool reads existing state or remote facts only.
    /// </summary>
    Read,

    /// <summary>
    /// The tool may write local planning or metadata state but should be available in plan mode.
    /// </summary>
    Plan,

    /// <summary>
    /// The tool may modify deck state or remote provider state and requires apply mode.
    /// </summary>
    Mutate,
}

/// <summary>
/// Captures one MCP tool method and the metadata needed to decide whether to advertise it.
/// </summary>
public sealed record ToolRegistryEntry(
    string Name,
    Type OwnerType,
    MethodInfo Method,
    string Toolset,
    ToolCapability Capability,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    bool OpenWorld,
    string Description);
