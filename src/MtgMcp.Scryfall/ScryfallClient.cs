using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Allows source providers to bypass raw Scryfall cache reads for explicit refresh calls.
/// </summary>
internal interface IScryfallCacheBypass
{
    /// <summary>
    /// Opens a scope where cached Scryfall responses are ignored but fresh responses may still update cache.
    /// </summary>
    IDisposable BypassCache();
}

/// <summary>
/// Provides Scryfall-backed card search, lookup, ruling, and print APIs.
/// </summary>
public sealed partial class ScryfallClient : ICardCatalog, IScryfallCacheBypass, IDisposable
{
    /// <summary>
    /// Stores the default rate limit delay.
    /// </summary>
    private static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Stores serializer options.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Identifies raw Scryfall response cache entries produced by this adapter.
    /// </summary>
    private const string CacheAdapterVersion = "scryfall-client-v1";

    /// <summary>
    /// Stores the http client.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores the options.
    /// </summary>
    private readonly ScryfallOptions options;

    /// <summary>
    /// Stores source-fact cache shared across agents using the same data directory.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores root mtg-mcp options for cache TTLs.
    /// </summary>
    private readonly MtgMcpOptions mtgOptions;

    /// <summary>
    /// Coordinates process-wide Scryfall request pacing across client instances.
    /// </summary>
    private static readonly SemaphoreSlim RequestLock = new(1, 1);

    /// <summary>
    /// Stores the last request at across all Scryfall client instances.
    /// </summary>
    private static DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Tracks request-local cache bypass scopes for refresh operations.
    /// </summary>
    private static readonly AsyncLocal<int> CacheBypassDepth = new();

    /// <summary>
    /// Creates a Scryfall client with shared source-fact caching.
    /// </summary>
    public ScryfallClient(
        HttpClient httpClient,
        IOptions<ScryfallOptions> options,
        ICorpusCache? cache = null,
        IOptions<MtgMcpOptions>? mtgOptions = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.cache = cache ?? new NullCorpusCache();
        this.mtgOptions = mtgOptions?.Value ?? new MtgMcpOptions();

        this.httpClient.BaseAddress ??= this.options.BaseAddress;
        MtgMcpHttpDefaults.ApplyUserAgent(this.httpClient, this.options.UserAgent);
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    /// <summary>
    /// Searches the cards.
    /// </summary>
    public async Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    )
    {
        int safeLimit = Math.Max(1, limit);
        string? uri = $"cards/search?q={Uri.EscapeDataString(query)}&unique=cards&order=edhrec";
        List<CardSearchResult> cards = [];

        while (!string.IsNullOrWhiteSpace(uri) && cards.Count < safeLimit)
        {
            JsonDocument? document = await GetJsonAsync(
                    uri,
                    cancellationToken,
                    returnNullOnNotFound: true
                )
                .ConfigureAwait(false);
            if (document is null)
            {
                return cards;
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("data", out JsonElement data))
                {
                    return cards;
                }

                foreach (JsonElement item in data.EnumerateArray())
                {
                    cards.Add(MapSearchResult(item));
                    if (cards.Count >= safeLimit)
                    {
                        break;
                    }
                }

                bool hasMore = document.RootElement.TryGetProperty("has_more", out JsonElement hasMoreValue)
                    && hasMoreValue.ValueKind == JsonValueKind.True;
                uri = hasMore && cards.Count < safeLimit
                    ? GetString(document.RootElement, "next_page")
                    : null;
            }
        }

