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
    /// Gets the response format the cards were written in: concise, standard, or full.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    [JsonPropertyOrder(100)]
    public string ResponseFormat { get; init; } = ScryfallCardFormat.Standard;

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
    /// Creates response operations around the card evidence operations.
    /// </summary>
    internal ScryfallCardResponseOperations(ScryfallCardEvidenceOperations cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        this.cards = cards;
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
            return Forward<ScryfallCardFormat, ScryfallSearchResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data);
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
            formatter.Fit,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallSearchResult> success)
        {
            return Forward<ScryfallSearchResult, ScryfallSearchResponse>(result);
        }

        ScryfallPage<ScryfallCardView> page = await FormatPageAsync(
            formatter,
            success.Data.Page,
            null,
            cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallSearchResponse>(
            new ScryfallSearchResponse(page, success.Data.Snapshot, success.Data.Warnings)
            {
                ResponseFormat = format.Data.Name,
                Limits = Limits(formatter),
                Truncated = formatter.Truncated ? true : null,
                Narrow = formatter.Truncated ? ResultSizeLimit.CardNarrowHint : null,
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
            return Forward<ScryfallCardFormat, ScryfallCardResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data);
        OperationResult<ScryfallCardResult> result = await cards.GetCardAsync(
            lookup,
            freshnessPolicy,
            format.Data.IncludeRaw,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCardResult> success)
        {
            return Forward<ScryfallCardResult, ScryfallCardResponse>(result);
        }

        IReadOnlyList<ScryfallCardView?> views = await formatter.FormatAsync(
            [success.Data.Card],
            success.Data.CorpusGenerationId,
            cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallCardResponse>(
            new ScryfallCardResponse(
                views[0]!.Value,
                success.Data.Origin,
                success.Data.Snapshot,
                success.Data.CorpusGenerationId)
            {
                ResponseFormat = format.Data.Name,
                Limits = Limits(formatter),
            });
    }

    /// <summary>
    /// Resolves a card collection and writes each row's card in the requested format.
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
        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(responseFormat, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return Forward<ScryfallCardFormat, ScryfallCollectionResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data);
        OperationResult<ScryfallCollectionResult> result = await cards.GetCollectionAsync(
            lookups,
            freshnessPolicy,
            cursor,
            pageSize,
            format.Data.IncludeRaw,
            formatter.Fit,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCollectionResult> success)
        {
            return Forward<ScryfallCollectionResult, ScryfallCollectionResponse>(result);
        }

        IReadOnlyList<ScryfallCollectionRow> rows = success.Data.Page.Items;
        IReadOnlyList<ScryfallCardView?> views = await formatter.FormatAsync(
            [.. rows.Select(row => row.Card)],
            success.Data.CorpusGenerationId,
            cancellationToken).ConfigureAwait(false);
        List<ScryfallCollectionRowResponse> items = new(rows.Count);
        for (int index = 0; index < rows.Count; index++)
        {
            ScryfallCollectionRow row = rows[index];
            items.Add(new ScryfallCollectionRowResponse(
                row.Index,
                row.Lookup,
                row.Status,
                row.Origin,
                views[index],
                row.Message));
        }

        return new OperationSuccess<ScryfallCollectionResponse>(
            new ScryfallCollectionResponse(
                new ScryfallPage<ScryfallCollectionRowResponse>(
                    items,
                    success.Data.Page.TotalCount,
                    success.Data.Page.NextCursor),
                success.Data.Snapshot,
                success.Data.CorpusGenerationId)
            {
                ResponseFormat = format.Data.Name,
                Limits = Limits(formatter),
                Truncated = formatter.Truncated ? true : null,
                Narrow = formatter.Truncated ? ResultSizeLimit.CardNarrowHint : null,
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
            return Forward<ScryfallCardFormat, ScryfallCardsByTagResponse>(parsed);
        }

        ScryfallCardFormatter formatter = new(cards.CardDataStore, format.Data);
        OperationResult<ScryfallCardsByTagResult> result = await cards.GetCardsByTagAsync(
            tagIdentity,
            tagType,
            includeDescendants,
            minimumWeight,
            cursor,
            pageSize,
            format.Data.IncludeRaw,
            formatter.Fit,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCardsByTagResult> success)
        {
            return Forward<ScryfallCardsByTagResult, ScryfallCardsByTagResponse>(result);
        }

        ScryfallPage<ScryfallCardView> page = await FormatPageAsync(
            formatter,
            success.Data.Page,
            success.Data.GenerationId,
            cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallCardsByTagResponse>(
            new ScryfallCardsByTagResponse(success.Data.Tag, page, success.Data.Assignments, success.Data.GenerationId)
            {
                ResponseFormat = format.Data.Name,
                Limits = Limits(formatter),
                Truncated = formatter.Truncated ? true : null,
                Narrow = formatter.Truncated ? ResultSizeLimit.CardNarrowHint : null,
            });
    }

    /// <summary>
    /// Passes a failure through to a result of another payload type.
    /// </summary>
    private static OperationResult<TTarget> Forward<TSource, TTarget>(OperationResult<TSource> result)
    {
        return ScryfallCardEvidenceOperations.ForwardFailure<TSource, TTarget>(result);
    }

    /// <summary>
    /// Writes every card of a page in the formatter's format, keeping the page's counts and cursor.
    /// </summary>
    private static async Task<ScryfallPage<ScryfallCardView>> FormatPageAsync(
        ScryfallCardFormatter formatter,
        ScryfallPage<ScryfallCard> page,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScryfallCardView?> views = await formatter.FormatAsync(
            [.. page.Items],
            generationId,
            cancellationToken).ConfigureAwait(false);
        List<ScryfallCardView> items = [];
        foreach (ScryfallCardView? view in views)
        {
            items.Add(view!.Value);
        }

        return new ScryfallPage<ScryfallCardView>(items, page.TotalCount, page.NextCursor);
    }

    /// <summary>
    /// Lists the limit notes for a response, or null when none apply.
    /// </summary>
    private static IReadOnlyList<string>? Limits(ScryfallCardFormatter formatter)
    {
        return formatter.NotInstalledNote is { } note ? [note] : null;
    }
}
