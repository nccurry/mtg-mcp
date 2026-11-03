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
    /// Waits for a short pause between provider calls.
    /// </summary>
    internal Task PauseAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        return session.PauseAsync(duration, cancellationToken);
    }

    /// <summary>
    /// Lists every deck the configured account owns.
    /// </summary>
    internal async Task<RemoteDeckPage> ListAsync(
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
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
        return ArchidektDeckContractMapper.MapDeckPage(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/v3/");
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
        deckId = ArchidektContract.Required(deckId, "remoteDeckId");
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
        deckId = ArchidektContract.Required(deckId, "remoteDeckId");
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
    /// Sends card-relation mutations for one deck in one request through the observed v2 route. Returns the patch IDs
    /// of the added relations the answer lists, or null when the answer cannot be read.
    /// </summary>
    internal async Task<IReadOnlySet<string>?> SendCardMutationsAsync(
        string deckId,
        IReadOnlyList<object> payloads,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Patch,
            $"api/decks/{Uri.EscapeDataString(deckId)}/modifyCards/v2/",
            new { cards = payloads },
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
        return ReadAddedPatchIds(response.Json);
    }

    /// <summary>
    /// Reads the patch IDs of the <c>add</c> objects of a card-mutation answer, or returns null when the answer is not
    /// JSON or holds no <c>add</c> list. A patch ID is compared as a GUID, so its spelling does not matter.
    /// </summary>
    private static HashSet<string>? ReadAddedPatchIds(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("add", out JsonElement added) ||
                added.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (JsonElement relation in added.EnumerateArray())
            {
                if (relation.ValueKind == JsonValueKind.Object &&
                    relation.TryGetProperty("patchId", out JsonElement patchId) &&
                    patchId.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(patchId.GetString(), out Guid parsed))
                {
                    ids.Add(parsed.ToString("N"));
                }
            }

            return ids;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads one page of Archidekt's public card search. The first page is requested from the search; a later page
    /// is requested with the query of the previous page's <c>next</c> link, so no other address is ever followed.
    /// </summary>
    internal async Task<ArchidektCardSearchPage> SearchCardsAsync(
        ArchidektCardSearch search,
        string? nextQuery,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        string path = nextQuery is null
            ? $"api/cards/v2/{search.FirstQuery()}"
            : $"api/cards/v2/{nextQuery}";
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            path,
            payload: null,
            requiresAuthentication: false,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        JsonElement root = document.RootElement;
        JsonElement collection = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out JsonElement results)
                ? results
                : default;
        List<ArchidektCardPrinting> candidates = [];
        int returned = 0;
        if (collection.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement candidate in collection.EnumerateArray())
            {
                returned++;
                if (ArchidektJsonContract.ReadPrinting(candidate) is { } printing)
                {
                    candidates.Add(printing);
                }
            }
        }

        int? count = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("count", out JsonElement countElement) &&
            countElement.ValueKind == JsonValueKind.Number &&
            countElement.TryGetInt32(out int total)
            ? total
            : null;
        string? next = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("next", out JsonElement nextElement) &&
            nextElement.ValueKind == JsonValueKind.String
            ? SearchNextQuery(nextElement.GetString())
            : null;
        return new ArchidektCardSearchPage(candidates, returned, count, next);
    }

    /// <summary>
    /// Takes the query of a <c>next</c> link, and only when the link points at the card search route.
    /// </summary>
    private static string? SearchNextQuery(string? link)
    {
        return Uri.TryCreate(new Uri("https://archidekt.com/"), link, out Uri? uri) &&
            uri.AbsolutePath == "/api/cards/v2/" &&
            uri.Query.Length > 1
            ? uri.Query
            : null;
    }

    /// <summary>
    /// Maps the supported local format vocabulary to Archidekt's observed numeric IDs.
    /// </summary>
    internal static int MapFormatId(string value)
    {
        return ArchidektContract.Required(value, "format").ToLowerInvariant() switch
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
                "The requested deck format is not mapped to Archidekt.")
            {
                Path = "format",
            },
        };
    }
}
