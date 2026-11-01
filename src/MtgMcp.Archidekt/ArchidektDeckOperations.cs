using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt deck reads, guarded writes, and exact remote update plans.
/// </summary>
internal sealed class ArchidektDeckOperations
{
    /// <summary>
    /// Says that Archidekt accepted an operation.
    /// </summary>
    private const string AcceptedMessage = "Archidekt accepted the operation.";

    /// <summary>
    /// Says that no request reached Archidekt for an operation.
    /// </summary>
    private const string NotSentMessage = "Nothing was sent to Archidekt for this operation.";

    /// <summary>
    /// Says that an operation was never started because an earlier one stopped the push.
    /// </summary>
    private const string NotAttemptedMessage = "Not sent: an earlier operation stopped the push.";

    /// <summary>
    /// Gives the most differing rows an apply result lists.
    /// </summary>
    private const int MaximumListedDifferences = 25;

    /// <summary>
    /// Says that Archidekt rejected one card and changed nothing for it.
    /// </summary>
    private const string CardRejectedMessage = "Archidekt rejected this card, so it was not added.";

    /// <summary>
    /// Says that a batch was rejected after the push had already sent one rejected batch card by card.
    /// </summary>
    private const string BatchRejectedAgainMessage =
        "Nothing was sent for this card: Archidekt rejected its batch, and this push had already sent one rejected " +
        "batch card by card. Push again to send it.";

    /// <summary>
    /// Says that Archidekt accepted a batch but its answer did not list a card of it.
    /// </summary>
    private const string CardNotConfirmedMessage =
        "Archidekt accepted the request, but its answer did not list this card, so it may or may not be on the deck.";

    /// <summary>
    /// Names the status of an operation Archidekt accepted.
    /// </summary>
    private const string AppliedStatus = "applied";

    /// <summary>
    /// Names the status of an operation that was sent and may or may not have taken effect.
    /// </summary>
    private const string UnknownStatus = "unknown";

    /// <summary>
    /// Names the status of an operation that Archidekt rejected and that changed nothing.
    /// </summary>
    private const string NotAppliedStatus = "not-applied";

    /// <summary>
    /// Names the status of an operation for which no request reached Archidekt.
    /// </summary>
    private const string NotSentStatus = "not-sent";

    /// <summary>
    /// Names the status of an operation that was never started.
    /// </summary>
    private const string NotAttemptedStatus = "not-attempted";

    /// <summary>
    /// Gives the reason code of a push that cannot be sent because a card has no Archidekt printing.
    /// </summary>
    private const string PrintingUnresolvedReasonCode = "push-printing-unresolved";

    /// <summary>
    /// Gives how many times a read may be tried again after a rate-limit response; the budget keeps room for them.
    /// </summary>
    private const int MaximumRetriesPerRead = 2;

    /// <summary>
    /// Gives the deck reads an apply makes after its plan is made: the read to start, the read to verify, the read
    /// again when that finds a difference, and a last read.
    /// </summary>
    private const int ReadsAfterPlanningInAnApply = 4;

    /// <summary>
    /// Gives the deck reads left for a preview to count: those of an apply, plus the read the apply makes before it
    /// plans, which the preview has already made when it counts.
    /// </summary>
    private const int ReadsAfterPlanningInAPreview = ReadsAfterPlanningInAnApply + 1;

    /// <summary>
    /// Gives the pause before the check after the writes is repeated when it found a difference.
    /// </summary>
    private static readonly TimeSpan VerificationSettleTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Provides deck provider routes for this workflow owner.
    /// </summary>
    private readonly ArchidektDeckTransport transport;

    /// <summary>
    /// Stores the hard request limit for standalone operations.
    /// </summary>
    private readonly int maximumRequestsPerOperation;

    /// <summary>
    /// Chooses the Archidekt printing for each card a push adds.
    /// </summary>
    private readonly ArchidektPrintingResolver printings;

