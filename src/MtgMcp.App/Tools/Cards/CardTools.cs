using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes MCP tools for card.
/// </summary>
[McpServerToolType]
public sealed class CardTools
{
    /// <summary>
    /// Largest batch lookup request accepted by the card tool.
    /// </summary>
    private const int MaxBatchLookupLimit = 75;

    /// <summary>
    /// Stores the cards.
    /// </summary>
    private readonly ICardCatalog cards;

    /// <summary>
    /// Creates the MCP card lookup tool group.
    /// </summary>
    public CardTools(ICardCatalog cards)
    {
        this.cards = cards;
    }

    /// <summary>
    /// Searches the cards.
    /// </summary>
    [McpServerTool(
        Name = "card_search",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Search Scryfall cards using normal Scryfall search syntax, with an optional format legality constraint.")]
    public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        string query,
        [Description("Optional Scryfall legality format such as commander, modern, standard, or pauper.")]
        string? format = null,
        int limit = 10,
        CancellationToken cancellationToken = default
    )
    {
        return string.IsNullOrWhiteSpace(format)
            ? cards.SearchCardsAsync(query, limit, cancellationToken)
            : cards.SuggestCardsAsync(query, format, limit, cancellationToken);
    }

    /// <summary>
    /// Returns a card by Scryfall id or fuzzy name.
    /// </summary>
    [McpServerTool(
        Name = "card_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get a single card by Scryfall id or fuzzy card name.")]
    public Task<CardInfo?> GetCardAsync(
        string cardNameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetCardAsync(cardNameOrId, cancellationToken);
    }

    /// <summary>
    /// Returns card details for several fuzzy names in one provider request.
    /// </summary>
    [McpServerTool(
        Name = "card_get_batch",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get several cards by fuzzy card name in one batch lookup.")]
    public async Task<CardBatchLookupResult> GetCardsBatchAsync(
        [Description("Card names to hydrate. Blank names are ignored and duplicate names are collapsed case-insensitively.")]
        IReadOnlyList<string> names,
        [Description("Maximum number of distinct nonblank names to look up. Clamped between 1 and 75.")]
        int limit = 25,
        CancellationToken cancellationToken = default
    )
    {
        int effectiveLimit = Math.Clamp(limit, 1, MaxBatchLookupLimit);
        List<string> requestedNames = NormalizeBatchNames(names, effectiveLimit);
        IReadOnlyDictionary<string, CardInfo> foundCards = await cards
            .GetCardsByNamesAsync(requestedNames, cancellationToken)
            .ConfigureAwait(false);
        List<CardBatchLookupRow> rows = [];
        List<string> missingNames = [];
        foreach (string requestedName in requestedNames)
        {
            if (foundCards.TryGetValue(requestedName, out CardInfo? card))
            {
                rows.Add(new CardBatchLookupRow
                {
                    RequestedName = requestedName,
                    Card = card,
                });
                continue;
            }

            missingNames.Add(requestedName);
        }

        return new CardBatchLookupResult
        {
            RequestedCount = requestedNames.Count,
            ReturnedCount = rows.Count,
            MissingCount = missingNames.Count,
            Limit = effectiveLimit,
            Truncated = CountDistinctNonblankNames(names) > effectiveLimit,
            Cards = rows,
            MissingNames = missingNames,
        };
    }

    /// <summary>
    /// Returns one image URI for a card without fetching the binary image.
    /// </summary>
    [McpServerTool(
        Name = "card_get_image",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get a Scryfall-hosted image URI for a card without downloading the image bytes.")]
    public async Task<CardImageLookupResult> GetCardImageAsync(
        string cardNameOrId,
        [Description("Image kind such as normal, large, small, png, art_crop, or border_crop.")]
        string kind = "normal",
        CancellationToken cancellationToken = default
    )
    {
        CardInfo? card = await cards.GetCardAsync(cardNameOrId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return new CardImageLookupResult
            {
                Status = "not-found",
                RequestedNameOrId = cardNameOrId,
                RequestedKind = kind,
            };
        }

        string requestedKind = string.IsNullOrWhiteSpace(kind) ? "normal" : kind.Trim();
        string? resolvedKind = ResolveImageKind(card.ImageUris, requestedKind);
        if (resolvedKind is null)
        {
            return new CardImageLookupResult
            {
                Status = "no-image",
                RequestedNameOrId = cardNameOrId,
                CardName = card.Name,
                RequestedKind = requestedKind,
                AvailableKinds = SortedKeys(card.ImageUris),
                ScryfallUri = card.ScryfallUri,
            };
        }

        return new CardImageLookupResult
        {
            Status = "ok",
            RequestedNameOrId = cardNameOrId,
            CardName = card.Name,
            RequestedKind = requestedKind,
            ResolvedKind = resolvedKind,
            Uri = card.ImageUris[resolvedKind],
            AvailableKinds = SortedKeys(card.ImageUris),
            ScryfallUri = card.ScryfallUri,
        };
    }

    /// <summary>
    /// Returns official Scryfall rulings for a card.
    /// </summary>
    [McpServerTool(
        Name = "card_get_rulings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get Scryfall rulings for a card.")]
    public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
        string cardNameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetRulingsAsync(cardNameOrId, cancellationToken);
    }

    /// <summary>
    /// Returns known Scryfall prints for a card.
    /// </summary>
    [McpServerTool(
        Name = "card_get_prints",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get known Scryfall prints for a card.")]
    public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
        string cardNameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetPrintsAsync(cardNameOrId, cancellationToken);
    }

    /// <summary>
    /// Normalizes batch lookup names while preserving first-seen order.
    /// </summary>
    private static List<string> NormalizeBatchNames(IReadOnlyList<string> names, int limit)
    {
        List<string> requestedNames = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (requestedNames.Count >= limit)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string trimmed = name.Trim();
            if (seen.Add(trimmed))
            {
                requestedNames.Add(trimmed);
            }
        }

        return requestedNames;
    }

    /// <summary>
    /// Counts distinct nonblank names without applying the batch limit.
    /// </summary>
    private static int CountDistinctNonblankNames(IReadOnlyList<string> names)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                seen.Add(name.Trim());
            }
        }

        return seen.Count;
    }

    /// <summary>
    /// Resolves a requested image kind to an available Scryfall image URI key.
    /// </summary>
    private static string? ResolveImageKind(IReadOnlyDictionary<string, string> imageUris, string kind)
    {
        if (imageUris.ContainsKey(kind))
        {
            return kind;
        }

        if (imageUris.ContainsKey("normal"))
        {
            return "normal";
        }

        if (imageUris.Count == 0)
        {
            return null;
        }

        List<string> keys = SortedKeys(imageUris);
        return keys[0];
    }

    /// <summary>
    /// Sorts dictionary keys for stable output.
    /// </summary>
    private static List<string> SortedKeys(IReadOnlyDictionary<string, string> values)
    {
        List<string> keys = values.Keys.ToList();
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return keys;
    }
}

/// <summary>
/// Describes the result of one batch card lookup request.
/// </summary>
public sealed class CardBatchLookupResult
{
    /// <summary>
    /// Gets or sets the number of distinct names looked up after normalization.
    /// </summary>
    public int RequestedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of cards returned by the catalog.
    /// </summary>
    public int ReturnedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of requested names with no returned card.
    /// </summary>
    public int MissingCount { get; set; }

