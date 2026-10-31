using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt deck reads, guarded writes, and exact remote update plans.
/// </summary>
internal sealed class ArchidektDeckOperations
{
    /// <summary>
    /// Provides deck provider routes for this workflow owner.
    /// </summary>
    private readonly ArchidektDeckTransport transport;

    /// <summary>
    /// Says that no request reached Archidekt for an operation.
    /// </summary>
    private const string NotSentMessage = "Nothing was sent to Archidekt for this operation.";

    /// <summary>
    /// Says that an operation was never started because an earlier one stopped the push.
    /// </summary>
    private const string NotAttemptedMessage = "Not sent: an earlier operation stopped the push.";

    /// <summary>
    /// Stores the hard provider request ceiling for standalone operations.
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
                RequireConfirmation(request.Confirmation, $"delete {request.DeckId}");
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
    /// <paramref name="known"/> already holds.
    /// </summary>
    internal Task<OperationResult<ArchidektPushPlan>> PlanPushAsync(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektPrintingChoices? known,
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
                // The apply makes two more reads, then one request per operation, a verifying read, and a final read.
                int needed = budget.RequestCount + plan.PredictedProviderRequests + 3;
                return new ArchidektPushPlan(
                    resolved,
                    plan,
                    DescribeOperations(plan, choices),
                    plan.PredictedProviderRequests + choices.SearchRequests,
                    BlockedNotice(choices, needed, budget.MaximumRequests),
                    choices);
            });
    }

    /// <summary>
    /// Lists the entries a plan adds that carry no Archidekt card ID yet.
    /// </summary>
    private static IEnumerable<RemoteDeckEntry> NewCardsWithoutProviderId(ArchidektRemotePlan plan)
    {
        return plan.PlannedOperations
            .Where(value => value.Public.Kind == "entry-add" &&
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
            if (shown.Kind == "entry-add" && entry is not null && string.IsNullOrWhiteSpace(entry.ProviderCardId))
            {
                string detail = choices.UnresolvedCardNames.Contains(entry.CardName, StringComparer.OrdinalIgnoreCase)
                    ? "Archidekt printing not found, so this add cannot be sent."
                    : "Archidekt printing not looked up.";
                shown = shown with { Summary = $"{shown.Summary.TrimEnd('.')}: {detail}" };
            }
            else if (shown.Kind == "entry-add" && entry is not null && choices.Find(entry) is { } choice)
            {
                shown = shown with { Summary = $"{shown.Summary.TrimEnd('.')}: {choice.Description}" };
            }

            described.Add(shown);
        }

        return described;
    }

    /// <summary>
    /// Explains why a push could not be sent, or returns null when it can.
    /// </summary>
    private static string? BlockedNotice(ArchidektPrintingChoices choices, int neededRequests, int maximumRequests)
    {
        if (choices.StoppedAtRequestLimit)
        {
            return "archidekt_push_apply would send nothing: looking up printings on Archidekt reached the limit of " +
                $"{maximumRequests} provider requests for one operation. Push fewer new cards at a time.";
        }

        if (choices.UnresolvedCardNames.Count > 0)
        {
            IEnumerable<string> shown = choices.UnresolvedCardNames.Take(10).Select(value => $"'{value}'");
            string more = choices.UnresolvedCardNames.Count > 10
                ? $" and {choices.UnresolvedCardNames.Count - 10} more"
                : string.Empty;
            return "archidekt_push_apply would send nothing: Archidekt has no printing for " +
                $"{string.Join(", ", shown)}{more}. Fix the card name or set a printing on the entry, then preview again.";
        }

        return neededRequests > maximumRequests
            ? $"archidekt_push_apply would send nothing: this push needs about {neededRequests} provider requests, " +
                $"over the limit of {maximumRequests} for one operation. Push fewer changes at a time."
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
    /// </summary>
    internal async Task<ArchidektApplyResult> ApplyPlanAsync(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektRemotePlan plan,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        budget.EnsureRequestBound(budget.RequestCount + plan.PredictedProviderRequests);
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

        List<ArchidektOperationStatus> statuses = [];
        for (int index = 0; index < plan.PlannedOperations.Count; index++)
        {
            ArchidektPlannedOperation operation = plan.PlannedOperations[index];
            try
            {
                await ExecutePlannedOperationAsync(
                    current.RemoteId,
                    target,
                    operation,
                    choices,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                statuses.Add(Status(operation.Public, "applied", "Archidekt accepted the operation."));
            }
            catch (ArchidektProviderException exception)
                when (exception.Kind is ArchidektFailureKind.Unavailable or ArchidektFailureKind.Unsupported)
            {
                statuses.Add(Status(operation.Public, "unknown", exception.Message));
                AddNotAttempted(statuses, plan, index + 1);
                return new ArchidektApplyResult(
                    "partial",
                    LocalDeckId: null,
                    LocalRevision: null,
                    current.RemoteId,
                    FinalRemoteFingerprint: null,
                    statuses);
            }
        }

        RemoteDeckSnapshot verified = await transport.GetAsync(
            current.RemoteId,
            requireAuthentication: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        ArchidektRemotePlan residual = ArchidektSyncPlanner.PlanRemoteVerification(verified, target);
        if (residual.PlannedOperations.Count > 0)
        {
            return new ArchidektApplyResult(
                "verification-mismatch",
                LocalDeckId: null,
                LocalRevision: null,
                current.RemoteId,
                verified.RemoteFingerprint,
                statuses);
        }

        return new ArchidektApplyResult(
            "applied",
            LocalDeckId: null,
            LocalRevision: null,
            current.RemoteId,
            verified.RemoteFingerprint,
            statuses);
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
            .First(pair => pair.value.Public.Kind == "entry-add" &&
                string.IsNullOrWhiteSpace(pair.value.TargetEntry?.ProviderCardId) &&
                (failure is not null || choices.Find(pair.value.TargetEntry!) is null))
            .index;
        string reason = failure ?? $"Archidekt has no printing for '{choices.UnresolvedCardNames[0]}'.";
        List<ArchidektOperationStatus> statuses = [];
        for (int index = 0; index < plan.PlannedOperations.Count; index++)
        {
            ArchidektRemoteOperation shown = plan.PlannedOperations[index].Public;
            statuses.Add(index == failedIndex
                ? Status(shown, "not-sent", $"{NotSentMessage} {reason}")
                : Status(shown, "not-attempted", NotAttemptedMessage));
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
        ArchidektRemotePlan plan,
        int firstIndex)
    {
        for (int remaining = firstIndex; remaining < plan.PlannedOperations.Count; remaining++)
        {
            statuses.Add(Status(plan.PlannedOperations[remaining].Public, "not-attempted", NotAttemptedMessage));
        }
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
                "provider-request-rejected" or
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
    /// Sends the provider request for one planned deck action.
    /// </summary>
    private async Task ExecutePlannedOperationAsync(
        string deckId,
        RemoteDeckSnapshot target,
        ArchidektPlannedOperation operation,
        ArchidektPrintingChoices choices,
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
            case "entry-add":
            case "entry-update":
            case "entry-remove":
                await ExecuteCardOperationAsync(
                    deckId,
                    operation,
                    choices,
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
    /// Sends one exact single-card add, modify, or remove operation. A new card uses the printing chosen before the
    /// first write.
    /// </summary>
    private async Task ExecuteCardOperationAsync(
        string deckId,
        ArchidektPlannedOperation operation,
        ArchidektPrintingChoices choices,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        RemoteDeckEntry entry = operation.TargetEntry ?? operation.CurrentEntry!;
        string providerCardId = entry.ProviderCardId;
        if (operation.Public.Kind == "entry-add" && string.IsNullOrWhiteSpace(providerCardId))
        {
            providerCardId = choices.Find(entry)?.ProviderCardId
                ?? throw new InvalidOperationException("A new card reached the write step without a chosen printing.");
        }

        string action = operation.Public.Kind switch
        {
            "entry-add" => "add",
            "entry-update" => "modify",
            "entry-remove" => "remove",
            _ => throw new InvalidOperationException("Unsupported card operation kind."),
        };
        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["cardid"] = ArchidektProviderId.Parse(providerCardId),
            ["patchId"] = ArchidektContract.StableGuid(
                "patch",
                $"{deckId}:{operation.Public.Sequence}:{operation.Public.Kind}:{operation.Public.Subject}").ToString("N"),
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

        await transport.SendCardMutationAsync(deckId, payload, budget, cancellationToken)
            .ConfigureAwait(false);
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
    /// Requires an exact non-case-folded confirmation phrase.
    /// </summary>
    private static void RequireConfirmation(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "confirmation-required",
                "The exact confirmation phrase is required.");
        }
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
            [Status(operation, "unknown", message)]);
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
}
