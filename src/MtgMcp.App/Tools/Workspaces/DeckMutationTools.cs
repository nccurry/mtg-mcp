using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes MCP tools for deck mutation.
/// </summary>
[McpServerToolType]
public sealed class DeckMutationTools
{
    /// <summary>
    /// Stores the decks.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Stores the operation mode.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates the MCP tools that mutate deck cards or metadata.
    /// </summary>
    public DeckMutationTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Adds the card.
    /// </summary>
    [McpServerTool(
        Name = "deck_add_card",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Add a card to a deck workspace and write back to Archidekt when bound. Included Commander additions that exceed 100 cards are refused unless force=true.")]
    public Task<object> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity = 1,
        string category = DeckDefaults.Mainboard,
        bool force = false,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_add_card");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.AddCardAsync(workspaceId, cardName, quantity, category, force, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Adds multiple cards.
    /// </summary>
    [McpServerTool(
        Name = "deck_add_cards_bulk",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Add multiple cards to a deck workspace in one persisted mutation, with primary and secondary categories per card.")]
    public Task<object> AddCardsBulkAsync(
        string workspaceId,
        BulkDeckCardAdd[] cards,
        bool force = false,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_add_cards_bulk");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.AddCardsBulkAsync(workspaceId, cards, force, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Removes the card.
    /// </summary>
    [McpServerTool(
        Name = "deck_remove_card",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Remove some or all copies of a card from a deck workspace.")]
    public Task<object> RemoveCardAsync(
        string workspaceId,
        string cardName,
        int quantity = 1,
        string? category = null,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_remove_card");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.RemoveCardAsync(workspaceId, cardName, quantity, category, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Sets the card quantity.
    /// </summary>
    [McpServerTool(
        Name = "deck_set_card_quantity",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Set a card quantity, removing it when quantity is zero.")]
    public Task<object> SetCardQuantityAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category = null,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_set_card_quantity");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.SetCardQuantityAsync(
                workspaceId,
                cardName,
                quantity,
                category,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Moves the card.
    /// </summary>
    [McpServerTool(
        Name = "deck_move_card",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description(
        "Move a card by making the target the first Archidekt category, such as Mainboard, Sideboard, or Maybeboard."
    )]
    public Task<object> MoveCardAsync(
        string workspaceId,
        string cardName,
        string toCategory,
        string? fromCategory = null,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_move_card");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.MoveCardAsync(
                workspaceId,
                cardName,
                toCategory,
                fromCategory,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Updates the deck metadata.
    /// </summary>
    [McpServerTool(
        Name = "deck_update_metadata",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Update deck name, format, or description.")]
    public Task<object> UpdateDeckMetadataAsync(
        string workspaceId,
        string? name = null,
        string? format = null,
        string? description = null,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_update_metadata");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.UpdateDeckMetadataAsync(
                workspaceId,
                name,
                format,
                description,
                cancellationToken),
            cancellationToken);
    }
}
