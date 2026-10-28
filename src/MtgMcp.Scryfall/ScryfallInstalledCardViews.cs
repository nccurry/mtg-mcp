using System.Globalization;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Names the outcomes of looking up one card in installed card data.
/// </summary>
public static class ScryfallInstalledCardStatus
{
    /// <summary>
    /// The card was found in installed card data.
    /// </summary>
    public const string Installed = "installed";

    /// <summary>
    /// No complete card data is installed, so no card was looked up.
    /// </summary>
    public const string NotInstalled = "not-installed";

    /// <summary>
    /// The request carried no exact card identity, or the identity is not in the installed card data.
    /// </summary>
    public const string Unresolved = "unresolved";
}

/// <summary>
/// Carries what installed card data says about one requested card.
/// </summary>
/// <param name="Status">One of the <see cref="ScryfallInstalledCardStatus"/> values.</param>
/// <param name="Card">The concise card, when cards were requested and the card was found.</param>
/// <param name="Tags">The oracle tag slugs, when tags were requested and the card was found.</param>
public sealed record ScryfallInstalledCardView(
    string Status,
    ScryfallConciseCard? Card,
    IReadOnlyList<string>? Tags);

/// <summary>
/// Carries installed-data views for a list of requested cards, in request order.
/// </summary>
/// <param name="Installed">Whether complete card data is installed.</param>
/// <param name="Cards">One view for each requested card.</param>
public sealed record ScryfallInstalledCardViews(
    bool Installed,
    IReadOnlyList<ScryfallInstalledCardView> Cards);

/// <summary>
/// Reads concise cards and tag slugs for a list of exact card identities from installed card data only.
/// </summary>
internal sealed class ScryfallInstalledCardViewOperations
{
    /// <summary>
    /// Reads installed cards, tags, and prices.
    /// </summary>
    private readonly ScryfallCardDataStore store;

    /// <summary>
    /// Creates view operations around the installed card-data store.
    /// </summary>
    internal ScryfallInstalledCardViewOperations(ScryfallCardDataStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>
    /// Reads one view for each lookup; a null lookup stands for a card with no exact identity.
    /// </summary>
    internal async Task<OperationResult<ScryfallInstalledCardViews>> ReadAsync(
        IReadOnlyList<ScryfallCardLookup?> lookups,
        IReadOnlyList<string>? fields,
        bool includeCards,
        bool includeTags,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookups);
        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(ScryfallCardFormat.Concise, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return ScryfallCardEvidenceOperations
                .ForwardFailure<ScryfallCardFormat, ScryfallInstalledCardViews>(parsed);
        }

        HashSet<string> selected = [.. format.Data.SelectedFields!];
        if (fields is null)
        {
            selected.Remove("tags");
        }

        Guid? generationId = await store.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
        if (generationId is null ||
            !await store.ContainsCompleteGenerationAsync(generationId.Value, cancellationToken).ConfigureAwait(false))
        {
            return new OperationSuccess<ScryfallInstalledCardViews>(new ScryfallInstalledCardViews(
                false,
                [.. lookups.Select(_ => new ScryfallInstalledCardView(
                    ScryfallInstalledCardStatus.NotInstalled,
                    null,
                    null))]));
        }

        List<ScryfallEvidenceLookup> requests = [];
        List<int> requestIndexes = [];
        for (int index = 0; index < lookups.Count; index++)
        {
            if (lookups[index] is { } lookup)
            {
                requests.Add(new ScryfallEvidenceLookup(lookup));
                requestIndexes.Add(index);
            }
        }

        List<StoredCardDataObject?> found = await store.FindCardsInGenerationAsync(
            requests,
            generationId.Value,
            cancellationToken).ConfigureAwait(false);
        ScryfallCard?[] mapped = new ScryfallCard?[lookups.Count];
        for (int position = 0; position < found.Count; position++)
        {
            if (found[position] is { } stored)
            {
                mapped[requestIndexes[position]] = MapStoredCard(stored);
            }
        }

        bool needsTags = includeTags || selected.Contains("tags");
        bool needsPrice = includeCards && selected.Contains("cheapestPriceUsd");
        ScryfallCardExtrasRead extras = needsTags || needsPrice
            ? await store.ReadCardExtrasAsync(
                generationId.Value,
                [.. mapped.Select(card => card?.OracleId).OfType<Guid>().Distinct()],
                needsTags,
                needsPrice,
                cancellationToken).ConfigureAwait(false)
            : ScryfallCardExtrasRead.NotInstalled;
        List<ScryfallInstalledCardView> views = new(lookups.Count);
        foreach (ScryfallCard? card in mapped)
        {
            views.Add(BuildView(card, extras, selected, includeCards, includeTags));
        }

        return new OperationSuccess<ScryfallInstalledCardViews>(new ScryfallInstalledCardViews(true, views));
    }

    /// <summary>
    /// Builds the view for one found or missing card.
    /// </summary>
    private static ScryfallInstalledCardView BuildView(
        ScryfallCard? card,
        ScryfallCardExtrasRead extras,
        IReadOnlySet<string> selected,
        bool includeCards,
        bool includeTags)
    {
        if (card is null)
        {
            return new ScryfallInstalledCardView(ScryfallInstalledCardStatus.Unresolved, null, null);
        }

        ScryfallCardExtras? cardExtras = null;
        if (card.OracleId is Guid oracleId)
        {
            extras.ByOracleId.TryGetValue(oracleId, out cardExtras);
        }

        return new ScryfallInstalledCardView(
            ScryfallInstalledCardStatus.Installed,
            includeCards ? ScryfallConciseProjection.Project(card, cardExtras, selected) : null,
            includeTags ? cardExtras?.Tags ?? [] : null);
    }

    /// <summary>
    /// Maps one installed card without tag evidence, which the concise projection reads separately.
    /// </summary>
    private static ScryfallCard MapStoredCard(StoredCardDataObject stored)
    {
        using JsonDocument document = JsonDocument.Parse(stored.RawJson);
        return ScryfallMapper.Card(
            document.RootElement,
            stored.RetrievedAtUtc,
            stored.GenerationId.ToString("D", CultureInfo.InvariantCulture),
            tagCoverage: "not-included",
            includeRaw: false);
    }
}