        return cards;
    }

    /// <summary>
    /// Searches cards from a provider-neutral deckbuilding request.
    /// </summary>
    public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        CardSearchRequest request,
        int limit,
        CancellationToken cancellationToken
    )
    {
        return SearchCardsAsync(BuildSearchQuery(request), limit, cancellationToken);
    }

    /// <summary>
    /// Looks up one Scryfall card by id, exact name, or fuzzy name.
    /// </summary>
    public async Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return null;
        }

        if (!Guid.TryParse(nameOrId, out _))
        {
            IReadOnlyDictionary<string, CardInfo> exactCards;
            try
            {
                exactCards = await GetCardsByNamesAsync(
                        [nameOrId.Trim()],
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException) when (!IsBasicLandName(nameOrId))
            {
                exactCards = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
            }

            if (exactCards.TryGetValue(nameOrId.Trim(), out CardInfo? exactCard))
            {
                return exactCard;
            }

            if (IsBasicLandName(nameOrId))
            {
                return null;
            }
        }

        string uri = Guid.TryParse(nameOrId, out _)
            ? $"cards/{nameOrId}"
            : $"cards/named?fuzzy={Uri.EscapeDataString(nameOrId)}";
        JsonDocument? document = await GetJsonAsync(
                uri,
                cancellationToken,
                returnNullOnNotFound: true
            )
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        using (document)
        {
            CardInfo card = MapCard(document.RootElement);
            return Guid.TryParse(nameOrId, out _)
                ? card
                : await SelectReleasedPricingSnapshotAsync(card, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks whether a caller supplied an exact basic land name that should never use fuzzy lookup.
    /// </summary>
    private static bool IsBasicLandName(string name)
    {
        string normalized = name.Trim();
        return normalized.Equals("Plains", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Island", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Swamp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Mountain", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Forest", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Wastes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets cards by names.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, CardInfo> results = new(StringComparer.OrdinalIgnoreCase);
        List<string> distinctNames = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Dictionary<string, List<string>> aliasesByName = new(StringComparer.OrdinalIgnoreCase);
        List<string> identifiers = [];
        HashSet<string> identifierSet = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in distinctNames)
        {
            List<string> aliases = BuildNameAliases(name);
            aliasesByName[name] = aliases;
            foreach (string alias in aliases)
            {
                if (identifierSet.Add(alias))
                {
                    identifiers.Add(alias);
                }
            }
        }

        Dictionary<string, CardInfo> returnedCards = new(StringComparer.OrdinalIgnoreCase);
        foreach (string[] chunk in identifiers.Chunk(75))
        {
            object body = new
            {
                identifiers = chunk.Select(name => new { name }).ToArray()
            };

            using JsonDocument? document = await PostJsonAsync(
                    "cards/collection",
                    body,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (document is null || !document.RootElement.TryGetProperty("data", out JsonElement data))
            {
                continue;
            }

            foreach (JsonElement item in data.EnumerateArray())
            {
                CardInfo card = MapCard(item);
                if (!string.IsNullOrWhiteSpace(card.Name))
                {
                    returnedCards[card.Name] = card;
                }
            }
        }

        foreach (string requestedName in distinctNames)
        {
            CardInfo? match = FindReturnedCard(
                requestedName,
                aliasesByName[requestedName],
                returnedCards
            );
            if (match is not null)
            {
                results[requestedName] = await SelectReleasedPricingSnapshotAsync(match, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return results;
    }

    /// <summary>
    /// Looks up official Scryfall rulings for a card.
    /// </summary>
    public async Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
        string nameOrId,
        CancellationToken cancellationToken
    )
    {
        CardInfo? card = Guid.TryParse(nameOrId, out _)
            ? new CardInfo { Id = nameOrId, Name = nameOrId }
            : await GetCardAsync(nameOrId, cancellationToken).ConfigureAwait(false);
        if (card is null || string.IsNullOrWhiteSpace(card.Id))
        {
            return [];
        }

        JsonDocument? document = await GetJsonAsync(
                $"cards/{card.Id}/rulings",
                cancellationToken,
                returnNullOnNotFound: true
            )
            .ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("data", out JsonElement data))
            {
                return [];
            }

            List<RulingInfo> rulings = [];
            foreach (JsonElement item in data.EnumerateArray())
            {
                rulings.Add(
                    new RulingInfo
                    {
                        Source = GetString(item, "source") ?? "scryfall",
                        PublishedAt = DateOnly.TryParse(
                            GetString(item, "published_at"),
                            out DateOnly date
                        )
                            ? date
                            : default,
                        Text = GetString(item, "comment") ?? "",
                    }
                );
            }

            return rulings;
        }
    }

    /// <summary>
    /// Looks up known Scryfall prints for a card.
    /// </summary>
    public async Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
        string nameOrId,
        CancellationToken cancellationToken
    )
    {
        CardInfo? card = await GetCardAsync(nameOrId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return [];
        }

        return await GetPrintsForCardAsync(card, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets printings for a resolved oracle card without performing another named-card lookup.
    /// </summary>
    private async Task<IReadOnlyList<CardInfo>> GetPrintsForCardAsync(
        CardInfo card,
        CancellationToken cancellationToken)
    {
        string query = card.OracleId is not null
            ? $"oracleid:{card.OracleId}"
            : $"!\"{card.Name}\"";
        string uri = $"cards/search?q={Uri.EscapeDataString(query)}&unique=prints&order=released";
        JsonDocument? document = await GetJsonAsync(
                uri,
                cancellationToken,
                returnNullOnNotFound: true
            )
            .ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("data", out JsonElement data))
            {
                return [];
            }

            List<CardInfo> cards = [];
            foreach (JsonElement item in data.EnumerateArray())
            {
                cards.Add(MapCard(item));
            }

            return cards;
        }
    }

    /// <summary>
    /// Replaces future or unpriced named-card snapshots with deterministic released priced printings.
    /// </summary>
    private async Task<CardInfo> SelectReleasedPricingSnapshotAsync(
        CardInfo card,
        CancellationToken cancellationToken)
    {
        DateOnly referenceDate = CurrentPricingDate();
        CardPriceEvaluation evaluation = CardPriceEvaluator.Evaluate(card, referenceDate);
        if (options.PricingMode == PricingMode.ReleasedIfNeeded
            && evaluation.PriceKnown
            && evaluation.PrintingStatus.Equals("released", StringComparison.OrdinalIgnoreCase))
        {
            card.SelectedPrintingReason = evaluation.SelectedPrintingReason;
            card.PricingMode = options.PricingMode.ToString();
            return card;
        }

        if (!ShouldInspectPrintings(card, evaluation, options.PricingMode))
        {
            card.SelectedPrintingReason = evaluation.SelectedPrintingReason;
            card.PricingMode = options.PricingMode.ToString();
            return card;
        }

        IReadOnlyList<CardInfo> printings = await GetPrintsForCardAsync(card, cancellationToken)
            .ConfigureAwait(false);
        CardPrintingSelectionOptions selectionOptions = new()
        {
            PricingMode = options.PricingMode,
            Format = options.PricingFormat,
            AllowAnyFinish = options.AllowAnyFinishForBudgetPricing,
        };
        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            card,
            printings,
            referenceDate,
            selectionOptions);
        selection.Card.SelectedPrintingReason = selection.PriceEvaluation.SelectedPrintingReason;
        selection.Card.PricingMode = options.PricingMode.ToString();
        return selection.Card;
    }

    /// <summary>
    /// Decides when a named-card result has enough pricing context to justify print replacement.
    /// </summary>
    private static bool ShouldInspectPrintings(
        CardInfo card,
        CardPriceEvaluation evaluation,
        PricingMode pricingMode)
    {
        if (pricingMode != PricingMode.ReleasedIfNeeded)
        {
            return true;
        }

        if (evaluation.PrintingStatus.Equals("future", StringComparison.OrdinalIgnoreCase)
            || evaluation.PrintingStatus.Equals("non-paper", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return card.ReleasedAt.HasValue && !evaluation.PriceKnown;
    }

    /// <summary>
    /// Gets the current UTC date for release-aware print selection.
    /// </summary>
    private DateOnly CurrentPricingDate()
    {
        return options.PricingReferenceDate
            ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }

    /// <summary>
    /// Suggests the cards.
    /// </summary>
    public async Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
        string prompt,
        string? format,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string query = prompt;
        if (!string.IsNullOrWhiteSpace(format))
        {
            query = $"{prompt} legal:{format}";
        }

        return await SearchCardsAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

}
