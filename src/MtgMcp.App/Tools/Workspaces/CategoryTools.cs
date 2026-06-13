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
    /// Creates the MCP tools that manage deck categories.
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
        Name = "deck_add_card_category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Append an Archidekt-style secondary category tag without changing categories[0], the primary category.")]
    public Task<object> AddCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_add_card_category");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.AddCardCategoryAsync(workspaceId, cardName, category, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Removes the card category.
    /// </summary>
    [McpServerTool(
        Name = "deck_remove_card_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Remove an Archidekt-style category tag; if it was first, the next category becomes primary.")]
    public Task<object> RemoveCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_remove_card_category");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.RemoveCardCategoryAsync(workspaceId, cardName, category, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Sets the primary card category.
    /// </summary>
    [McpServerTool(
        Name = "deck_set_primary_card_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description(
        "Set the first Archidekt category, which is the card's primary category for deck organization."
    )]
    public Task<object> SetPrimaryCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_set_primary_card_category");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.SetPrimaryCardCategoryAsync(
                workspaceId,
                cardName,
                category,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Creates the category.
    /// </summary>
    [McpServerTool(
        Name = "deck_create_category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Create or update a deck category.")]
    public Task<object> CreateCategoryAsync(
        string workspaceId,
        string category,
        bool includedInDeck = true,
        bool includedInPrice = true,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_create_category");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.CreateCategoryAsync(
                workspaceId,
                category,
                includedInDeck,
                includedInPrice,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    [McpServerTool(
        Name = "deck_rename_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Rename a deck category and update card category references.")]
    public Task<object> RenameCategoryAsync(
        string workspaceId,
        string oldName,
        string newName,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_rename_category");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.RenameCategoryAsync(workspaceId, oldName, newName, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the category.
    /// </summary>
    [McpServerTool(
        Name = "deck_delete_category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Delete a category and move affected cards to a replacement category.")]
    public Task<object> DeleteCategoryAsync(
        string workspaceId,
        string category,
        string replacementCategory = DeckDefaults.Mainboard,
        bool? includeWorkspace = null,
        [Description("Output detail level: summary, normal, or full. Explicit detailLevel overrides includeWorkspace.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("deck_delete_category");
        return CompactMutationPresenter.RunMutationAsync(
            decks,
            workspaceId,
            includeWorkspace,
            detailLevel,
            () => decks.DeleteCategoryAsync(
                workspaceId,
                category,
                replacementCategory,
                cancellationToken),
            cancellationToken);
    }
}
