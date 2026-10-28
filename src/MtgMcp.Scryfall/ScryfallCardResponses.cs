using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Carries the format notes shared by every card-returning result: the format used, limits that left fields
/// empty, and whether the page was cut to stay within the size limit.
/// </summary>
public abstract record ScryfallFormattedResponse
{
    /// <summary>
    /// Says what to do to read a cut page: take the next cursor, ask for fewer fields, or ask for a smaller page.
    /// </summary>
    public const string NarrowHint =
        "The page was cut to stay under the result size limit. Use nextCursor to read the rest, or ask for fewer " +
        "fields or a smaller pageSize.";

    /// <summary>
    /// Gets the response format the cards were written in: concise, standard, or full.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    [JsonPropertyOrder(100)]
    public string ResponseFormat { get; init; } = "standard";

    /// <summary>
    /// Gets the reasons some requested fields are empty, or null when there are none.
    /// </summary>
    [JsonPropertyName("limits")]
    [JsonPropertyOrder(101)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Limits { get; init; }

    /// <summary>
    /// Gets true when the page holds fewer items than requested because of the result size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonPropertyOrder(102)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to read a truncated page in smaller pieces.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonPropertyOrder(103)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}

/// <summary>
/// Returns one ordered card search page in the requested response format.
/// </summary>
public sealed record ScryfallSearchResponse(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCardView> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference Snapshot,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings) : ScryfallFormattedResponse;

/// <summary>
/// Returns one resolved card in the requested response format.
/// </summary>
public sealed record ScryfallCardResponse(
    [property: JsonPropertyName("card")] ScryfallCardView Card,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId) : ScryfallFormattedResponse;

/// <summary>
/// Preserves the positional outcome for one collection identifier, with the card in the requested format.
/// </summary>
public sealed record ScryfallCollectionRowResponse(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("lookup")] ScryfallCardLookup Lookup,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("card")] ScryfallCardView? Card,
    [property: JsonPropertyName("message")] string? Message);

/// <summary>
/// Returns ordered collection rows in the requested response format.
/// </summary>
public sealed record ScryfallCollectionResponse(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCollectionRowResponse> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId) : ScryfallFormattedResponse;

/// <summary>
/// Returns cards assigned to one requested tag in the requested response format.
/// </summary>
public sealed record ScryfallCardsByTagResponse(
    [property: JsonPropertyName("tag")] ScryfallTag Tag,
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCardView> Page,
    [property: JsonPropertyName("assignments")] IReadOnlyList<ScryfallTagEvidence> Assignments,
    [property: JsonPropertyName("generationId")] Guid GenerationId) : ScryfallFormattedResponse;

/// <summary>
/// Builds card responses in the requested format around the card evidence operations.
/// </summary>
internal sealed class ScryfallCardResponseOperations
{
    /// <summary>
    /// Owns the card reads that produce mapped cards.
    /// </summary>
    private readonly ScryfallCardEvidenceOperations cards;

    /// <summary>
    /// Writes values the way the host writes tool results, so page sizes are measured as clients receive them.
    /// </summary>
    private readonly JsonSerializerOptions resultOptions;

