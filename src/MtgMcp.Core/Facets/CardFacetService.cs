namespace MtgMcp.Core;

/// <summary>
/// Extracts factual card facets and evaluates explicit predicates over them.
/// </summary>
public sealed class CardFacetService
{
    /// <summary>
    /// Stores workspace data.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Creates a card facet service.
    /// </summary>
    public CardFacetService(IDeckWorkspaceRepository repository)
    {
        this.repository = repository;
    }

    /// <summary>
    /// Gets normalized facets for one workspace card.
    /// </summary>
    public async Task<CardFacetSnapshot> GetCardFacetsAsync(
        string workspaceId,
        string cardName,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckCard card = FindCard(workspace, cardName);
        return CreateSnapshot(workspace, card, DeckCategoryInclusion.IsIncludedInDeck(workspace, card));
    }

    /// <summary>
    /// Gets normalized facets for cards in a workspace.
    /// </summary>
    public async Task<DeckFacetSnapshot> GetDeckFacetsAsync(
        string workspaceId,
        bool includedOnly,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        List<CardFacetSnapshot> cards = [];

        foreach (DeckCard card in workspace.Cards)
        {
            bool included = DeckCategoryInclusion.IsIncludedInDeck(categoryMap, card);
            if (includedOnly && !included)
            {
                continue;
            }

            cards.Add(CreateSnapshot(workspace, card, included));
        }

        return new DeckFacetSnapshot
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Format = workspace.Format,
            IncludedOnly = includedOnly,
            Cards = cards
        };
    }

    /// <summary>
    /// Explains whether one card matches a caller-supplied facet predicate.
    /// </summary>
    public async Task<CardFacetMatchResult> ExplainCardMatchAsync(
        string workspaceId,
        string cardName,
        string predicateJson,
        CancellationToken cancellationToken)
    {
        CardFacetSnapshot facets = await GetCardFacetsAsync(workspaceId, cardName, cancellationToken)
            .ConfigureAwait(false);
        return FacetPredicateEvaluator.Evaluate(facets, predicateJson);
    }

    /// <summary>
    /// Counts deck cards that match a caller-supplied facet predicate.
    /// </summary>
    public async Task<DeckFacetCountResult> CountDeckCardsMatchingAsync(
        string workspaceId,
        string predicateJson,
        bool includedOnly,
        CancellationToken cancellationToken)
    {
        DeckFacetSnapshot deck = await GetDeckFacetsAsync(workspaceId, includedOnly, cancellationToken)
            .ConfigureAwait(false);
        DeckFacetCountResult result = new()
        {
            WorkspaceId = deck.WorkspaceId,
            IncludedOnly = includedOnly
        };

        foreach (CardFacetSnapshot card in deck.Cards)
        {
            CardFacetMatchResult match = FacetPredicateEvaluator.Evaluate(card, predicateJson);
            result.PredicateJson = match.PredicateJson;
            if (!match.Matched)
            {
                continue;
            }

            result.Matches.Add(new DeckFacetCountCard
            {
                CardName = card.CardName,
                Quantity = Math.Max(0, card.Quantity),
                IncludedInDeck = card.IncludedInDeck,
                Evidence = match.Evidence.Where(row => row.Matched).ToList()
            });
        }

        result.TotalQuantity = result.Matches.Sum(card => card.Quantity);
        result.DistinctCards = result.Matches.Count;
        if (string.IsNullOrWhiteSpace(result.PredicateJson))
        {
            result.PredicateJson = predicateJson;
        }

        return result;
    }

    /// <summary>
    /// Saves local user and Tagger annotations for one workspace card.
    /// </summary>
    public async Task<CardFacetAnnotationResult> SetCardAnnotationsAsync(
        string workspaceId,
        string cardName,
        IReadOnlyList<string>? userTags,
        IReadOnlyList<string>? userCategories,
        IReadOnlyList<string>? taggerOracleTags,
        IReadOnlyList<string>? taggerArtTags,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckCard card = FindCard(workspace, cardName);

        SetAnnotation(card, CardFacetNames.UserTags, userTags);
        SetAnnotation(card, CardFacetNames.UserCategories, userCategories);
        SetAnnotation(card, CardFacetNames.TaggerOracleTags, taggerOracleTags);
        SetAnnotation(card, CardFacetNames.TaggerArtTags, taggerArtTags);
        workspace.UpdatedAt = DateTimeOffset.UtcNow;

        DeckWorkspace saved = await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        DeckCard savedCard = FindCard(saved, cardName);
        CardFacetSnapshot facets = CreateSnapshot(saved, savedCard, DeckCategoryInclusion.IsIncludedInDeck(saved, savedCard));

        return new CardFacetAnnotationResult
        {
            WorkspaceId = saved.Id,
            CardName = savedCard.Name,
            Facets = facets,
            Notes =
            [
                "Annotations are local mtg-mcp workspace metadata; they do not write back to Archidekt."
            ]
        };
    }

    /// <summary>
    /// Loads a workspace or reports a clear missing-workspace error.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository.GetAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return workspace ?? throw new InvalidOperationException($"Deck workspace '{workspaceId}' was not found.");
    }

    /// <summary>
    /// Finds a card by name in a workspace.
    /// </summary>
    private static DeckCard FindCard(DeckWorkspace workspace, string cardName)
    {
        return workspace.Cards.FirstOrDefault(card => card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Card '{cardName}' was not found in workspace '{workspace.Id}'.");
    }

    /// <summary>
    /// Creates a normalized facet snapshot for one card.
    /// </summary>
    private static CardFacetSnapshot CreateSnapshot(
        DeckWorkspace workspace,
        DeckCard card,
        bool included)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        CardFacetSnapshot result = new()
        {
            WorkspaceId = workspace.Id,
            CardId = card.Id,
            CardName = card.Name,
            Quantity = card.Quantity,
            IncludedInDeck = included,
            ScryfallId = card.ScryfallId,
            ScryfallOracleId = card.ScryfallOracleId
        };

        AddValue(result, CardFacetNames.CardName, CardFacetSourceNames.Workspace, card.Name);
        AddValue(result, CardFacetNames.CardQuantity, CardFacetSourceNames.Workspace, card.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddValue(result, CardFacetNames.CardIncludedInDeck, CardFacetSourceNames.Workspace, included.ToString());
        AddValue(result, CardFacetNames.WorkspacePrimaryCategory, CardFacetSourceNames.Workspace, DeckCategoryOrdering.PrimaryCategory(card));
        AddValues(result, CardFacetNames.WorkspaceCategories, CardFacetSourceNames.Workspace, card.Categories);

        AddValue(result, "scryfall.id", CardFacetSourceNames.Scryfall, card.ScryfallId);
        AddValue(result, "scryfall.oracle_id", CardFacetSourceNames.Scryfall, card.ScryfallOracleId);
        AddValue(result, "scryfall.mana_cost", CardFacetSourceNames.Scryfall, snapshot.ManaCost);
        AddValue(result, "scryfall.mana_value", CardFacetSourceNames.Scryfall, snapshot.ManaValue?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddValue(result, "scryfall.type_line", CardFacetSourceNames.Scryfall, snapshot.TypeLine);
        AddValue(result, "scryfall.oracle_text", CardFacetSourceNames.Scryfall, snapshot.OracleText);
        AddValue(result, "scryfall.set", CardFacetSourceNames.Scryfall, snapshot.Set);
        AddValue(result, "scryfall.collector_number", CardFacetSourceNames.Scryfall, snapshot.CollectorNumber);
        AddValue(result, "scryfall.rarity", CardFacetSourceNames.Scryfall, snapshot.Rarity);
        AddValue(
            result,
            "scryfall.released_at",
            CardFacetSourceNames.Scryfall,
            snapshot.ReleasedAt?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        AddValue(result, "scryfall.uri", CardFacetSourceNames.Scryfall, snapshot.ScryfallUri);
        AddValue(result, "scryfall.edhrec_rank", CardFacetSourceNames.Scryfall, snapshot.EdhrecRank?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddValues(result, "scryfall.color_identity", CardFacetSourceNames.Scryfall, snapshot.ColorIdentity);
        AddValues(result, "scryfall.keywords", CardFacetSourceNames.Scryfall, snapshot.Keywords);
        AddValues(result, "scryfall.produced_mana", CardFacetSourceNames.Scryfall, snapshot.ProducedMana);
        AddDictionaryValues(result, "scryfall.legalities", CardFacetSourceNames.Scryfall, snapshot.Legalities);
        AddDictionaryValues(result, "scryfall.prices", CardFacetSourceNames.Scryfall, snapshot.Prices);
        AddDictionaryValues(result, "scryfall.image_uris", CardFacetSourceNames.Scryfall, snapshot.ImageUris);

        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        AddValue(result, "classifier.primary_role", CardFacetSourceNames.Classifier, role.PrimaryRole);
        AddValues(result, "classifier.tags", CardFacetSourceNames.Classifier, role.Tags);

        AddMetadataFacets(result, card.Metadata);
        AddAnnotatedValues(result, CardFacetNames.UserTags, CardFacetSourceNames.User, card.Metadata);
        AddAnnotatedValues(result, CardFacetNames.UserCategories, CardFacetSourceNames.User, card.Metadata);
        AddAnnotatedValues(result, CardFacetNames.TaggerOracleTags, CardFacetSourceNames.Tagger, card.Metadata);
        AddAnnotatedValues(result, CardFacetNames.TaggerArtTags, CardFacetSourceNames.Tagger, card.Metadata);

        return result;
    }

    /// <summary>
    /// Adds all metadata keys as facets.
    /// </summary>
    private static void AddMetadataFacets(CardFacetSnapshot result, IReadOnlyDictionary<string, string> metadata)
    {
        foreach ((string key, string value) in metadata)
        {
            AddValue(result, $"metadata.{NormalizeFacetSegment(key)}", CardFacetSourceNames.Metadata, value);
        }
    }

    /// <summary>
    /// Adds annotation values from a metadata key if present.
    /// </summary>
    private static void AddAnnotatedValues(
        CardFacetSnapshot result,
        string facetName,
        string source,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue(facetName, out string? value))
        {
            AddValues(result, facetName, source, SplitAnnotationValue(value));
        }
    }

    /// <summary>
    /// Saves or removes one local annotation metadata value.
    /// </summary>
    private static void SetAnnotation(DeckCard card, string key, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        List<string> normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0)
        {
            card.Metadata.Remove(key);
            return;
        }

        card.Metadata[key] = string.Join(", ", normalized);
    }

    /// <summary>
    /// Splits a comma, semicolon, pipe, or newline separated annotation value.
    /// </summary>
    private static List<string> SplitAnnotationValue(string value)
    {
        return value
            .Split([',', ';', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds dictionary entries as both aggregate and key-specific facets.
    /// </summary>
    private static void AddDictionaryValues(
        CardFacetSnapshot result,
        string facetName,
        string source,
        IReadOnlyDictionary<string, string> values)
    {
        foreach ((string key, string value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            AddValue(result, facetName, source, $"{key}:{value}");
            AddValue(result, $"{facetName}.{NormalizeFacetSegment(key)}", source, value);
        }
    }

    /// <summary>
    /// Adds values to one facet.
    /// </summary>
    private static void AddValues(
        CardFacetSnapshot result,
        string name,
        string source,
        IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (string value in values)
        {
            AddValue(result, name, source, value);
        }
    }

    /// <summary>
    /// Adds one value to a facet when it is present.
    /// </summary>
    private static void AddValue(
        CardFacetSnapshot result,
        string name,
        string source,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!result.Facets.TryGetValue(name, out CardFacet? facet))
        {
            facet = new CardFacet
            {
                Name = name,
                Source = source
            };
            result.Facets[name] = facet;
        }

        if (!facet.Values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            facet.Values.Add(value);
        }
    }

    /// <summary>
    /// Converts arbitrary metadata keys into facet path segments.
    /// </summary>
    private static string NormalizeFacetSegment(string value)
    {
        return value
            .Trim()
            .Replace(' ', '_')
            .Replace('-', '_')
            .ToLowerInvariant();
    }
}
