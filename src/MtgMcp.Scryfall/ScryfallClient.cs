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
/// Calls the scryfall client service.
/// </summary>
public sealed class ScryfallClient : ICardCatalog, IScryfallCacheBypass, IDisposable
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
    /// Handles request lock shared by all Scryfall client instances.
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
        this.httpClient.DefaultRequestHeaders.UserAgent.Clear();
        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(this.options.UserAgent);
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
    /// Gets the card.
    /// </summary>
    public async Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return null;
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
            return MapCard(document.RootElement);
        }
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
                results[requestedName] = match;
            }
        }

        return results;
    }

    /// <summary>
    /// Gets the rulings.
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
    /// Gets the prints.
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

    /// <summary>
    /// Gets the json.
    /// </summary>
    private async Task<JsonDocument?> GetJsonAsync(
        string relativeUri,
        CancellationToken cancellationToken,
        bool returnNullOnNotFound = false
    )
    {
        TimeSpan cacheTtl = GetCacheTtl(relativeUri);
        string? cachedBody = await GetCachedResponseBodyAsync(
                HttpMethod.Get.Method,
                relativeUri,
                requestBody: null,
                cacheTtl,
                cancellationToken)
            .ConfigureAwait(false);
        if (cachedBody is not null)
        {
            return JsonDocument.Parse(cachedBody);
        }

        int maxRetries = Math.Max(0, options.MaxRateLimitRetries);
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            await DelayIfNeededAsync(cancellationToken).ConfigureAwait(false);

            using HttpResponseMessage response = await httpClient
                .GetAsync(relativeUri, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                await DelayForRateLimitAsync(response, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (returnNullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Scryfall request failed with {(int)response.StatusCode}: {errorBody}"
                );
            }

            string successBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            await SetCachedResponseBodyAsync(
                    HttpMethod.Get.Method,
                    relativeUri,
                    requestBody: null,
                    successBody,
                    cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(successBody);
        }

        throw new HttpRequestException("Scryfall request failed after rate limit retry.");
    }

    /// <summary>
    /// Posts the json.
    /// </summary>
    private async Task<JsonDocument?> PostJsonAsync(
        string relativeUri,
        object body,
        CancellationToken cancellationToken
    )
    {
        string json = JsonSerializer.Serialize(body, SerializerOptions);
        TimeSpan cacheTtl = GetCacheTtl(relativeUri);
        string? cachedBody = await GetCachedResponseBodyAsync(
                HttpMethod.Post.Method,
                relativeUri,
                json,
                cacheTtl,
                cancellationToken)
            .ConfigureAwait(false);
        if (cachedBody is not null)
        {
            return JsonDocument.Parse(cachedBody);
        }

        int maxRetries = Math.Max(0, options.MaxRateLimitRetries);
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            await DelayIfNeededAsync(cancellationToken).ConfigureAwait(false);

            using StringContent content = new(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await httpClient
                .PostAsync(relativeUri, content, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                await DelayForRateLimitAsync(response, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Scryfall request failed with {(int)response.StatusCode}: {errorBody}"
                );
            }

            string successBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            await SetCachedResponseBodyAsync(
                    HttpMethod.Post.Method,
                    relativeUri,
                    json,
                    successBody,
                    cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(successBody);
        }

        throw new HttpRequestException("Scryfall request failed after rate limit retry.");
    }

    /// <summary>
    /// Delays according to Scryfall rate limit guidance.
    /// </summary>
    private static async Task DelayForRateLimitAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
            ?? await GetRateLimitDelayFromBodyAsync(response, cancellationToken)
                .ConfigureAwait(false)
            ?? DefaultRateLimitDelay;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads Scryfall error details for retry timing when headers are absent.
    /// </summary>
    private static async Task<TimeSpan?> GetRateLimitDelayFromBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        const string marker = "after ";
        int start = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = start;
        while (end < body.Length && char.IsDigit(body[end]))
        {
            end++;
        }

        if (end <= start || !int.TryParse(body.AsSpan(start, end - start), out int seconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Reads a cached raw Scryfall JSON body.
    /// </summary>
    private async Task<string?> GetCachedResponseBodyAsync(
        string method,
        string relativeUri,
        string? requestBody,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        if (CacheBypassDepth.Value > 0)
        {
            return null;
        }

        return await cache
            .GetAsync<string>(
                CreateCacheKey(method, relativeUri, requestBody),
                timeToLive,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stores a raw Scryfall JSON response body as a reusable source fact.
    /// </summary>
    private async Task SetCachedResponseBodyAsync(
        string method,
        string relativeUri,
        string? requestBody,
        string responseBody,
        CancellationToken cancellationToken)
    {
        await cache
            .SetAsync(
                CreateCacheKey(method, relativeUri, requestBody),
                responseBody,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a stable cache key for a Scryfall API call.
    /// </summary>
    private static CorpusCacheKey CreateCacheKey(
        string method,
        string relativeUri,
        string? requestBody)
    {
        return new CorpusCacheKey
        {
            Source = "scryfall",
            Endpoint = method.ToUpperInvariant(),
            Query = $"{relativeUri.Trim()}|{requestBody ?? ""}",
            AdapterVersion = CacheAdapterVersion,
            Budget = "source-fact"
        };
    }

    /// <summary>
    /// Gets the configured TTL for a Scryfall API call.
    /// </summary>
    private TimeSpan GetCacheTtl(string relativeUri)
    {
        bool isSearch = relativeUri.Contains("cards/search", StringComparison.OrdinalIgnoreCase);
        return CorpusCacheFactory.ParseDuration(
            isSearch
                ? mtgOptions.Intelligence.Cache.Ttls.ScryfallSearch
                : mtgOptions.Intelligence.Cache.Ttls.ScryfallCardMetadata,
            isSearch ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Handles delay if needed.
    /// </summary>
    private async Task DelayIfNeededAsync(CancellationToken cancellationToken)
    {
        await RequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - lastRequestAt;
            if (elapsed < options.MinimumDelay)
            {
                await Task.Delay(options.MinimumDelay - elapsed, cancellationToken)
                    .ConfigureAwait(false);
            }

            lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            RequestLock.Release();
        }
    }

    /// <summary>
    /// Releases resources held by the instance.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Opens a scope where Scryfall response cache reads are bypassed.
    /// </summary>
    IDisposable IScryfallCacheBypass.BypassCache()
    {
        CacheBypassDepth.Value++;
        return new CacheBypassScope();
    }

    /// <summary>
    /// Restores the previous cache-bypass depth when a refresh call completes.
    /// </summary>
    private sealed class CacheBypassScope : IDisposable
    {
        /// <summary>
        /// Closes this cache-bypass scope.
        /// </summary>
        public void Dispose()
        {
            CacheBypassDepth.Value = Math.Max(0, CacheBypassDepth.Value - 1);
        }
    }

    /// <summary>
    /// Maps the search result.
    /// </summary>
    private static CardSearchResult MapSearchResult(JsonElement element)
    {
        return new CardSearchResult
        {
            Id = GetString(element, "id") ?? "",
            Name = GetString(element, "name") ?? "",
            ManaCost = GetString(element, "mana_cost") ?? GetFaceString(element, "mana_cost"),
            TypeLine = GetString(element, "type_line") ?? GetFaceString(element, "type_line"),
            Set = GetString(element, "set"),
            CollectorNumber = GetString(element, "collector_number"),
            ReleasedAt = GetDateOnly(element, "released_at"),
            ScryfallUri = GetString(element, "scryfall_uri"),
        };
    }

    /// <summary>
    /// Maps the card.
    /// </summary>
    private static CardInfo MapCard(JsonElement element)
    {
        CardInfo card = new()
        {
            Id = GetString(element, "id") ?? "",
            OracleId = GetString(element, "oracle_id"),
            Name = GetString(element, "name") ?? "",
            ManaCost = GetString(element, "mana_cost") ?? GetFaceString(element, "mana_cost"),
            ManaValue = GetDouble(element, "cmc"),
            TypeLine = GetString(element, "type_line") ?? GetFaceString(element, "type_line"),
            OracleText = GetString(element, "oracle_text") ?? GetFaceText(element, "oracle_text"),
            Set = GetString(element, "set"),
            CollectorNumber = GetString(element, "collector_number"),
            Rarity = GetString(element, "rarity"),
            ReleasedAt = GetDateOnly(element, "released_at"),
            ScryfallUri = GetString(element, "scryfall_uri"),
            EdhrecRank = GetInt(element, "edhrec_rank"),
        };

        AddStringArray(element, "colors", card.Colors);
        AddStringArray(element, "color_identity", card.ColorIdentity);
        AddStringArray(element, "keywords", card.Keywords);
        AddStringArray(element, "produced_mana", card.ProducedMana);
        AddStringDictionary(element, "legalities", card.Legalities);
        AddStringDictionary(element, "prices", card.Prices);
        AddStringDictionary(element, "image_uris", card.ImageUris);

        if (
            card.ImageUris.Count == 0
            && element.TryGetProperty("card_faces", out JsonElement faces)
        )
        {
            foreach (JsonElement face in faces.EnumerateArray())
            {
                AddStringDictionary(face, "image_uris", card.ImageUris);
                if (card.ImageUris.Count > 0)
                {
                    break;
                }
            }
        }

        return card;
    }

    /// <summary>
    /// Gets the string.
    /// </summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    /// <summary>
    /// Gets the date.
    /// </summary>
    private static DateOnly? GetDateOnly(JsonElement element, string propertyName)
    {
        return DateOnly.TryParse(GetString(element, propertyName), out DateOnly date)
            ? date
            : null;
    }

    /// <summary>
    /// Gets the double.
    /// </summary>
    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result)
            ? result
            : null;
    }

    /// <summary>
    /// Gets the int.
    /// </summary>
    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : null;
    }

    /// <summary>
    /// Gets the face string.
    /// </summary>
    private static string? GetFaceString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty("card_faces", out JsonElement faces))
        {
            return null;
        }

        foreach (JsonElement face in faces.EnumerateArray())
        {
            string? value = GetString(face, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets combined face text.
    /// </summary>
    private static string? GetFaceText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty("card_faces", out JsonElement faces))
        {
            return null;
        }

        List<string> values = [];
        foreach (JsonElement face in faces.EnumerateArray())
        {
            string? value = GetString(face, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : string.Join("\n\n", values);
    }

    /// <summary>
    /// Builds lookup aliases for cards with multiple faces.
    /// </summary>
    private static List<string> BuildNameAliases(string name)
    {
        List<string> aliases = [];
        AddAlias(aliases, name);

        string[] faces = name.Split(
            ["//"],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );
        if (faces.Length > 1)
        {
            AddAlias(aliases, string.Join(" // ", faces));
            foreach (string face in faces)
            {
                AddAlias(aliases, face);
            }
        }

        return aliases;
    }

    /// <summary>
    /// Adds a unique alias.
    /// </summary>
    private static void AddAlias(List<string> aliases, string alias)
    {
        string normalized = alias.Trim();
        if (
            !string.IsNullOrWhiteSpace(normalized)
            && !aliases.Any(value => value.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        )
        {
            aliases.Add(normalized);
        }
    }

    /// <summary>
    /// Finds a returned card for the requested aliases.
    /// </summary>
    private static CardInfo? FindReturnedCard(
        string requestedName,
        IReadOnlyList<string> aliases,
        IReadOnlyDictionary<string, CardInfo> returnedCards
    )
    {
        foreach (string alias in aliases)
        {
            if (returnedCards.TryGetValue(alias, out CardInfo? exact))
            {
                return exact;
            }
        }

        foreach (CardInfo card in returnedCards.Values)
        {
            if (CardNameMatches(card.Name, requestedName) || CardNameMatchesAnyAlias(card.Name, aliases))
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a returned card name matches any alias.
    /// </summary>
    private static bool CardNameMatchesAnyAlias(string returnedName, IEnumerable<string> aliases)
    {
        foreach (string alias in aliases)
        {
            if (CardNameMatches(returnedName, alias))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a returned card name matches a requested name or face.
    /// </summary>
    private static bool CardNameMatches(string returnedName, string requestedName)
    {
        List<string> returnedAliases = BuildNameAliases(returnedName);
        List<string> requestedAliases = BuildNameAliases(requestedName);
        foreach (string returnedAlias in returnedAliases)
        {
            foreach (string requestedAlias in requestedAliases)
            {
                if (returnedAlias.Equals(requestedAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the string array.
    /// </summary>
    private static void AddStringArray(
        JsonElement element,
        string propertyName,
        List<string> target
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            string? value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value);
            }
        }
    }

    /// <summary>
    /// Adds the string dictionary.
    /// </summary>
    private static void AddStringDictionary(
        JsonElement element,
        string propertyName,
        Dictionary<string, string> target
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement jsonObject)
            || jsonObject.ValueKind != JsonValueKind.Object
        )
        {
            return;
        }

        foreach (JsonProperty property in jsonObject.EnumerateObject())
        {
            string? value =
                property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[property.Name] = value;
            }
        }
    }
}
