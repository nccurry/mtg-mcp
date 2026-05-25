namespace MtgMcp.Core;

/// <summary>
/// Copies provider-neutral workspaces into Archidekt decks.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Creates an empty Archidekt deck and stores the writeback workspace locally.
    /// </summary>
    public async Task<DeckWorkspace> CreateArchidektDeckAsync(
        string name,
        string format,
        string? description,
        string visibility,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await RequireArchidektGateway()
            .CreateDeckAsync(
                new ArchidektDeckCreateRequest
                {
                    Name = name,
                    Format = format,
                    Description = description,
                    Visibility = visibility,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Previews or applies a full workspace copy into a new or existing Archidekt deck.
    /// </summary>
    public async Task<ArchidektCopyResult> CopyWorkspaceToArchidektAsync(
        string workspaceId,
        bool dryRun,
        bool createNew,
        string? destinationDeckIdOrUrl,
        string? name,
        string? format,
        string? description,
        string visibility,
        bool allowNonEmptyDestination,
        bool replaceExistingDestination,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace source = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        ArchidektCopyResult result = CreateCopyResult(
            source,
            dryRun,
            createNew,
            destinationDeckIdOrUrl,
            name);

        ValidateDestinationChoice(
            createNew,
            destinationDeckIdOrUrl,
            allowNonEmptyDestination,
            replaceExistingDestination);

        DeckWorkspace? destination = null;
        if (!string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            destination = await RequireArchidektGateway()
                .ImportDeckAsync(destinationDeckIdOrUrl, writeBack: !dryRun, cancellationToken)
                .ConfigureAwait(false);
            result.DestinationArchidektDeckId = destination.ArchidektDeckId;
            result.DestinationName = destination.Name;
            AddDestinationWarnings(source, destination, result);
            if (destination.Cards.Count > 0 && !allowNonEmptyDestination && !replaceExistingDestination)
            {
                result.Warnings.Add(
                    "Destination Archidekt deck is not empty; set allowNonEmptyDestination=true to append cards "
                        + "or replaceExistingDestination=true to replace its cards."
                );
            }

            if (destination.Cards.Count > 0 && replaceExistingDestination)
            {
                result.Warnings.Add("Destination Archidekt deck cards will be replaced.");
            }
        }

        if (dryRun)
        {
            return result;
        }

        if (destination?.Cards.Count > 0 && !allowNonEmptyDestination && !replaceExistingDestination)
        {
            throw new InvalidOperationException(
                "Destination Archidekt deck is not empty. "
                    + "Set allowNonEmptyDestination=true to append cards intentionally "
                    + "or replaceExistingDestination=true to replace its cards."
            );
        }

        destination = createNew
            ? await CreateArchidektDeckAsync(
                    name ?? source.Name,
                    format ?? source.Format,
                    BuildMigrationDescription(description ?? source.Description, source),
                    visibility,
                    cancellationToken)
                .ConfigureAwait(false)
            : await OpenArchidektDeckAsync(
                    destinationDeckIdOrUrl
                        ?? throw new InvalidOperationException("Destination Archidekt deck id or URL is required."),
                    writeBack: true,
                    cancellationToken)
                .ConfigureAwait(false);

        destination.Name = string.IsNullOrWhiteSpace(name) ? destination.Name : name.Trim();
        destination.Format = string.IsNullOrWhiteSpace(format) ? destination.Format : format.Trim();
        destination.Description = BuildMigrationDescription(
            description ?? destination.Description,
            source);
        await RequireArchidektGateway()
            .PersistMetadataAsync(destination, cancellationToken)
            .ConfigureAwait(false);
        await Repository.SaveAsync(destination, cancellationToken).ConfigureAwait(false);

        await CopyCategoriesToArchidektAsync(source, destination, cancellationToken)
            .ConfigureAwait(false);

        List<DeckCard> copiedCards = source.Cards.Select(CloneForArchidektCopy).ToList();
        if (replaceExistingDestination)
        {
            CopyKnownArchidektCardIds(destination.Cards, copiedCards);
        }

        if (replaceExistingDestination && destination.Cards.Count > 0)
        {
            List<DeckCard> removedCards = destination.Cards.ToList();
            await PersistCardsAsync(destination, [], removedCards, cancellationToken)
                .ConfigureAwait(false);
            destination.Cards.Clear();
        }

        destination.Cards.AddRange(copiedCards);
        await PersistCardsAsync(destination, copiedCards, [], cancellationToken)
            .ConfigureAwait(false);

        result.DestinationWorkspaceId = destination.Id;
        result.DestinationArchidektDeckId = destination.ArchidektDeckId;
        result.DestinationName = destination.Name;
        return result;
    }

    /// <summary>
    /// Creates the shared report body for dry-run and apply responses.
    /// </summary>
    private static ArchidektCopyResult CreateCopyResult(
        DeckWorkspace source,
        bool dryRun,
        bool createNew,
        string? destinationDeckIdOrUrl,
        string? name
    )
    {
        Dictionary<string, DeckCategory> categories = source.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<string> warnings = source.Warnings.ToList();
        int missingScryfallIds = source.Cards.Count(card => string.IsNullOrWhiteSpace(card.ScryfallId));
        if (source.Cards.Count == 0)
        {
            warnings.Add("Source workspace has no cards to copy.");
        }

        if (missingScryfallIds > 0)
        {
            warnings.Add($"{missingScryfallIds} card(s) have no Scryfall id; Archidekt print matching may fall back to name.");
        }

        return new ArchidektCopyResult
        {
            DryRun = dryRun,
            SourceWorkspaceId = source.Id,
            DestinationArchidektDeckId = destinationDeckIdOrUrl,
            CreatedNewDeck = createNew,
            DestinationName = name ?? source.Name,
            TotalCards = source.Cards.Sum(card => Math.Max(0, card.Quantity)),
            IncludedCards = source.Cards
                .Where(card => IsIncludedByPrimaryCategory(categories, card))
                .Sum(card => Math.Max(0, card.Quantity)),
            Categories = source.Categories
                .Select(category => category.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Commanders = source.Cards
                .Where(IsCommanderCard)
                .Select(card => card.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Rejects ambiguous destination choices before any write.
    /// </summary>
    private static void ValidateDestinationChoice(
        bool createNew,
        string? destinationDeckIdOrUrl,
        bool allowNonEmptyDestination,
        bool replaceExistingDestination
    )
    {
        if (allowNonEmptyDestination && replaceExistingDestination)
        {
            throw new InvalidOperationException(
                "Choose either allowNonEmptyDestination=true to append cards "
                    + "or replaceExistingDestination=true to replace cards, not both."
            );
        }

        if (createNew && !string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            throw new InvalidOperationException(
                "Choose either createNew=true or destinationDeckIdOrUrl, not both."
            );
        }

        if (!createNew && string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            throw new InvalidOperationException(
                "Copying into an existing Archidekt deck requires destinationDeckIdOrUrl."
            );
        }
    }

    /// <summary>
    /// Checks whether a card's primary category contributes to the active deck.
    /// </summary>
    private static bool IsIncludedByPrimaryCategory(
        IReadOnlyDictionary<string, DeckCategory> categories,
        DeckCard card
    )
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return !categories.TryGetValue(primaryCategory, out DeckCategory? category)
            || category.IncludedInDeck;
    }

    /// <summary>
    /// Adds warnings that depend on reading an existing destination deck.
    /// </summary>
    private static void AddDestinationWarnings(
        DeckWorkspace source,
        DeckWorkspace destination,
        ArchidektCopyResult result
    )
    {
        foreach (DeckSourceReference sourceReference in source.SourceReferences)
        {
            if (
                !string.IsNullOrWhiteSpace(destination.Description)
                && destination.Description.Contains(
                    $"{sourceReference.Provider}:{sourceReference.ExternalId}",
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                result.Warnings.Add(
                    $"Destination description already references {sourceReference.Provider}:{sourceReference.ExternalId}."
                );
            }
        }
    }

    /// <summary>
    /// Creates or updates Archidekt categories before card upload.
    /// </summary>
    private async Task CopyCategoriesToArchidektAsync(
        DeckWorkspace source,
        DeckWorkspace destination,
        CancellationToken cancellationToken
    )
    {
        foreach (DeckCategory sourceCategory in source.Categories)
        {
            DeckCategory? destinationCategory = destination.Categories.FirstOrDefault(category =>
                category.Name.Equals(sourceCategory.Name, StringComparison.OrdinalIgnoreCase)
            );
            if (destinationCategory is null)
            {
                destinationCategory = new DeckCategory
                {
                    Name = sourceCategory.Name,
                    IncludedInDeck = sourceCategory.IncludedInDeck,
                    IncludedInPrice = sourceCategory.IncludedInPrice,
                };
                destination.Categories.Add(destinationCategory);
            }
            else
            {
                destinationCategory.IncludedInDeck = sourceCategory.IncludedInDeck;
                destinationCategory.IncludedInPrice = sourceCategory.IncludedInPrice;
            }

            await PersistCategoryAsync(destination, destinationCategory, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Copies a workspace card while clearing destination-specific Archidekt relation state.
    /// </summary>
    private static DeckCard CloneForArchidektCopy(DeckCard source)
    {
        DeckCard copy = new()
        {
            Name = source.Name,
            Quantity = source.Quantity,
            PrimaryCategory = DeckCategoryOrdering.PrimaryCategory(source),
            Categories = DeckCategoryOrdering.OrderedDistinct(
                DeckCategoryOrdering.PrimaryCategory(source),
                source.Categories),
            ScryfallId = source.ScryfallId,
            ScryfallOracleId = source.ScryfallOracleId,
            ArchidektCardId = source.ArchidektCardId,
            Modifier = source.Modifier,
            Companion = source.Companion,
            FlippedDefault = source.FlippedDefault,
            Snapshot = CloneSnapshot(source.Snapshot),
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase),
        };

        copy.ArchidektDeckRelationId = null;
        return copy;
    }

    /// <summary>
    /// Reuses known Archidekt print ids before replacing destination card rows.
    /// </summary>
    private static void CopyKnownArchidektCardIds(
        IReadOnlyList<DeckCard> destinationCards,
        IReadOnlyList<DeckCard> copiedCards
    )
    {
        Dictionary<string, Queue<DeckCard>> byPrint = BuildDestinationCardIdLookup(
            destinationCards,
            includePrint: true);
        Dictionary<string, Queue<DeckCard>> byNameAndCategory = BuildDestinationCardIdLookup(
            destinationCards,
            includePrint: false);

        foreach (DeckCard copiedCard in copiedCards)
        {
            if (!string.IsNullOrWhiteSpace(copiedCard.ArchidektCardId))
            {
                continue;
            }

            DeckCard? matched = DequeueMatch(byPrint, BuildCardIdReuseKey(copiedCard, includePrint: true))
                ?? DequeueMatch(byNameAndCategory, BuildCardIdReuseKey(copiedCard, includePrint: false));
            if (!string.IsNullOrWhiteSpace(matched?.ArchidektCardId))
            {
                copiedCard.ArchidektCardId = matched.ArchidektCardId;
            }
        }
    }

    /// <summary>
    /// Groups destination cards by identity fields useful for reusing Archidekt print ids.
    /// </summary>
    private static Dictionary<string, Queue<DeckCard>> BuildDestinationCardIdLookup(
        IEnumerable<DeckCard> destinationCards,
        bool includePrint
    )
    {
        Dictionary<string, Queue<DeckCard>> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in destinationCards.Where(card => !string.IsNullOrWhiteSpace(card.ArchidektCardId)))
        {
            string? key = BuildCardIdReuseKey(card, includePrint);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!lookup.TryGetValue(key, out Queue<DeckCard>? queue))
            {
                queue = new Queue<DeckCard>();
                lookup[key] = queue;
            }

            queue.Enqueue(card);
        }

        return lookup;
    }

    /// <summary>
    /// Creates a stable card-id reuse key from source-visible card identity.
    /// </summary>
    private static string? BuildCardIdReuseKey(DeckCard card, bool includePrint)
    {
        string primary = DeckCategoryOrdering.PrimaryCategory(card);
        if (!includePrint)
        {
            return $"{card.Name}|{primary}";
        }

        string? print = string.IsNullOrWhiteSpace(card.ScryfallId)
            ? null
            : card.ScryfallId;
        if (string.IsNullOrWhiteSpace(print)
            && !string.IsNullOrWhiteSpace(card.Snapshot.Set)
            && !string.IsNullOrWhiteSpace(card.Snapshot.CollectorNumber))
        {
            print = $"{card.Snapshot.Set}|{card.Snapshot.CollectorNumber}";
        }

        if (string.IsNullOrWhiteSpace(print))
        {
            return null;
        }

        return $"{card.Name}|{primary}|{print}";
    }

    /// <summary>
    /// Removes one matched destination card from a reuse lookup.
    /// </summary>
    private static DeckCard? DequeueMatch(
        Dictionary<string, Queue<DeckCard>> lookup,
        string? key
    )
    {
        if (string.IsNullOrWhiteSpace(key)
            || !lookup.TryGetValue(key, out Queue<DeckCard>? matches)
            || matches.Count == 0)
        {
            return null;
        }

        return matches.Dequeue();
    }

    /// <summary>
    /// Copies cached card facts without sharing mutable collection instances.
    /// </summary>
    private static CardSnapshot CloneSnapshot(CardSnapshot snapshot)
    {
        return new CardSnapshot
        {
            ManaCost = snapshot.ManaCost,
            TypeLine = snapshot.TypeLine,
            ManaValue = snapshot.ManaValue,
            OracleText = snapshot.OracleText,
            ColorIdentity = snapshot.ColorIdentity.ToList(),
            Set = snapshot.Set,
            CollectorNumber = snapshot.CollectorNumber,
            Rarity = snapshot.Rarity,
            ReleasedAt = snapshot.ReleasedAt,
            ScryfallUri = snapshot.ScryfallUri,
            EdhrecRank = snapshot.EdhrecRank,
            Keywords = snapshot.Keywords.ToList(),
            ProducedMana = snapshot.ProducedMana.ToList(),
            Legalities = new Dictionary<string, string>(snapshot.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(snapshot.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(snapshot.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Appends a small provenance marker to the destination description for repeat-copy warnings.
    /// </summary>
    private static string? BuildMigrationDescription(string? description, DeckWorkspace source)
    {
        if (source.SourceReferences.Count == 0)
        {
            return description;
        }

        string sourceText = string.Join(
            ", ",
            source.SourceReferences.Select(reference => $"{reference.Provider}:{reference.ExternalId}")
        );
        string marker = $"MTG MCP Migration Source: {sourceText}; Workspace: {source.Id}";
        if (!string.IsNullOrWhiteSpace(description)
            && description.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            return description;
        }

        return string.IsNullOrWhiteSpace(description)
            ? marker
            : $"{description.Trim()}\n\n{marker}";
    }
}
