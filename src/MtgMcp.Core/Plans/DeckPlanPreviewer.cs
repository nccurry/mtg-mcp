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
        switch (operation.Operation)
        {
            case DeckEditOperations.AddCard:
                await AddCardAsync(
                    workspace,
                    Require(operation.CardName, "cardName"),
                    operation.Quantity ?? 1,
                    operation.Category ?? DeckDefaults.Mainboard,
                    resolveAddedCards,
                    warnings,
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.RemoveCard:
                RemoveCard(workspace, Require(operation.CardName, "cardName"), operation.Quantity ?? 1, operation.Category, warnings);
                break;
            case DeckEditOperations.SetCardQuantity:
                SetCardQuantity(workspace, Require(operation.CardName, "cardName"), operation.Quantity ?? 1, operation.Category, warnings);
                break;
            case DeckEditOperations.MoveCard:
                MoveCard(workspace, Require(operation.CardName, "cardName"), Require(operation.ToCategory, "toCategory"), operation.FromCategory, warnings);
                break;
            case DeckEditOperations.AddCardCategory:
                AddCardCategory(workspace, Require(operation.CardName, "cardName"), Require(operation.Category, "category"), warnings);
                break;
            case DeckEditOperations.RemoveCardCategory:
                RemoveCardCategory(workspace, Require(operation.CardName, "cardName"), Require(operation.Category, "category"), warnings);
                break;
            case DeckEditOperations.SetPrimaryCardCategory:
                SetPrimaryCardCategory(workspace, Require(operation.CardName, "cardName"), Require(operation.Category, "category"), warnings);
                break;
            case DeckEditOperations.CreateCategory:
                DeckCategory category = EnsureCategory(workspace, Require(operation.Category, "category"));
                category.IncludedInDeck = operation.IncludedInDeck ?? true;
                category.IncludedInPrice = operation.IncludedInPrice ?? true;
                break;
            case DeckEditOperations.RenameCategory:
                RenameCategory(workspace, Require(operation.FromCategory, "fromCategory"), Require(operation.ToCategory, "toCategory"), warnings);
                break;
            case DeckEditOperations.DeleteCategory:
                DeleteCategory(workspace, Require(operation.Category, "category"), operation.ToCategory ?? DeckDefaults.Mainboard);
                break;
            case DeckEditOperations.UpdateDeckMetadata:
                workspace.Name = string.IsNullOrWhiteSpace(operation.Name) ? workspace.Name : operation.Name;
                workspace.Format = string.IsNullOrWhiteSpace(operation.Format) ? workspace.Format : operation.Format;
                workspace.Description = operation.Description ?? workspace.Description;
                break;
            default:
                warnings.Add($"Preview skipped unsupported operation '{operation.Operation}'.");
                break;
        }
    }

    /// <summary>
    /// Adds a card to the preview workspace.
    /// </summary>
    private async Task AddCardAsync(
        DeckWorkspace workspace,
        string cardName,
        int quantity,
        string category,
        bool resolveAddedCards,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCard? existing = FindCard(workspace, cardName, normalizedCategory);
        if (existing is not null)
        {
            existing.Quantity += Math.Max(1, quantity);
            return;
        }

        CardInfo? cardInfo = resolveAddedCards
            ? await TryGetCardForPreviewAsync(cardName, cancellationToken).ConfigureAwait(false)
            : null;
        DeckCard card = new()
        {
            Name = cardInfo?.Name ?? cardName.Trim(),
            Quantity = Math.Max(1, quantity),
            PrimaryCategory = normalizedCategory,
            Categories = [normalizedCategory],
            ScryfallId = cardInfo?.Id,
            ScryfallOracleId = cardInfo?.OracleId
        };

        if (cardInfo is not null)
        {
            ApplyCardSnapshot(card, cardInfo);
        }
        else if (resolveAddedCards)
        {
            warnings.Add($"Could not resolve added card '{cardName}' for preview metrics.");
        }

        workspace.Cards.Add(card);
    }

    /// <summary>
    /// Resolves optional card metadata while allowing preview metrics to continue during catalog outages.
    /// </summary>
    private async Task<CardInfo?> TryGetCardForPreviewAsync(
        string cardName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await cardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes a card from the preview workspace.
    /// </summary>
    private static void RemoveCard(
        DeckWorkspace workspace,
        string cardName,
        int quantity,
        string? category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category);
        if (card is null)
        {
            warnings.Add($"Preview could not remove missing card '{cardName}'.");
            return;
        }

        int amount = Math.Max(1, quantity);
        if (card.Quantity <= amount)
        {
            workspace.Cards.Remove(card);
        }
        else
        {
            card.Quantity -= amount;
        }
    }

    /// <summary>
    /// Sets a preview card quantity.
    /// </summary>
    private static void SetCardQuantity(
        DeckWorkspace workspace,
        string cardName,
        int quantity,
        string? category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category);
        if (card is null)
        {
            warnings.Add($"Preview could not set quantity for missing card '{cardName}'.");
            return;
        }

        if (quantity <= 0)
        {
            workspace.Cards.Remove(card);
            return;
        }

        card.Quantity = quantity;
    }

    /// <summary>
    /// Moves a card to another primary category in the preview workspace.
    /// </summary>
    private static void MoveCard(
        DeckWorkspace workspace,
        string cardName,
        string toCategory,
        string? fromCategory,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, fromCategory);
        if (card is null)
        {
            warnings.Add($"Preview could not move missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(toCategory);
        EnsureCategory(workspace, normalizedCategory);
        card.PrimaryCategory = normalizedCategory;
        AddCategoryName(card, normalizedCategory);
    }

    /// <summary>
    /// Adds a secondary category to a preview card.
    /// </summary>
    private static void AddCardCategory(
        DeckWorkspace workspace,
        string cardName,
        string category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is null)
        {
            warnings.Add($"Preview could not add a category to missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        AddCategoryName(card, normalizedCategory);
    }

    /// <summary>
    /// Removes a secondary category from a preview card.
    /// </summary>
    private static void RemoveCardCategory(
        DeckWorkspace workspace,
        string cardName,
        string category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is null)
        {
            warnings.Add($"Preview could not remove a category from missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(category);
        card.Categories.RemoveAll(value => value.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase));
        if (card.PrimaryCategory.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase))
        {
            card.PrimaryCategory = card.Categories.FirstOrDefault() ?? DeckDefaults.Mainboard;
            EnsureCategory(workspace, card.PrimaryCategory);
        }
    }

    /// <summary>
    /// Changes a preview card's primary category.
    /// </summary>
    private static void SetPrimaryCardCategory(
        DeckWorkspace workspace,
        string cardName,
        string category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is null)
        {
            warnings.Add($"Preview could not set a primary category for missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        card.PrimaryCategory = normalizedCategory;
        AddCategoryName(card, normalizedCategory);
    }

    /// <summary>
    /// Renames a category and updates card category references in the preview workspace.
    /// </summary>
    private static void RenameCategory(
        DeckWorkspace workspace,
        string fromCategory,
        string toCategory,
        List<string> warnings)
    {
        DeckCategory? category = workspace.Categories.FirstOrDefault(value =>
            value.Name.Equals(fromCategory, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            warnings.Add($"Preview could not rename missing category '{fromCategory}'.");
            return;
        }

        string normalizedNewName = NormalizeCategoryName(toCategory);
        string previousName = category.Name;
        category.Name = normalizedNewName;
        foreach (DeckCard card in workspace.Cards)
        {
            for (int index = 0; index < card.Categories.Count; index++)
            {
                if (card.Categories[index].Equals(previousName, StringComparison.OrdinalIgnoreCase))
                {
                    card.Categories[index] = normalizedNewName;
                }
            }

            if (card.PrimaryCategory.Equals(previousName, StringComparison.OrdinalIgnoreCase))
            {
                card.PrimaryCategory = normalizedNewName;
            }
        }
    }

    /// <summary>
    /// Deletes a category and moves primary cards to a replacement category in the preview workspace.
    /// </summary>
    private static void DeleteCategory(
        DeckWorkspace workspace,
        string categoryName,
        string replacementCategory)
    {
        string replacement = NormalizeCategoryName(replacementCategory);
        EnsureCategory(workspace, replacement);
        workspace.Categories.RemoveAll(category =>
            category.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

        foreach (DeckCard card in workspace.Cards)
        {
            bool wasPrimary = card.PrimaryCategory.Equals(categoryName, StringComparison.OrdinalIgnoreCase);
            card.Categories.RemoveAll(value => value.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
            if (wasPrimary)
            {
                card.PrimaryCategory = replacement;
                AddCategoryName(card, replacement);
            }
        }
    }

    /// <summary>
    /// Finds a card by name and optional primary category.
    /// </summary>
    private static DeckCard? FindCard(DeckWorkspace workspace, string cardName, string? category)
    {
        foreach (DeckCard card in workspace.Cards)
        {
            if (!card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (category is null || card.PrimaryCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>
    /// Ensures a category row exists in the preview workspace.
    /// </summary>
    private static DeckCategory EnsureCategory(DeckWorkspace workspace, string category)
    {
        DeckCategory? existing = workspace.Categories.FirstOrDefault(value =>
            value.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        DeckCategory created = new()
        {
            Name = category,
            IncludedInDeck = !category.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase)
                && !category.Equals(DeckDefaults.Sideboard, StringComparison.OrdinalIgnoreCase),
            IncludedInPrice = true,
        };

        workspace.Categories.Add(created);
        return created;
    }

    /// <summary>
    /// Adds a category name to a card if it is not already present.
    /// </summary>
    private static void AddCategoryName(DeckCard card, string category)
    {
        if (!card.Categories.Any(value => value.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            card.Categories.Add(category);
        }
    }

    /// <summary>
    /// Normalizes empty category input to the mainboard.
    /// </summary>
    private static string NormalizeCategoryName(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? DeckDefaults.Mainboard : category.Trim();
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
    /// Copies catalog card facts into a preview card snapshot.
    /// </summary>
    private static void ApplyCardSnapshot(DeckCard card, CardInfo cardInfo)
    {
        card.Snapshot = new CardSnapshot
        {
            ManaCost = cardInfo.ManaCost,
            TypeLine = cardInfo.TypeLine,
            ManaValue = cardInfo.ManaValue,
            OracleText = cardInfo.OracleText,
            ColorIdentity = cardInfo.ColorIdentity.ToList(),
            Set = cardInfo.Set,
            CollectorNumber = cardInfo.CollectorNumber,
            Rarity = cardInfo.Rarity,
            ReleasedAt = cardInfo.ReleasedAt,
            ScryfallUri = cardInfo.ScryfallUri,
            EdhrecRank = cardInfo.EdhrecRank,
            Keywords = cardInfo.Keywords.ToList(),
            ProducedMana = cardInfo.ProducedMana.ToList(),
            Legalities = new Dictionary<string, string>(cardInfo.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(cardInfo.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(cardInfo.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }
}
