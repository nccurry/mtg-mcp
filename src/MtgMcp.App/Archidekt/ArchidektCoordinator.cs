using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Composes provider evidence with the revisioned local deck store without leaking persistence into the adapter.
/// </summary>
internal sealed class ArchidektSynchronizationContext
{
    /// <summary>
    /// Identifies local bindings owned by this provider adapter.
    /// </summary>
    private const string ProviderName = "archidekt";

    /// <summary>
    /// Explains why a pull is refused over a deck whose baseline an earlier version recorded, and how to leave.
    /// </summary>
    private const string UnverifiedPullConflictMessage =
        "The local deck may have changed since its synchronization baseline. The earlier version that made the " +
        "baseline did not record category colors or ordering, so the server cannot tell whether they changed. " +
        "Push the deck to keep the local content (the push is refused too if Archidekt also changed), or preview " +
        "and apply the pull with discardLocalChanges to take the Archidekt state, which drops the local edits.";

    /// <summary>
    /// Provides revisioned local state and transactional baseline updates.
    /// </summary>
    private readonly SqliteDeckStore deckStore;

    /// <summary>
    /// Creates one App-owned composition boundary.
    /// </summary>
    internal ArchidektSynchronizationContext(ArchidektService service, SqliteDeckStore deckStore)
    {
        Service = service ?? throw new ArgumentNullException(nameof(service));
        this.deckStore = deckStore ?? throw new ArgumentNullException(nameof(deckStore));
    }

    /// <summary>
    /// Gets the provider service for direct evidence and lifecycle tools.
    /// </summary>
    internal ArchidektService Service { get; }

    /// <summary>
    /// Computes local/baseline/fresh-remote synchronization evidence for one bound local deck.
    /// </summary>
    internal async Task<OperationResult<ArchidektSyncDiff>> DiffAsync(
        Guid localDeckId,
        CancellationToken cancellationToken)
    {
        ArchidektOperationScope operationScope = Service.BeginOperation();
        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            localDeckId,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return OperationFailure.Forward<DeckDocument, ArchidektSyncDiff>(localResult);
        }

        OperationResult<BindingState> stateResult = await LoadBindingStateAsync(
            local.Data,
            cancellationToken).ConfigureAwait(false);
        if (stateResult is not OperationSuccess<BindingState> state)
        {
            return OperationFailure.Forward<BindingState, ArchidektSyncDiff>(stateResult);
        }

        OperationResult<RemoteDeckSnapshot> remoteResult = await GetBoundRemoteAsync(
            state.Data.Binding.RemoteId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (remoteResult is not OperationSuccess<RemoteDeckSnapshot> remote)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektSyncDiff>(remoteResult);
        }

