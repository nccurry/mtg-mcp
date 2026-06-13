using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using MtgMcp.Core;
using MtgMcp.Decklists;

namespace MtgMcp.App;

/// <summary>
/// Provides mtg resources behavior.
/// </summary>
[McpServerResourceType]
public sealed class MtgResources
{
    /// <summary>
    /// Configures JSON serialization for MCP resource payloads.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Stores the decks.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Supplies recommendation source status.
    /// </summary>
    private readonly DeckRecommendationService recommendations;

    /// <summary>
    /// Stores the configuration.
    /// </summary>
    private readonly IConfiguration configuration;

    /// <summary>
    /// Stores the archidekt gateway.
    /// </summary>
    private readonly IArchidektGateway archidektGateway;

    /// <summary>
    /// Stores the operation mode.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Stores the Playgroup aggregation service.
    /// </summary>
    private readonly PlaygroupService playgroups;

    /// <summary>
    /// Stores Reddit discussion provider auth status.
    /// </summary>
    private readonly RedditDiscussionCorpusSignalProvider reddit;

    /// <summary>
    /// Stores server version and runtime diagnostics.
    /// </summary>
    private readonly ServerInfoService serverInfo;

    /// <summary>
    /// Creates the MCP resource endpoint group.
    /// </summary>
    public MtgResources(
        DeckWorkspaceService decks,
        DeckRecommendationService recommendations,
        IConfiguration configuration,
        IArchidektGateway archidektGateway,
        OperationModeGuard operationMode,
        PlaygroupService playgroups,
        RedditDiscussionCorpusSignalProvider reddit,
        ServerInfoService serverInfo
    )
    {
        this.decks = decks;
        this.recommendations = recommendations;
        this.configuration = configuration;
        this.archidektGateway = archidektGateway;
        this.operationMode = operationMode;
        this.playgroups = playgroups;
        this.reddit = reddit;
        this.serverInfo = serverInfo;
    }

