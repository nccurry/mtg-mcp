namespace MtgMcp.Core;

/// <summary>
/// Loads mutation-ready workspaces and persists local or writeback changes.
/// </summary>
public abstract partial class DeckMutationServiceBase
{
    /// <summary>
    /// Loads a workspace and refreshes Archidekt-bound state before mutation.
    /// </summary>
    protected async Task<DeckWorkspace> LoadForMutationAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        if (workspace.Mode != WorkspaceMode.Archidekt || !workspace.WriteBack)
        {
            return workspace;
        }

        if (string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            throw new InvalidOperationException(
                "Archidekt workspace is missing an Archidekt deck id."
            );
        }

        // Writeback workspaces are refreshed before each mutation so local edits
        // apply on top of Archidekt's latest deck state instead of stale cache.
        DeckWorkspace fresh = await RequireArchidektGateway()
            .ImportDeckAsync(workspace.ArchidektDeckId, writeBack: true, cancellationToken)
            .ConfigureAwait(false);

        fresh.Id = workspace.Id;
        fresh.WriteBack = true;
        PreserveEnrichedSnapshots(workspace, fresh);
        NormalizeWorkspaceCategories(fresh);
        return await Repository.SaveAsync(fresh, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists local card edits and writes them back to Archidekt when enabled.
    /// </summary>
    protected async Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken
    )
    {
        NormalizeWorkspaceCategories(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .PersistCardsAsync(workspace, upsertedCards, removedCards, cancellationToken)
                .ConfigureAwait(false);
            await Repository.SaveAsync(workspace, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists a category locally and to Archidekt when enabled.
    /// </summary>
    protected async Task PersistCategoryAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    )
    {
        NormalizeWorkspaceCategories(workspace);

        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .PersistCategoryAsync(workspace, category, cancellationToken)
                .ConfigureAwait(false);
        }

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a category in Archidekt when the workspace is writeback-enabled.
    /// </summary>
    protected async Task DeleteCategoryInAdapterAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    )
    {
        NormalizeWorkspaceCategories(workspace);

        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .DeleteCategoryAsync(workspace, category, cancellationToken)
                .ConfigureAwait(false);
        }

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a standard deck change response.
    /// </summary>
    protected static DeckChangeResult Change(
        DeckWorkspace workspace,
        DeckMutationKind kind,
        string message
    )
    {
        return new DeckChangeResult
        {
            WorkspaceId = workspace.Id,
            Kind = kind,
            Persistence = DeckPersistence.For(workspace),
            Message = message,
            Workspace = workspace,
        };
    }

    /// <summary>
    /// Keeps cached card category mirrors aligned before persistence or adapter writeback.
    /// </summary>
    private static void NormalizeWorkspaceCategories(DeckWorkspace workspace)
    {
        foreach (DeckCard card in workspace.Cards)
        {
            DeckCategoryOrdering.Normalize(card);
        }
    }

    /// <summary>
    /// Preserves rich Scryfall metadata when a sparse Archidekt refresh represents the same card.
    /// </summary>
    private static void PreserveEnrichedSnapshots(DeckWorkspace cached, DeckWorkspace fresh)
    {
        Dictionary<string, DeckCard> byPrint = [];
        Dictionary<string, DeckCard> byOracle = [];
        Dictionary<string, DeckCard?> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard cachedCard in cached.Cards)
        {
            if (!HasPreservableSnapshot(cachedCard))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cachedCard.ScryfallId))
            {
                byPrint.TryAdd(cachedCard.ScryfallId.Trim(), cachedCard);
            }

            if (!string.IsNullOrWhiteSpace(cachedCard.ScryfallOracleId))
            {
                byOracle.TryAdd(cachedCard.ScryfallOracleId.Trim(), cachedCard);
            }

