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
        return PreviewPullAsync(remoteDeckId, localDeckId, discardLocalChanges: false, cancellationToken);
    }

    /// <summary>
    /// Previews a pull, optionally taking the Archidekt state over local edits made since the baseline.
    /// </summary>
    internal async Task<OperationResult<ArchidektSyncPreview>> PreviewPullAsync(
        string remoteDeckId,
        Guid? localDeckId,
        bool discardLocalChanges,
        CancellationToken cancellationToken)
    {
        OperationResult<PullPreviewState> state = await PreviewPullAsync(
            remoteDeckId,
            localDeckId,
            discardLocalChanges,
            Service.BeginOperation(),
            cancellationToken).ConfigureAwait(false);
        return state is OperationSuccess<PullPreviewState> success
            ? new OperationSuccess<ArchidektSyncPreview>(success.Data.Preview)
            : OperationFailure.Forward<PullPreviewState, ArchidektSyncPreview>(state);
    }

    /// <summary>
    /// Holds a pull preview with what the pull keeps of the local deck, and why the pull would be refused, if it would.
    /// </summary>
    private sealed record PullPreviewState(
        ArchidektSyncPreview Preview,
        ArchidektPullKeep? Keep,
        string? ConflictReason);

    /// <summary>
    /// Previews a pull while charging an existing composed-tool request scope. Local additions are kept; any other
    /// local change since the last sync is a conflict unless the caller asked to discard local changes.
    /// </summary>
    private async Task<OperationResult<PullPreviewState>> PreviewPullAsync(
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
            return OperationFailure.Forward<RemoteDeckSnapshot, PullPreviewState>(classified);
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
            return new OperationSuccess<PullPreviewState>(new PullPreviewState(
                new ArchidektSyncPreview(
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
                    PredictedProviderRequests: 1),
                Keep: null,
                ConflictReason: null));
        }

        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            localDeckId.Value,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return OperationFailure.Forward<DeckDocument, PullPreviewState>(localResult);
        }

        OperationResult<DeckProviderBinding?> bindingResult = ResolveBinding(local.Data);
        if (bindingResult is not OperationSuccess<DeckProviderBinding?> bindingSuccess)
        {
            return OperationFailure.Forward<DeckProviderBinding?, PullPreviewState>(bindingResult);
        }

        DeckProviderBinding? binding = bindingSuccess.Data;
        List<ArchidektDifference> differences = [];
        ArchidektSyncDiff? diff = null;
        ArchidektPullKeep? keep = null;
        string? conflictReason = null;
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
                return OperationFailure.Forward<BindingState, PullPreviewState>(stateResult);
            }

            diff = ArchidektSyncPlanner.Diff(
                local.Data,
                ArchidektLocalMapper.ToRemoteTarget(
                    local.Data,
                    state.Data.Baseline,
                    state.Data.Baseline.RemoteSnapshot),
                remote.Data,
                state.Data.Baseline);
            differences.AddRange(diff.Differences);
            differences.AddRange(ArchidektSyncPlanner.FindStaleZoneMoves(state.Data.Baseline.RemoteSnapshot, remote.Data)
                .Where(move => differences.TrueForAll(shown => shown.Path != move.Path)));
            if (HasLocalChanges(diff) && !discardLocalChanges)
            {
                keep = ArchidektLocalMapper.PlanPullKeep(local.Data, state.Data.Baseline, remote.Data, diff);
                conflictReason = DescribePullConflict(keep, differences);
                keep = conflictReason is null ? keep : null;
            }
        }

        List<ArchidektRemoteOperation> operations =
        [
            new(
                1,
                "local-deck-replace",
                local.Data.DeckId.ToString("D"),
                discardLocalChanges
                    ? DescribeDiscard(diff)
                    : "Replace local content transactionally from fresh Archidekt evidence."),
        ];
        if (keep is not null)
        {
            operations.Add(new ArchidektRemoteOperation(
                2,
                "local-keep",
                local.Data.DeckId.ToString("D"),
                $"Keep {CardCount(keep.Entries.Count)} added locally since the last sync; push them afterwards."));
        }

        string previewFingerprint = PullPreviewFingerprint(
            remote.Data,
            local.Data.DeckId,
            local.Data.Revision,
            discardLocalChanges,
            operations);
        return new OperationSuccess<PullPreviewState>(new PullPreviewState(
            new ArchidektSyncPreview(
                "pull",
                local.Data.DeckId,
                local.Data.Revision,
                remote.Data.RemoteId,
                remote.Data.RemoteFingerprint,
                remote.Data.ContentFingerprint,
                previewFingerprint,
                conflictReason is not null,
                differences,
                operations,
                PredictedProviderRequests: 1)
            {
                ApplyBlockedNotice = conflictReason is null
                    ? null
                    : $"archidekt_pull_apply would refuse this pull: {conflictReason}",
            },
            keep,
            conflictReason));
    }

    /// <summary>
    /// Applies one unchanged pull preview using one transactional local mutation.
    /// </summary>
    internal async Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArchidektOperationScope operationScope = Service.BeginOperation();
        OperationResult<PullPreviewState> previewResult = await PreviewPullAsync(
            request.RemoteDeckId,
            request.LocalDeckId,
            request.DiscardLocalChanges,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (previewResult is not OperationSuccess<PullPreviewState> previewState)
        {
            return OperationFailure.Forward<PullPreviewState, ArchidektApplyResult>(previewResult);
        }

        ArchidektSyncPreview preview = previewState.Data.Preview;
        if (preview.HasConflicts)
        {
            return new OperationConflict(
                "pull-conflict",
                previewState.Data.ConflictReason ?? "The local deck changed since its synchronization baseline.",
                new ConflictState(preview.LocalRevision, preview.RemoteFingerprint));
        }

        if (!string.Equals(
                request.ExpectedRemoteFingerprint,
                preview.RemoteFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(request.PreviewFingerprint, preview.PreviewFingerprint, StringComparison.Ordinal) ||
            request.ExpectedLocalRevision != preview.LocalRevision)
        {
            return new OperationConflict(
                "pull-preview-changed",
                "The local or remote deck changed after the pull preview.",
                new ConflictState(preview.LocalRevision, preview.RemoteFingerprint));
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
                baseline,
                previewState.Data.Keep);
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

        List<ArchidektOperationStatus> statuses =
        [
            new(
                1,
                request.LocalDeckId is null ? "local-deck-create" : "local-deck-replace",
                committed.DeckId.ToString("D"),
                "applied",
                "Committed one transactional local synchronization update."),
        ];
        if (previewState.Data.Keep is { } keep)
        {
            statuses.Add(new ArchidektOperationStatus(
                2,
                "local-keep",
                committed.DeckId.ToString("D"),
                "applied",
                $"Kept {CardCount(keep.Entries.Count)} added locally; the next push adds them to Archidekt."));
        }

        return new OperationSuccess<ArchidektApplyResult>(new ArchidektApplyResult(
            "applied",
            committed.DeckId,
            committed.Revision,
            remote.Data.RemoteId,
            remote.Data.RemoteFingerprint,
            statuses));
    }

    /// <summary>
    /// Explains why a pull cannot go ahead over the local changes since the last sync, or returns null when it can.
    /// A pull keeps cards and categories added locally; anything else needs <c>discardLocalChanges</c>.
    /// </summary>
    private static string? DescribePullConflict(ArchidektPullKeep? keep, IReadOnlyList<ArchidektDifference> differences)
    {
        if (keep is null)
        {
            return HasUnverifiedLocalChange(differences)
                ? UnverifiedPullConflictMessage
                : "The local deck changed since its synchronization baseline in a way a pull cannot keep: a pull " +
                    "keeps cards and categories added locally and replaces everything else from Archidekt. Preview " +
                    "the pull with discardLocalChanges to take the Archidekt state instead, which drops the local " +
                    "edits.";
        }

        if (keep.DuplicateCardNames.Count > 0)
        {
            return "Archidekt also added " + NamedList(keep.DuplicateCardNames) + ", which was added locally too, " +
                "so a pull would leave two entries for the same card. Remove the local copy and pull again, or " +
                "preview the pull with discardLocalChanges to take the Archidekt state, which drops the local edits.";
        }

        return keep.ZoneContradictions.Count > 0
            ? "A card added locally would come back in another zone because of its categories: " +
                DescribeContradictionList(keep.ZoneContradictions) + " Change its zone or categories and pull again, " +
                "or preview the pull with discardLocalChanges to take the Archidekt state, which drops the local edits."
            : null;
    }

    /// <summary>
    /// Says what a pull with <c>discardLocalChanges</c> throws away, counted from the local-* differences.
    /// </summary>
    private static string DescribeDiscard(ArchidektSyncDiff? diff)
    {
        const string replace = "Replace the local deck with the Archidekt deck.";
        IReadOnlyList<ArchidektDifference> local =
            [.. diff?.Differences.Where(ArchidektDifferenceStates.IsLocalChange) ?? []];
        if (local.Count == 0)
        {
            return $"{replace} There are no local changes since the last sync to discard.";
        }

        (int cardsAdded, int cardsChanged, int cardsRemoved) = CountChanges(local, "/entries/");
        (int categoriesAdded, int categoriesChanged, int categoriesRemoved) = CountChanges(local, "/categories/");
        return $"{replace} This discards local changes made since the last sync: {cardsAdded} cards added, " +
            $"{cardsChanged} changed, {cardsRemoved} removed (categories: {categoriesAdded} added, " +
            $"{categoriesChanged} changed, {categoriesRemoved} removed). They are listed in differences with " +
            "local-* states.";
    }

    /// <summary>
    /// Counts the added, changed, and removed local rows under one path prefix.
    /// </summary>
    private static (int Added, int Changed, int Removed) CountChanges(
        IReadOnlyList<ArchidektDifference> local,
        string pathPrefix)
    {
        List<ArchidektDifference> rows = [.. local.Where(
            value => value.Path.StartsWith(pathPrefix, StringComparison.Ordinal))];
        return (
            rows.Count(value => value.State == ArchidektDifferenceStates.LocalAdded),
            rows.Count(value => value.State is ArchidektDifferenceStates.LocalChanged
                or ArchidektDifferenceStates.ConcurrentChanged),
            rows.Count(value => value.State == ArchidektDifferenceStates.LocalRemoved));
    }

    /// <summary>
    /// Writes a count of cards as text, such as "1 card" or "3 cards".
    /// </summary>
    private static string CardCount(int count)
    {
        return count == 1 ? "1 card" : $"{count} cards";
    }

    /// <summary>
    /// Names up to five cards, then says how many more there are.
    /// </summary>
    private static string NamedList(IReadOnlyList<string> names)
    {
        string shown = string.Join(", ", names.Take(5).Select(value => $"'{value}'"));
        return names.Count > 5 ? $"{shown} and {names.Count - 5} more" : shown;
    }

    /// <summary>
    /// Describes cards whose zone differs from the zone their categories give them.
    /// </summary>
    private static string DescribeContradictionList(IReadOnlyList<ArchidektZoneContradiction> contradictions)
    {
        IEnumerable<string> shown = contradictions.Take(5).Select(value =>
            $"'{value.CardName}' (zone '{value.Zone}', but its categories give zone '{value.ExpectedZone}')");
        string more = contradictions.Count > 5 ? $" and {contradictions.Count - 5} more" : string.Empty;
        return $"{string.Join("; ", shown)}{more}.";
    }

    /// <summary>
    /// Previews local-to-remote primitive operations while refusing any remote drift since baseline.
    /// </summary>
    internal async Task<OperationResult<ArchidektSyncPreview>> PreviewPushAsync(
        Guid localDeckId,
        CancellationToken cancellationToken,
        int? confirmRemovals = null)
    {
        OperationResult<PushPreviewState> state = await PreviewPushAsync(
            localDeckId,
            confirmRemovals,
            Service.BeginOperation(),
            cancellationToken).ConfigureAwait(false);
        return state is OperationSuccess<PushPreviewState> success
            ? new OperationSuccess<ArchidektSyncPreview>(success.Data.Preview)
            : OperationFailure.Forward<PushPreviewState, ArchidektSyncPreview>(state);
    }

    /// <summary>
    /// Holds a push preview with the Archidekt printings chosen for it, so the apply that follows reuses them. A push
    /// that Archidekt's own changes block plans nothing, so it has no choices. RemovalNotice is set when the push
    /// removes too many entries for the confirmation the caller gave.
    /// </summary>
    private sealed record PushPreviewState(
        ArchidektSyncPreview Preview,
        ArchidektPrintingChoices? Choices,
        string? RemovalNotice = null);

    /// <summary>
    /// Previews a push while charging an existing composed-tool request scope. Every new card is matched to an
    /// Archidekt printing here, and the preview says which one.
    /// </summary>
    private async Task<OperationResult<PushPreviewState>> PreviewPushAsync(
        Guid localDeckId,
        int? confirmRemovals,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckDocument> localResult = await deckStore.GetAsync(
            localDeckId,
            cancellationToken).ConfigureAwait(false);
        if (localResult is not OperationSuccess<DeckDocument> local)
        {
            return OperationFailure.Forward<DeckDocument, PushPreviewState>(localResult);
        }

        OperationResult<BindingState> stateResult = await LoadBindingStateAsync(
            local.Data,
            cancellationToken).ConfigureAwait(false);
        if (stateResult is not OperationSuccess<BindingState> state)
        {
            return OperationFailure.Forward<BindingState, PushPreviewState>(stateResult);
        }

        OperationResult<RemoteDeckSnapshot> remoteResult = await GetBoundRemoteAsync(
            state.Data.Binding.RemoteId,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (remoteResult is not OperationSuccess<RemoteDeckSnapshot> remote)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, PushPreviewState>(remoteResult);
        }

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            local.Data,
            ArchidektLocalMapper.ToRemoteTarget(
                local.Data,
                state.Data.Baseline,
                state.Data.Baseline.RemoteSnapshot),
            remote.Data,
            state.Data.Baseline);
        if (HasRemoteChanges(diff))
        {
            return new OperationSuccess<PushPreviewState>(new PushPreviewState(
                ConflictingPushPreview(local.Data, remote.Data, diff),
                Choices: null));
        }

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

        OperationResult<ArchidektPushPlan> plannedResult = await Service.PlanPushAsync(
            remote.Data,
            target,
            known: null,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (plannedResult is not OperationSuccess<ArchidektPushPlan> planned)
        {
            return OperationFailure.Forward<ArchidektPushPlan, PushPreviewState>(plannedResult);
        }

        string previewFingerprint = PushPreviewFingerprint(local.Data, remote.Data, planned.Data.Plan);
        string? removalNotice = ArchidektRemovalGuard.Describe(
            ArchidektRemovalGuard.CountRemovals(planned.Data.Operations),
            remote.Data.Entries.Count,
            confirmRemovals);
        return new OperationSuccess<PushPreviewState>(new PushPreviewState(
            new ArchidektSyncPreview(
                "push",
                local.Data.DeckId,
                local.Data.Revision,
                remote.Data.RemoteId,
                remote.Data.RemoteFingerprint,
                target.ContentFingerprint,
                previewFingerprint,
                HasConflicts: false,
                diff.Differences,
                planned.Data.Operations,
                planned.Data.PredictedProviderRequests)
            {
                ApplyBlockedNotice = ArchidektApplyAvailability.WithBlockedNotice(
                    removalNotice,
                    planned.Data.BlockedNotice),
            },
            planned.Data.Choices,
            removalNotice));
    }

    /// <summary>
    /// Builds the preview of a push that Archidekt's own changes block. The push would lay the local deck over the
    /// Archidekt deck and undo those changes, so the preview lists no operations and says the apply would refuse.
    /// </summary>
    private static ArchidektSyncPreview ConflictingPushPreview(
        DeckDocument local,
        RemoteDeckSnapshot remote,
        ArchidektSyncDiff diff)
    {
        int remoteRows = diff.Differences.Count(ArchidektDifferenceStates.IsRemoteChange);
        string fingerprint = Fingerprint(new
        {
            direction = "push",
            local.DeckId,
            local.Revision,
            localFingerprint = ArchidektLocalMapper.LocalFingerprint(local),
            remote.RemoteFingerprint,
            conflict = true,
        });
        return new ArchidektSyncPreview(
            "push",
            local.DeckId,
            local.Revision,
            remote.RemoteId,
            remote.RemoteFingerprint,
            remote.ContentFingerprint,
            fingerprint,
            HasConflicts: true,
            diff.Differences,
            [],
            PredictedProviderRequests: 0)
        {
            ApplyBlockedNotice = "archidekt_push_apply would refuse this push: the Archidekt deck changed since the " +
                $"last sync ({remoteRows} {(remoteRows == 1 ? "row" : "rows")}; see differences with remote-* " +
                "states). Run archidekt_pull_preview first; cards added locally are kept. Then push again.",
        };
    }

    /// <summary>
    /// Explains which entries the push cannot place, and what to change.
    /// </summary>
    private static string DescribeContradictions(IReadOnlyList<ArchidektZoneContradiction> contradictions)
    {
        return "Archidekt keeps no zone of its own: a card's zone comes from its categories (a category named " +
            "Commander, Sideboard or Maybeboard, or the commander category; otherwise main, or maybeboard when the first " +
            "category is not included in the deck). These cards would come " +
            $"back in another zone: {DescribeContradictionList(contradictions)} Change the zone, or add or remove the " +
            "matching category.";
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
        OperationResult<PushPreviewState> previewResult = await PreviewPushAsync(
            request.LocalDeckId,
            request.ConfirmRemovals,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (previewResult is not OperationSuccess<PushPreviewState> previewState)
        {
            return OperationFailure.Forward<PushPreviewState, ArchidektApplyResult>(previewResult);
        }

        ArchidektSyncPreview preview = previewState.Data.Preview;
        if (preview.HasConflicts)
        {
            return new OperationConflict(
                "push-conflict",
                "The Archidekt deck changed since its synchronization baseline.",
                new ConflictState(preview.LocalRevision, preview.RemoteFingerprint));
        }

        if (preview.LocalRevision != request.ExpectedLocalRevision ||
            !string.Equals(
                preview.RemoteFingerprint,
                request.ExpectedRemoteFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                preview.PreviewFingerprint,
                request.PreviewFingerprint,
                StringComparison.Ordinal))
        {
            return new OperationConflict(
                "push-preview-changed",
                "The local or remote deck changed after the push preview.",
                new ConflictState(preview.LocalRevision, preview.RemoteFingerprint));
        }

        if (previewState.Data.RemovalNotice is { } removalNotice)
        {
            return new OperationInvalidInput("push-removals-unconfirmed", removalNotice, "confirmRemovals");
        }

        if (preview.ApplyBlockedNotice is not null)
        {
            return new OperationInvalidInput("push-printing-unresolved", preview.ApplyBlockedNotice);
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

        RemoteDeckSnapshot unresolvedTarget = ArchidektLocalMapper.ToRemoteTarget(
            local.Data,
            state.Data.Baseline,
            current.Data);
        OperationResult<ArchidektPushPlan> plannedResult = await Service.PlanPushAsync(
            current.Data,
            unresolvedTarget,
            previewState.Data.Choices,
            operationScope,
            cancellationToken).ConfigureAwait(false);
        if (plannedResult is not OperationSuccess<ArchidektPushPlan> planned)
        {
            return OperationFailure.Forward<ArchidektPushPlan, ArchidektApplyResult>(plannedResult);
        }

        RemoteDeckSnapshot target = planned.Data.Target;
        OperationResult<ArchidektApplyResult> appliedResult = await Service.ApplyRemoteTargetAsync(
            target,
            request.ExpectedRemoteFingerprint,
            planned.Data.Plan.PlanFingerprint,
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
        return differences.Any(value => value.State == ArchidektDifferenceStates.LocalChangedUnverified);
    }

    /// <summary>
    /// Reports whether a three-way diff contains any caller-local change.
    /// </summary>
    private static bool HasLocalChanges(ArchidektSyncDiff diff)
    {
        return diff.Differences.Any(ArchidektDifferenceStates.IsLocalChange);
    }

    /// <summary>
    /// Reports whether a three-way diff contains any fresh-remote change.
    /// </summary>
    private static bool HasRemoteChanges(ArchidektSyncDiff diff)
    {
        return diff.Differences.Any(ArchidektDifferenceStates.IsRemoteChange);
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
