namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Stores the Commander deck size limit enforced before included-card additions.
    /// </summary>
    private const int CommanderDeckSizeLimit = 100;

    /// <summary>
    /// Adds a card to a workspace while refusing accidental Commander overfills.
    /// </summary>
    public Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken
    )
    {
        return AddCardAsync(
            workspaceId,
            cardName,
            quantity,
            category,
            force: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds a card to a workspace and optionally permits an intentional Commander overfill.
    /// </summary>
    public async Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string category,
        bool force,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string normalizedCategory = NormalizeCategoryName(category);
        int amount = Math.Max(1, quantity);
        EnsureCategory(workspace, normalizedCategory);
        EnsureCommanderIncludedAdditionIsSafe(workspace, normalizedCategory, amount, force);
        DeckCard? existing = FindCard(workspace, cardName, normalizedCategory);
        DeckCard changed;

        if (existing is null)
        {
            changed = await CreateDeckCardAsync(
                    cardName,
                    amount,
                    normalizedCategory,
                    cancellationToken
                )
                .ConfigureAwait(false);
            workspace.Cards.Add(changed);
        }
        else
        {
            existing.Quantity += amount;
            changed = existing;
        }

        await PersistCardsAsync(workspace, [changed], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardAdded,
            $"Added {amount} {changed.Name} to {normalizedCategory}."
        );
    }

    /// <summary>
    /// Adds multiple cards to a workspace after validating the full package.
    /// </summary>
    public async Task<DeckChangeResult> AddCardsBulkAsync(
        string workspaceId,
        IReadOnlyList<BulkDeckCardAdd> cards,
        bool force,
        CancellationToken cancellationToken)
    {
        if (cards.Count == 0)
        {
            throw new InvalidOperationException("At least one card add is required.");
        }

        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        List<ValidatedBulkDeckCardAdd> validatedAdds = [];
        for (int index = 0; index < cards.Count; index++)
        {
            BulkDeckCardAdd request = cards[index];
            if (string.IsNullOrWhiteSpace(request.CardName))
            {
                throw new InvalidOperationException($"Bulk add at index {index} is missing a card name.");
            }

            string primaryCategory = NormalizeCategoryName(request.PrimaryCategory);
            validatedAdds.Add(new ValidatedBulkDeckCardAdd(
                request.CardName.Trim(),
                Math.Max(1, request.Quantity),
                primaryCategory,
                NormalizeSecondaryCategories(request.SecondaryCategories, primaryCategory)));
        }

        EnsureCommanderBulkAdditionIsSafe(workspace, validatedAdds, force);
        foreach (ValidatedBulkDeckCardAdd add in validatedAdds)
        {
            EnsureCategory(workspace, add.PrimaryCategory);
            foreach (string secondaryCategory in add.SecondaryCategories)
            {
                EnsureCategory(workspace, secondaryCategory);
            }
        }

        IReadOnlyDictionary<string, CardInfo> resolvedCards = await ResolveCardsForMutationAsync(
                validatedAdds.Select(add => add.CardName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                cancellationToken)
            .ConfigureAwait(false);
        List<DeckCard> changedCards = [];
        int totalQuantity = 0;
        foreach (ValidatedBulkDeckCardAdd add in validatedAdds)
        {
            DeckCard? existing = FindCard(workspace, add.CardName, add.PrimaryCategory);
            DeckCard changed;
            if (existing is null)
            {
                resolvedCards.TryGetValue(add.CardName, out CardInfo? cardInfo);
                changed = CreateDeckCard(add.CardName, add.Quantity, add.PrimaryCategory, cardInfo);
                workspace.Cards.Add(changed);
            }
            else
            {
                existing.Quantity += add.Quantity;
                changed = existing;
            }

            foreach (string secondaryCategory in add.SecondaryCategories)
            {
                DeckCategoryOrdering.AddSecondary(changed, secondaryCategory);
            }

            if (!changedCards.Contains(changed))
            {
                changedCards.Add(changed);
            }

            totalQuantity += add.Quantity;
        }

        await PersistCardsAsync(workspace, changedCards, [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardAdded,
            $"Added {totalQuantity} card(s) across {validatedAdds.Count} bulk request(s).");
    }

    /// <summary>
    /// Removes the card.
    /// </summary>
    public async Task<DeckChangeResult> RemoveCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category);
        int amount = Math.Max(1, quantity);
        DeckCard? removed = null;

        if (card.Quantity <= amount)
        {
            workspace.Cards.Remove(card);
            removed = card;
        }
        else
        {
            card.Quantity -= amount;
        }

        await PersistCardsAsync(
                workspace,
                removed is null ? [card] : [],
                removed is null ? [] : [removed],
                cancellationToken
            )
            .ConfigureAwait(false);

        return Change(workspace, DeckMutationKind.CardRemoved, $"Removed {amount} {card.Name}.");
    }

    /// <summary>
    /// Sets the card quantity.
    /// </summary>
    public async Task<DeckChangeResult> SetCardQuantityAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category);
        DeckCard? removed = null;

        if (quantity <= 0)
        {
            workspace.Cards.Remove(card);
            removed = card;
        }
        else
        {
            card.Quantity = quantity;
        }

        await PersistCardsAsync(
                workspace,
                removed is null ? [card] : [],
                removed is null ? [] : [removed],
                cancellationToken
            )
            .ConfigureAwait(false);

        return Change(
            workspace,
            DeckMutationKind.QuantityChanged,
            $"Set {card.Name} quantity to {quantity}."
        );
    }

    /// <summary>
    /// Moves the card.
    /// </summary>
    public async Task<DeckChangeResult> MoveCardAsync(
        string workspaceId,
        string cardName,
        string toCategory,
        string? fromCategory,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, fromCategory);
        string normalizedCategory = NormalizeCategoryName(toCategory);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.SetPrimary(card, normalizedCategory);

        await PersistCardsAsync(workspace, [card], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardMoved,
            $"Moved {card.Name} to {normalizedCategory}."
        );
    }

    /// <summary>
    /// Moves multiple cards in one persisted mutation.
    /// </summary>
    public async Task<DeckChangeResult> MoveCardsBulkAsync(
        string workspaceId,
        IReadOnlyList<BulkDeckCardMove> moves,
        CancellationToken cancellationToken)
    {
        if (moves.Count == 0)
        {
            throw new InvalidOperationException("At least one card move is required.");
        }

        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        List<DeckCard> changedCards = [];
        foreach (BulkDeckCardMove move in moves)
        {
            if (string.IsNullOrWhiteSpace(move.CardName))
            {
                throw new InvalidOperationException("Every bulk move must include a card name.");
            }

            string normalizedCategory = NormalizeCategoryName(move.ToCategory);
            EnsureCategory(workspace, normalizedCategory);
            DeckCard card = FindRequiredCard(workspace, move.CardName, move.FromCategory);
            int requestedQuantity = move.Quantity ?? card.Quantity;
            if (requestedQuantity <= 0)
            {
                throw new InvalidOperationException("Bulk move quantity must be greater than zero when supplied.");
            }

            if (requestedQuantity >= card.Quantity)
            {
                DeckCategoryOrdering.SetPrimary(card, normalizedCategory);
                AddChangedCard(changedCards, card);
                continue;
            }

            if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
            {
                throw new InvalidOperationException(
                    "Partial bulk moves are not writeback-safe for Archidekt workspaces. "
                        + "Move the whole card row or refresh/import as a local-only workspace first.");
            }

            DeckCard? target = FindCard(workspace, card.Name, normalizedCategory);
            card.Quantity -= requestedQuantity;
            AddChangedCard(changedCards, card);
            if (target is not null)
            {
                target.Quantity += requestedQuantity;
                AddChangedCard(changedCards, target);
                continue;
            }

            DeckCard split = CloneCardForPartialMove(card, requestedQuantity, normalizedCategory);
            workspace.Cards.Add(split);
            AddChangedCard(changedCards, split);
        }

        await PersistCardsAsync(workspace, changedCards, [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardMoved,
            $"Moved {moves.Count} card row(s).");
    }

    /// <summary>
    /// Clones a card row for a local-only partial category move.
    /// </summary>
    private static DeckCard CloneCardForPartialMove(
        DeckCard source,
        int quantity,
        string toCategory)
    {
        DeckCard clone = new()
        {
            Name = source.Name,
            Quantity = quantity,
            PrimaryCategory = source.PrimaryCategory,
            Categories = source.Categories.ToList(),
            ScryfallId = source.ScryfallId,
            ScryfallOracleId = source.ScryfallOracleId,
            ArchidektCardId = source.ArchidektCardId,
            Modifier = source.Modifier,
            Companion = source.Companion,
            FlippedDefault = source.FlippedDefault,
            Snapshot = CopyCardSnapshot(source.Snapshot),
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase),
        };
        DeckCategoryOrdering.SetPrimary(clone, toCategory);
        clone.ArchidektDeckRelationId = null;
        return clone;
    }

    /// <summary>
    /// Adds a changed card once by local row id.
    /// </summary>
    private static void AddChangedCard(List<DeckCard> changedCards, DeckCard card)
    {
        if (!changedCards.Any(existing => existing.Id.Equals(card.Id, StringComparison.OrdinalIgnoreCase)))
        {
            changedCards.Add(card);
        }
    }

    /// <summary>
    /// Creates the deck card.
    /// </summary>
    private async Task<DeckCard> CreateDeckCardAsync(
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken
    )
    {
        CardInfo? cardInfo = await TryGetCardForMutationAsync(cardName, cancellationToken)
            .ConfigureAwait(false);
        return CreateDeckCard(cardName, quantity, category, cardInfo);
    }

    /// <summary>
    /// Creates a workspace card from optional catalog metadata.
    /// </summary>
    private static DeckCard CreateDeckCard(
        string cardName,
        int quantity,
        string category,
        CardInfo? cardInfo)
    {
        string requestedName = cardName.Trim();
        string displayName = BasicLandIdentity.TryGetCanonicalName(requestedName, out string canonicalName)
            ? canonicalName
            : cardInfo?.Name ?? requestedName;
        DeckCard card = new()
        {
            Name = displayName,
            Quantity = Math.Max(1, quantity),
            PrimaryCategory = category,
            Categories = [category],
            ScryfallId = cardInfo?.Id,
            ScryfallOracleId = cardInfo?.OracleId,
        };

        if (cardInfo is not null)
        {
            ApplyCardSnapshot(card, cardInfo);
        }

        DeckCategoryOrdering.Normalize(card, category);
        return card;
    }

    /// <summary>
    /// Resolves optional card metadata for mutations while allowing deck edits to continue during catalog outages.
    /// </summary>
    private async Task<CardInfo?> TryGetCardForMutationAsync(
        string cardName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a batch of card names for mutation snapshots while tolerating catalog outages.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, CardInfo>> ResolveCardsForMutationAsync(
        IReadOnlyList<string> cardNames,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CardCatalog.GetCardsByNamesAsync(cardNames, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Finds the required card.
    /// </summary>
    private static DeckCard FindRequiredCard(
        DeckWorkspace workspace,
        string cardName,
        string? category
    )
    {
        return FindCard(workspace, cardName, category)
            ?? throw new InvalidOperationException(
                $"Card '{cardName}' was not found in workspace '{workspace.Id}'."
            );
    }

    /// <summary>
    /// Refuses included Commander additions that would unexpectedly exceed the deck size limit.
    /// </summary>
    private static void EnsureCommanderIncludedAdditionIsSafe(
        DeckWorkspace workspace,
        string category,
        int quantity,
        bool force)
    {
        if (force || !workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        if (!DeckCategoryInclusion.IsIncludedInDeck(categoryMap, category))
        {
            return;
        }

        int includedCount = DeckCategoryInclusion.IncludedCards(workspace)
            .Sum(card => Math.Max(0, card.Quantity));
        int projectedCount = includedCount + Math.Max(1, quantity);
        if (projectedCount <= CommanderDeckSizeLimit)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Adding {Math.Max(1, quantity)} card(s) to included category '{category}' would raise this Commander deck from {includedCount} to {projectedCount} included cards. Add to an excluded category such as Maybeboard, set the category to IncludedInDeck=false, or retry with force=true if this overfill is intentional."
        );
    }

    /// <summary>
    /// Refuses bulk included Commander additions that would unexpectedly exceed the deck size limit.
    /// </summary>
    private static void EnsureCommanderBulkAdditionIsSafe(
        DeckWorkspace workspace,
        IReadOnlyList<ValidatedBulkDeckCardAdd> adds,
        bool force)
    {
        if (force || !workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        int includedAdditions = 0;
        foreach (ValidatedBulkDeckCardAdd add in adds)
        {
            if (IsCategoryIncludedAfterEnsure(categoryMap, add.PrimaryCategory))
            {
                includedAdditions += add.Quantity;
            }
        }

        if (includedAdditions == 0)
        {
            return;
        }

        int includedCount = DeckCategoryInclusion.IncludedCards(workspace)
            .Sum(card => Math.Max(0, card.Quantity));
        int projectedCount = includedCount + includedAdditions;
        if (projectedCount <= CommanderDeckSizeLimit)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Adding {includedAdditions} card(s) to included categories would raise this Commander deck from {includedCount} to {projectedCount} included cards. Add cards to excluded categories such as Maybeboard, set those categories to IncludedInDeck=false, or retry with force=true if this overfill is intentional."
        );
    }

    /// <summary>
    /// Checks the inclusion flag a category will have after implicit creation.
    /// </summary>
    private static bool IsCategoryIncludedAfterEnsure(
        IReadOnlyDictionary<string, DeckCategory> categoryMap,
        string category)
    {
        return categoryMap.TryGetValue(category, out DeckCategory? existing)
            ? existing.IncludedInDeck
            : !DeckDefaults.IsDefaultExcludedCategory(category);
    }

    /// <summary>
    /// Normalizes a secondary category list while removing the primary category.
    /// </summary>
    private static List<string> NormalizeSecondaryCategories(
        IReadOnlyList<string>? categories,
        string primaryCategory)
    {
        List<string> normalized = [];
        foreach (string? category in categories ?? [])
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                continue;
            }

            string normalizedCategory = NormalizeCategoryName(category);
            if (normalizedCategory.Equals(primaryCategory, StringComparison.OrdinalIgnoreCase)
                || normalized.Any(value => value.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(normalizedCategory);
        }

        return normalized;
    }

    /// <summary>
    /// Stores a validated bulk add request.
    /// </summary>
    private sealed record ValidatedBulkDeckCardAdd(
        string CardName,
        int Quantity,
        string PrimaryCategory,
        List<string> SecondaryCategories);
}
