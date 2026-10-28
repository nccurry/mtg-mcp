using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Decides how many leading items of a card page fit the result size limit.
/// </summary>
/// <param name="cards">The cards of the page in order; null for a page item that carries no card.</param>
/// <param name="extraBytes">The serialized size of everything else each page item carries.</param>
/// <param name="generationId">The card-data generation the page was read from, or null for the active one.</param>
/// <param name="cancellationToken">Cancels the read of installed card data.</param>
/// <returns>The number of leading items to keep, at least one when the page is not empty.</returns>
internal delegate Task<int> ScryfallPageFit(
    IReadOnlyList<ScryfallCard?> cards,
    IReadOnlyList<int> extraBytes,
    Guid? generationId,
    CancellationToken cancellationToken);

/// <summary>
/// Measures serialized sizes the way tool results are written, so size limits match what a client receives.
/// </summary>
internal static class ScryfallJsonSize
{
    /// <summary>
    /// Mirrors the casing, null handling, and escaping of the server's result serializer.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Returns the UTF-8 size of one value written as JSON.
    /// </summary>
    internal static int Measure<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options).Length;
    }
}

/// <summary>
/// Shapes mapped cards for one response: the concise projection with facts from installed data, or the
/// mapped card unchanged.
/// </summary>
internal sealed class ScryfallCardFormatter
{
    /// <summary>
    /// Reads tag slugs and prices from installed card data.
    /// </summary>
    private readonly ScryfallCardDataStore store;

    /// <summary>
    /// Creates a formatter for one request.
    /// </summary>
    internal ScryfallCardFormatter(ScryfallCardDataStore store, ScryfallCardFormat format)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(format);
        this.store = store;
        Format = format;
    }

    /// <summary>
    /// Gets the response format this formatter applies.
    /// </summary>
    internal ScryfallCardFormat Format { get; }

    /// <summary>
    /// Gets a value showing that a page was cut to stay within the size limit.
    /// </summary>
    internal bool Truncated { get; private set; }

    /// <summary>
    /// Gets the note explaining which fields are null because card data is not installed, or null when none apply.
    /// </summary>
    internal string? NotInstalledNote { get; private set; }

    /// <summary>
    /// Gets the size check callers pass to page builders; null when the format is not size limited.
    /// </summary>
    internal ScryfallPageFit? Fit => Format.IsConcise ? FitAsync : null;

    /// <summary>
    /// Shapes cards in order; a null card stays null.
    /// </summary>
    internal async Task<IReadOnlyList<ScryfallCardView?>> FormatAsync(
        IReadOnlyList<ScryfallCard?> cards,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cards);
        List<ScryfallCardView?> views = new(cards.Count);
        if (!Format.IsConcise)
        {
            foreach (ScryfallCard? card in cards)
            {
                if (card is null)
                {
                    views.Add(null);
                    continue;
                }

                views.Add(new ScryfallCardView(card));
            }

            return views;
        }

        ScryfallCardExtrasRead extras = await ReadExtrasAsync(cards, generationId, cancellationToken)
            .ConfigureAwait(false);
        NotInstalledNote = !extras.Installed && Format.NeedsInstalledData
            ? NotInstalledMessage(Format)
            : null;
        foreach (ScryfallCard? card in cards)
        {
            if (card is null)
            {
                views.Add(null);
                continue;
            }

            ScryfallCardExtras? cardExtras = null;
            if (card.OracleId is Guid oracleId)
            {
                extras.ByOracleId.TryGetValue(oracleId, out cardExtras);
            }

            views.Add(new ScryfallCardView(
                ScryfallConciseProjection.Project(card, cardExtras, Format.SelectedFields!)));
        }

        return views;
    }

    /// <summary>
    /// Builds the note that names the fields left null when card data is not installed.
    /// </summary>
    internal static string NotInstalledMessage(ScryfallCardFormat format)
    {
        List<string> names = [];
        if (format.NeedsTags)
        {
            names.Add("tags");
        }

        if (format.NeedsPrice)
        {
            names.Add("cheapestPriceUsd");
        }

        return $"{string.Join(" and ", names)} {(names.Count == 1 ? "is" : "are")} null because Scryfall card " +
            "data is not installed. Run scryfall_corpus_sync to install it.";
    }

    /// <summary>
    /// Counts how many leading page items fit the result size limit, always keeping at least one.
    /// </summary>
    private async Task<int> FitAsync(
        IReadOnlyList<ScryfallCard?> cards,
        IReadOnlyList<int> extraBytes,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScryfallCardView?> views = await FormatAsync(cards, generationId, cancellationToken)
            .ConfigureAwait(false);
        int budget = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes;
        int total = 0;
        int keep = 0;
        for (int index = 0; index < views.Count; index++)
        {
            ScryfallCardView? view = views[index];
            int size = extraBytes[index] + 1 + (view is null ? 0 : ScryfallJsonSize.Measure(view.Value));
            if (keep > 0 && total + size > budget)
            {
                break;
            }

            total += size;
            keep++;
        }

        Truncated = keep < views.Count;
        return keep;
    }

    /// <summary>
    /// Reads installed-data facts for the Oracle IDs of the cards, or reports not installed when none are needed.
    /// </summary>
    private async Task<ScryfallCardExtrasRead> ReadExtrasAsync(
        IReadOnlyList<ScryfallCard?> cards,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        if (!Format.NeedsInstalledData)
        {
            return new ScryfallCardExtrasRead(true, new Dictionary<Guid, ScryfallCardExtras>());
        }

        HashSet<Guid> oracleIds = [];
        foreach (ScryfallCard? card in cards)
        {
            if (card?.OracleId is Guid oracleId)
            {
                oracleIds.Add(oracleId);
            }
        }

        return await store.ReadCardExtrasAsync(
            generationId,
            oracleIds,
            Format.NeedsTags,
            Format.NeedsPrice,
            cancellationToken).ConfigureAwait(false);
    }
}