    /// <summary>
    /// Creates response operations around the card evidence operations.
    /// </summary>
    internal ScryfallCardResponseOperations(
        ScryfallCardEvidenceOperations cards,
        JsonSerializerOptions resultOptions)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(resultOptions);
        this.cards = cards;
        this.resultOptions = resultOptions;
    }

    /// <summary>
    /// Runs a search and writes its page in the requested format.
    /// </summary>
    internal async Task<OperationResult<ScryfallSearchResponse>> SearchAsync(
        string query,
        string unique,
        string order,
        string direction,
        bool includeExtras,
        bool includeMultilingual,
        bool includeVariations,
        string freshnessPolicy,
        string? cursor,
        int pageSize,
        string? responseFormat,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(responseFormat, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return OperationFailure.Forward<ScryfallCardFormat, ScryfallSearchResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data, resultOptions);
        OperationResult<ScryfallSearchResult> result = await cards.SearchAsync(
            query,
            unique,
            order,
            direction,
            includeExtras,
            includeMultilingual,
            includeVariations,
            freshnessPolicy,
            cursor,
            pageSize,
            format.Data.IncludeRaw,
            formatter.CountItemsThatFit,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallSearchResult> success)
        {
            return OperationFailure.Forward<ScryfallSearchResult, ScryfallSearchResponse>(result);
        }

        ScryfallFormattedCards formatted = PageCards(success.Data.Page);
        ScryfallPage<ScryfallCardView> page = ToPage(formatted, success.Data.Page);
        return new OperationSuccess<ScryfallSearchResponse>(
            new ScryfallSearchResponse(page, success.Data.Snapshot, success.Data.Warnings)
            {
                ResponseFormat = format.Data.Name,
                Limits = formatted.Limits,
                Truncated = formatted.Truncated ? true : null,
                Narrow = formatted.Truncated ? ScryfallFormattedResponse.NarrowHint : null,
            });
    }

    /// <summary>
    /// Resolves one card and writes it in the requested format.
    /// </summary>
    internal async Task<OperationResult<ScryfallCardResponse>> GetCardAsync(
        ScryfallCardLookup lookup,
        string freshnessPolicy,
        string? responseFormat,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(responseFormat, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return OperationFailure.Forward<ScryfallCardFormat, ScryfallCardResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data, resultOptions);
        OperationResult<ScryfallCardResult> result = await cards.GetCardAsync(
            lookup,
            freshnessPolicy,
            format.Data.IncludeRaw,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCardResult> success)
        {
            return OperationFailure.Forward<ScryfallCardResult, ScryfallCardResponse>(result);
        }

        ScryfallFormattedCards formatted = await formatter.FormatAsync(
            [success.Data.Card],
            success.Data.CorpusGenerationId,
            cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallCardResponse>(
            new ScryfallCardResponse(
                formatted.Views[0]!.Value,
                success.Data.Origin,
                success.Data.Snapshot,
                success.Data.CorpusGenerationId)
            {
                ResponseFormat = format.Data.Name,
                Limits = formatted.Limits,
            });
    }

    /// <summary>
    /// Resolves a card collection and writes each row's card in the requested format. One fuzzy-name lookup is
    /// answered as a single-row collection, because the provider's collection endpoint cannot match fuzzy names.
    /// </summary>
    internal async Task<OperationResult<ScryfallCollectionResponse>> GetCollectionAsync(
        IReadOnlyList<ScryfallCardLookup>? lookups,
        string freshnessPolicy,
        string? cursor,
        int pageSize,
        string? responseFormat,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        if (cursor is null && lookups is [{ Kind: "fuzzy-name" } fuzzyLookup])
        {
            return await GetFuzzyRowAsync(
                fuzzyLookup, freshnessPolicy, pageSize, responseFormat, fields, cancellationToken)
                .ConfigureAwait(false);
        }

        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(responseFormat, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return OperationFailure.Forward<ScryfallCardFormat, ScryfallCollectionResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data, resultOptions);
        OperationResult<ScryfallCollectionResult> result = await cards.GetCollectionAsync(
            lookups,
            freshnessPolicy,
            cursor,
            pageSize,
            format.Data.IncludeRaw,
            formatter.CountItemsThatFit,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCollectionResult> success)
        {
            return OperationFailure.Forward<ScryfallCollectionResult, ScryfallCollectionResponse>(result);
        }

        IReadOnlyList<ScryfallCollectionRow> rows = success.Data.Page.Items;
        ScryfallFormattedCards formatted = PageCards(success.Data.Page);
        List<ScryfallCollectionRowResponse> items = new(rows.Count);
        for (int index = 0; index < rows.Count; index++)
        {
            ScryfallCollectionRow row = rows[index];
            items.Add(new ScryfallCollectionRowResponse(
                row.Index,
                row.Lookup,
                row.Status,
                row.Origin,
                formatted.Views[index],
                row.Message));
        }

        ScryfallPage<ScryfallCollectionRowResponse> page =
            new(items, success.Data.Page.TotalCount, success.Data.Page.NextCursor);
        return new OperationSuccess<ScryfallCollectionResponse>(
            new ScryfallCollectionResponse(page, success.Data.Snapshot, success.Data.CorpusGenerationId)
            {
                ResponseFormat = format.Data.Name,
                Limits = formatted.Limits,
                Truncated = formatted.Truncated ? true : null,
                Narrow = formatted.Truncated ? ScryfallFormattedResponse.NarrowHint : null,
            });
    }

    /// <summary>
    /// Reads a card miss as a row status, the way the collection path reports it: a card the provider does not have
    /// is not-found, and a card that cache-only mode could not read is not-cached.
    /// </summary>
    private static bool TryDescribeMiss(
        OperationResult<ScryfallCardResponse> single,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? status,
        out string? message)
    {
        (status, message) = single switch
        {
            OperationNotFound missing => ("not-found", missing.Message),
            OperationNotCached notCached => ("not-cached", notCached.Message),
            _ => (null, null),
        };
        return status is not null;
    }

    /// <summary>
    /// Resolves one fuzzy-name lookup as a card read and returns it as a one-row collection.
    /// </summary>
    private async Task<OperationResult<ScryfallCollectionResponse>> GetFuzzyRowAsync(
        ScryfallCardLookup lookup,
        string freshnessPolicy,
        int pageSize,
        string? responseFormat,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? pageFailure = ScryfallCardEvidenceOperations.ValidatePageSize(
            pageSize,
            string.Equals(responseFormat, "full", StringComparison.Ordinal));
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        OperationInvalidInput? lookupFailure = ScryfallCardEvidenceOperations.ValidateLookup(lookup);
        if (lookupFailure is not null)
        {
            return lookupFailure;
        }

        OperationResult<ScryfallCardResponse> single = await GetCardAsync(
            lookup,
            freshnessPolicy,
            responseFormat,
            fields,
            cancellationToken).ConfigureAwait(false);
        if (TryDescribeMiss(single, out string? missStatus, out string? missMessage))
        {
            ScryfallCollectionRowResponse missRow = new(0, lookup, missStatus, null, null, missMessage);
            return new OperationSuccess<ScryfallCollectionResponse>(
                new ScryfallCollectionResponse(
                    new ScryfallPage<ScryfallCollectionRowResponse>([missRow], 1, null),
                    null,
                    null)
                {
                    ResponseFormat = responseFormat ?? "standard",
                });
        }

        if (single is not OperationSuccess<ScryfallCardResponse> success)
        {
            return OperationFailure.Forward<ScryfallCardResponse, ScryfallCollectionResponse>(single);
        }

        ScryfallCardResponse card = success.Data;
        ScryfallCollectionRowResponse row = new(0, lookup, "found", card.Origin, card.Card, null);
        return new OperationSuccess<ScryfallCollectionResponse>(
            new ScryfallCollectionResponse(
                new ScryfallPage<ScryfallCollectionRowResponse>([row], 1, null),
                card.Snapshot,
                card.CorpusGenerationId)
            {
                ResponseFormat = card.ResponseFormat,
                Limits = card.Limits,
            });
    }

    /// <summary>
    /// Reads cards assigned to one tag and writes them in the requested format.
    /// </summary>
    internal async Task<OperationResult<ScryfallCardsByTagResponse>> GetCardsByTagAsync(
        string tagIdentity,
        string tagType,
        bool includeDescendants,
        string minimumWeight,
        string? cursor,
        int pageSize,
        string? responseFormat,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(responseFormat, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return OperationFailure.Forward<ScryfallCardFormat, ScryfallCardsByTagResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data, resultOptions);
        OperationResult<ScryfallCardsByTagResult> result = await cards.GetCardsByTagAsync(
            tagIdentity,
            tagType,
            includeDescendants,
            minimumWeight,
            cursor,
            pageSize,
            format.Data.IncludeRaw,
            formatter.CountItemsThatFit,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCardsByTagResult> success)
        {
            return OperationFailure.Forward<ScryfallCardsByTagResult, ScryfallCardsByTagResponse>(result);
        }

        ScryfallFormattedCards formatted = PageCards(success.Data.Page);
        ScryfallPage<ScryfallCardView> page = ToPage(formatted, success.Data.Page);
        return new OperationSuccess<ScryfallCardsByTagResponse>(
            new ScryfallCardsByTagResponse(success.Data.Tag, page, success.Data.Assignments, success.Data.GenerationId)
            {
                ResponseFormat = format.Data.Name,
                Limits = formatted.Limits,
                Truncated = formatted.Truncated ? true : null,
                Narrow = formatted.Truncated ? ScryfallFormattedResponse.NarrowHint : null,
            });
    }

    /// <summary>
    /// Takes the cards that the page builder formatted and cut with the item counter.
    /// </summary>
    private static ScryfallFormattedCards PageCards<T>(ScryfallPage<T> page)
    {
        return page.Formatted ?? throw new InvalidOperationException("The page was built without an item counter.");
    }

    /// <summary>
    /// Builds a page of card views with the counts and cursor of the page the cards came from.
    /// </summary>
    private static ScryfallPage<ScryfallCardView> ToPage(
        ScryfallFormattedCards formatted,
        ScryfallPage<ScryfallCard> source)
    {
        List<ScryfallCardView> items = [];
        foreach (ScryfallCardView? view in formatted.Views)
        {
            items.Add(view!.Value);
        }

        return new ScryfallPage<ScryfallCardView>(items, source.TotalCount, source.NextCursor);
    }
}