        return new OperationSuccess<ArchidektSyncDiff>(ArchidektSyncPlanner.Diff(
            local.Data,
            ArchidektLocalMapper.ToRemoteTarget(
                local.Data,
                state.Data.Baseline,
                state.Data.Baseline.RemoteSnapshot),
            remote.Data,
            state.Data.Baseline));
    }

    /// <summary>
    /// Previews a fresh remote-to-local replacement or local creation without writing either system.
    /// </summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPullAsync(
        string remoteDeckId,
        Guid? localDeckId,
        CancellationToken cancellationToken)
    {
        return PreviewPullAsync(
            remoteDeckId,
            localDeckId,
            discardLocalChanges: false,
            Service.BeginOperation(),
            cancellationToken);
    }

    /// <summary>
    /// Previews a pull, optionally taking the Archidekt state over local edits made since the baseline.
    /// </summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPullAsync(
        string remoteDeckId,
        Guid? localDeckId,
        bool discardLocalChanges,
        CancellationToken cancellationToken)
    {
        return PreviewPullAsync(
            remoteDeckId,
            localDeckId,
            discardLocalChanges,
            Service.BeginOperation(),
            cancellationToken);
    }

    /// <summary>
    /// Previews a pull while charging an existing composed-tool request scope.
    /// </summary>
    private async Task<OperationResult<ArchidektSyncPreview>> PreviewPullAsync(
        string remoteDeckId,
        Guid? localDeckId,
        bool discardLocalChanges,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        OperationResult<RemoteDeckSnapshot> remoteResult = await Service.GetDeckAsync(
            remoteDeckId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (remoteResult is not OperationSuccess<RemoteDeckSnapshot> remote)
        {
            OperationResult<RemoteDeckSnapshot> classified = await ClassifyPullRemoteFailureAsync(
                remoteResult,
                remoteDeckId,
                localDeckId,
                operationScope,
                cancellationToken).ConfigureAwait(false);
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektSyncPreview>(classified);
        }

        if (discardLocalChanges && localDeckId is null)
        {
            return new OperationInvalidInput(
                "discard-needs-local-deck",
                "discardLocalChanges replaces an existing local deck; pass localDeckId or leave it off.",
                "discardLocalChanges");
        }

        if (localDeckId is null)
        {
            ArchidektRemoteOperation operation = new(
                1,
                "local-deck-create",
                remote.Data.RemoteId,
                "Create one local deck from fresh Archidekt evidence.");
            string fingerprint = PullPreviewFingerprint(
                remote.Data,
                localDeckId: null,
                localRevision: null,
                discardLocalChanges: false,
                [operation]);
            return new OperationSuccess<ArchidektSyncPreview>(new ArchidektSyncPreview(
                "pull",
                LocalDeckId: null,
                LocalRevision: null,
                remote.Data.RemoteId,
                remote.Data.RemoteFingerprint,
                remote.Data.ContentFingerprint,
                fingerprint,
                HasConflicts: false,
                Differences: [],
                [operation],
                PredictedProviderRequests: 1));
        }

        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            localDeckId.Value,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return OperationFailure.Forward<DeckDocument, ArchidektSyncPreview>(localResult);
        }

        OperationResult<DeckProviderBinding?> bindingResult = ResolveBinding(local.Data);
        if (bindingResult is not OperationSuccess<DeckProviderBinding?> bindingSuccess)
        {
            return OperationFailure.Forward<DeckProviderBinding?, ArchidektSyncPreview>(bindingResult);
        }

        DeckProviderBinding? binding = bindingSuccess.Data;
        List<ArchidektDifference> differences = [];
        bool conflicts = false;
        if (binding is not null)
        {
            if (!string.Equals(binding.RemoteId, remote.Data.RemoteId, StringComparison.Ordinal))
            {
                return new OperationConflict(
                    "binding-remote-mismatch",
                    "The local deck is bound to a different Archidekt deck.");
            }

            OperationResult<BindingState> stateResult = await LoadBindingStateAsync(
                local.Data,
                cancellationToken).ConfigureAwait(false);
            if (stateResult is not OperationSuccess<BindingState> state)
            {
                return OperationFailure.Forward<BindingState, ArchidektSyncPreview>(stateResult);
            }

            ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
                local.Data,
                ArchidektLocalMapper.ToRemoteTarget(
                    local.Data,
                    state.Data.Baseline,
                    state.Data.Baseline.RemoteSnapshot),
                remote.Data,
                state.Data.Baseline);
            differences.AddRange(diff.Differences);
            conflicts = HasLocalChanges(diff) && !discardLocalChanges;
        }

        ArchidektRemoteOperation replace = new(
            1,
            "local-deck-replace",
            local.Data.DeckId.ToString("D"),
discardLocalChanges
                ? "Replace local content transactionally from fresh Archidekt evidence, discarding local changes."
                : "Replace local content transactionally from fresh Archidekt evidence.");
        string previewFingerprint = PullPreviewFingerprint(
            remote.Data,
            local.Data.DeckId,
            local.Data.Revision,
            discardLocalChanges,
            [replace]);
        return new OperationSuccess<ArchidektSyncPreview>(new ArchidektSyncPreview(
            "pull",
            local.Data.DeckId,
            local.Data.Revision,
            remote.Data.RemoteId,
            remote.Data.RemoteFingerprint,
            remote.Data.ContentFingerprint,
            previewFingerprint,
            conflicts,
            differences,
            [replace],
            PredictedProviderRequests: 1));
    }

    /// <summary>
    /// Applies one unchanged pull preview using one transactional local mutation.
    /// </summary>
    internal async Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArchidektOperationScope operationScope = Service.BeginOperation();
        OperationResult<ArchidektSyncPreview> previewResult = await PreviewPullAsync(
            request.RemoteDeckId,
            request.LocalDeckId,
            request.DiscardLocalChanges,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (previewResult is not OperationSuccess<ArchidektSyncPreview> preview)
        {
            return OperationFailure.Forward<ArchidektSyncPreview, ArchidektApplyResult>(previewResult);
        }

        if (preview.Data.HasConflicts)
        {
            return new OperationConflict(
                "pull-conflict",
                HasUnverifiedLocalChange(preview.Data.Differences)
                    ? UnverifiedPullConflictMessage
                    : "The local deck changed since its synchronization baseline. Preview the pull with " +
                    "discardLocalChanges to take the Archidekt state instead, which drops the local edits.",
                new ConflictState(preview.Data.LocalRevision, preview.Data.RemoteFingerprint));
        }

        if (!string.Equals(
                request.ExpectedRemoteFingerprint,
                preview.Data.RemoteFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(request.PreviewFingerprint, preview.Data.PreviewFingerprint, StringComparison.Ordinal) ||
            request.ExpectedLocalRevision != preview.Data.LocalRevision)
        {
            return new OperationConflict(
                "pull-preview-changed",
                "The local or remote deck changed after the pull preview.",
                new ConflictState(preview.Data.LocalRevision, preview.Data.RemoteFingerprint));
        }

        OperationResult<RemoteDeckSnapshot> remoteResult = await Service.GetDeckAsync(
            request.RemoteDeckId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (remoteResult is not OperationSuccess<RemoteDeckSnapshot> remote)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektApplyResult>(remoteResult);
        }

        if (!string.Equals(
            remote.Data.RemoteFingerprint,
            request.ExpectedRemoteFingerprint,
            StringComparison.Ordinal))
        {
            return new OperationConflict(
                "remote-deck-changed",
                "The Archidekt deck changed after the pull preview.",
                new ConflictState(RemoteFingerprint: remote.Data.RemoteFingerprint));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeckProviderBinding binding = BuildBinding(
            request.LocalDeckId,
            remote.Data,
            existing: null,
            lastPulledAtUtc: now,
            lastPushedAtUtc: null);
        DeckDocument committed;
        if (request.LocalDeckId is null)
        {
            DeckCreateRequest create = ArchidektLocalMapper.ToCreateRequest(remote.Data, binding);
            string baseline = ArchidektLocalMapper.CreateBaseline(create, remote.Data);
            OperationResult<DeckDocument> created = await deckStore.CreateSynchronizedAsync(
                create,
                new DeckSyncBaseline(binding.BindingId, baseline),
                cancellationToken).ConfigureAwait(false);
            if (created is not OperationSuccess<DeckDocument> success)
            {
                return OperationFailure.Forward<DeckDocument, ArchidektApplyResult>(created);
            }

            committed = success.Data;
        }
        else
        {
            OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
                request.LocalDeckId.Value,
                cancellationToken).ConfigureAwait(false);
            if (localResult is not OperationSuccess<DeckDocument> local)
            {
                return OperationFailure.Forward<DeckDocument, ArchidektApplyResult>(localResult);
            }

            OperationResult<DeckProviderBinding?> bindingResult = ResolveBinding(local.Data);
            if (bindingResult is not OperationSuccess<DeckProviderBinding?> bindingSuccess)
            {
                return OperationFailure.Forward<DeckProviderBinding?, ArchidektApplyResult>(bindingResult);
            }

            DeckProviderBinding? existing = bindingSuccess.Data;
            binding = BuildBinding(
                request.LocalDeckId,
                remote.Data,
                existing,
                now,
                existing?.LastPushedAtUtc);
            DeckCreateRequest target = ArchidektLocalMapper.ToCreateRequest(
                remote.Data,
                binding,
                local.Data.DeckId);
            string baseline = ArchidektLocalMapper.CreateBaseline(target, remote.Data);
            IReadOnlyList<DeckChange> changes = ArchidektLocalMapper.BuildPullChanges(
                local.Data,
                remote.Data,
                binding,
                baseline);
            OperationResult<DeckDocument> updated = await deckStore.ApplyChangesAsync(
                local.Data.DeckId,
                request.ExpectedLocalRevision!.Value,
                changes,
                cancellationToken).ConfigureAwait(false);
            if (updated is not OperationSuccess<DeckDocument> success)
            {
                return OperationFailure.Forward<DeckDocument, ArchidektApplyResult>(updated);
            }

            committed = success.Data;
        }

        return new OperationSuccess<ArchidektApplyResult>(new ArchidektApplyResult(
            "applied",
            committed.DeckId,
            committed.Revision,
            remote.Data.RemoteId,
            remote.Data.RemoteFingerprint,
            [new ArchidektOperationStatus(
                1,
                request.LocalDeckId is null ? "local-deck-create" : "local-deck-replace",
                committed.DeckId.ToString("D"),
                "applied",
                "Committed one transactional local synchronization update.")]));
    }

    /// <summary>
    /// Previews local-to-remote primitive operations while refusing any remote drift since baseline.
    /// </summary>
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPushAsync(
        Guid localDeckId,
        CancellationToken cancellationToken)
    {
        return PreviewPushAsync(localDeckId, Service.BeginOperation(), cancellationToken);
    }

    /// <summary>
    /// Previews a push while charging an existing composed-tool request scope.
    /// </summary>
    private async Task<OperationResult<ArchidektSyncPreview>> PreviewPushAsync(
        Guid localDeckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            localDeckId,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return OperationFailure.Forward<DeckDocument, ArchidektSyncPreview>(localResult);
        }

        OperationResult<BindingState> stateResult = await LoadBindingStateAsync(
            local.Data,
            cancellationToken).ConfigureAwait(false);
        if (stateResult is not OperationSuccess<BindingState> state)
        {
            return OperationFailure.Forward<BindingState, ArchidektSyncPreview>(stateResult);
        }

        OperationResult<RemoteDeckSnapshot> remoteResult = await GetBoundRemoteAsync(
            state.Data.Binding.RemoteId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (remoteResult is not OperationSuccess<RemoteDeckSnapshot> remote)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektSyncPreview>(remoteResult);
        }

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            local.Data,
            ArchidektLocalMapper.ToRemoteTarget(
                local.Data,
                state.Data.Baseline,
                state.Data.Baseline.RemoteSnapshot),
            remote.Data,
            state.Data.Baseline);
        bool remoteChanged = HasRemoteChanges(diff);
        RemoteDeckSnapshot target = ArchidektLocalMapper.ToRemoteTarget(
            local.Data,
            state.Data.Baseline,
            remote.Data);
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(remote.Data, target);
        IReadOnlyList<ArchidektZoneContradiction> contradictions =
            ArchidektSyncPlanner.FindZoneContradictions(plan, target);
        if (contradictions.Count > 0)
        {
            return new OperationInvalidInput("entry-zone-contradicts-category", DescribeContradictions(contradictions));
        }

        string previewFingerprint = PushPreviewFingerprint(local.Data, remote.Data, plan);
        return new OperationSuccess<ArchidektSyncPreview>(new ArchidektSyncPreview(
            "push",
            local.Data.DeckId,
            local.Data.Revision,
            remote.Data.RemoteId,
            remote.Data.RemoteFingerprint,
            target.ContentFingerprint,
            previewFingerprint,
            remoteChanged,
            diff.Differences,
            plan.PublicOperations,
            plan.PredictedProviderRequests));
    }

    /// <summary>
    /// Explains which entries the push cannot place, and what to change.
    /// </summary>
    private static string DescribeContradictions(IReadOnlyList<ArchidektZoneContradiction> contradictions)
    {
        IEnumerable<string> shown = contradictions.Take(5).Select(value =>
            $"'{value.CardName}' (zone '{value.Zone}', but its categories give zone '{value.ExpectedZone}')");
        string more = contradictions.Count > 5 ? $" and {contradictions.Count - 5} more" : string.Empty;
        return "Archidekt keeps no zone of its own: a card's zone comes from its categories (a category named " +
            "Commander, Sideboard or Maybeboard, or the commander category; otherwise main). These cards would come " +
            $"back in another zone: {string.Join("; ", shown)}{more}. Change the zone, or add or remove the matching " +
            "category.";
    }

    /// <summary>
    /// Applies one unchanged push preview and updates its local baseline only after remote verification. When the
    /// verification finds a difference, the operations that were applied are still recorded, so a created card is
    /// linked to its Archidekt row and is not added again by the next push.
    /// </summary>
    internal async Task<OperationResult<ArchidektApplyResult>> ApplyPushAsync(
        ArchidektPushApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArchidektOperationScope operationScope = Service.BeginOperation();
        OperationResult<ArchidektSyncPreview> previewResult = await PreviewPushAsync(
            request.LocalDeckId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (previewResult is not OperationSuccess<ArchidektSyncPreview> preview)
        {
            return OperationFailure.Forward<ArchidektSyncPreview, ArchidektApplyResult>(previewResult);
        }

        if (preview.Data.HasConflicts)
        {
            return new OperationConflict(
                "push-conflict",
                "The Archidekt deck changed since its synchronization baseline.",
                new ConflictState(preview.Data.LocalRevision, preview.Data.RemoteFingerprint));
        }

        if (preview.Data.LocalRevision != request.ExpectedLocalRevision ||
            !string.Equals(
                preview.Data.RemoteFingerprint,
                request.ExpectedRemoteFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                preview.Data.PreviewFingerprint,
                request.PreviewFingerprint,
                StringComparison.Ordinal))
        {
            return new OperationConflict(
                "push-preview-changed",
                "The local or remote deck changed after the push preview.",
                new ConflictState(preview.Data.LocalRevision, preview.Data.RemoteFingerprint));
        }

        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            request.LocalDeckId,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return OperationFailure.Forward<DeckDocument, ArchidektApplyResult>(localResult);
        }

        OperationResult<BindingState> stateResult = await LoadBindingStateAsync(
            local.Data,
            cancellationToken).ConfigureAwait(false);
        if (stateResult is not OperationSuccess<BindingState> state)
        {
            return OperationFailure.Forward<BindingState, ArchidektApplyResult>(stateResult);
        }

        OperationResult<RemoteDeckSnapshot> currentResult = await GetBoundRemoteAsync(
            state.Data.Binding.RemoteId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (currentResult is not OperationSuccess<RemoteDeckSnapshot> current)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektApplyResult>(currentResult);
        }

        RemoteDeckSnapshot target = ArchidektLocalMapper.ToRemoteTarget(
            local.Data,
            state.Data.Baseline,
            current.Data);
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current.Data, target);
        OperationResult<ArchidektApplyResult> appliedResult = await Service.ApplyRemoteTargetAsync(
            target,
            request.ExpectedRemoteFingerprint,
            plan.PlanFingerprint,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (appliedResult is not OperationSuccess<ArchidektApplyResult> applied ||
            applied.Data.Outcome is not ("applied" or "verification-mismatch"))
        {
            return appliedResult;
        }

        OperationResult<RemoteDeckSnapshot> verifiedResult = await Service.GetDeckAsync(
            state.Data.Binding.RemoteId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (verifiedResult is not OperationSuccess<RemoteDeckSnapshot> verified)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektApplyResult>(verifiedResult);
        }

        DeckProviderBinding binding = state.Data.Binding with
        {
            BaselineFingerprint = verified.Data.RemoteFingerprint,
            LastPushedAtUtc = DateTimeOffset.UtcNow,
        };
        (string baseline, IReadOnlyList<Guid>? unlinkedLocalIds) =
            ArchidektLocalMapper.CreatePushBaseline(
                local.Data,
                verified.Data,
                target,
                verificationMismatch: applied.Data.Outcome == "verification-mismatch");
        OperationResult<DeckDocument> baselineUpdate = await deckStore.ApplyChangesAsync(
            local.Data.DeckId,
            local.Data.Revision,
            [new UpsertDeckProviderBindingChange(binding, baseline)],
            cancellationToken).ConfigureAwait(false);
        if (baselineUpdate is not OperationSuccess<DeckDocument> updated)
        {
            return OperationFailure.Forward<DeckDocument, ArchidektApplyResult>(baselineUpdate);
        }

        return new OperationSuccess<ArchidektApplyResult>(applied.Data with
        {
            LocalDeckId = updated.Data.DeckId,
            LocalRevision = updated.Data.Revision,
            FinalRemoteFingerprint = verified.Data.RemoteFingerprint,
            UnlinkedLocalIds = unlinkedLocalIds,
        });
    }

    /// <summary>
    /// Creates one remote shell and optionally binds it to an unchanged local deck without assuming content equality.
    /// </summary>
    internal async Task<OperationResult<RemoteDeckSnapshot>> CreateRemoteDeckAsync(
        Guid? localDeckId,
        long? expectedLocalRevision,
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        DeckDocument? local = null;
        if (localDeckId is not null)
        {
            OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
                localDeckId.Value,
                cancellationToken).ConfigureAwait(false);
            if (localResult is not OperationSuccess<DeckDocument> success)
            {
                return OperationFailure.Forward<DeckDocument, RemoteDeckSnapshot>(localResult);
            }

            if (success.Data.Revision != expectedLocalRevision)
            {
                return new OperationConflict(
                    "local-deck-changed",
                    "The local deck changed before remote creation.",
                    new ConflictState(Revision: success.Data.Revision));
            }

            OperationResult<DeckProviderBinding?> bindingResult = ResolveBinding(success.Data);
            if (bindingResult is not OperationSuccess<DeckProviderBinding?> bindingSuccess)
            {
                return OperationFailure.Forward<DeckProviderBinding?, RemoteDeckSnapshot>(bindingResult);
            }

            if (bindingSuccess.Data is not null)
            {
                return new OperationConflict(
                    "binding-already-exists",
                    "The local deck already has an Archidekt binding.");
            }

            local = success.Data;
        }

        OperationResult<RemoteDeckSnapshot> createdResult = await Service.CreateDeckAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        if (createdResult is not OperationSuccess<RemoteDeckSnapshot> created || local is null)
        {
            return createdResult;
        }

        DeckProviderBinding binding = BuildBinding(
            local.DeckId,
            created.Data,
            existing: null,
            lastPulledAtUtc: null,
            lastPushedAtUtc: null) with
        {
            BaselineFingerprint = null,
        };
        OperationResult<DeckDocument> bindResult = await deckStore.ApplyChangesAsync(
            local.DeckId,
            local.Revision,
            [new UpsertDeckProviderBindingChange(binding, CanonicalBaseline: null)],
            cancellationToken).ConfigureAwait(false);
        return bindResult is OperationSuccess<DeckDocument>
            ? createdResult
            : OperationFailure.Forward<DeckDocument, RemoteDeckSnapshot>(bindResult);
    }

    /// <summary>
    /// Loads and validates one Archidekt binding and its required canonical baseline.
    /// </summary>
    private async Task<OperationResult<BindingState>> LoadBindingStateAsync(
        DeckDocument local,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckProviderBinding?> bindingResult = ResolveBinding(local);
        if (bindingResult is not OperationSuccess<DeckProviderBinding?> bindingSuccess)
        {
            return OperationFailure.Forward<DeckProviderBinding?, BindingState>(bindingResult);
        }

        DeckProviderBinding? binding = bindingSuccess.Data;
        if (binding is null)
        {
            return new OperationConflict(
                "binding-missing",
                "The local deck has no Archidekt binding.");
        }

        OperationResult<DeckSyncBaseline> baselineResult = await deckStore.GetSyncBaselineAsync(
            local.DeckId,
            binding.BindingId,
            cancellationToken).ConfigureAwait(false);
        if (baselineResult is OperationNotFound)
        {
            return new OperationConflict(
                "baseline-missing",
                "The Archidekt binding has no synchronization baseline.");
        }

        if (baselineResult is not OperationSuccess<DeckSyncBaseline> baseline)
        {
            return OperationFailure.Forward<DeckSyncBaseline, BindingState>(baselineResult);
        }

        try
        {
            ArchidektSyncBaseline parsed = ArchidektLocalMapper.ParseBaseline(
                baseline.Data.CanonicalSnapshot);
            if (!string.Equals(parsed.RemoteDeckId, binding.RemoteId, StringComparison.Ordinal))
            {
                return new OperationUnavailable(
                    "baseline-unavailable",
                    "The Archidekt synchronization baseline does not match its binding.");
            }

            return new OperationSuccess<BindingState>(new BindingState(binding, parsed));
        }
        catch (InvalidDataException)
        {
            return new OperationUnavailable(
                "baseline-unavailable",
                "The Archidekt synchronization baseline is corrupt.");
        }
    }

    /// <summary>
    /// Finds the one allowed Archidekt binding and rejects ambiguous local state.
    /// </summary>
    private static OperationResult<DeckProviderBinding?> ResolveBinding(DeckDocument local)
    {
        DeckProviderBinding[] bindings = local.ProviderBindings
            .Where(value => value.Provider.Equals(ProviderName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return bindings.Length switch
        {
            0 => new OperationSuccess<DeckProviderBinding?>(null),
            1 => new OperationSuccess<DeckProviderBinding?>(bindings[0]),
            _ => new OperationUnavailable(
                "binding-unavailable",
                "The local deck has ambiguous Archidekt bindings."),
        };
    }

    /// <summary>
    /// Creates or refreshes provider-neutral binding metadata from verified remote evidence.
    /// </summary>
    private static DeckProviderBinding BuildBinding(
        Guid? localDeckId,
        RemoteDeckSnapshot remote,
        DeckProviderBinding? existing,
        DateTimeOffset? lastPulledAtUtc,
        DateTimeOffset? lastPushedAtUtc)
    {
        _ = localDeckId;
        return new DeckProviderBinding(
            existing?.BindingId ?? Guid.CreateVersion7(),
            ProviderName,
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            remote.RemoteFingerprint,
            lastPulledAtUtc,
            lastPushedAtUtc);
    }

    /// <summary>
    /// Computes a guarded pull preview identity from exact local and remote evidence.
    /// </summary>
    private static string PullPreviewFingerprint(
        RemoteDeckSnapshot remote,
        Guid? localDeckId,
        long? localRevision,
        bool discardLocalChanges,
        IReadOnlyList<ArchidektRemoteOperation> operations)
    {
        return Fingerprint(new
        {
            direction = "pull",
            localDeckId,
            localRevision,
            discardLocalChanges,
            remote.RemoteId,
            remote.RemoteFingerprint,
            remote.ContentFingerprint,
            operations,
        });
    }

    /// <summary>
    /// Computes a guarded push preview identity from exact local, remote, and plan evidence.
    /// </summary>
    private static string PushPreviewFingerprint(
        DeckDocument local,
        RemoteDeckSnapshot remote,
        ArchidektRemotePlan plan)
    {
        return Fingerprint(new
        {
            direction = "push",
            local.DeckId,
            local.Revision,
            localFingerprint = ArchidektLocalMapper.LocalFingerprint(local),
            remote.RemoteFingerprint,
            plan.PlanFingerprint,
        });
    }

    /// <summary>
    /// Reports whether the differences include a local change the server cannot verify from an earlier baseline.
    /// </summary>
    private static bool HasUnverifiedLocalChange(IReadOnlyList<ArchidektDifference> differences)
    {
        return differences.Any(value => value.State == ArchidektSyncPlanner.LocalChangedUnverifiedState);
    }

    /// <summary>
    /// Reports whether a three-way diff contains any caller-local change.
    /// </summary>
    private static bool HasLocalChanges(ArchidektSyncDiff diff)
    {
        return diff.Differences.Any(value =>
            value.State.StartsWith("local-", StringComparison.Ordinal) ||
            value.State == "concurrent-changed");
    }

    /// <summary>
    /// Reports whether a three-way diff contains any fresh-remote change.
    /// </summary>
    private static bool HasRemoteChanges(ArchidektSyncDiff diff)
    {
        return diff.Differences.Any(value =>
            value.State.StartsWith("remote-", StringComparison.Ordinal) ||
            value.State == "concurrent-changed");
    }

    /// <summary>
    /// Gets one bound remote deck and confirms authenticated-list absence before calling it deleted.
    /// </summary>
    private async Task<OperationResult<RemoteDeckSnapshot>> GetBoundRemoteAsync(
        string remoteDeckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        OperationResult<RemoteDeckSnapshot> result = await Service.GetDeckAsync(
            remoteDeckId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        return result is OperationNotFound
            ? await ClassifyMissingBoundRemoteAsync(
                remoteDeckId,
                operationScope,
                cancellationToken).ConfigureAwait(false)
            : result;
    }

    /// <summary>
    /// Confirms deletion only when a failed pull targets the exact binding of an existing local deck.
    /// </summary>
    private async Task<OperationResult<RemoteDeckSnapshot>> ClassifyPullRemoteFailureAsync(
        OperationResult<RemoteDeckSnapshot> failure,
        string remoteDeckId,
        Guid? localDeckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        if (failure is not OperationNotFound || localDeckId is null)
        {
            return failure;
        }

        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            localDeckId.Value,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return failure;
        }

        OperationResult<DeckProviderBinding?> bindingResult = ResolveBinding(local.Data);
        if (bindingResult is not OperationSuccess<DeckProviderBinding?> bindingSuccess)
        {
            return failure;
        }

        DeckProviderBinding? binding = bindingSuccess.Data;
        if (binding is null || !string.Equals(binding.RemoteId, remoteDeckId, StringComparison.Ordinal))
        {
            return failure;
        }

        return await ClassifyMissingBoundRemoteAsync(
            remoteDeckId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Distinguishes a deleted owned deck from an unavailable detail route using fresh owned-list evidence.
    /// </summary>
    private async Task<OperationResult<RemoteDeckSnapshot>> ClassifyMissingBoundRemoteAsync(
        string remoteDeckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            OperationResult<RemoteDeckPage> pageResult = await Service.ListDecksAsync(
                cursor,
                100,
                operationScope,
                cancellationToken).ConfigureAwait(false);
            if (pageResult is not OperationSuccess<RemoteDeckPage> page)
            {
                return OperationFailure.Forward<RemoteDeckPage, RemoteDeckSnapshot>(pageResult);
            }

            if (page.Data.Items.Any(value =>
                string.Equals(value.RemoteId, remoteDeckId, StringComparison.Ordinal)))
            {
                return new OperationUnavailable(
                    "remote-detail-unavailable",
                    "The bound Archidekt deck is listed but its detail could not be retrieved.");
            }

            cursor = page.Data.NextCursor;
        }
        while (cursor is not null);

        return new OperationConflict(
            "remote-deleted",
            "The bound Archidekt deck is absent from the authenticated deck listing.");
    }

    /// <summary>
    /// Computes a stable SHA-256 fingerprint without exposing adapter-internal hashing helpers.
    /// </summary>
    private static string Fingerprint<T>(T value)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(value);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    /// <summary>
    /// Carries one validated binding and parsed canonical baseline.
    /// </summary>
    private sealed record BindingState(
        DeckProviderBinding Binding,
        ArchidektSyncBaseline Baseline);
}
