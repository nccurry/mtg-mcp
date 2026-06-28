namespace MtgMcp.Core;

/// <summary>
/// Evaluates card prices from normalized catalog metadata.
/// </summary>
public interface IPriceSource
{
    /// <summary>
    /// Gets the stable source label used in provenance and diagnostics.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Evaluates a cached card snapshot against a deterministic reference date.
    /// </summary>
    CardPriceEvaluation Evaluate(CardSnapshot? snapshot, DateOnly referenceDate);

    /// <summary>
    /// Evaluates catalog card details against a deterministic reference date.
    /// </summary>
    CardPriceEvaluation Evaluate(CardInfo card, DateOnly referenceDate);

    /// <summary>
    /// Selects the best printing under the configured deterministic pricing policy.
    /// </summary>
    CardPriceEvaluator.CardPrintingSelection SelectPrinting(
        CardInfo canonical,
        IReadOnlyList<CardInfo> printings,
        DateOnly referenceDate,
        CardPrintingSelectionOptions options);
}

/// <summary>
/// Default price source for Scryfall-shaped catalog price fields.
/// </summary>
public sealed class CatalogPriceSource : IPriceSource
{
    /// <summary>
    /// Gets a shared stateless catalog price source.
    /// </summary>
    public static readonly CatalogPriceSource Instance = new();

    /// <summary>
    /// Creates a catalog price source.
    /// </summary>
    public CatalogPriceSource()
    {
    }

    /// <summary>
    /// Gets the normalized catalog source label.
    /// </summary>
    public string SourceName => "catalog";

    /// <summary>
    /// Evaluates a cached snapshot through the default card price evaluator.
    /// </summary>
    public CardPriceEvaluation Evaluate(CardSnapshot? snapshot, DateOnly referenceDate)
    {
        return snapshot is null
            ? MissingPrice("missing-snapshot", "No cached card snapshot was available.")
            : CardPriceEvaluator.Evaluate(snapshot, referenceDate);
    }

    /// <summary>
    /// Evaluates catalog card details through the default card price evaluator.
    /// </summary>
    public CardPriceEvaluation Evaluate(CardInfo card, DateOnly referenceDate)
    {
        return CardPriceEvaluator.Evaluate(card, referenceDate);
    }

    /// <summary>
    /// Selects a printing through the default deterministic pricing policy.
    /// </summary>
    public CardPriceEvaluator.CardPrintingSelection SelectPrinting(
        CardInfo canonical,
        IReadOnlyList<CardInfo> printings,
        DateOnly referenceDate,
        CardPrintingSelectionOptions options)
    {
        return CardPriceEvaluator.SelectPrinting(canonical, printings, referenceDate, options);
    }

    /// <summary>
    /// Builds an unknown price evaluation with a deterministic reason.
    /// </summary>
    private static CardPriceEvaluation MissingPrice(string printingStatus, string reason)
    {
        return new CardPriceEvaluation
        {
            PriceKnown = false,
            PrintingStatus = printingStatus,
            SelectedPrintingReason = reason
        };
    }
}
