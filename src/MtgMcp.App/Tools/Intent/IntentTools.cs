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
    /// Creates the MCP tools for reading and editing deck intent.
    /// </summary>
    public IntentTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Gets deck intent from the workspace description.
    /// </summary>
    [McpServerTool(Name = "deck_intent_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "deck_intent_suggest", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "deck_intent_set", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Insert or replace the MTG MCP Deck Intent section in the workspace description; Archidekt writeback persists it to the deck description.")]
    public async Task<object> SetDeckIntentAsync(
        string workspaceId,
        string intentText,
        [Description("Deprecated: use detailLevel=full.")]
        bool includeWorkspace = false,
        [Description("Output detail level: summary, normal, or full. Deprecated: compact is accepted as summary.")]
        string detailLevel = "summary",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("deck_intent_set");
        DeckIntentResult before = await decks.GetDeckIntentAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckIntentChangeResult result = await decks.SetDeckIntentAsync(workspaceId, intentText, cancellationToken)
            .ConfigureAwait(false);
        return ShouldReturnFull(includeWorkspace, detailLevel)
            ? result
            : ToCompactResult(before, result);
    }

    /// <summary>
    /// Clears deck intent from the workspace description.
    /// </summary>
    [McpServerTool(Name = "deck_intent_clear", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Remove the MTG MCP Deck Intent section from the workspace description while preserving other description text.")]
    public async Task<object> ClearDeckIntentAsync(
        string workspaceId,
        [Description("Deprecated: use detailLevel=full.")]
        bool includeWorkspace = false,
        [Description("Output detail level: summary, normal, or full. Deprecated: compact is accepted as summary.")]
        string detailLevel = "summary",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("deck_intent_clear");
        DeckIntentResult before = await decks.GetDeckIntentAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckIntentChangeResult result = await decks.ClearDeckIntentAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return ShouldReturnFull(includeWorkspace, detailLevel)
            ? result
            : ToCompactResult(before, result);
    }

    /// <summary>
    /// Determines whether the caller requested the legacy full mutation shape.
    /// </summary>
    private static bool ShouldReturnFull(bool includeWorkspace, string? detailLevel)
    {
        return includeWorkspace
            || DetailLevelParser.Parse(detailLevel, allowCompactAlias: true) == DetailLevel.Full;
    }

    /// <summary>
    /// Builds compact intent mutation output for agent loops.
    /// </summary>
    private static CompactDeckIntentChangeResult ToCompactResult(
        DeckIntentResult before,
        DeckIntentChangeResult result)
    {
        bool changed = !before.IntentText.Equals(result.Intent.IntentText, StringComparison.Ordinal);
        return new CompactDeckIntentChangeResult
        {
            WorkspaceId = result.Intent.WorkspaceId,
            Changed = changed,
            IntentVersion = result.Intent.Intent?.Version,
            DescriptionUpdated = changed,
            ArchidektWriteBack = result.Persistence.Equals(DeckPersistence.ArchidektWriteBack, StringComparison.OrdinalIgnoreCase),
            Warnings = result.Intent.Warnings.ToList(),
            IntentSummary = BuildSummary(result.Intent),
            Persistence = result.Persistence,
            Message = result.Message
        };
    }

    /// <summary>
    /// Builds a bounded intent summary from a parsed result.
    /// </summary>
    private static DeckIntentSummary BuildSummary(DeckIntentResult result)
    {
        DeckIntent? intent = result.Intent;
        return new DeckIntentSummary
        {
            Found = result.Found,
            Commander = intent?.Commander,
            Archetype = intent?.Archetype,
            Goal = intent?.Goal,
            PowerLevel = intent?.PowerLevel,
            HeuristicProfile = intent?.HeuristicProfile,
            SimulationProfile = intent?.SimulationProfile,
            PackageTemplate = intent?.PackageTemplate,
            ArchetypeTags = intent?.ArchetypeTags.ToList() ?? []
        };
    }
}
