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
    /// Stores the hard provider request ceiling for standalone operations.
    /// </summary>
    private readonly int maximumRequestsPerOperation;

    /// <summary>
    /// Creates deck workflows over one deck provider route owner.
    /// </summary>
    internal ArchidektDeckOperations(
        ArchidektDeckTransport transport,
        int maximumRequestsPerOperation)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
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
                    "remote-deck-changed");
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
                    "remote-deck-changed");
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                RequireFingerprint(
                    expectedPlanFingerprint,
                    plan.PlanFingerprint,
                    "push-preview-changed");
                return await ApplyPlanAsync(
                    current,
                    target,
                    plan,
                    budget,
                    cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Executes one exact update plan in stable order and reports partial or unknown state without retries.
    /// </summary>
    internal async Task<ArchidektApplyResult> ApplyPlanAsync(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektRemotePlan plan,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        budget.EnsureRequestBound(budget.RequestCount + plan.PredictedProviderRequests);
        List<ArchidektOperationStatus> statuses = [];
        Dictionary<string, string> resolvedCards = new(StringComparer.Ordinal);
        for (int index = 0; index < plan.PlannedOperations.Count; index++)
        {
            ArchidektPlannedOperation operation = plan.PlannedOperations[index];
            try
            {
                await ExecutePlannedOperationAsync(
                    current.RemoteId,
                    target,
                    operation,
                    resolvedCards,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                statuses.Add(Status(operation.Public, "applied", "Archidekt accepted the operation."));
            }
            catch (ArchidektProviderException exception)
                when (exception.Kind is ArchidektFailureKind.Unavailable or ArchidektFailureKind.Unsupported)
            {
                statuses.Add(Status(operation.Public, "unknown", exception.Message));
                for (int remaining = index + 1; remaining < plan.PlannedOperations.Count; remaining++)
                {
                    statuses.Add(Status(
                        plan.PlannedOperations[remaining].Public,
                        "not-attempted",
                        "A prior provider operation did not complete safely."));
                }

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
    /// Translates one planned primitive into its exact observed provider request.
    /// </summary>
    private async Task ExecutePlannedOperationAsync(
        string deckId,
        RemoteDeckSnapshot target,
        ArchidektPlannedOperation operation,
        IDictionary<string, string> resolvedCards,
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
                    resolvedCards,
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
    /// Resolves and sends one exact single-card add, modify, or remove operation.
    /// </summary>
    private async Task ExecuteCardOperationAsync(
        string deckId,
        ArchidektPlannedOperation operation,
        IDictionary<string, string> resolvedCards,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        RemoteDeckEntry entry = operation.TargetEntry ?? operation.CurrentEntry!;
        string providerCardId = entry.ProviderCardId;
        if (operation.Public.Kind == "entry-add" && string.IsNullOrWhiteSpace(providerCardId))
        {
            string key = $"{entry.PrintingId}:{entry.SetCode}:{entry.CollectorNumber}:{entry.CardName}";
            if (!resolvedCards.TryGetValue(key, out providerCardId!))
            {
                providerCardId = await transport.ResolveCardIdAsync(
                    entry,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                resolvedCards[key] = providerCardId;
            }
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
    private static void RequireFingerprint(string expected, string actual, string reasonCode)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw Conflict(reasonCode, "Provider evidence changed after the preview.");
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
            !string.Equals(value, entry.PrimaryCategoryName, StringComparison.OrdinalIgnoreCase)));
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
    private static ArchidektProviderException Conflict(string reasonCode, string message)
    {
        return new ArchidektProviderException(
            ArchidektFailureKind.Conflict,
            reasonCode,
            message);
    }
}