    /// <summary>
    /// Returns the full JSON representation for a saved workspace.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://workspace/{workspaceId}", Name = "Workspace")]
    [Description("Full JSON representation of a saved deck workspace.")]
    public async Task<string> GetDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        DeckWorkspace workspace = await decks
            .GetDeckResourceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(workspace, JsonOptions);
    }

    /// <summary>
    /// Returns a compact JSON summary for a saved workspace.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://workspace/{workspaceId}/summary", Name = "Workspace Summary")]
    [Description("Summary, counts, validation status, and category list for a deck workspace.")]
    public async Task<string> GetDeckSummaryAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        object summary = await decks
            .GetDeckSummaryAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    /// <summary>
    /// Returns compact current state for a saved workspace.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://workspace/{workspaceId}/state", Name = "Workspace State")]
    [Description("Compact workspace state: included count, commanders, category counts, role counts, sideboard/maybeboard cards, validation, and top warnings.")]
    public async Task<string> GetDeckStateAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        DeckWorkspaceState state = await decks
            .GetWorkspaceStateAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>
    /// Returns parsed deck intent stored in a workspace description.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://workspace/{workspaceId}/intent", Name = "Workspace Deck Intent")]
    [Description("Parsed MTG MCP Deck Intent stored in the workspace description.")]
    public async Task<string> GetDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        DeckIntentResult intent = await decks
            .GetDeckIntentAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(intent, JsonOptions);
    }

    /// <summary>
    /// Returns compact state plus parsed deck intent for assistant workflows.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://workspace/{workspaceId}/assistant-context", Name = "Workspace Assistant Context")]
    [Description("Assistant-facing context derived from compact workspace state and the existing deck intent section.")]
    public async Task<string> GetAssistantContextAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        DeckAssistantContext context = await decks
            .GetAssistantContextAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(context, JsonOptions);
    }

    /// <summary>
    /// Returns a compact Scryfall search syntax reference.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mtg://scryfall/syntax-cheatsheet",
        Name = "Scryfall Syntax Cheatsheet"
    )]
    [Description("Compact Scryfall search syntax cheatsheet.")]
    public string GetScryfallSyntaxCheatsheet()
    {
        return """
            name:lightning or !"Lightning Bolt" for names
            type:creature, t:land, oracle:draw, o:flying for rules text
            color<=uw, id<=grixis, commander:WUBRG for color identity
            legal:commander, banned:modern, format:legacy for legality
            mana>=3, mv<=2, pow>=4, tou<3 for numeric filters
            set:otj, rarity:rare, usd<5, art:dragon for print filters
            Combine terms with spaces, or, -, and parentheses.
            """;
    }

    /// <summary>
    /// Returns lightweight deck construction rules for a format.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mtg://formats/{format}/deck-rules",
        Name = "Format Deck Rules"
    )]
    [Description("Lightweight deck construction rules for common formats.")]
    public string GetFormatRules(string format)
    {
        return format.Trim().ToLowerInvariant() switch
        {
            "commander" or "edh" =>
                "Commander: 100 included cards, singleton except basic lands, "
                    + "one commander or legal commander pair, color identity restrictions.",
            "standard" =>
                "Standard: at least 60 mainboard cards, optional sideboard up to 15, "
                    + "up to four copies except basic lands.",
            "modern" =>
                "Modern: at least 60 mainboard cards, optional sideboard up to 15, "
                    + "up to four copies except basic lands.",
            "legacy" =>
                "Legacy: at least 60 mainboard cards, optional sideboard up to 15, "
                    + "up to four copies except basic lands unless restricted or banned.",
            "pauper" =>
                "Pauper: at least 60 mainboard cards, optional sideboard up to 15, "
                    + "only cards printed at common in a supported release.",
            _ =>
                "Generic constructed: at least 60 mainboard cards, optional sideboard up to 15, "
                    + "up to four copies except basic lands.",
        };
    }

    /// <summary>
    /// Returns guidance for choosing local, Moxfield, or Archidekt workspace modes.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mtg://usage/workspace-selection",
        Name = "Workspace Selection Guidance"
    )]
    [Description("Policy for choosing local, Moxfield import, or Archidekt workspaces and when to ask the user.")]
    public string GetWorkspaceSelectionGuidance()
    {
        return """
            Use local mode when the user wants a new unsynced brew, a scratch deck, or an import from pasted deck text.
            Use Moxfield mode when the user provides a Moxfield deck id or URL; Moxfield imports become local-only workspaces.
            Use Archidekt mode when the user provides an Archidekt deck id or URL,
            asks to update an online deck, or asks for Archidekt checkpoints.
            If local, Moxfield import, and Archidekt are each plausible, ask the user which workspace mode to use
            before creating or opening a workspace.
            To migrate an imported or local workspace to Archidekt, dry-run archidekt_copy_workspace first,
            then apply it only after the user confirms the destination and warnings.
            Never enable Archidekt writeback unless the user explicitly asks to update,
            organize, tag, move cards, checkpoint, or otherwise persist changes to Archidekt.
            If Archidekt writeback intent is unclear, ask whether edits should write back
            to Archidekt or stay local-only.
            Prefer workspace_start as the first deck workspace tool because it requires
            an explicit mode and writeback choice.
            """;
    }

    /// <summary>
    /// Returns the current operation mode and mutation policy.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mtg://usage/operation-modes",
        Name = "Operation Mode Guidance"
    )]
    [Description("Current operation mode and policy for read-only, plan, and apply behavior.")]
    public string GetOperationModeGuidance()
    {
        object status = operationMode.GetStatus();
        string statusJson = JsonSerializer.Serialize(status, JsonOptions);
        return $$"""
            Current mode:
            {{statusJson}}

            apply or act: mutating tools are allowed, subject to each tool's arguments
            and Archidekt writeback checks.
            plan: read-only tools and non-mutating planning tools are allowed, but
            deck-content changes, checkpoints, and Archidekt writeback are blocked.
            read-only or ask: read-only tools are allowed, but deck-content changes,
            planning-state writes, checkpoints, and writeback are blocked.

            MCP tool annotations also mark tools as read-only/destructive/open-world
            so compatible clients can ask for approval before risky calls.
            If a blocked mutating tool is needed, ask the user to restart or reconfigure
            the MCP server with MTGMCP__OPERATION_MODE=apply.
            """;
    }

    /// <summary>
    /// Gets deck intent guidance.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mtg://usage/deck-intent",
        Name = "Deck Intent Guidance"
    )]
    [Description("How to read and write human-readable deck intent sections.")]
    public string GetDeckIntentGuidance()
    {
        return """
            Deck intent captures what the user is aiming for: archetype, budget,
            power target, heuristic profile, simulation profile, archetype tags,
            package template, local meta, build targets, cards/packages to protect,
            and things to avoid.
            Store it in the deck description as a human-readable section titled
            "MTG MCP Deck Intent" and ending with "End MTG MCP Deck Intent".
            Existing tools consume role targets, budget, preferences, avoided
            cards, protected cards, simulation settings, and deck-local win routes;
            heuristic, simulation, package, and local-meta fields are parsed for
            profile-aware brewing workflows.
            Local-meta scoring can use deck_score_cards_for_playgroup_meta to score
            explicit candidate cards, or excluded workspace cards, against
            Playgroup-derived pressures. That output reports separate plan-fit,
            performance-delta, meta-coverage, self-harm, price/bracket, and
            evidence-confidence factors.
            Values such as Power Level, Heuristic Profile, Simulation Profile,
            and Package Template are case-insensitive; spaces and underscores
            normalize to hyphens. Preferred v2 sections are Build Targets,
            Simulation, and Win Routes.
            Simulation Profile resolves in this order: explicit tool argument,
            deck intent value, auto inference, then neutral. Built-ins are
            neutral, aggro, combo, control, value, big-mana, and stax; auto asks
            the resolver to choose from deck facts.
            Simulation can also set Prefer Commander On Curve, Preferred
            Commander Turn, Preferred Background Turn, and Command Zone Order.
            Command Zone Order accepts Background, Commander, or exact card
            names for Background and Partner-style decks.
            Win Routes use lines such as:
            Altar Loop: requires commander, repeatable-blink, card:Altar of the Brood; earliest turn 5; kind combo
            Supported route requirements are commander, repeatable-blink,
            card:<name>, role:<role>, tag:<tag>, mana>=N, tokens>=N,
            interactionHeld>=N, dungeonProgress>=N, turn>=N, or a bare card name.
            Use deck_intent_get before analysis and recommendations.
            Use deck_intent_suggest to draft an intent section, then ask the user
            before calling deck_intent_set.
            deck_intent_set updates the workspace description and writes back to
            Archidekt only when the workspace has writeBack=true.
            """;
    }

    /// <summary>
    /// Returns redacted effective configuration values.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://config/effective", Name = "Effective Configuration")]
    [Description("Effective non-secret configuration values visible to the server.")]
    public string GetEffectiveConfiguration()
    {
        Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> pair in configuration.AsEnumerable())
        {
            if (
                pair.Value is not null
                && pair.Key.StartsWith("MtgMcp", StringComparison.OrdinalIgnoreCase)
            )
            {
                values[pair.Key] = pair.Value;
            }
        }

        return JsonSerializer.Serialize(SecretRedactor.Redact(values), JsonOptions);
    }

    /// <summary>
    /// Gets deck recommendation source status.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://sources/status", Name = "Recommendation Sources")]
    [Description("Configured deck recommendation source providers with stability, attribution, and permission notes.")]
    public string GetCorpusSources()
    {
        return JsonSerializer.Serialize(recommendations.ListCorpusSources(), JsonOptions);
    }

    /// <summary>
    /// Gets version and runtime details for the running server.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://server/info", Name = "Server Info")]
    [Description("Version, git commit, git branch, operation mode, data directory, and runtime details for the running mtg-mcp server.")]
    public string GetServerInfo()
    {
        return JsonSerializer.Serialize(serverInfo.GetInfo(), JsonOptions);
    }

    /// <summary>
    /// Returns redacted provider credential availability.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://providers/{provider}/auth-status", Name = "Provider Auth Status")]
    [Description("Redacted provider credential availability status for archidekt, playgroup, or reddit.")]
    public async Task<string> GetProviderAuthStatusAsync(
        [Description("Provider key: archidekt, playgroup, or reddit.")]
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException(
                "Provider must be archidekt, playgroup, or reddit.",
                nameof(provider)
            );
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "archidekt" => JsonSerializer.Serialize(
                await archidektGateway.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false),
                JsonOptions
            ),
            "playgroup" => JsonSerializer.Serialize(
                await playgroups.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false),
                JsonOptions
            ),
            "reddit" => JsonSerializer.Serialize(
                await reddit.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false),
                JsonOptions
            ),
            _ => throw new ArgumentException(
                "Provider must be archidekt, playgroup, or reddit.",
                nameof(provider)
            ),
        };
    }
}
