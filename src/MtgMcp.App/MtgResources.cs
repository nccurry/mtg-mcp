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
    /// Handles mtg resources.
    /// </summary>
    public MtgResources(
        DeckWorkspaceService decks,
        IConfiguration configuration,
        IArchidektGateway archidektGateway,
        OperationModeGuard operationMode
    )
    {
        this.decks = decks;
        this.configuration = configuration;
        this.archidektGateway = archidektGateway;
        this.operationMode = operationMode;
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
            plan: read-only tools are allowed, but all tools that create, cache, mutate,
            checkpoint, or write back are blocked.
            read-only or ask: read-only tools are allowed, but all mutating tools are blocked.

            MCP tool annotations also mark tools as read-only/destructive/open-world
            so compatible clients can ask for approval before risky calls.
            If a blocked mutating tool is needed, ask the user to restart or reconfigure
            the MCP server with MTGMCP__OPERATION_MODE=apply.
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
}
