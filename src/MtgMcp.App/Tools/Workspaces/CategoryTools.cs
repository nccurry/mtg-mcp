using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes MCP tools for category.
/// </summary>
[McpServerToolType]
public sealed class CategoryTools
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
    /// Handles category tools.
    /// </summary>
    public CategoryTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Adds the card category.
    /// </summary>
    [McpServerTool(
        Name = "add_card_category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Append an Archidekt-style secondary category tag without changing categories[0], the primary category.")]
    public Task<DeckChangeResult> AddCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("add_card_category");
        return decks.AddCardCategoryAsync(workspaceId, cardName, category, cancellationToken);
    }

    /// <summary>
    /// Removes the card category.
    /// </summary>
    [McpServerTool(
        Name = "remove_card_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Remove an Archidekt-style category tag; if it was first, the next category becomes primary.")]
    public Task<DeckChangeResult> RemoveCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("remove_card_category");
        return decks.RemoveCardCategoryAsync(workspaceId, cardName, category, cancellationToken);
    }

    /// <summary>
    /// Sets the primary card category.
    /// </summary>
    [McpServerTool(
        Name = "set_primary_card_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description(
        "Set the first Archidekt category, which is the card's primary category for deck organization."
    )]
    public Task<DeckChangeResult> SetPrimaryCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("set_primary_card_category");
        return decks.SetPrimaryCardCategoryAsync(
            workspaceId,
            cardName,
            category,
            cancellationToken
        );
    }

    /// <summary>
    /// Creates the category.
    /// </summary>
    [McpServerTool(
        Name = "create_category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Create or update a deck category.")]
    public Task<DeckChangeResult> CreateCategoryAsync(
        string workspaceId,
        string category,
        bool includedInDeck = true,
        bool includedInPrice = true,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("create_category");
        return decks.CreateCategoryAsync(
            workspaceId,
            category,
            includedInDeck,
            includedInPrice,
            cancellationToken
        );
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    [McpServerTool(
        Name = "rename_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Rename a deck category and update card category references.")]
    public Task<DeckChangeResult> RenameCategoryAsync(
        string workspaceId,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("rename_category");
        return decks.RenameCategoryAsync(workspaceId, oldName, newName, cancellationToken);
    }

    /// <summary>
    /// Deletes the category.
    /// </summary>
    [McpServerTool(
        Name = "delete_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Delete a category and move affected cards to a replacement category.")]
    public Task<DeckChangeResult> DeleteCategoryAsync(
        string workspaceId,
        string category,
        string replacementCategory = DeckDefaults.Mainboard,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("delete_category");
        return decks.DeleteCategoryAsync(
            workspaceId,
            category,
            replacementCategory,
            cancellationToken
        );
    }
}
