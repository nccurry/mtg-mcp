using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Shapes mapped cards for one response: the concise projection with facts from installed data, or the
/// mapped card unchanged. It holds no state between calls.
/// </summary>
internal sealed class ScryfallCardFormatter
{
    /// <summary>
    /// Says that cards whose faces are separate cards are looked up by their front face.
    /// </summary>
    private const string ReversibleCardMessage =
        "Some cards have no Oracle ID of their own because each face is a separate card. For them, oracleId and " +
        "tags describe the front face, and cheapestPriceUsd is null.";

    /// <summary>
    /// Reads tag slugs and prices from installed card data.
    /// </summary>
    private readonly ScryfallCardDataStore store;

    /// <summary>
    /// Writes values the way tool results are written, so measured sizes match what a client receives.
    /// </summary>
    private readonly JsonSerializerOptions resultOptions;

    /// <summary>
    /// Creates a formatter for one request.
    /// </summary>
    internal ScryfallCardFormatter(
        ScryfallCardDataStore store,
        ScryfallCardFormat format,
        JsonSerializerOptions resultOptions)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(resultOptions);
        this.store = store;
        this.resultOptions = resultOptions;
        Format = format;
    }

    /// <summary>
    /// Gets the response format this formatter applies.
    /// </summary>
    internal ScryfallCardFormat Format { get; }

    /// <summary>
    /// Gets the item counter that page builders call. For concise cards it cuts the page to the size limit; for the
    /// other formats it keeps every item.
    /// </summary>
    internal ScryfallPageItemCounter CountItemsThatFit => CountAsync;

    /// <summary>
    /// Builds the note that names the fields left null when card data is not installed.
    /// </summary>
    internal static string NotInstalledMessage(ScryfallCardFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
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
            "data is not installed. Run the card-data sync tool to install it.";
    }

    /// <summary>
    /// Shapes cards in order; a null card stays null.
    /// </summary>
    internal async Task<ScryfallFormattedCards> FormatAsync(
        IReadOnlyList<ScryfallCard?> cards,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cards);
        (List<ScryfallCardView?> views, ScryfallInstalledCardFactsRead facts) = await ShapeAsync(
            cards,
            generationId,
            cancellationToken).ConfigureAwait(false);
        return new ScryfallFormattedCards(views, false, Limits(cards, facts));
    }

    /// <summary>
    /// Shapes the cards and keeps the installed-data facts they were shaped with.
    /// </summary>
    private async Task<(List<ScryfallCardView?> Views, ScryfallInstalledCardFactsRead Facts)> ShapeAsync(
        IReadOnlyList<ScryfallCard?> cards,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
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

            return (views, ScryfallInstalledCardFactsRead.NotRequested);
        }

        ScryfallInstalledCardFactsRead facts = await ReadFactsAsync(cards, generationId, cancellationToken)
            .ConfigureAwait(false);
        foreach (ScryfallCard? card in cards)
        {
            if (card is null)
            {
                views.Add(null);
                continue;
            }

            views.Add(new ScryfallCardView(
                ScryfallConciseProjection.Project(card, facts.FactsFor(card), Format.SelectedFields!)));
        }

        return (views, facts);
    }

    /// <summary>
    /// Shapes a page and, for concise cards, keeps the leading items that fit the result size limit, always at
    /// least one. The limit notes describe only the kept cards.
    /// </summary>
    private async Task<ScryfallFormattedCards> CountAsync(
        IReadOnlyList<ScryfallCard?> cards,
        IReadOnlyList<object?> companions,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        (List<ScryfallCardView?> views, ScryfallInstalledCardFactsRead facts) = await ShapeAsync(
            cards,
            generationId,
            cancellationToken).ConfigureAwait(false);
        int keep = views.Count;
        if (Format.IsConcise)
        {
            List<int> sizes = new(views.Count);
            for (int index = 0; index < views.Count; index++)
            {
                int size = 1 + Measure(companions[index]);
                if (views[index] is { } view)
                {
                    size += Measure(view);
                }

                sizes.Add(size);
            }

            keep = ResultSizeLimit.CountFitting(sizes);
        }

        return new ScryfallFormattedCards(
            [.. views.Take(keep)],
            keep < views.Count,
            Limits([.. cards.Take(keep)], facts));
    }

    /// <summary>
    /// Measures the UTF-8 size of a value written with the result serializer options.
    /// </summary>
    private int Measure(object? value)
    {
        return value is null ? 0 : JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), resultOptions).Length;
    }

    /// <summary>
    /// Reads installed-data facts for the cards, or says nothing was needed.
    /// </summary>
    private async Task<ScryfallInstalledCardFactsRead> ReadFactsAsync(
        IReadOnlyList<ScryfallCard?> cards,
        Guid? generationId,
        CancellationToken cancellationToken)
    {
        if (!Format.NeedsInstalledData)
        {
            return ScryfallInstalledCardFactsRead.NotRequested;
        }

        HashSet<Guid> oracleIds = [];
        foreach (ScryfallCard? card in cards)
        {
            if (card is not null && ScryfallConciseFieldTable.EffectiveOracleId(card) is Guid oracleId)
            {
                oracleIds.Add(oracleId);
            }
        }

        return await store.ReadInstalledCardFactsAsync(
            generationId,
            oracleIds,
            Format.NeedsTags,
            Format.NeedsPrice,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the reasons some requested fields are empty for the given cards, or null when there are none.
    /// </summary>
    private List<string>? Limits(IReadOnlyList<ScryfallCard?> cards, ScryfallInstalledCardFactsRead facts)
    {
        List<string> limits = [];
        if (!facts.Installed)
        {
            limits.Add(NotInstalledMessage(Format));
        }

        bool usesFrontFace = cards.Any(
            card => card is not null && ScryfallConciseFieldTable.UsesFrontFaceOracleId(card));
        if (Format.NeedsInstalledData && usesFrontFace)
        {
            limits.Add(ReversibleCardMessage);
        }

        return limits.Count == 0 ? null : limits;
    }
}

/// <summary>
/// Carries formatted cards, whether the page was cut to fit the size limit, and the reasons some fields are empty.
/// </summary>
/// <param name="Views">The formatted cards in order; null where the page item has no card.</param>
/// <param name="Truncated">Whether leading items were kept and the rest left out.</param>
/// <param name="Limits">The reasons some requested fields are empty, or null.</param>
internal sealed record ScryfallFormattedCards(
    IReadOnlyList<ScryfallCardView?> Views,
    bool Truncated,
    IReadOnlyList<string>? Limits);

/// <summary>
/// Shapes the cards of a page and decides how many leading items fit the result size limit.
/// </summary>
/// <param name="cards">The cards of the page in order; null for a page item that carries no card.</param>
/// <param name="companions">
/// The other content each page item carries, written when measuring size; null where an item has none.
/// </param>
/// <param name="generationId">The card-data generation the page was read from, or null for the active one.</param>
/// <param name="cancellationToken">Cancels the read of installed card data.</param>
/// <returns>The kept cards; their count is the number of leading items to keep.</returns>
internal delegate Task<ScryfallFormattedCards> ScryfallPageItemCounter(
    IReadOnlyList<ScryfallCard?> cards,
    IReadOnlyList<object?> companions,
    Guid? generationId,
    CancellationToken cancellationToken);
