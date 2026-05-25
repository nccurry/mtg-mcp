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
        Name = "add_card",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Add a card to a deck workspace and write back to Archidekt when bound. Included Commander additions that exceed 100 cards are refused unless force=true.")]
    public Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity = 1,
        string category = DeckDefaults.Mainboard,
        bool force = false,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("add_card");
        return decks.AddCardAsync(workspaceId, cardName, quantity, category, force, cancellationToken);
    }

    /// <summary>
    /// Removes the card.
    /// </summary>
    [McpServerTool(
        Name = "remove_card",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Remove some or all copies of a card from a deck workspace.")]
    public Task<DeckChangeResult> RemoveCardAsync(
        string workspaceId,
        string cardName,
        int quantity = 1,
        string? category = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("remove_card");
        return decks.RemoveCardAsync(workspaceId, cardName, quantity, category, cancellationToken);
    }

    /// <summary>
    /// Sets the card quantity.
    /// </summary>
    [McpServerTool(
        Name = "set_card_quantity",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Set a card quantity, removing it when quantity is zero.")]
    public Task<DeckChangeResult> SetCardQuantityAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("set_card_quantity");
        return decks.SetCardQuantityAsync(
            workspaceId,
            cardName,
            quantity,
            category,
            cancellationToken
        );
    }

    /// <summary>
    /// Moves the card.
    /// </summary>
    [McpServerTool(
        Name = "move_card",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description(
        "Move a card by making the target the first Archidekt category, such as Mainboard, Sideboard, or Maybeboard."
    )]
    public Task<DeckChangeResult> MoveCardAsync(
        string workspaceId,
        string cardName,
        string toCategory,
        string? fromCategory = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("move_card");
        return decks.MoveCardAsync(
            workspaceId,
            cardName,
            toCategory,
            fromCategory,
            cancellationToken
        );
    }

    /// <summary>
    /// Updates the deck metadata.
    /// </summary>
    [McpServerTool(
        Name = "update_deck_metadata",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Update deck name, format, or description.")]
    public Task<DeckChangeResult> UpdateDeckMetadataAsync(
        string workspaceId,
        string? name = null,
        string? format = null,
        string? description = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("update_deck_metadata");
        return decks.UpdateDeckMetadataAsync(
            workspaceId,
            name,
            format,
            description,
            cancellationToken
        );
    }
}
