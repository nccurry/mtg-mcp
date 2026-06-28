using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes local card collection and ownership MCP tools.
/// </summary>
[McpServerToolType]
public sealed class CollectionTools
{
    /// <summary>
    /// Manages local collection state and ownership comparisons.
    /// </summary>
    private readonly CardCollectionService collection;

    /// <summary>
    /// Guards local collection writes.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates collection tools for the MCP surface.
    /// </summary>
    public CollectionTools(CardCollectionService collection, OperationModeGuard operationMode)
    {
        this.collection = collection;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Replaces or merges the local card collection.
    /// </summary>
    [McpServerTool(Name = "collection_set", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Replace or merge the local card collection from structured entries, optional decklist text, and an optional workspace. " +
        "Writes local planning state only; replace=true overwrites the collection, replace=false adds to existing quantities.")]
    public Task<CardCollectionSetResult> SetCollectionAsync(
        CardCollectionEntry[]? entries = null,
        string? decklist = null,
        string? workspaceId = null,
        bool replace = true,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("collection_set");
        return collection.SetCollectionAsync(entries, decklist, workspaceId, replace, cancellationToken);
    }

    /// <summary>
    /// Gets the local card collection.
    /// </summary>
    [McpServerTool(Name = "collection_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get the local card collection with total owned quantity and sorted card rows.")]
    public Task<CardCollectionSnapshot> GetCollectionAsync(CancellationToken cancellationToken = default)
    {
        return collection.GetCollectionAsync(cancellationToken);
    }

    /// <summary>
    /// Compares the local collection against a workspace's included cards.
    /// </summary>
    [McpServerTool(Name = "collection_diff_workspace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compare local collection quantities against a workspace's included cards, including known missing replacement cost from cached prices.")]
    public Task<CollectionWorkspaceDiffResult> DiffWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return collection.DiffWorkspaceAsync(workspaceId, cancellationToken);
    }
}
