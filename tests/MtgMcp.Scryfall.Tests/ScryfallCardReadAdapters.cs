using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Lets service tests that check card evidence (origin, tags, raw objects, cursors, snapshots) keep reading plain
/// cards while they call the service's response methods in the standard and full formats.
/// </summary>
internal static class ScryfallCardReadAdapters
{
    /// <summary>
    /// Searches in the standard format, or the full format when raw objects are wanted, and returns plain cards.
    /// </summary>
    internal static async Task<OperationResult<ScryfallSearchResult>> ReadSearchAsync(
        this ScryfallService service,
        string query,
        string unique = "cards",
        string order = "name",
        string direction = "auto",
        bool includeExtras = false,
        bool includeMultilingual = false,
        bool includeVariations = false,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ScryfallSearchResponse> result = await service.SearchAsync(
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
            Format(includeRaw),
            null,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallSearchResponse> success)
        {
            return Forward<ScryfallSearchResponse, ScryfallSearchResult>(result);
        }

        return new OperationSuccess<ScryfallSearchResult>(new ScryfallSearchResult(
            Cards(success.Data.Page),
            success.Data.Snapshot,
            success.Data.Warnings));
    }

    /// <summary>
    /// Gets one card in the standard format, or the full format when raw objects are wanted.
    /// </summary>
    internal static async Task<OperationResult<ScryfallCardResult>> ReadCardAsync(
        this ScryfallService service,
        ScryfallCardLookup lookup,
        string freshnessPolicy = "default",
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ScryfallCardResponse> result = await service.GetCardAsync(
            lookup,
            freshnessPolicy,
            Format(includeRaw),
            null,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCardResponse> success)
        {
            return Forward<ScryfallCardResponse, ScryfallCardResult>(result);
        }

        return new OperationSuccess<ScryfallCardResult>(new ScryfallCardResult(
            Card(success.Data.Card),
            success.Data.Origin,
            success.Data.Snapshot,
            success.Data.CorpusGenerationId));
    }

    /// <summary>
    /// Resolves a collection in the standard format, or the full format when raw objects are wanted.
    /// </summary>
    internal static async Task<OperationResult<ScryfallCollectionResult>> ReadCollectionAsync(
        this ScryfallService service,
        IReadOnlyList<ScryfallCardLookup>? lookups,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ScryfallCollectionResponse> result = await service.GetCollectionAsync(
            lookups,
            freshnessPolicy,
            cursor,
            pageSize,
            Format(includeRaw),
            null,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCollectionResponse> success)
        {
            return Forward<ScryfallCollectionResponse, ScryfallCollectionResult>(result);
        }

        List<ScryfallCollectionRow> rows = [];
        foreach (ScryfallCollectionRowResponse row in success.Data.Page.Items)
        {
            ScryfallCard? card = row.Card is { } view ? Card(view) : null;
            rows.Add(new ScryfallCollectionRow(row.Index, row.Lookup, row.Status, row.Origin, card, row.Message));
        }

        return new OperationSuccess<ScryfallCollectionResult>(new ScryfallCollectionResult(
            new ScryfallPage<ScryfallCollectionRow>(rows, success.Data.Page.TotalCount, success.Data.Page.NextCursor),
            success.Data.Snapshot,
            success.Data.CorpusGenerationId));
    }

    /// <summary>
    /// Reads cards of one tag in the standard format, or the full format when raw objects are wanted.
    /// </summary>
    internal static async Task<OperationResult<ScryfallCardsByTagResult>> ReadCardsByTagAsync(
        this ScryfallService service,
        string tagIdentity,
        string tagType,
        bool includeDescendants = false,
        string minimumWeight = "weak",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ScryfallCardsByTagResponse> result = await service.GetCardsByTagAsync(
            tagIdentity,
            tagType,
            includeDescendants,
            minimumWeight,
            cursor,
            pageSize,
            Format(includeRaw),
            null,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<ScryfallCardsByTagResponse> success)
        {
            return Forward<ScryfallCardsByTagResponse, ScryfallCardsByTagResult>(result);
        }

        return new OperationSuccess<ScryfallCardsByTagResult>(new ScryfallCardsByTagResult(
            success.Data.Tag,
            Cards(success.Data.Page),
            success.Data.Assignments,
            success.Data.GenerationId));
    }

    /// <summary>
    /// Names the response format that corresponds to the old raw option.
    /// </summary>
    private static string Format(bool includeRaw)
    {
        return includeRaw ? "full" : "standard";
    }

    /// <summary>
    /// Reads the plain card out of a standard or full card view.
    /// </summary>
    private static ScryfallCard Card(ScryfallCardView view)
    {
        return Assert.IsType<ScryfallCard>(view.Value);
    }

    /// <summary>
    /// Reads the plain cards out of a page of standard or full card views.
    /// </summary>
    private static ScryfallPage<ScryfallCard> Cards(ScryfallPage<ScryfallCardView> page)
    {
        return new ScryfallPage<ScryfallCard>([.. page.Items.Select(Card)], page.TotalCount, page.NextCursor);
    }

    /// <summary>
    /// Passes a failure through to a result of another payload type.
    /// </summary>
    private static OperationResult<TTarget> Forward<TSource, TTarget>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationSuccess<TSource> => throw new InvalidOperationException("A success cannot be forwarded."),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}