            string nameKey = NormalizeSnapshotMatchName(cachedCard.Name);
            if (byName.TryGetValue(nameKey, out DeckCard? existingByName))
            {
                byName[nameKey] = existingByName is null || SameOracleIdentity(existingByName, cachedCard)
                    ? existingByName
                    : null;
            }
            else
            {
                byName[nameKey] = cachedCard;
            }
        }

        foreach (DeckCard freshCard in fresh.Cards)
        {
            if (!string.IsNullOrWhiteSpace(freshCard.ScryfallId)
                && byPrint.TryGetValue(freshCard.ScryfallId.Trim(), out DeckCard? printMatch))
            {
                PreserveFullSnapshot(freshCard, printMatch);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(freshCard.ScryfallOracleId)
                && byOracle.TryGetValue(freshCard.ScryfallOracleId.Trim(), out DeckCard? oracleMatch))
            {
                PreserveOracleLevelSnapshotFields(freshCard, oracleMatch);
                continue;
            }

            if (byName.TryGetValue(NormalizeSnapshotMatchName(freshCard.Name), out DeckCard? nameMatch)
                && nameMatch is not null)
            {
                PreserveOracleLevelSnapshotFields(freshCard, nameMatch);
            }
        }
    }

    /// <summary>
    /// Checks whether a cached card has source-backed metadata worth preserving.
    /// </summary>
    private static bool HasPreservableSnapshot(DeckCard card)
    {
        CardSnapshot? snapshot = card.Snapshot;
        return snapshot is not null
            && (snapshot.Provenance.Provider?.Equals("scryfall", StringComparison.OrdinalIgnoreCase) == true
                || !string.IsNullOrWhiteSpace(snapshot.SelectedPrintingReason)
                || snapshot.Legalities.Count > 0
                || snapshot.ProducedMana.Count > 0
                || snapshot.Games.Count > 0
                || snapshot.Finishes.Count > 0
                || snapshot.Faces.Count > 0
                || snapshot.ImageUris.Count > 0);
    }

    /// <summary>
    /// Preserves full snapshot metadata only for exact printing identity matches.
    /// </summary>
    private static void PreserveFullSnapshot(DeckCard target, DeckCard source)
    {
        target.ScryfallId = source.ScryfallId;
        target.ScryfallOracleId = source.ScryfallOracleId;
        target.Snapshot = CopyCardSnapshot(source.Snapshot);
    }

    /// <summary>
    /// Preserves only oracle-level fields when print identity is not exact.
    /// </summary>
    private static void PreserveOracleLevelSnapshotFields(DeckCard target, DeckCard source)
    {
        if (string.IsNullOrWhiteSpace(target.ScryfallOracleId))
        {
            target.ScryfallOracleId = source.ScryfallOracleId;
        }

        CardSnapshot targetSnapshot = target.Snapshot ?? new CardSnapshot();
        CardSnapshot sourceSnapshot = source.Snapshot;
        targetSnapshot.ManaCost = sourceSnapshot.ManaCost;
        targetSnapshot.Layout = sourceSnapshot.Layout;
        targetSnapshot.TypeLine = sourceSnapshot.TypeLine;
        targetSnapshot.ManaValue = sourceSnapshot.ManaValue;
        targetSnapshot.OracleText = sourceSnapshot.OracleText;
        targetSnapshot.Power = sourceSnapshot.Power;
        targetSnapshot.Toughness = sourceSnapshot.Toughness;
        targetSnapshot.Loyalty = sourceSnapshot.Loyalty;
        targetSnapshot.Defense = sourceSnapshot.Defense;
        targetSnapshot.ColorIdentity = sourceSnapshot.ColorIdentity.ToList();
        targetSnapshot.EdhrecRank = sourceSnapshot.EdhrecRank;
        targetSnapshot.Keywords = sourceSnapshot.Keywords.ToList();
        targetSnapshot.ProducedMana = sourceSnapshot.ProducedMana.ToList();
        targetSnapshot.Faces = sourceSnapshot.Faces.Select(CloneFace).ToList();
        targetSnapshot.Legalities = new Dictionary<string, string>(
            sourceSnapshot.Legalities,
            StringComparer.OrdinalIgnoreCase);
        target.Snapshot = targetSnapshot;
    }

    /// <summary>
    /// Checks whether two cached cards share a non-empty oracle identity.
    /// </summary>
    private static bool SameOracleIdentity(DeckCard left, DeckCard right)
    {
        return !string.IsNullOrWhiteSpace(left.ScryfallOracleId)
            && left.ScryfallOracleId.Equals(right.ScryfallOracleId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes names for the last-resort no-conflict metadata preservation fallback.
    /// </summary>
    private static string NormalizeSnapshotMatchName(string name)
    {
        return name.Trim();
    }
}
