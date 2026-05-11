using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Provides deck intent tool behavior.
/// </summary>
[McpServerToolType]
public sealed class IntentTools
{
    /// <summary>
    /// Stores the decks service.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Stores the operation mode.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Handles intent tools.
    /// </summary>
    public IntentTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Gets deck intent from the workspace description.
    /// </summary>
    [McpServerTool(Name = "get_deck_intent", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read the human-readable MTG MCP Deck Intent section from a workspace description.")]
    public Task<DeckIntentResult> GetDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.GetDeckIntentAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Suggests deck intent from the current workspace.
    /// </summary>
    [McpServerTool(Name = "suggest_deck_intent", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Suggest a human-readable MTG MCP Deck Intent section from commander, categories, and current cards without saving it.")]
    public Task<DeckIntentResult> SuggestDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.SuggestDeckIntentAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Sets deck intent in the workspace description.
    /// </summary>
    [McpServerTool(Name = "set_deck_intent", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Insert or replace the MTG MCP Deck Intent section in the workspace description; Archidekt writeback persists it to the deck description.")]
    public Task<DeckIntentChangeResult> SetDeckIntentAsync(
        string workspaceId,
        string intentText,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("set_deck_intent");
        return decks.SetDeckIntentAsync(workspaceId, intentText, cancellationToken);
    }

    /// <summary>
    /// Clears deck intent from the workspace description.
    /// </summary>
    [McpServerTool(Name = "clear_deck_intent", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Remove the MTG MCP Deck Intent section from the workspace description while preserving other description text.")]
    public Task<DeckIntentChangeResult> ClearDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("clear_deck_intent");
        return decks.ClearDeckIntentAsync(workspaceId, cancellationToken);
    }
}