    /// <summary>
    /// Gets or sets the effective lookup limit after clamping.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets whether distinct input names exceeded the effective limit.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Gets or sets returned cards in first-requested order.
    /// </summary>
    public List<CardBatchLookupRow> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets requested names that were not returned by the catalog.
    /// </summary>
    public List<string> MissingNames { get; set; } = [];
}

/// <summary>
/// Pairs one normalized batch request name with the returned card.
/// </summary>
public sealed class CardBatchLookupRow
{
    /// <summary>
    /// Gets or sets the normalized request name.
    /// </summary>
    public string RequestedName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card returned for the request name.
    /// </summary>
    public CardInfo Card { get; set; } = new();
}

/// <summary>
/// Describes a card image URI lookup without embedding image bytes.
/// </summary>
public sealed class CardImageLookupResult
{
    /// <summary>
    /// Gets or sets the lookup status: ok, not-found, or no-image.
    /// </summary>
    public string Status { get; set; } = "ok";

    /// <summary>
    /// Gets or sets the original card name or id requested by the caller.
    /// </summary>
    public string RequestedNameOrId { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved card name when a card was found.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the image kind requested by the caller.
    /// </summary>
    public string RequestedKind { get; set; } = "normal";

    /// <summary>
    /// Gets or sets the image kind actually returned.
    /// </summary>
    public string? ResolvedKind { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall-hosted image URI when available.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the image kinds available on the card.
    /// </summary>
    public List<string> AvailableKinds { get; set; } = [];

    /// <summary>
    /// Gets or sets the Scryfall card page URI for attribution and inspection.
    /// </summary>
    public string? ScryfallUri { get; set; }
}