    /// <summary>
    /// Creates deck workflows over one deck provider route owner.
    /// </summary>
    internal ArchidektDeckOperations(
        ArchidektDeckTransport transport,
        int maximumRequestsPerOperation)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        printings = new ArchidektPrintingResolver(transport);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestsPerOperation);
        this.maximumRequestsPerOperation = maximumRequestsPerOperation;
    }

    /// <summary>
    /// Lists one bounded authenticated deck page.
    /// </summary>
    internal Task<OperationResult<RemoteDeckPage>> ListAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(
            maximumRequestsPerOperation,
            budget => transport.ListAsync(cursor, pageSize, budget, cancellationToken));
    }

    /// <summary>
    /// Lists one deck page under a caller-owned composed-operation budget.
    /// </summary>
    internal Task<OperationResult<RemoteDeckPage>> ListAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ArchidektOperationResults.ExecuteAsync(
            operationScope.Budget,
            budget => transport.ListAsync(cursor, pageSize, budget, cancellationToken));
    }

    /// <summary>
    /// Gets one fresh public or authenticated remote deck observation.
    /// </summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> GetAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(
            maximumRequestsPerOperation,
            budget => GetAsync(deckId, budget, cancellationToken));
    }

    /// <summary>
    /// Gets one remote deck under a caller-owned composed-operation budget.
    /// </summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> GetAsync(
        string deckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ArchidektOperationResults.ExecuteAsync(
            operationScope.Budget,
            budget => GetAsync(deckId, budget, cancellationToken));
    }

    /// <summary>
    /// Creates and verifies one private-by-default remote deck.
    /// </summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> CreateAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(
            maximumRequestsPerOperation,
            async budget =>
            {
                RemoteDeckSnapshot created = await transport.CreateAsync(
                    request,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteDeckSnapshot verified = await transport.GetAsync(
                    created.RemoteId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(created.Name, verified.Name, StringComparison.Ordinal) ||
                    !string.Equals(created.Visibility, verified.Visibility, StringComparison.Ordinal))
                {
                    throw Conflict(
                        "remote-verification-mismatch",
                        "Archidekt created a deck whose verified state did not match the request.");
                }

                return verified;
            });
    }

    /// <summary>
    /// Deletes one unchanged exact remote deck and verifies absence through authenticated listing evidence.
    /// </summary>
    internal Task<OperationResult<ArchidektApplyResult>> DeleteAsync(
        ArchidektDeckDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(
            maximumRequestsPerOperation,
            async budget =>
            {
                ArchidektOperationResults.RequireConfirmation(request.Confirmation, $"delete {request.DeckId}");
                RemoteDeckSnapshot current = await transport.GetAsync(
                    request.DeckId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedRemoteFingerprint,
                    current.RemoteFingerprint,
                    "remote-deck-changed",
                    current.RemoteFingerprint);
                try
                {
                    await transport.DeleteAsync(
                        request.DeckId,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.Kind == ArchidektFailureKind.Unavailable)
                {
                    return PartialResult(
                        request.DeckId,
                        new ArchidektRemoteOperation(1, "deck-delete", request.DeckId, "Delete one exact deck."),
                        exception.Message);
                }

                bool present = await DeckAppearsInAuthenticatedListingAsync(
                    request.DeckId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (present)
                {
                    throw Conflict(
                        "remote-delete-unverified",
                        "Archidekt still lists the deck after deletion.");
                }

                return new ArchidektApplyResult(
                    "applied",
                    LocalDeckId: null,
                    LocalRevision: null,
                    request.DeckId,
                    FinalRemoteFingerprint: null,
                    [new ArchidektOperationStatus(
                        1,
                        "deck-delete",
                        request.DeckId,
                        "applied",
                        "Verified absent from the authenticated deck listing.")]);
            });
    }

    /// <summary>
    /// Plans a push of <paramref name="target"/> onto <paramref name="current"/> and picks the Archidekt printing of
    /// every card it adds, under a caller-owned composed-operation budget. A preview and the apply that follows it
    /// both use this, so what is previewed is what is sent. Searches are skipped for entries that
    /// <paramref name="known"/> already holds. <paramref name="forPreview"/> says the plan is for a preview, which
    /// counts the extra read its apply will make.
    /// </summary>
    internal Task<OperationResult<ArchidektPushPlan>> PlanPushAsync(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektPrintingChoices? known,
        bool forPreview,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ArchidektOperationResults.ExecuteAsync(
            operationScope.Budget,
            async budget =>
            {
                ArchidektPrintingChoices choices = known ?? new ArchidektPrintingChoices();
                ArchidektRemotePlan initial = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                await printings.ResolveAsync(
                    NewCardsWithoutProviderId(initial),
                    choices,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteDeckSnapshot resolved = choices.ApplyTo(target);
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, resolved);
                int needed = budget.RequestCount + RequestsAfterPlanning(plan, forPreview);
                return new ArchidektPushPlan(
                    resolved,
                    plan,
                    DescribeOperations(plan, choices),
                    needed,
                    budget.MaximumRequests,
                    Blocked(choices, needed, budget.MaximumRequests, plan),
                    choices);
            });
    }

    /// <summary>
    /// Counts the requests an apply makes after its plan is made. The plan's own count holds one request per operation
    /// and one read. The apply also reads the deck to start (a preview adds the read the apply makes before that),
    /// reads it to verify, reads it again when that finds a difference, and reads it a last time; room is left for each
    /// read to be tried again after a rate-limit response.
    /// </summary>
    private static int RequestsAfterPlanning(ArchidektRemotePlan plan, bool planningForAPreview)
    {
        int reads = planningForAPreview ? ReadsAfterPlanningInAPreview : ReadsAfterPlanningInAnApply;
        return plan.PredictedProviderRequests - 1 + reads + (reads * MaximumRetriesPerRead);
    }

    /// <summary>
    /// Lists the entries a plan adds that carry no Archidekt card ID yet.
    /// </summary>
    private static IEnumerable<RemoteDeckEntry> NewCardsWithoutProviderId(ArchidektRemotePlan plan)
    {
        return plan.PlannedOperations
            .Where(value => value.Public.Kind == ArchidektSyncPlanner.EntryAddKind &&
                string.IsNullOrWhiteSpace(value.TargetEntry?.ProviderCardId))
            .Select(value => value.TargetEntry!);
    }

    /// <summary>
    /// Names the chosen printing in each add operation, or says that none was found.
    /// </summary>
    private static IReadOnlyList<ArchidektRemoteOperation> DescribeOperations(
        ArchidektRemotePlan plan,
        ArchidektPrintingChoices choices)
    {
        List<ArchidektRemoteOperation> described = [];
        foreach (ArchidektPlannedOperation operation in plan.PlannedOperations)
        {
            ArchidektRemoteOperation shown = operation.Public;
            RemoteDeckEntry? entry = operation.TargetEntry;
            if (shown.Kind == ArchidektSyncPlanner.EntryAddKind && entry is not null &&
                string.IsNullOrWhiteSpace(entry.ProviderCardId))
            {
                string detail = choices.UnresolvedCardNames.Contains(entry.CardName, StringComparer.OrdinalIgnoreCase)
                    ? "Archidekt printing not found, so this add cannot be sent."
                    : "Archidekt printing not looked up.";
                shown = shown with { Summary = $"{shown.Summary.TrimEnd('.')}: {detail}" };
            }
            else if (shown.Kind == ArchidektSyncPlanner.EntryAddKind && entry is not null &&
                choices.Find(entry) is { } choice)
            {
                shown = shown with { Summary = $"{shown.Summary.TrimEnd('.')}: {choice.Description}" };
            }

            described.Add(shown);
        }

        return described;
    }

    /// <summary>
    /// Explains why a push could not be sent, with its reason code, or returns null when it can.
    /// </summary>
    private static ArchidektPushBlock? Blocked(
        ArchidektPrintingChoices choices,
        int neededRequests,
        int maximumRequests,
        ArchidektRemotePlan plan)
    {
        if (choices.StoppedAtRequestLimit)
        {
            return new ArchidektPushBlock(
                "archidekt_push_apply would send nothing: looking up printings on Archidekt reached the " +
                    $"{maximumRequests}-request limit for one archidekt_push_apply call after " +
                    $"{choices.SearchRequests} searches. The push can be split: apply part of the local changes " +
                    "first (fewer new cards), then preview and push again for the rest.",
                ArchidektProviderException.RequestLimitReasonCode);
        }

        if (choices.UnresolvedCardNames.Count > 0)
        {
            IEnumerable<string> shown = choices.UnresolvedCardNames.Take(10).Select(value => $"'{value}'");
            string more = choices.UnresolvedCardNames.Count > 10
                ? $" and {choices.UnresolvedCardNames.Count - 10} more"
                : string.Empty;
            return new ArchidektPushBlock(
                "archidekt_push_apply would send nothing: Archidekt has no printing for " +
                    $"{string.Join(", ", shown)}{more}. Fix the card name or set a printing on the entry, then " +
                    "preview again.",
                PrintingUnresolvedReasonCode);
        }

        return neededRequests > maximumRequests
            ? new ArchidektPushBlock(
                "archidekt_push_apply would send nothing: this push " +
                    ArchidektOperationBudget.DescribeOverLimit(
                        neededRequests,
                        maximumRequests,
                        plan.PlannedOperations.Count,
                        plan.OperationRequestCount),
                ArchidektProviderException.RequestLimitReasonCode)
            : null;
    }

    /// <summary>
    /// Applies one caller-previewed remote target under an operation-local budget.
    /// </summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        CancellationToken cancellationToken)
    {
        return ApplyTargetAsync(
            target,
            expectedRemoteFingerprint,
            expectedPlanFingerprint,
            new ArchidektOperationScope(maximumRequestsPerOperation),
            cancellationToken);
    }

    /// <summary>
    /// Applies one remote target under a caller-owned composed-operation budget.
    /// </summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ArchidektOperationResults.ExecuteAsync(
            operationScope.Budget,
            async budget =>
            {
                RemoteDeckSnapshot current = await transport.GetAsync(
                    target.RemoteId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    expectedRemoteFingerprint,
                    current.RemoteFingerprint,
                    "remote-deck-changed",
                    current.RemoteFingerprint);
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                RequireFingerprint(
                    expectedPlanFingerprint,
                    plan.PlanFingerprint,
                    "push-preview-changed",
                    current.RemoteFingerprint);
                return await ApplyPlanAsync(
                    current,
                    target,
                    plan,
                    budget,
                    cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Executes one exact update plan in stable order. Every new card is matched to an Archidekt printing before the
    /// first write, so a card that cannot be placed stops the push with nothing sent. After that, a failure is
    /// reported as unknown, because the write may have reached Archidekt, and the rest are reported as not sent.
    /// Archidekt rejects a batch of added cards whole and changes nothing, so a rejected batch is sent again card by
    /// card, once per push: the cards it accepts are applied, a card it rejects is reported as not applied, and the
    /// push goes on. When any operation did not take effect, the push is reported as partial or not applied and is
    /// not verified here; the caller reads a partial push back and records what landed.
    /// </summary>
    internal async Task<ArchidektApplyResult> ApplyPlanAsync(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektRemotePlan plan,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        budget.EnsurePushRequestBound(
            budget.RequestCount + plan.PredictedProviderRequests,
            plan.PlannedOperations.Count,
            plan.OperationRequestCount);
        ArchidektPrintingChoices choices = new();
        ArchidektApplyResult? refused = await ResolveBeforeWritingAsync(
            current.RemoteId,
            plan,
            choices,
            budget,
            cancellationToken).ConfigureAwait(false);
        if (refused is not null)
        {
            return refused;
        }

        ArchidektSendProgress progress = new();
        int index = 0;
        while (index < plan.PlannedOperations.Count)
        {
            int size = ArchidektSyncPlanner.RequestSize(plan.PlannedOperations, index);
            ArchidektPlannedOperation[] run = [.. plan.PlannedOperations.Skip(index).Take(size)];
            if (!await SendRunAsync(current.RemoteId, target, run, choices, progress, budget, cancellationToken)
                .ConfigureAwait(false))
            {
                AddNotAttempted(progress.Statuses, plan.PlannedOperations, index + size);
                return UnfinishedResult(current.RemoteId, progress.Statuses);
            }

            index += size;
        }

        if (progress.Statuses.Exists(value => value.Status != AppliedStatus))
        {
            return UnfinishedResult(current.RemoteId, progress.Statuses);
        }

        RemoteDeckSnapshot? verified = await ReadForVerificationAsync(
            current.RemoteId,
            budget,
            cancellationToken).ConfigureAwait(false);
        ArchidektRemotePlan? residual = verified is null
            ? null
            : ArchidektSyncPlanner.PlanRemoteVerification(verified, target);
        if (residual is { PlannedOperations.Count: > 0 })
        {
            // Archidekt can lag behind its own writes, so one more read decides whether the difference is real.
            await transport.PauseAsync(VerificationSettleTime, cancellationToken).ConfigureAwait(false);
            verified = await ReadForVerificationAsync(current.RemoteId, budget, cancellationToken)
                .ConfigureAwait(false);
            residual = verified is null ? null : ArchidektSyncPlanner.PlanRemoteVerification(verified, target);
        }

        if (verified is null || residual is null)
        {
            return new ArchidektApplyResult(
                "applied-unverified",
                LocalDeckId: null,
                LocalRevision: null,
                current.RemoteId,
                FinalRemoteFingerprint: null,
                progress.Statuses)
            {
                Notice = ArchidektApplyResult.UnverifiedNotice,
            };
        }

        if (residual.PlannedOperations.Count > 0)
        {
            return new ArchidektApplyResult(
                "verification-mismatch",
                LocalDeckId: null,
                LocalRevision: null,
                current.RemoteId,
                verified.RemoteFingerprint,
                progress.Statuses)
            {
                Differences =
                [
                    .. residual.PlannedOperations
                        .Take(MaximumListedDifferences)
                        .Select(value => new ArchidektVerificationDifference(
                            value.Public.Kind,
                            value.Public.Subject,
                            value.Public.Summary)),
                ],
                Notice = $"{residual.PlannedOperations.Count} rows of the Archidekt deck still differ from the " +
                    "pushed deck after a second read.",
            };
        }

        return new ArchidektApplyResult(
            "applied",
            LocalDeckId: null,
            LocalRevision: null,
            current.RemoteId,
            verified.RemoteFingerprint,
            progress.Statuses);
    }

    /// <summary>
    /// Reads the deck again after the writes, or returns null when Archidekt cannot answer or the operation has no
    /// requests left, so the statuses of the writes that were sent are kept instead of lost.
    /// </summary>
    private async Task<RemoteDeckSnapshot?> ReadForVerificationAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.GetAsync(
                deckId,
                requireAuthentication: true,
                budget,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArchidektProviderException exception)
            when (exception.Kind is ArchidektFailureKind.Unavailable or ArchidektFailureKind.Unsupported ||
                exception.ReasonCode == ArchidektProviderException.RequestLimitReasonCode)
        {
            return null;
        }
    }

    /// <summary>
    /// Matches every new card of the plan to an Archidekt printing. Returns a result that says nothing was sent when a
    /// card cannot be matched or the search fails, or null when every card has a printing.
    /// </summary>
    private async Task<ArchidektApplyResult?> ResolveBeforeWritingAsync(
        string deckId,
        ArchidektRemotePlan plan,
        ArchidektPrintingChoices choices,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        List<RemoteDeckEntry> unresolvedAdds = [.. NewCardsWithoutProviderId(plan)];
        if (unresolvedAdds.Count == 0)
        {
            return null;
        }

        string? failure = null;
        try
        {
            await printings.ResolveAsync(unresolvedAdds, choices, budget, cancellationToken).ConfigureAwait(false);
        }
        catch (ArchidektProviderException exception)
            when (exception.Kind is ArchidektFailureKind.Unavailable or ArchidektFailureKind.Unsupported)
        {
            failure = exception.Message;
        }

        if (failure is null && choices.UnresolvedCardNames.Count == 0)
        {
            return null;
        }

        int failedIndex = plan.PlannedOperations
            .Select((value, index) => (value, index))
            .First(pair => pair.value.Public.Kind == ArchidektSyncPlanner.EntryAddKind &&
                string.IsNullOrWhiteSpace(pair.value.TargetEntry?.ProviderCardId) &&
                (failure is not null || choices.Find(pair.value.TargetEntry!) is null))
            .index;
        string reason = failure ?? $"Archidekt has no printing for '{choices.UnresolvedCardNames[0]}'.";
        List<ArchidektOperationStatus> statuses = [];
        for (int index = 0; index < plan.PlannedOperations.Count; index++)
        {
            ArchidektRemoteOperation shown = plan.PlannedOperations[index].Public;
            statuses.Add(index == failedIndex
                ? Status(shown, NotSentStatus, $"{NotSentMessage} {reason}")
                : Status(shown, NotAttemptedStatus, NotAttemptedMessage));
        }

        return new ArchidektApplyResult(
            "not-applied",
            LocalDeckId: null,
            LocalRevision: null,
            deckId,
            FinalRemoteFingerprint: null,
            statuses);
    }

    /// <summary>
    /// Adds a not-attempted status for every operation from <paramref name="firstIndex"/> on.
    /// </summary>
    private static void AddNotAttempted(
        List<ArchidektOperationStatus> statuses,
        IReadOnlyList<ArchidektPlannedOperation> operations,
        int firstIndex)
    {
        for (int remaining = firstIndex; remaining < operations.Count; remaining++)
        {
            statuses.Add(Status(operations[remaining].Public, NotAttemptedStatus, NotAttemptedMessage));
        }
    }

    /// <summary>
    /// Reports a push that did not finish or that left an operation without effect: not applied when no operation
    /// took effect or may have, and partial otherwise.
    /// </summary>
    private static ArchidektApplyResult UnfinishedResult(
        string remoteId,
        IReadOnlyList<ArchidektOperationStatus> statuses)
    {
        bool nothingChanged = statuses.All(
            value => value.Status is NotSentStatus or NotAttemptedStatus or NotAppliedStatus);
        return new ArchidektApplyResult(
            nothingChanged ? "not-applied" : "partial",
            LocalDeckId: null,
            LocalRevision: null,
            remoteId,
            FinalRemoteFingerprint: null,
            statuses);
    }

    /// <summary>
    /// Sends one request: a run of added cards in one batch, or one other operation. Adds the status of each operation
    /// to <paramref name="progress"/>, and returns false when the push must stop after this run. A card Archidekt
    /// rejects is known not to be applied; a rejected batch is sent again card by card, once per push, because the
    /// request prediction reserves the requests of one such fallback and no more.
    /// </summary>
    private async Task<bool> SendRunAsync(
        string deckId,
        RemoteDeckSnapshot target,
        ArchidektPlannedOperation[] run,
        ArchidektPrintingChoices choices,
        ArchidektSendProgress progress,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(deckId, target, run, choices, progress, budget, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ArchidektProviderException exception)
            when (exception.ReasonCode == ArchidektProviderException.RequestRejectedReasonCode &&
                run[0].Public.Kind == ArchidektSyncPlanner.EntryAddKind)
        {
            if (run.Length == 1)
            {
                progress.Statuses.Add(Status(run[0].Public, NotAppliedStatus, CardRejectedMessage));
                return true;
            }

            if (progress.SentBatchOneByOne)
            {
                progress.Statuses.AddRange(
                    run.Select(value => Status(value.Public, NotSentStatus, BatchRejectedAgainMessage)));
                return false;
            }

            progress.SentBatchOneByOne = true;
            for (int index = 0; index < run.Length; index++)
            {
                if (!await SendRunAsync(deckId, target, [run[index]], choices, progress, budget, cancellationToken)
                    .ConfigureAwait(false))
                {
                    AddNotAttempted(progress.Statuses, run, index + 1);
                    return false;
                }
            }

            return true;
        }
        catch (ArchidektProviderException exception)
            when (exception.ReasonCode is ArchidektProviderException.RateLimitedReasonCode
                or ArchidektProviderException.RequestLimitReasonCode)
        {
            // The rate limit answered, or the request limit stopped the request before it was sent (a sign-in
            // again after a 401 can use the last request), so this write did not reach Archidekt.
            progress.Statuses.AddRange(run.Select(
                value => Status(value.Public, NotSentStatus, $"{NotSentMessage} {exception.Message}")));
            return false;
        }
        catch (ArchidektProviderException exception)
        {
            // Any other failure, a 404 included, may follow a write Archidekt made, so the run is unknown and the
            // statuses of the runs before it are kept.
            progress.Statuses.AddRange(run.Select(value => Status(value.Public, UnknownStatus, exception.Message)));
            return false;
        }
    }

    /// <summary>
    /// Sends the request of one run and adds its statuses. Each added card must come back in the answer under the
    /// patch ID that was sent with it; a card that does not is unknown, because the request succeeded and the card is
    /// not confirmed.
    /// </summary>
    private async Task SendAsync(
        string deckId,
        RemoteDeckSnapshot target,
        ArchidektPlannedOperation[] run,
        ArchidektPrintingChoices choices,
        ArchidektSendProgress progress,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        if (!IsCardOperation(run[0]))
        {
            await ExecutePlannedOperationAsync(deckId, target, run[0], budget, cancellationToken).ConfigureAwait(false);
            progress.Statuses.Add(Status(run[0].Public, AppliedStatus, AcceptedMessage));
            return;
        }

        IReadOnlySet<string>? returned = await transport.SendCardMutationsAsync(
            deckId,
            [.. run.Select(value => (object)CardPayload(deckId, value, choices))],
            budget,
            cancellationToken).ConfigureAwait(false);
        foreach (ArchidektPlannedOperation operation in run)
        {
            bool confirmed = operation.Public.Kind != ArchidektSyncPlanner.EntryAddKind ||
                (returned?.Contains(PatchId(deckId, operation)) ?? false);
            progress.Statuses.Add(confirmed
                ? Status(operation.Public, AppliedStatus, AcceptedMessage)
                : Status(operation.Public, UnknownStatus, CardNotConfirmedMessage));
        }
    }

    /// <summary>
    /// Tells whether an operation adds, changes, or removes a card relation, which goes through the card route.
    /// </summary>
    private static bool IsCardOperation(ArchidektPlannedOperation operation)
    {
        return operation.Public.Kind is ArchidektSyncPlanner.EntryAddKind
            or "entry-update"
            or ArchidektSyncPlanner.EntryRemoveKind;
    }

    /// <summary>
    /// Gives the patch ID sent with a card operation, which Archidekt echoes for an added card. It is the same for the
    /// same deck and operation, as a GUID in its 32-digit form.
    /// </summary>
    private static string PatchId(string deckId, ArchidektPlannedOperation operation)
    {
        return ArchidektContract.StableGuid(
            "patch",
            $"{deckId}:{operation.Public.Sequence}:{operation.Public.Kind}:{operation.Public.Subject}").ToString("N");
    }

    /// <summary>
    /// Reads one deck with a public request first, then retries with authentication when the provider requires it.
    /// </summary>
    private async Task<RemoteDeckSnapshot> GetAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.GetAsync(
                deckId,
                requireAuthentication: false,
                budget,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArchidektProviderException exception)
            when (exception.ReasonCode is
                "provider-forbidden" or
                ArchidektProviderException.RequestRejectedReasonCode or
                "provider-entity-not-found")
        {
            return await transport.GetAsync(
                deckId,
                requireAuthentication: true,
                budget,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends the provider request for one planned deck action that is not a card operation.
    /// </summary>
    private async Task ExecutePlannedOperationAsync(
        string deckId,
        RemoteDeckSnapshot target,
        ArchidektPlannedOperation operation,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        switch (operation.Public.Kind)
        {
            case "metadata-update":
                await transport.SendMetadataAsync(
                    deckId,
                    new
                    {
                        name = target.Name,
                        description = target.Description,
                        deckFormat = ArchidektDeckTransport.MapFormatId(target.Format),
                        @private = target.Visibility == "private",
                        unlisted = target.Visibility == "unlisted",
                        parentFolder = ArchidektProviderId.Parse(target.ParentFolderId),
                    },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "category-create":
                await transport.SendCategoryCreateAsync(
                    CategoryPayload(deckId, operation.TargetCategory!),
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "category-update":
                await transport.SendCategoryUpdateAsync(
                    operation.CurrentCategory!.ProviderCategoryId,
                    CategoryPayload(deckId, operation.TargetCategory!),
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "category-delete":
                await transport.SendCategoryDeleteAsync(
                    operation.CurrentCategory!.ProviderCategoryId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArchidektProviderException(
                    ArchidektFailureKind.Unsupported,
                    "provider-contract-unsupported",
                    "The remote operation kind is not supported.");
        }
    }

    /// <summary>
    /// Builds the payload of one card operation. A new card uses the printing chosen before the first write.
    /// </summary>
    private static Dictionary<string, object?> CardPayload(
        string deckId,
        ArchidektPlannedOperation operation,
        ArchidektPrintingChoices choices)
    {
        RemoteDeckEntry entry = operation.TargetEntry ?? operation.CurrentEntry!;
        string providerCardId = entry.ProviderCardId;
        if (operation.Public.Kind == ArchidektSyncPlanner.EntryAddKind && string.IsNullOrWhiteSpace(providerCardId))
        {
            providerCardId = choices.Find(entry)?.ProviderCardId
                ?? throw new InvalidOperationException("A new card reached the write step without a chosen printing.");
        }

        string action = operation.Public.Kind switch
        {
            ArchidektSyncPlanner.EntryAddKind => "add",
            "entry-update" => "modify",
            "entry-remove" => "remove",
            _ => throw new InvalidOperationException("Unsupported card operation kind."),
        };
        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["cardid"] = ArchidektProviderId.Parse(providerCardId),
            ["patchId"] = PatchId(deckId, operation),
            ["categories"] = ProviderCategories(entry),
            ["modifications"] = new
            {
                quantity = action == "remove" ? 0 : entry.Quantity,
                companion = false,
                flippedDefault = false,
                modifier = ProviderModifier(entry.Finish),
            },
        };
        string? relationId = operation.CurrentEntry?.ProviderRelationId;
        if (!string.IsNullOrWhiteSpace(relationId))
        {
            payload["deckRelationId"] = ArchidektProviderId.Parse(relationId);
        }

        return payload;
    }

    /// <summary>
    /// Creates one exact category mutation payload.
    /// </summary>
    private static object CategoryPayload(string deckId, RemoteDeckCategory category)
    {
        return new
        {
            deck = ArchidektProviderId.Parse(deckId),
            name = category.Name,
            includedInDeck = category.IncludedInDeck ?? true,
            includedInPrice = category.IncludedInPrice ?? true,
            isPremier = category.IsPremier,
            sortOrder = category.SortOrder,
        };
    }

    /// <summary>
    /// Checks every authenticated deck-list page until the exact ID is found or the list ends.
    /// </summary>
    private async Task<bool> DeckAppearsInAuthenticatedListingAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            RemoteDeckPage page = await transport.ListAsync(
                cursor,
                100,
                budget,
                cancellationToken).ConfigureAwait(false);
            if (page.Items.Any(value => string.Equals(value.RemoteId, deckId, StringComparison.Ordinal)))
            {
                return true;
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return false;
    }

    /// <summary>
    /// Requires one caller fingerprint to match freshly retrieved evidence.
    /// </summary>
    private static void RequireFingerprint(
        string expected,
        string actual,
        string reasonCode,
        string? currentRemoteFingerprint = null)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw Conflict(reasonCode, "Provider evidence changed after the preview.", currentRemoteFingerprint);
        }
    }

    /// <summary>
    /// Maps local finish vocabulary to Archidekt's observed modifier names.
    /// </summary>
    private static string ProviderModifier(string finish)
    {
        return finish switch
        {
            "foil" => "Foil",
            "etched" => "Etched",
            _ => "Normal",
        };
    }

    /// <summary>
    /// Orders category names with the explicit primary first because Archidekt promotes the first submitted category.
    /// </summary>
    private static IReadOnlyList<string> ProviderCategories(RemoteDeckEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.PrimaryCategoryName))
        {
            return entry.CategoryNames;
        }

        List<string> ordered = [entry.PrimaryCategoryName];
        ordered.AddRange(entry.CategoryNames.Where(value =>
            !string.Equals(value, entry.PrimaryCategoryName, StringComparison.Ordinal)));
        return ordered;
    }

    /// <summary>
    /// Maps one safe operation descriptor into a final status row.
    /// </summary>
    private static ArchidektOperationStatus Status(
        ArchidektRemoteOperation operation,
        string status,
        string message)
    {
        return new ArchidektOperationStatus(
            operation.Sequence,
            operation.Kind,
            operation.Subject,
            status,
            message);
    }

    /// <summary>
    /// Creates a one-operation unknown-state result after an ambiguous mutation failure.
    /// </summary>
    private static ArchidektApplyResult PartialResult(
        string remoteId,
        ArchidektRemoteOperation operation,
        string message)
    {
        return new ArchidektApplyResult(
            "partial",
            LocalDeckId: null,
            LocalRevision: null,
            remoteId,
            FinalRemoteFingerprint: null,
            [Status(operation, UnknownStatus, message)]);
    }

    /// <summary>
    /// Creates one provider-state conflict without transport details.
    /// </summary>
    private static ArchidektProviderException Conflict(
        string reasonCode,
        string message,
        string? currentRemoteFingerprint = null)
    {
        return new ArchidektProviderException(
            ArchidektFailureKind.Conflict,
            reasonCode,
            message)
        {
            CurrentRemoteFingerprint = currentRemoteFingerprint,
        };
    }

    /// <summary>
    /// Collects the statuses of the operations of one push as they are sent, and whether the one allowed fallback of a
    /// rejected batch to single-card sends has been used.
    /// </summary>
    private sealed class ArchidektSendProgress
    {
        /// <summary>
        /// Gets the statuses added so far, in operation order.
        /// </summary>
        internal List<ArchidektOperationStatus> Statuses { get; } = [];

        /// <summary>
        /// Gets or sets whether a rejected batch was already sent card by card in this push.
        /// </summary>
        internal bool SentBatchOneByOne { get; set; }
    }
}
