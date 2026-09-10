using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Reads exact installed Scryfall card facts for one saved deck without provider fallback.
/// </summary>
internal sealed class DeckOnCurveCardResolver
{
    /// <summary>
    /// Stores the only source-card lookup boundary used by this estimate.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates a resolver around the shared Scryfall service.
    /// </summary>
    internal DeckOnCurveCardResolver(ScryfallService scryfall)
    {
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Resolves every supplied mainboard printing from installed card data only.
    /// </summary>
    internal async Task<OperationResult<DeckOnCurveResolvedCards>> ResolveAsync(
        IReadOnlyList<DeckEntry>? entries,
        CancellationToken cancellationToken)
    {
        if (entries is null || entries.Count is < 1 or > 150)
        {
            return Invalid("The deck mainboard must contain 1 through 150 entries.");
        }

        List<ScryfallEvidenceLookup> lookups = [];
        foreach (DeckEntry entry in entries)
        {
            if (entry.PrintingId is not Guid printingId || printingId == Guid.Empty)
            {
                return Invalid("Every mainboard entry must name one exact Scryfall printing ID.");
            }

            lookups.Add(new ScryfallEvidenceLookup(
                new ScryfallCardLookup("scryfall-id", printingId.ToString("D"))));
        }

        OperationResult<ScryfallExactCollectionEvidence> evidenceResult = await ScryfallToolExecution.RunAsync(
            () => scryfall.ResolveExactCollectionAsync(lookups, "cache-only", cancellationToken)).ConfigureAwait(false);
        if (evidenceResult is not OperationSuccess<ScryfallExactCollectionEvidence> evidenceSuccess)
        {
            return ForwardFailure<ScryfallExactCollectionEvidence, DeckOnCurveResolvedCards>(evidenceResult);
        }

        ScryfallExactCollectionEvidence evidence = evidenceSuccess.Data;
        if (evidence.Binding.CorpusGenerationId is not Guid || evidence.Binding.Snapshot is not null ||
            evidence.Rows.Count != entries.Count)
        {
            return Unavailable();
        }

        List<DeckOnCurveResolvedCard> cards = [];
        for (int index = 0; index < entries.Count; index++)
        {
            DeckEntry entry = entries[index];
            ScryfallCollectionRow row = evidence.Rows[index];
            if (row.Index != index ||
                !string.Equals(row.Status, "found", StringComparison.Ordinal) ||
                !string.Equals(row.Origin, "corpus", StringComparison.Ordinal) ||
                row.Card is null ||
                row.Card.Id != entry.PrintingId)
            {
                return Unavailable();
            }

            cards.Add(new DeckOnCurveResolvedCard(entry, row.Card));
        }

        return new OperationSuccess<DeckOnCurveResolvedCards>(
            new DeckOnCurveResolvedCards(cards, evidence.Binding));
    }

    /// <summary>
    /// Maps a malformed local mainboard reference to one stable input failure.
    /// </summary>
    private static OperationInvalidInput Invalid(string message)
    {
        return new OperationInvalidInput("invalid-on-curve-mainboard", message);
    }

    /// <summary>
    /// States that the exact installed source fact needed for the estimate is absent or unusable.
    /// </summary>
    private static OperationUnavailable Unavailable()
    {
        return new OperationUnavailable(
            "on-curve-card-fact-unavailable",
            "The exact installed Scryfall fact required for this estimate is unavailable.");
    }

    /// <summary>
    /// Preserves every recognized operation failure without changing its reason code.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable(
                "unexpected-on-curve-source-result",
                "The Scryfall source lookup returned an unexpected result."),
            _ => new OperationUnavailable(
                "unexpected-on-curve-source-result",
                "The Scryfall source lookup returned an unexpected result."),
        };
    }
}

/// <summary>
/// Couples one local mainboard entry to its exact installed Scryfall card fact.
/// </summary>
internal sealed record DeckOnCurveResolvedCard(DeckEntry Entry, ScryfallCard Card);

/// <summary>
/// Carries one ordered resolved mainboard and the source binding used to read it.
/// </summary>
internal sealed record DeckOnCurveResolvedCards(
    IReadOnlyList<DeckOnCurveResolvedCard> Cards,
    ScryfallCollectionEvidenceBinding Binding);
