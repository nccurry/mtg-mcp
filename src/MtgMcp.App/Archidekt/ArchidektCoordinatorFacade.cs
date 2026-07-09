using MtgMcp.Archidekt;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Provides a thin synchronization boundary over pull, push, and binding workflows.
/// </summary>
internal sealed class ArchidektCoordinator
{
    /// <summary>Owns the complete shared synchronization state for one MCP host.</summary>
    private readonly ArchidektSynchronizationContext context;

    /// <summary>Owns remote-to-local preview and application workflows.</summary>
    private readonly ArchidektPullWorkflow pull;

    /// <summary>Owns diff and local-to-remote preview and application workflows.</summary>
    private readonly ArchidektPushWorkflow push;

    /// <summary>Owns remote deck creation and local provider-binding workflows.</summary>
    private readonly ArchidektBindingWorkflow bindings;

    /// <summary>Creates one App-owned composition boundary.</summary>
    internal ArchidektCoordinator(ArchidektService service, SqliteDeckStore deckStore)
    {
        context = new ArchidektSynchronizationContext(service, deckStore);
        pull = new ArchidektPullWorkflow(context);
        push = new ArchidektPushWorkflow(context);
        bindings = new ArchidektBindingWorkflow(context);
    }

    /// <summary>Gets the provider service for direct evidence and lifecycle tools.</summary>
    internal ArchidektService Service => context.Service;

    /// <summary>Computes local, baseline, and fresh-remote synchronization evidence.</summary>
    internal Task<OperationResult<ArchidektSyncDiff>> DiffAsync(
        Guid localDeckId,
        CancellationToken cancellationToken)
    {
        return push.DiffAsync(localDeckId, cancellationToken);
    }

    /// <summary>Previews one fresh remote-to-local synchronization.</summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPullAsync(
        string remoteDeckId,
        Guid? localDeckId,
        CancellationToken cancellationToken)
    {
        return pull.PreviewAsync(remoteDeckId, localDeckId, cancellationToken);
    }

    /// <summary>Applies one unchanged remote-to-local preview.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken)
    {
        return pull.ApplyAsync(request, cancellationToken);
    }

    /// <summary>Previews one local-to-remote synchronization.</summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPushAsync(
        Guid localDeckId,
        CancellationToken cancellationToken)
    {
        return push.PreviewAsync(localDeckId, cancellationToken);
    }

    /// <summary>Applies one unchanged local-to-remote preview.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyPushAsync(
        ArchidektPushApplyRequest request,
        CancellationToken cancellationToken)
    {
        return push.ApplyAsync(request, cancellationToken);
    }

    /// <summary>Creates one remote deck and optionally binds it to unchanged local state.</summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> CreateRemoteDeckAsync(
        Guid? localDeckId,
        long? expectedLocalRevision,
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        return bindings.CreateRemoteDeckAsync(
            localDeckId,
            expectedLocalRevision,
            request,
            cancellationToken);
    }
}

/// <summary>
/// Owns remote-to-local synchronization preview and application.
/// </summary>
internal sealed class ArchidektPullWorkflow
{
    /// <summary>Stores the shared synchronization context.</summary>
    private readonly ArchidektSynchronizationContext context;

    /// <summary>Creates pull workflows around one shared context.</summary>
    internal ArchidektPullWorkflow(ArchidektSynchronizationContext context)
    {
        this.context = context;
    }

    /// <summary>Previews one remote-to-local synchronization.</summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewAsync(
        string remoteDeckId,
        Guid? localDeckId,
        CancellationToken cancellationToken)
    {
        return context.PreviewPullAsync(remoteDeckId, localDeckId, cancellationToken);
    }

    /// <summary>Applies one unchanged remote-to-local preview.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyAsync(
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken)
    {
        return context.ApplyPullAsync(request, cancellationToken);
    }
}

/// <summary>
/// Owns synchronization diff and local-to-remote preview and application.
/// </summary>
internal sealed class ArchidektPushWorkflow
{
    /// <summary>Stores the shared synchronization context.</summary>
    private readonly ArchidektSynchronizationContext context;

    /// <summary>Creates push workflows around one shared context.</summary>
    internal ArchidektPushWorkflow(ArchidektSynchronizationContext context)
    {
        this.context = context;
    }

    /// <summary>Computes local, baseline, and fresh-remote synchronization evidence.</summary>
    internal Task<OperationResult<ArchidektSyncDiff>> DiffAsync(
        Guid localDeckId,
        CancellationToken cancellationToken)
    {
        return context.DiffAsync(localDeckId, cancellationToken);
    }

    /// <summary>Previews one local-to-remote synchronization.</summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewAsync(
        Guid localDeckId,
        CancellationToken cancellationToken)
    {
        return context.PreviewPushAsync(localDeckId, cancellationToken);
    }

    /// <summary>Applies one unchanged local-to-remote preview.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyAsync(
        ArchidektPushApplyRequest request,
        CancellationToken cancellationToken)
    {
        return context.ApplyPushAsync(request, cancellationToken);
    }
}

/// <summary>
/// Owns remote deck creation and optional local binding application.
/// </summary>
internal sealed class ArchidektBindingWorkflow
{
    /// <summary>Stores the shared synchronization context.</summary>
    private readonly ArchidektSynchronizationContext context;

    /// <summary>Creates binding workflows around one shared context.</summary>
    internal ArchidektBindingWorkflow(ArchidektSynchronizationContext context)
    {
        this.context = context;
    }

    /// <summary>Creates one remote deck and optionally binds it to unchanged local state.</summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> CreateRemoteDeckAsync(
        Guid? localDeckId,
        long? expectedLocalRevision,
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        return context.CreateRemoteDeckAsync(
            localDeckId,
            expectedLocalRevision,
            request,
            cancellationToken);
    }
}
