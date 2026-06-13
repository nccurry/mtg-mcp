using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Applies edit-plan operations to cloned workspaces for preview calculations.
/// </summary>
internal sealed class DeckPlanPreviewer
{
    /// <summary>
    /// Resolves added cards when preview metrics request catalog-backed snapshots.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Creates a previewer backed by the configured card catalog.
    /// </summary>
    public DeckPlanPreviewer(ICardCatalog cardCatalog)
    {
        this.cardCatalog = cardCatalog;
    }

    /// <summary>
    /// Clones a deck workspace so preview operations cannot mutate saved state.
    /// </summary>
    public DeckWorkspace CloneWorkspace(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        return JsonSerializer.Deserialize<DeckWorkspace>(json)
            ?? throw new InvalidOperationException("Unable to clone deck workspace for preview.");
    }

    /// <summary>
    /// Applies one edit operation to a preview workspace.
    /// </summary>
    public async Task ApplyOperationAsync(
        DeckWorkspace workspace,
        DeckEditOperation operation,
        bool resolveAddedCards,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        await ApplyOperationsAsync(workspace, [operation], resolveAddedCards, warnings, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies edit operations to a preview workspace while sharing one resolved-card catalog.
    /// </summary>
    public async Task ApplyOperationsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckEditOperation> operations,
        bool resolveAddedCards,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        PreviewCardCatalog previewCatalog = new(cardCatalog, resolveAddedCards);
        await previewCatalog.PreloadAsync(operations, cancellationToken).ConfigureAwait(false);
        DeckWorkspaceService workspaceService = new(
            new PreviewWorkspaceRepository(workspace),
            previewCatalog);
        WorkspaceMode originalMode = workspace.Mode;
        bool originalWriteBack = workspace.WriteBack;

        try
        {
            workspace.Mode = WorkspaceMode.Local;
            workspace.WriteBack = false;

            foreach (DeckEditOperation operation in operations)
            {
                try
                {
                    await ApplyOperationAsync(workspaceService, workspace.Id, operation, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    warnings.Add($"Preview skipped operation '{operation.Operation}': {exception.Message}");
                }
            }
        }
        finally
        {
            workspace.Mode = originalMode;
            workspace.WriteBack = originalWriteBack;
        }

        foreach (string cardName in previewCatalog.UnresolvedCardNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"Could not resolve added card '{cardName}' for preview metrics.");
        }
    }

    /// <summary>
    /// Routes one preview operation through the same workspace mutation methods used by plan application.
    /// </summary>
    private static async Task ApplyOperationAsync(
        DeckWorkspaceService workspaceService,
        string workspaceId,
        DeckEditOperation operation,
        CancellationToken cancellationToken)
    {
        switch (operation.Operation)
        {
            case DeckEditOperations.AddCard:
                await workspaceService.AddCardAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    operation.Quantity ?? 1,
                    operation.Category ?? DeckDefaults.Mainboard,
                    force: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.RemoveCard:
                await workspaceService.RemoveCardAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    operation.Quantity ?? 1,
                    operation.Category,
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.SetCardQuantity:
                await workspaceService.SetCardQuantityAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    operation.Quantity ?? 1,
                    operation.Category,
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.MoveCard:
                await workspaceService.MoveCardAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    Require(operation.ToCategory, "toCategory"),
                    operation.FromCategory,
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.AddCardCategory:
                await workspaceService.AddCardCategoryAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    Require(operation.Category, "category"),
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.RemoveCardCategory:
                await workspaceService.RemoveCardCategoryAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    Require(operation.Category, "category"),
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.SetPrimaryCardCategory:
                await workspaceService.SetPrimaryCardCategoryAsync(
                    workspaceId,
                    Require(operation.CardName, "cardName"),
                    Require(operation.Category, "category"),
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.CreateCategory:
                await workspaceService.CreateCategoryAsync(
                    workspaceId,
                    Require(operation.Category, "category"),
                    operation.IncludedInDeck ?? !DeckDefaults.IsDefaultExcludedCategory(Require(operation.Category, "category")),
                    operation.IncludedInPrice ?? !DeckDefaults.IsDefaultPriceExcludedCategory(Require(operation.Category, "category")),
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.RenameCategory:
                await workspaceService.RenameCategoryAsync(
                    workspaceId,
                    Require(operation.FromCategory, "fromCategory"),
                    Require(operation.ToCategory, "toCategory"),
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.DeleteCategory:
                await workspaceService.DeleteCategoryAsync(
                    workspaceId,
                    Require(operation.Category, "category"),
                    operation.ToCategory ?? DeckDefaults.Mainboard,
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.UpdateDeckMetadata:
                await workspaceService.UpdateDeckMetadataAsync(
                    workspaceId,
                    operation.Name,
                    operation.Format,
                    operation.Description,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unknown deck edit operation '{operation.Operation}'.");
        }
    }

    /// <summary>
    /// Requires an operation field value.
    /// </summary>
    private static string Require(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Deck edit operation is missing required field '{name}'.");
    }

    /// <summary>
    /// Stores the cloned preview workspace behind the normal repository contract.
    /// </summary>
    private sealed class PreviewWorkspaceRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Stores the preview workspace reference.
        /// </summary>
        private DeckWorkspace workspace;

        /// <summary>
        /// Creates a repository around one cloned workspace.
        /// </summary>
        public PreviewWorkspaceRepository(DeckWorkspace workspace)
        {
            this.workspace = workspace;
        }

        /// <summary>
        /// Saves the preview workspace in memory.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            this.workspace = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets the preview workspace by id.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            DeckWorkspace? result = workspace.Id.Equals(workspaceId, StringComparison.OrdinalIgnoreCase)
                ? workspace
                : null;
            return Task.FromResult(result);
        }

        /// <summary>
        /// Lists the single preview workspace.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>([workspace]);
        }
    }

    /// <summary>
    /// Controls whether preview add-card operations resolve catalog snapshots.
    /// </summary>
    private sealed class PreviewCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Stores the configured card catalog.
        /// </summary>
        private readonly ICardCatalog inner;

        /// <summary>
        /// Stores whether added cards should be looked up.
        /// </summary>
        private readonly bool resolveAddedCards;

        /// <summary>
        /// Stores cards resolved before preview operations are applied.
        /// </summary>
        private readonly Dictionary<string, CardInfo> preloadedCards = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Stores whether the bulk preload failed and single lookups should be skipped.
        /// </summary>
        private bool preloadFailed;

        /// <summary>
        /// Creates a preview catalog wrapper.
        /// </summary>
        public PreviewCardCatalog(ICardCatalog inner, bool resolveAddedCards)
        {
            this.inner = inner;
            this.resolveAddedCards = resolveAddedCards;
        }

        /// <summary>
        /// Gets names whose optional preview metadata could not be resolved.
        /// </summary>
        public List<string> UnresolvedCardNames { get; } = [];

        /// <summary>
        /// Resolves distinct added card names before mutation playback.
        /// </summary>
        public async Task PreloadAsync(
            IReadOnlyList<DeckEditOperation> operations,
            CancellationToken cancellationToken)
        {
            if (!resolveAddedCards)
            {
                return;
            }

            List<string> names = [];
            foreach (DeckEditOperation operation in operations)
            {
                if (!operation.Operation.Equals(DeckEditOperations.AddCard, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(operation.CardName))
                {
                    continue;
                }

                string cardName = operation.CardName.Trim();
                if (!names.Contains(cardName, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(cardName);
                }
            }

            if (names.Count == 0)
            {
                return;
            }

            try
            {
                IReadOnlyDictionary<string, CardInfo> resolved = await inner
                    .GetCardsByNamesAsync(names, cancellationToken)
                    .ConfigureAwait(false);
                foreach (KeyValuePair<string, CardInfo> pair in resolved)
                {
                    preloadedCards[pair.Key] = pair.Value;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                preloadFailed = true;
                foreach (string name in names)
                {
                    AddUnresolved(name);
                }
            }
        }

        /// <summary>
        /// Searches cards with provider-specific syntax.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            return inner.SearchCardsAsync(query, limit, cancellationToken);
        }

        /// <summary>
        /// Searches cards from a provider-neutral request.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return inner.SearchCardsAsync(request, limit, cancellationToken);
        }

        /// <summary>
        /// Gets card details only when preview metrics requested resolved additions.
        /// </summary>
        public async Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            if (!resolveAddedCards)
            {
                return null;
            }

            if (preloadedCards.Count > 0 || preloadFailed)
            {
                if (preloadedCards.TryGetValue(nameOrId, out CardInfo? preloaded))
                {
                    return preloaded;
                }

                AddUnresolved(nameOrId);
                return null;
            }

            try
            {
                CardInfo? card = await inner.GetCardAsync(nameOrId, cancellationToken)
                    .ConfigureAwait(false);
                if (card is null)
                {
                    AddUnresolved(nameOrId);
                }

                return card;
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                AddUnresolved(nameOrId);
                return null;
            }
        }

        /// <summary>
        /// Gets cards by name through the configured catalog.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return inner.GetCardsByNamesAsync(names, cancellationToken);
        }

        /// <summary>
        /// Gets rulings through the configured catalog.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return inner.GetRulingsAsync(nameOrId, cancellationToken);
        }

        /// <summary>
        /// Gets prints through the configured catalog.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return inner.GetPrintsAsync(nameOrId, cancellationToken);
        }

        /// <summary>
        /// Suggests cards through the configured catalog.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return inner.SuggestCardsAsync(prompt, format, limit, cancellationToken);
        }

        /// <summary>
        /// Records an unresolved name once.
        /// </summary>
        private void AddUnresolved(string name)
        {
            if (!UnresolvedCardNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                UnresolvedCardNames.Add(name);
            }
        }
    }
}
