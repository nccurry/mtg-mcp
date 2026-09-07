using System.Text;
using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt deck and card-resolution provider routes.
/// </summary>
internal sealed class ArchidektDeckTransport
{
    /// <summary>
    /// Sends shared authenticated provider requests for deck routes.
    /// </summary>
    private readonly ArchidektSession session;

    /// <summary>
    /// Creates deck route operations around one shared provider session.
    /// </summary>
    internal ArchidektDeckTransport(ArchidektSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Lists one authenticated deck page.
    /// </summary>
    internal async Task<RemoteDeckPage> ListAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        (int offset, string? expectedChecksum) = ParseDeckListCursor(cursor, pageSize);
        string username = session.GetConfiguredUsername();
        string path = $"api/decks/v3/?ownerUsername={Uri.EscapeDataString(username)}";
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            path,
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        RemoteDeckPage complete = ArchidektDeckContractMapper.MapDeckPage(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/v3/");
        if (expectedChecksum is not null && !string.Equals(
                expectedChecksum,
                complete.Evidence.SourceChecksum,
                StringComparison.Ordinal))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Conflict,
                "deck-list-changed",
                "The Archidekt deck list changed after the previous page.");
        }

        if (offset > complete.Items.Count)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-deck-list-cursor",
                "The Archidekt deck-list cursor is invalid.");
        }

        RemoteDeckSummary[] items = complete.Items.Skip(offset).Take(pageSize).ToArray();
        int nextOffset = offset + items.Length;
        string? nextCursor = nextOffset < complete.Items.Count
            ? FormatDeckListCursor(nextOffset, complete.Evidence.SourceChecksum)
            : null;
        return new RemoteDeckPage(items, nextCursor, complete.Evidence);
    }

    /// <summary>
    /// Gets one public or authenticated deck.
    /// </summary>
    internal async Task<RemoteDeckSnapshot> GetAsync(
        string deckId,
        bool requireAuthentication,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            $"api/decks/{Uri.EscapeDataString(deckId)}/",
            payload: null,
            requireAuthentication,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/{deckId}/");
    }

    /// <summary>
    /// Creates one private-by-default empty deck shell.
    /// </summary>
    internal async Task<RemoteDeckSnapshot> CreateAsync(
        ArchidektDeckCreateRequest request,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = ArchidektContract.Required(request.Name, nameof(request.Name));
        string visibility = ArchidektContract.NormalizeVisibility(request.Visibility);
        object payload = new
        {
            name,
            description = ArchidektContract.Optional(request.Description),
            deckFormat = MapFormatId(request.Format),
            edhBracket = (int?)null,
            parentFolder = ArchidektProviderId.Parse(request.ParentFolderId),
            @private = visibility == "private",
            unlisted = visibility == "unlisted",
            theorycrafted = false,
            game = (string?)null,
            cardPackage = (string?)null,
            extras = new
            {
                decksToInclude = Array.Empty<int>(),
                commandersToAdd = Array.Empty<int>(),
                forceCardsToSingleton = false,
                ignoreCardsOutOfCommanderIdentity = true,
            },
        };
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Post,
            "api/decks/v2/",
            payload,
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "POST /api/decks/v2/");
    }

    /// <summary>
    /// Deletes one exact deck through the observed provider route without retrying ambiguous failure.
    /// </summary>
    internal async Task DeleteAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        await session.SendMutationAsync(
            HttpMethod.Delete,
            $"api/decks/{Uri.EscapeDataString(deckId)}/",
            payload: null,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates caller-selected deck metadata fields through one primitive mutation.
    /// </summary>
    internal Task SendMetadataAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Patch,
            $"api/decks/{Uri.EscapeDataString(deckId)}/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Creates one provider category.
    /// </summary>
    internal Task SendCategoryCreateAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Post,
            "api/decks/createCategory/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Updates one exact provider category.
    /// </summary>
    internal Task SendCategoryUpdateAsync(
        string categoryId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Patch,
            $"api/decks/category/{Uri.EscapeDataString(categoryId)}/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Deletes one exact provider category without retrying ambiguous failure.
    /// </summary>
    internal Task SendCategoryDeleteAsync(
        string categoryId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Delete,
            $"api/decks/category/{Uri.EscapeDataString(categoryId)}/",
            payload: null,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Sends exactly one card-relation mutation through the observed v2 route.
    /// </summary>
    internal Task SendCardMutationAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Patch,
            $"api/decks/{Uri.EscapeDataString(deckId)}/modifyCards/v2/",
            new { cards = new[] { payload } },
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Resolves one exact Archidekt printing ID for a new relation without fuzzy fallback.
    /// </summary>
    internal async Task<string> ResolveCardIdAsync(
        RemoteDeckEntry entry,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        string path = $"api/cards/v2/?name={Uri.EscapeDataString(entry.CardName)}&pageSize=25";
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            path,
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        List<JsonElement> candidates = [];
        JsonElement root = document.RootElement;
        JsonElement collection = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("results", out JsonElement results)
                ? results
                : default;
        if (collection.ValueKind == JsonValueKind.Array)
        {
            candidates.AddRange(collection.EnumerateArray());
        }

        JsonElement? match = SelectExactCard(candidates, entry);
        if (match is null || !ArchidektProviderId.TryRead(match.Value, "id", out string? cardId))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "printing-resolution-unavailable",
                "Archidekt could not resolve the exact requested printing.");
        }

        return cardId!;
    }

    /// <summary>
    /// Maps the supported local format vocabulary to Archidekt's observed numeric IDs.
    /// </summary>
    internal static int MapFormatId(string value)
    {
        return ArchidektContract.Required(value, nameof(value)).ToLowerInvariant() switch
        {
            "standard" => 1,
            "modern" => 2,
            "commander" or "edh" => 3,
            "legacy" => 4,
            "vintage" => 5,
            "pauper" => 6,
            "pioneer" => 7,
            "brawl" => 8,
            "historic" => 9,
            "oathbreaker" => 10,
            _ => throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "unsupported-deck-format",
                "The requested deck format is not mapped to Archidekt."),
        };
    }

    /// <summary>
    /// Builds one bounded authenticated list route without accepting an arbitrary URL.
    /// </summary>
    private static (int Offset, string? ExpectedChecksum) ParseDeckListCursor(
        string? cursor,
        int pageSize)
    {
        if (pageSize is < 1 or > 100)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-page-size",
                "Archidekt deck page size must be between 1 and 100.");
        }

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (0, null);
        }

        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor.Trim()));
            DeckListCursor? parsed = JsonSerializer.Deserialize<DeckListCursor>(
                json,
                ArchidektContract.JsonOptions);
            if (parsed is null || parsed.Offset <= 0 || parsed.Checksum.Length != 64)
            {
                throw new FormatException("Invalid cursor payload.");
            }

            return (parsed.Offset, parsed.Checksum);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-deck-list-cursor",
                "The Archidekt deck-list cursor is invalid.");
        }
    }

    /// <summary>
    /// Creates one opaque offset bound to the exact provider list bytes observed on the first page.
    /// </summary>
    private static string FormatDeckListCursor(int offset, string checksum)
    {
        string json = JsonSerializer.Serialize(
            new DeckListCursor(offset, checksum),
            ArchidektContract.JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Carries a local page offset and immutable source checksum inside the opaque continuation.
    /// </summary>
    private sealed record DeckListCursor(int Offset, string Checksum);

    /// <summary>
    /// Selects one exact name and printing match from a bounded provider card search.
    /// </summary>
    private static JsonElement? SelectExactCard(
        IReadOnlyList<JsonElement> candidates,
        RemoteDeckEntry entry)
    {
        List<JsonElement> nameMatches = [];
        foreach (JsonElement candidate in candidates)
        {
            string? name = candidate.TryGetProperty("oracleCard", out JsonElement oracle) &&
                oracle.ValueKind == JsonValueKind.Object &&
                oracle.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()
                : null;
            if (!string.Equals(name, entry.CardName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.PrintingId is not null &&
                candidate.TryGetProperty("uid", out JsonElement uid) &&
                Guid.TryParse(uid.GetString(), out Guid candidateId) &&
                candidateId == entry.PrintingId)
            {
                return candidate;
            }

            string? setCode = candidate.TryGetProperty("setCode", out JsonElement set)
                ? set.GetString()
                : null;
            string? collectorNumber = candidate.TryGetProperty("collectorNumber", out JsonElement collector)
                ? collector.GetString()
                : null;
            if (entry.SetCode is not null && entry.CollectorNumber is not null &&
                string.Equals(setCode, entry.SetCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(collectorNumber, entry.CollectorNumber, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            nameMatches.Add(candidate);
        }

        return entry.PrintingId is null && entry.SetCode is null && nameMatches.Count == 1
            ? nameMatches[0]
            : null;
    }
}
