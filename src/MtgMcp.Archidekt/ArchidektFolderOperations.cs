using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt folder tree reads, metadata writes, moves, and safe deletion.
/// </summary>
internal sealed class ArchidektFolderOperations
{
    /// <summary>
    /// Provides folder provider routes for this workflow owner.
    /// </summary>
    private readonly ArchidektFolderTransport folderTransport;

    /// <summary>
    /// Provides deck provider routes needed to enrich and verify folder changes.
    /// </summary>
    private readonly ArchidektDeckTransport deckTransport;

    /// <summary>
    /// Stores the hard provider request ceiling for standalone operations.
    /// </summary>
    private readonly int maximumRequestsPerOperation;

    /// <summary>
    /// Creates folder workflows over their folder and deck provider route owners.
    /// </summary>
    internal ArchidektFolderOperations(
        ArchidektFolderTransport folderTransport,
        ArchidektDeckTransport deckTransport,
        int maximumRequestsPerOperation)
    {
        this.folderTransport = folderTransport ?? throw new ArgumentNullException(nameof(folderTransport));
        this.deckTransport = deckTransport ?? throw new ArgumentNullException(nameof(deckTransport));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestsPerOperation);
        this.maximumRequestsPerOperation = maximumRequestsPerOperation;
    }

    /// <summary>
    /// Lists the complete authenticated folder tree.
    /// </summary>
    internal Task<OperationResult<RemoteFolderTree>> ListAsync(CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteFolderTree tree = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                return await EnrichFolderDecksAsync(tree, budget, cancellationToken)
                    .ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Gets one authenticated folder and its direct contents.
    /// </summary>
    internal Task<OperationResult<RemoteFolderTree>> GetAsync(
        string folderId,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteFolderTree tree = await folderTransport.GetAsync(
                    folderId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                return await EnrichFolderDecksAsync(tree, budget, cancellationToken)
                    .ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Creates one folder beneath an exact parent and verifies it in a fresh tree.
    /// </summary>
    internal Task<OperationResult<RemoteFolderRecord>> CreateAsync(
        ArchidektFolderCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteFolderTree before = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                string parentFolderId = ResolveProviderParent(before, request.ParentFolderId);
                string createdFolderId = await folderTransport.CreateAsync(
                    request with { ParentFolderId = parentFolderId },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderTree verified = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderRecord? match = verified.Items.FirstOrDefault(value =>
                    string.Equals(value.FolderId, createdFolderId, StringComparison.Ordinal));
                return match ?? throw Conflict(
                    "folder-create-unverified",
                    "Archidekt did not return the created folder in a fresh tree.");
            });
    }

    /// <summary>
    /// Updates allowlisted folder metadata only when the fresh tree still matches the caller guard.
    /// </summary>
    internal Task<OperationResult<RemoteFolderRecord>> UpdateAsync(
        ArchidektFolderUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteFolderTree before = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                before = await EnrichFolderDecksAsync(before, budget, cancellationToken)
                    .ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedTreeFingerprint,
                    before.TreeFingerprint,
                    "folder-tree-changed");
                RemoteFolderRecord current = FindFolder(before, request.FolderId);
                string? requestedParent = null;
                if (request.UpdateParent)
                {
                    requestedParent = ResolveProviderParent(before, request.ParentFolderId);
                    PreventFolderCycle(before, request.FolderId, requestedParent);
                }

                string name = request.Name is null
                    ? current.Name
                    : ArchidektContract.Required(request.Name, nameof(request.Name));
                string visibility = request.Visibility is null
                    ? current.Visibility
                    : NormalizeVisibility(request.Visibility);
                object payload = new
                {
                    name,
                    @private = visibility == "private",
                    parentFolder = ArchidektProviderId.Parse(
                        request.UpdateParent ? requestedParent : current.ParentFolderId),
                };
                await folderTransport.SendUpdateAsync(
                    request.FolderId,
                    payload,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderTree after = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderRecord updated = FindFolder(after, request.FolderId);
                string? expectedParent = request.UpdateParent
                    ? requestedParent
                    : current.ParentFolderId;
                if (!string.Equals(updated.Name, name, StringComparison.Ordinal) ||
                    !string.Equals(updated.Visibility, visibility, StringComparison.Ordinal) ||
                    !string.Equals(updated.ParentFolderId, expectedParent, StringComparison.Ordinal))
                {
                    throw Conflict(
                        "folder-update-unverified",
                        "Archidekt folder state did not match the requested update.");
                }

                return updated;
            });
    }

    /// <summary>
    /// Moves exact typed folder items after stale-source, missing-item, and cycle preflight checks.
    /// </summary>
    internal Task<OperationResult<ArchidektFolderMoveResult>> MoveItemsAsync(
        ArchidektFolderMoveRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteFolderTree before = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                before = await EnrichFolderDecksAsync(before, budget, cancellationToken)
                    .ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedTreeFingerprint,
                    before.TreeFingerprint,
                    "folder-tree-changed");
                string destinationFolderId = ResolveProviderParent(before, request.DestinationFolderId);
                ArchidektFolderMoveItem[] items = DeduplicateMoveItems(request.Items);
                int deckCount = items.Count(value => value.Kind == "deck");
                budget.EnsureRequestBound(budget.RequestCount + (deckCount * 2) + 2);
                foreach (ArchidektFolderMoveItem item in items)
                {
                    if (item.Kind == "folder")
                    {
                        ValidateFolderMoveItem(before, item, destinationFolderId);
                        continue;
                    }

                    RemoteDeckSnapshot deck = await deckTransport.GetAsync(
                        item.Id,
                        requireAuthentication: true,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                    RequireMoveParent(item.ExpectedParentFolderId, deck.ParentFolderId);
                }

                object payload = new
                {
                    items = items.Select(value => new
                    {
                        type = value.Kind,
                        id = ArchidektProviderId.Parse(value.Id),
                        patch = new
                        {
                            parentFolder = ArchidektProviderId.Parse(destinationFolderId),
                        },
                    }),
                };
                await folderTransport.SendMoveAsync(payload, budget, cancellationToken)
                    .ConfigureAwait(false);
                RemoteFolderTree after = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                List<ArchidektFolderMoveStatus> statuses = [];
                foreach (ArchidektFolderMoveItem item in items)
                {
                    string? finalParent = item.Kind == "folder"
                        ? FindFolder(after, item.Id).ParentFolderId
                        : (await deckTransport.GetAsync(
                            item.Id,
                            requireAuthentication: true,
                            budget,
                            cancellationToken).ConfigureAwait(false)).ParentFolderId;
                    string status = string.Equals(
                        finalParent,
                        destinationFolderId,
                        StringComparison.Ordinal)
                        ? "applied"
                        : "unknown";
                    statuses.Add(new ArchidektFolderMoveStatus(
                        item.Kind,
                        item.Id,
                        item.ExpectedParentFolderId,
                        finalParent,
                        status));
                }

                return new ArchidektFolderMoveResult(statuses, after.TreeFingerprint);
            });
    }

    /// <summary>
    /// Deletes one confirmed empty folder and verifies its absence from a fresh tree.
    /// </summary>
    internal Task<OperationResult<ArchidektApplyResult>> DeleteAsync(
        ArchidektFolderDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RequireConfirmation(request.Confirmation, $"delete folder {request.FolderId}");
                RemoteFolderTree before = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                before = await EnrichFolderDecksAsync(before, budget, cancellationToken)
                    .ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedTreeFingerprint,
                    before.TreeFingerprint,
                    "folder-tree-changed");
                RemoteFolderRecord folder = FindFolder(before, request.FolderId);
                if (!string.Equals(folder.Name, request.ExpectedName, StringComparison.Ordinal))
                {
                    throw Conflict("folder-name-changed", "Archidekt folder name changed before deletion.");
                }

                if (folder.ChildFolderIds.Count > 0 || folder.Decks.Count > 0)
                {
                    throw Conflict("folder-not-empty", "Only an empty Archidekt folder can be deleted.");
                }

                ArchidektRemoteOperation operation = new(
                    1,
                    "folder-delete",
                    request.FolderId,
                    "Delete one verified empty folder.");
                try
                {
                    await folderTransport.SendDeleteAsync(
                        new
                        {
                            items = new[]
                            {
                                new
                                {
                                    type = "folder",
                                    id = ArchidektProviderId.Parse(request.FolderId),
                                },
                            },
                        },
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.Kind == ArchidektFailureKind.Unavailable)
                {
                    return PartialResult(request.FolderId, operation, exception.Message);
                }

                RemoteFolderTree after = await folderTransport.ListAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (after.Items.Any(value =>
                    string.Equals(value.FolderId, request.FolderId, StringComparison.Ordinal)))
                {
                    throw Conflict(
                        "folder-delete-unverified",
                        "Archidekt still lists the folder after deletion.");
                }

                return AppliedResult(request.FolderId, operation, finalFingerprint: after.TreeFingerprint);
            });
    }

    /// <summary>
    /// Joins owned-deck rows into a folder response because the observed tree omits deck children.
    /// </summary>
    private async Task<RemoteFolderTree> EnrichFolderDecksAsync(
        RemoteFolderTree tree,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        List<RemoteDeckSummary> deckSummaries = [];
        string? cursor = null;
        do
        {
            RemoteDeckPage page = await deckTransport.ListAsync(
                cursor,
                100,
                budget,
                cancellationToken).ConfigureAwait(false);
            deckSummaries.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        string? rootFolderId = tree.Items.Count(value => value.ParentFolderId is null) == 1
            ? tree.Items.Single(value => value.ParentFolderId is null).FolderId
            : null;
        RemoteFolderRecord[] items = tree.Items.Select(folder => folder with
        {
            Decks = Array.AsReadOnly(deckSummaries
                .Where(deck => string.Equals(
                    deck.ParentFolderId ?? rootFolderId,
                    folder.FolderId,
                    StringComparison.Ordinal))
                .OrderBy(deck => deck.RemoteId, StringComparer.Ordinal)
                .ToArray()),
        }).ToArray();
        string fingerprint = ArchidektContract.Fingerprint(items.Select(value => new
        {
            value.FolderId,
            value.Name,
            value.Visibility,
            value.ParentFolderId,
            value.Path,
            value.ChildFolderIds,
            decks = value.Decks.Select(deck => deck.RemoteId),
        }));
        return new RemoteFolderTree(items, tree.Evidence, fingerprint);
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
    /// Requires an exact destination folder when one was supplied.
    /// </summary>
    private static void RequireFolderParent(RemoteFolderTree tree, string? parentFolderId)
    {
        if (parentFolderId is not null && !tree.Items.Any(value =>
            string.Equals(value.FolderId, parentFolderId, StringComparison.Ordinal)))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "folder-not-found",
                "The requested Archidekt folder was not found.");
        }
    }

    /// <summary>
    /// Resolves an omitted logical parent to the account's one explicit provider root folder.
    /// </summary>
    private static string ResolveProviderParent(RemoteFolderTree tree, string? parentFolderId)
    {
        if (parentFolderId is not null)
        {
            RequireFolderParent(tree, parentFolderId);
            return parentFolderId;
        }

        RemoteFolderRecord[] roots = tree.Items
            .Where(value => value.ParentFolderId is null)
            .ToArray();
        if (roots.Length != 1)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "root-folder-unavailable",
                "Archidekt did not expose one unambiguous root folder.");
        }

        return roots[0].FolderId;
    }

    /// <summary>
    /// Gets one exact folder or returns a structured not-found outcome.
    /// </summary>
    private static RemoteFolderRecord FindFolder(RemoteFolderTree tree, string folderId)
    {
        return tree.Items.FirstOrDefault(value =>
            string.Equals(value.FolderId, folderId, StringComparison.Ordinal))
            ?? throw new ArchidektProviderException(
                ArchidektFailureKind.NotFound,
                "folder-not-found",
                "The requested Archidekt folder was not found.");
    }

    /// <summary>
    /// Rejects a move that would place one folder beneath itself or a descendant.
    /// </summary>
    private static void PreventFolderCycle(
        RemoteFolderTree tree,
        string folderId,
        string? destinationFolderId)
    {
        string? cursor = destinationFolderId;
        while (cursor is not null)
        {
            if (string.Equals(cursor, folderId, StringComparison.Ordinal))
            {
                throw Conflict("folder-cycle", "The requested folder move would create a cycle.");
            }

            cursor = tree.Items.FirstOrDefault(value =>
                string.Equals(value.FolderId, cursor, StringComparison.Ordinal))?.ParentFolderId;
        }
    }

    /// <summary>
    /// Deduplicates typed move items while rejecting incompatible duplicate identities.
    /// </summary>
    private static ArchidektFolderMoveItem[] DeduplicateMoveItems(
        IReadOnlyList<ArchidektFolderMoveItem> items)
    {
        if (items.Count == 0)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "move-items-required",
                "At least one folder move item is required.");
        }

        Dictionary<string, ArchidektFolderMoveItem> unique = new(StringComparer.Ordinal);
        foreach (ArchidektFolderMoveItem item in items)
        {
            string kind = item.Kind.ToLowerInvariant();
            if (kind is not ("deck" or "folder"))
            {
                throw new ArchidektProviderException(
                    ArchidektFailureKind.InvalidInput,
                    "invalid-folder-item-kind",
                    "Folder move item kind must be deck or folder.");
            }

            string id = ArchidektContract.Required(item.Id, nameof(item.Id));
            string key = $"{kind}:{id}";
            ArchidektFolderMoveItem normalized = item with { Kind = kind, Id = id };
            if (unique.TryGetValue(key, out ArchidektFolderMoveItem? existing) &&
                !Equals(existing, normalized))
            {
                throw new ArchidektProviderException(
                    ArchidektFailureKind.InvalidInput,
                    "conflicting-folder-items",
                    "Duplicate folder move items disagree about their current parent.");
            }

            unique[key] = normalized;
        }

        return unique.Values.OrderBy(value => value.Kind).ThenBy(value => value.Id).ToArray();
    }

    /// <summary>
    /// Validates one exact current parent and cycle boundary before a move request is sent.
    /// </summary>
    private static void ValidateFolderMoveItem(
        RemoteFolderTree tree,
        ArchidektFolderMoveItem item,
        string? destinationFolderId)
    {
        RemoteFolderRecord folder = FindFolder(tree, item.Id);
        RequireMoveParent(item.ExpectedParentFolderId, folder.ParentFolderId);
        PreventFolderCycle(tree, item.Id, destinationFolderId);
    }

    /// <summary>
    /// Requires one typed item to retain its exact caller-observed parent before mutation.
    /// </summary>
    private static void RequireMoveParent(string? expectedParent, string? actualParent)
    {
        if (!string.Equals(expectedParent, actualParent, StringComparison.Ordinal))
        {
            throw Conflict("folder-assignment-changed", "A folder item changed parents before apply.");
        }
    }

    /// <summary>
    /// Maps the explicit provider visibility vocabulary.
    /// </summary>
    private static string NormalizeVisibility(string value)
    {
        return ArchidektContract.Required(value, nameof(value)).ToLowerInvariant() switch
        {
            "private" => "private",
            "public" => "public",
            _ => throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-folder-visibility",
                "Folder visibility must be private or public."),
        };
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
    /// Creates a verified one-operation success result.
    /// </summary>
    private static ArchidektApplyResult AppliedResult(
        string remoteId,
        ArchidektRemoteOperation operation,
        string? finalFingerprint)
    {
        return new ArchidektApplyResult(
            "applied",
            LocalDeckId: null,
            LocalRevision: null,
            remoteId,
            finalFingerprint,
            [Status(operation, "applied", "Provider absence was verified.")]);
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
