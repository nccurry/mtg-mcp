using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Calls the scryfall client service.
/// </summary>
public sealed class ScryfallClient : ICardCatalog, IDisposable
{
    /// <summary>
    /// Stores the http client.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores the options.
    /// </summary>
    private readonly ScryfallOptions options;

    /// <summary>
    /// Handles request lock.
    /// </summary>
    private readonly SemaphoreSlim requestLock = new(1, 1);

    /// <summary>
    /// Stores the last request at.
    /// </summary>
    private DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Handles scryfall client.
    /// </summary>
    public ScryfallClient(HttpClient httpClient, IOptions<ScryfallOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;

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
        string uri = $"cards/search?q={Uri.EscapeDataString(query)}&unique=cards&order=edhrec";
        List<CardSearchResult> cards = [];
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
                if (cards.Count >= Math.Max(1, limit))
                {
                    break;
                }
            }

            return cards;
        }
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
        await DelayIfNeededAsync(cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await httpClient
            .GetAsync(relativeUri, cancellationToken)
            .ConfigureAwait(false);
        if (returnNullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            string body = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new HttpRequestException(
                $"Scryfall request failed with {(int)response.StatusCode}: {body}"
            );
        }

        await using Stream stream = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles delay if needed.
    /// </summary>
    private async Task DelayIfNeededAsync(CancellationToken cancellationToken)
    {
        await requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            requestLock.Release();
        }
    }

    /// <summary>
    /// Releases resources held by the instance.
    /// </summary>
    public void Dispose()
    {
        requestLock.Dispose();
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
            OracleText = GetString(element, "oracle_text") ?? GetFaceString(element, "oracle_text"),
            Set = GetString(element, "set"),
            CollectorNumber = GetString(element, "collector_number"),
            Rarity = GetString(element, "rarity"),
            ScryfallUri = GetString(element, "scryfall_uri"),
        };

        AddStringArray(element, "colors", card.Colors);
        AddStringArray(element, "color_identity", card.ColorIdentity);
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
