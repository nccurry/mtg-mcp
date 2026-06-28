namespace MtgMcp.Core;

/// <summary>
/// Manages the local card collection and compares it with saved workspaces.
/// </summary>
public sealed class CardCollectionService
{
    /// <summary>
    /// Persists the workstation-local collection.
    /// </summary>
    private readonly ICardCollectionRepository collections;

    /// <summary>
    /// Loads workspaces for ownership diffs.
    /// </summary>
    private readonly IDeckWorkspaceRepository workspaces;

    /// <summary>
    /// Estimates missing-card cost from cached workspace snapshots.
    /// </summary>
    private readonly IPriceSource priceSource;

    /// <summary>
    /// Supplies the reference date used for deterministic price evaluation.
    /// </summary>
    private readonly Func<DateOnly> currentDateProvider;

    /// <summary>
    /// Creates a local collection service.
    /// </summary>
    public CardCollectionService(
        ICardCollectionRepository collections,
        IDeckWorkspaceRepository workspaces,
        IPriceSource priceSource)
        : this(collections, workspaces, priceSource, CurrentUtcDate)
    {
    }

    /// <summary>
    /// Creates a local collection service with a deterministic date provider.
    /// </summary>
    internal CardCollectionService(
        ICardCollectionRepository collections,
        IDeckWorkspaceRepository workspaces,
        IPriceSource priceSource,
        Func<DateOnly> currentDateProvider)
    {
        this.collections = collections;
        this.workspaces = workspaces;
        this.priceSource = priceSource;
        this.currentDateProvider = currentDateProvider;
    }

    /// <summary>
    /// Loads the local collection, returning an empty snapshot when none has been saved.
    /// </summary>
    public async Task<CardCollectionSnapshot> GetCollectionAsync(CancellationToken cancellationToken)
    {
        CardCollectionDocument collection = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        return CreateSnapshot(collection);
    }

    /// <summary>
    /// Replaces or merges local collection entries from structured rows and optional decklist text.
    /// </summary>
    public async Task<CardCollectionSetResult> SetCollectionAsync(
        IReadOnlyList<CardCollectionEntry>? entries,
        string? decklist,
        bool replace,
        CancellationToken cancellationToken)
    {
        return await SetCollectionAsync(
            entries,
            decklist,
            workspaceId: null,
            replace,
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces or merges local collection entries from rows, decklist text, and an optional workspace.
    /// </summary>
    public async Task<CardCollectionSetResult> SetCollectionAsync(
        IReadOnlyList<CardCollectionEntry>? entries,
        string? decklist,
        string? workspaceId,
        bool replace,
        CancellationToken cancellationToken)
    {
        List<string> warnings = [];
        Dictionary<string, CardCollectionEntry> incoming = BuildIncomingEntries(entries, decklist, warnings);
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            AddWorkspaceEntries(incoming, workspace);
        }

        if (incoming.Count == 0)
        {
            throw new InvalidOperationException(
                "Collection update must include at least one entry or parsed decklist card.");
        }

        CardCollectionDocument collection = replace
            ? new CardCollectionDocument()
            : await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, CardCollectionEntry> merged = replace
            ? new Dictionary<string, CardCollectionEntry>(StringComparer.OrdinalIgnoreCase)
            : BuildEntryMap(collection.Cards);

        foreach (CardCollectionEntry entry in incoming.Values)
        {
            AddEntry(merged, entry.CardName, entry.Quantity);
        }

        collection.Id = CardCollectionIds.Default;
        collection.SchemaVersion = 1;
        collection.UpdatedAt = DateTimeOffset.UtcNow;
        collection.Cards = SortEntries(merged.Values);
        CardCollectionDocument saved = await collections.SaveAsync(collection, cancellationToken).ConfigureAwait(false);

        return new CardCollectionSetResult
        {
            Mode = replace ? "replace" : "merge",
            InputQuantity = SumQuantities(incoming.Values),
            Warnings = warnings,
            Collection = CreateSnapshot(saved)
        };
    }

    /// <summary>
    /// Compares the local collection with a workspace's included cards.
    /// </summary>
    public async Task<CollectionWorkspaceDiffResult> DiffWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        CardCollectionDocument collection = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, CardCollectionEntry> owned = BuildEntryMap(collection.Cards);
        Dictionary<string, NeededWorkspaceCard> needed = BuildNeededWorkspaceCards(workspace);

        CollectionWorkspaceDiffResult result = new()
        {
            CollectionId = collection.Id,
            WorkspaceId = workspace.Id,
            WorkspaceName = workspace.Name,
            TotalNeededQuantity = SumNeededQuantities(needed.Values),
            UniqueNeededCards = needed.Count
        };

        foreach (NeededWorkspaceCard neededCard in needed.Values)
        {
            int ownedQuantity = owned.TryGetValue(neededCard.CardName, out CardCollectionEntry? entry)
                ? Math.Max(0, entry.Quantity)
                : 0;
            int ownedForWorkspace = Math.Min(ownedQuantity, neededCard.Quantity);
            int missingQuantity = Math.Max(0, neededCard.Quantity - ownedQuantity);
            CollectionWorkspaceDiffCard row = new()
            {
                CardName = neededCard.CardName,
                NeededQuantity = neededCard.Quantity,
                OwnedQuantity = ownedQuantity,
                OwnedForWorkspaceQuantity = ownedForWorkspace,
                MissingQuantity = missingQuantity
            };

            if (missingQuantity > 0)
            {
                AddMissingPrice(row, neededCard.Card, missingQuantity, result);
                result.MissingCards.Add(row);
            }

            result.Cards.Add(row);
            result.TotalOwnedForWorkspaceQuantity += ownedForWorkspace;
            result.TotalMissingQuantity += missingQuantity;
        }

        result.UniqueMissingCards = result.MissingCards.Count;
        result.FullyOwned = result.TotalMissingQuantity == 0;
        SortDiffRows(result.Cards);
        SortDiffRows(result.MissingCards);
        return result;
    }

