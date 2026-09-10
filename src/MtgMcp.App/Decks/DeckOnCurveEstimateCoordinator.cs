using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.OnCurve;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Coordinates one saved-deck read, installed card-data read, and pure estimate.
/// </summary>
internal sealed class DeckOnCurveEstimateCoordinator
{
    /// <summary>
    /// Defines the stored mainboard zone name.
    /// </summary>
    private const string MainZone = "main";

    /// <summary>
    /// Stores the local deck read boundary.
    /// </summary>
    private readonly SqliteDeckStore deckStore;

    /// <summary>
    /// Resolves exact installed source facts without provider fallback.
    /// </summary>
    private readonly DeckOnCurveCardResolver cardResolver;

    /// <summary>
    /// Creates one coordinator around the existing deck and Scryfall owners.
    /// </summary>
    internal DeckOnCurveEstimateCoordinator(SqliteDeckStore deckStore, ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(deckStore);
        ArgumentNullException.ThrowIfNull(scryfall);
        this.deckStore = deckStore;
        cardResolver = new DeckOnCurveCardResolver(scryfall);
    }

    /// <summary>
    /// Builds one complete sampled estimate without changing the deck or local source data.
    /// </summary>
    internal async Task<OperationResult<DeckOnCurveEstimateResult>> EstimateAsync(
        DeckOnCurveEstimateToolRequest? request,
        CancellationToken cancellationToken)
    {
        if (!HasValidRequestIdentity(request))
        {
            return Invalid("The deck ID, expected revision, target entry ID, and landRules are required.");
        }

        if (request!.TurnLimit is < OnCurveRequestValidator.MinimumTurnLimit or > OnCurveRequestValidator.MaximumTurnLimit ||
            request.SampleCount is < OnCurveRequestValidator.MinimumSampleCount or > OnCurveRequestValidator.MaximumSampleCount ||
            request.Seed is not null && !OnCurveSeed.TryParse(request.Seed, out _))
        {
            return Invalid("The turn limit, sample count, or replay seed is outside the supported bounds.");
        }

        OperationResult<DeckDocument> deckResult = await deckStore.GetAsync(request.DeckId, cancellationToken)
            .ConfigureAwait(false);
        if (deckResult is not OperationSuccess<DeckDocument> deckSuccess)
        {
            return ForwardFailure<DeckDocument, DeckOnCurveEstimateResult>(deckResult);
        }

        DeckDocument deck = deckSuccess.Data;
        if (deck.Revision != request.ExpectedRevision)
        {
            return new OperationConflict(
                "deck-revision-conflict",
                "The local deck revision changed; load it again before running an on-curve estimate.");
        }

        OperationResult<DeckOnCurveDeckSelection> selectionResult = SelectMainboard(deck, request.TargetEntryId);
        if (selectionResult is not OperationSuccess<DeckOnCurveDeckSelection> selected)
        {
            return ForwardFailure<DeckOnCurveDeckSelection, DeckOnCurveEstimateResult>(selectionResult);
        }

        OperationResult<DeckOnCurveResolvedCards> sourceResult = await cardResolver.ResolveAsync(
            selected.Data.Mainboard,
            cancellationToken).ConfigureAwait(false);
        if (sourceResult is not OperationSuccess<DeckOnCurveResolvedCards> source)
        {
            return ForwardFailure<DeckOnCurveResolvedCards, DeckOnCurveEstimateResult>(sourceResult);
        }

        OperationResult<DeckOnCurvePreparedEstimate> preparation = DeckOnCurveEstimatePreparer.Prepare(
            deck,
            selected.Data,
            source.Data,
            request);
        if (preparation is not OperationSuccess<DeckOnCurvePreparedEstimate> prepared)
        {
            return ForwardFailure<DeckOnCurvePreparedEstimate, DeckOnCurveEstimateResult>(preparation);
        }

        OperationResult<OnCurveRunReport> calculation = OnCurveCalculator.Calculate(
            prepared.Data.Request,
            cancellationToken);
        return calculation switch
        {
            OperationSuccess<OnCurveRunReport> success => new OperationSuccess<DeckOnCurveEstimateResult>(
                DeckOnCurveEstimateResultMapper.Map(deck, prepared.Data, success.Data)),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Checks the request fields that must be present before reading local state.
    /// </summary>
    private static bool HasValidRequestIdentity(DeckOnCurveEstimateToolRequest? request)
    {
        return request is not null &&
            request.DeckId != Guid.Empty &&
            request.ExpectedRevision > 0 &&
            request.TargetEntryId != Guid.Empty &&
            request.LandRules is not null;
    }

    /// <summary>
    /// Selects the stored mainboard and proves the requested target belongs to it.
    /// </summary>
    private static OperationResult<DeckOnCurveDeckSelection> SelectMainboard(DeckDocument deck, Guid targetEntryId)
    {
        List<DeckEntry> mainboard = [];
        DeckEntry? target = null;
        foreach (DeckEntry entry in deck.Entries)
        {
            if (entry.EntryId == targetEntryId)
            {
                target = entry;
            }

            if (string.Equals(entry.Zone, MainZone, StringComparison.Ordinal))
            {
                mainboard.Add(entry);
            }
        }

        if (target is null)
        {
            return new OperationNotFound(
                "deck-entry-not-found",
                "The selected target entry was not found in the local deck.");
        }

        if (!string.Equals(target.Zone, MainZone, StringComparison.Ordinal))
        {
            return Invalid<DeckOnCurveDeckSelection>("The selected target entry must be in the deck mainboard.");
        }

        if (mainboard.Count is < 1 or > OnCurveRequestValidator.MaximumMainboardEntries)
        {
            return Invalid<DeckOnCurveDeckSelection>("The deck mainboard must contain 1 through 150 entries.");
        }

        return new OperationSuccess<DeckOnCurveDeckSelection>(new DeckOnCurveDeckSelection(mainboard, target));
    }

    /// <summary>
    /// Creates one stable invalid-input result for a malformed public request.
    /// </summary>
    private static OperationInvalidInput Invalid(string message)
    {
        return Invalid<DeckOnCurveEstimateResult>(message);
    }

    /// <summary>
    /// Creates one stable invalid-input result for a malformed intermediate request.
    /// </summary>
    private static OperationInvalidInput Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-on-curve-request", message);
    }

    /// <summary>
    /// Preserves each recognized operation failure while changing only the success payload type.
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
                "unexpected-on-curve-result",
                "The on-curve estimate returned an unexpected result."),
            _ => new OperationUnavailable(
                "unexpected-on-curve-result",
                "The on-curve estimate returned an unexpected result."),
        };
    }
}

/// <summary>
/// Carries the mainboard and selected target from one exact saved-deck revision.
/// </summary>
internal sealed record DeckOnCurveDeckSelection(
    IReadOnlyList<DeckEntry> Mainboard,
    DeckEntry Target);
