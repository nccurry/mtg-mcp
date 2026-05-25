using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Provides mtg resources behavior.
/// </summary>
[McpServerResourceType]
public sealed class MtgResources
{
    /// <summary>
    /// Handles json options.
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
    /// Supplies corpus source status.
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
    /// Stores server version and runtime diagnostics.
    /// </summary>
    private readonly ServerInfoService serverInfo;

    /// <summary>
    /// Handles mtg resources.
    /// </summary>
    public MtgResources(
        DeckWorkspaceService decks,
        DeckRecommendationService recommendations,
        IConfiguration configuration,
        IArchidektGateway archidektGateway,
        OperationModeGuard operationMode,
        PlaygroupService playgroups,
        ServerInfoService serverInfo
    )
    {
        this.decks = decks;
        this.recommendations = recommendations;
        this.configuration = configuration;
        this.archidektGateway = archidektGateway;
        this.operationMode = operationMode;
        this.playgroups = playgroups;
        this.serverInfo = serverInfo;
    }

    /// <summary>
    /// Gets the deck.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://deck/{deckId}", Name = "Deck Workspace")]
    [Description("Full JSON representation of a saved deck workspace.")]
    public async Task<string> GetDeckAsync(
        string deckId,
        CancellationToken cancellationToken = default
    )
    {
        DeckWorkspace workspace = await decks
            .GetDeckResourceAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(workspace, JsonOptions);
    }

    /// <summary>
    /// Gets the deck summary.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://deck/{deckId}/summary", Name = "Deck Summary")]
    [Description("Summary, counts, validation status, and category list for a deck workspace.")]
    public async Task<string> GetDeckSummaryAsync(
        string deckId,
        CancellationToken cancellationToken = default
    )
    {
        object summary = await decks
            .GetDeckSummaryAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    /// <summary>
    /// Gets the deck intent.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://deck/{deckId}/intent", Name = "Deck Intent")]
    [Description("Parsed MTG MCP Deck Intent stored in the workspace description.")]
    public async Task<string> GetDeckIntentAsync(
        string deckId,
        CancellationToken cancellationToken = default
    )
    {
        DeckIntentResult intent = await decks
            .GetDeckIntentAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(intent, JsonOptions);
    }

    /// <summary>
    /// Gets the scryfall syntax cheatsheet.
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
    /// Gets the format rules.
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
    /// Gets the workspace selection guidance.
    /// </summary>
    [McpServerResource(
        UriTemplate = "mtg://usage/workspace-selection",
        Name = "Workspace Selection Guidance"
    )]
    [Description("Policy for choosing local versus Archidekt workspaces and when to ask the user.")]
    public string GetWorkspaceSelectionGuidance()
    {
        return """
            Use local mode when the user wants a new unsynced brew, a scratch deck, or an import from pasted deck text.
            Use Archidekt mode when the user provides an Archidekt deck id or URL,
            asks to update an online deck, or asks for Archidekt checkpoints.
            If local and Archidekt are both plausible, ask the user which workspace mode to use
            before creating or opening a workspace.
            Never enable Archidekt writeback unless the user explicitly asks to update,
            organize, tag, move cards, checkpoint, or otherwise persist changes to Archidekt.
            If Archidekt writeback intent is unclear, ask whether edits should write back
            to Archidekt or stay local-only.
            Prefer start_deck_workspace as the first deck workspace tool because it requires
            an explicit mode and writeback choice.
            """;
    }

    /// <summary>
    /// Gets the operation mode guidance.
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
            Local-meta scoring can use score_cards_for_playgroup_meta to score
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
            Win Routes use lines such as:
            Altar Loop: requires commander, repeatable-blink, card:Altar of the Brood; earliest turn 5; kind combo
            Supported route requirements are commander, repeatable-blink,
            card:<name>, role:<role>, tag:<tag>, mana>=N, tokens>=N,
            interactionHeld>=N, dungeonProgress>=N, turn>=N, or a bare card name.
            Use get_deck_intent before analysis and recommendations.
            Use suggest_deck_intent to draft an intent section, then ask the user
            before calling set_deck_intent.
            set_deck_intent updates the workspace description and writes back to
            Archidekt only when the workspace has writeBack=true.
            """;
    }

    /// <summary>
    /// Gets the effective configuration.
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
    /// Gets deck corpus source status.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://corpus/sources", Name = "Corpus Sources")]
    [Description("Enabled and planned deck-corpus sources with stability, attribution, and permission notes.")]
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
    /// Gets the archidekt auth status.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://archidekt/auth-status", Name = "Archidekt Auth Status")]
    [Description("Redacted Archidekt credential availability status.")]
    public async Task<string> GetArchidektAuthStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        AuthStatus status = await archidektGateway
            .GetAuthStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(status, JsonOptions);
    }

    /// <summary>
    /// Gets the Playgroup auth status.
    /// </summary>
    [McpServerResource(UriTemplate = "mtg://playgroup/auth-status", Name = "Playgroup Auth Status")]
    [Description("Redacted Playgroup.gg API-key and credentials-file availability status.")]
    public async Task<string> GetPlaygroupAuthStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        PlaygroupAuthStatus status = await playgroups
            .GetAuthStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(status, JsonOptions);
    }
}