    /// <summary>
    /// Builds the default empty collection when no collection has been saved.
    /// </summary>
    private async Task<CardCollectionDocument> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        CardCollectionDocument? collection = await collections.GetAsync(cancellationToken).ConfigureAwait(false);
        return collection ?? new CardCollectionDocument();
    }

    /// <summary>
    /// Loads a workspace or reports that the id was not found.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(string workspaceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException("workspaceId is required.");
        }

        DeckWorkspace? workspace = await workspaces
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    /// <summary>
    /// Builds normalized input rows from structured entries and optional decklist text.
    /// </summary>
    private static Dictionary<string, CardCollectionEntry> BuildIncomingEntries(
        IReadOnlyList<CardCollectionEntry>? entries,
        string? decklist,
        List<string> warnings)
    {
        Dictionary<string, CardCollectionEntry> incoming = new(StringComparer.OrdinalIgnoreCase);
        if (entries is not null)
        {
            foreach (CardCollectionEntry entry in entries)
            {
                AddEntry(incoming, entry.CardName, entry.Quantity);
            }
        }

        if (!string.IsNullOrWhiteSpace(decklist))
        {
            ParsedDecklist parsed = DeckParser.Parse(decklist);
            warnings.AddRange(parsed.Warnings);
            foreach (ParsedDecklistLine card in parsed.Cards)
            {
                AddEntry(incoming, card.Name, card.Quantity);
            }
        }

        return incoming;
    }

    /// <summary>
    /// Adds included workspace cards to a collection-entry map.
    /// </summary>
    private static void AddWorkspaceEntries(
        Dictionary<string, CardCollectionEntry> entries,
        DeckWorkspace workspace)
    {
        foreach (DeckCard card in DeckCategoryInclusion.IncludedCards(workspace))
        {
            if (card.Quantity <= 0 || string.IsNullOrWhiteSpace(card.Name))
            {
                continue;
            }

            AddEntry(entries, card.Name, card.Quantity);
        }
    }

    /// <summary>
    /// Builds a normalized owned-card map.
    /// </summary>
    private static Dictionary<string, CardCollectionEntry> BuildEntryMap(IEnumerable<CardCollectionEntry> entries)
    {
        Dictionary<string, CardCollectionEntry> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardCollectionEntry entry in entries)
        {
            if (entry.Quantity <= 0 || string.IsNullOrWhiteSpace(entry.CardName))
            {
                continue;
            }

            AddEntry(map, entry.CardName, entry.Quantity);
        }

        return map;
    }

    /// <summary>
    /// Adds one normalized card quantity to the target map.
    /// </summary>
    private static void AddEntry(Dictionary<string, CardCollectionEntry> entries, string cardName, int quantity)
    {
        string normalizedName = NormalizeCardName(cardName);
        if (quantity < 1)
        {
            throw new InvalidOperationException($"Collection quantity for '{normalizedName}' must be at least 1.");
        }

        if (entries.TryGetValue(normalizedName, out CardCollectionEntry? existing))
        {
            existing.Quantity += quantity;
            return;
        }

        entries[normalizedName] = new CardCollectionEntry
        {
            CardName = normalizedName,
            Quantity = quantity
        };
    }

    /// <summary>
    /// Builds included workspace quantities by card name.
    /// </summary>
    private static Dictionary<string, NeededWorkspaceCard> BuildNeededWorkspaceCards(DeckWorkspace workspace)
    {
        Dictionary<string, NeededWorkspaceCard> needed = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in DeckCategoryInclusion.IncludedCards(workspace))
        {
            int quantity = Math.Max(0, card.Quantity);
            if (quantity == 0 || string.IsNullOrWhiteSpace(card.Name))
            {
                continue;
            }

            string cardName = card.Name.Trim();
            if (needed.TryGetValue(cardName, out NeededWorkspaceCard? existing))
            {
                existing.Quantity += quantity;
                continue;
            }

            needed[cardName] = new NeededWorkspaceCard(cardName, quantity, card);
        }

        return needed;
    }

    /// <summary>
    /// Adds known replacement cost for a missing row when cached prices are available.
    /// </summary>
    private void AddMissingPrice(
        CollectionWorkspaceDiffCard row,
        DeckCard card,
        int missingQuantity,
        CollectionWorkspaceDiffResult result)
    {
        CardPriceEvaluation price = priceSource.Evaluate(
            DeckServiceHelpers.GetSnapshot(card),
            currentDateProvider());
        if (!price.PriceKnown || !price.Price.HasValue)
        {
            result.MissingPriceCards.Add(row.CardName);
            return;
        }

        decimal missingUsd = price.Price.Value * missingQuantity;
        row.UnitPriceUsd = price.Price.Value;
        row.MissingUsd = missingUsd;
        row.PriceSource = price.PriceSource;
        result.KnownMissingUsd += missingUsd;
    }

    /// <summary>
    /// Builds a stable snapshot from a collection document.
    /// </summary>
    private static CardCollectionSnapshot CreateSnapshot(CardCollectionDocument collection)
    {
        List<CardCollectionEntry> cards = SortEntries(BuildEntryMap(collection.Cards).Values);
        return new CardCollectionSnapshot
        {
            CollectionId = collection.Id,
            UpdatedAt = collection.UpdatedAt,
            TotalQuantity = SumQuantities(cards),
            UniqueCards = cards.Count,
            Cards = cards
        };
    }

    /// <summary>
    /// Sorts card entries by display name.
    /// </summary>
    private static List<CardCollectionEntry> SortEntries(IEnumerable<CardCollectionEntry> entries)
    {
        List<CardCollectionEntry> sorted = [];
        foreach (CardCollectionEntry entry in entries)
        {
            sorted.Add(new CardCollectionEntry
            {
                CardName = entry.CardName,
                Quantity = entry.Quantity
            });
        }

        sorted.Sort(static (left, right) =>
            string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase));
        return sorted;
    }

    /// <summary>
    /// Sorts ownership diff rows with missing cards first, then by card name.
    /// </summary>
    private static void SortDiffRows(List<CollectionWorkspaceDiffCard> rows)
    {
        rows.Sort(static (left, right) =>
        {
            int missingComparison = right.MissingQuantity.CompareTo(left.MissingQuantity);
            return missingComparison != 0
                ? missingComparison
                : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Counts owned quantities.
    /// </summary>
    private static int SumQuantities(IEnumerable<CardCollectionEntry> entries)
    {
        int total = 0;
        foreach (CardCollectionEntry entry in entries)
        {
            total += Math.Max(0, entry.Quantity);
        }

        return total;
    }

    /// <summary>
    /// Counts workspace-needed quantities.
    /// </summary>
    private static int SumNeededQuantities(IEnumerable<NeededWorkspaceCard> entries)
    {
        int total = 0;
        foreach (NeededWorkspaceCard entry in entries)
        {
            total += Math.Max(0, entry.Quantity);
        }

        return total;
    }

    /// <summary>
    /// Normalizes display names while preserving card spelling.
    /// </summary>
    private static string NormalizeCardName(string cardName)
    {
        return !string.IsNullOrWhiteSpace(cardName)
            ? cardName.Trim()
            : throw new InvalidOperationException("Collection cardName is required.");
    }

    /// <summary>
    /// Returns the current UTC date for price evaluation.
    /// </summary>
    private static DateOnly CurrentUtcDate()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Tracks an included workspace card and its aggregated needed quantity.
    /// </summary>
    private sealed class NeededWorkspaceCard
    {
        /// <summary>
        /// Creates an included workspace-card accumulator.
        /// </summary>
        public NeededWorkspaceCard(string cardName, int quantity, DeckCard card)
        {
            CardName = cardName;
            Quantity = quantity;
            Card = card;
        }

        /// <summary>
        /// Gets the card display name.
        /// </summary>
        public string CardName { get; }

        /// <summary>
        /// Gets or sets the included quantity needed by the workspace.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Gets a representative workspace card carrying cached metadata.
        /// </summary>
        public DeckCard Card { get; }
    }
}
