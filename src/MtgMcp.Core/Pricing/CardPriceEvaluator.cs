namespace MtgMcp.Core;

/// <summary>
/// Selects how aggressively mtg-mcp replaces catalog snapshots with budget-relevant printings.
/// </summary>
public enum PricingMode
{
    /// <summary>
    /// Keeps a usable released named-card result and only searches prints for missing, future, or non-paper snapshots.
    /// </summary>
    ReleasedIfNeeded,

    /// <summary>
    /// Chooses the cheapest released paper printing from any supported price field.
    /// </summary>
    CheapestReleasedPaper,

    /// <summary>
    /// Chooses a practical budget printing using legal, released, English-preferred, non-foil USD prices by default.
    /// </summary>
    BudgetPlayable,
}

/// <summary>
/// Controls deterministic printing selection for budget-sensitive card metadata.
/// </summary>
public sealed class CardPrintingSelectionOptions
{
    /// <summary>
    /// Gets or sets the pricing mode.
    /// </summary>
    public PricingMode PricingMode { get; set; } = PricingMode.ReleasedIfNeeded;

    /// <summary>
    /// Gets or sets the format whose legality should be honored when legality data is available.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets whether budget-playable mode may use foil, etched, or market fallback prices.
    /// </summary>
    public bool AllowAnyFinish { get; set; }
}

/// <summary>
/// Applies mtg-mcp's deterministic policy for budget prices and selected printings.
/// </summary>
public static class CardPriceEvaluator
{
    /// <summary>
    /// Lists price fields from highest confidence to lowest confidence.
    /// </summary>
    private static readonly IReadOnlyList<PriceField> PriceFieldOrder =
    [
        new PriceField("usd", LowConfidence: false),
        new PriceField("usd_foil", LowConfidence: true),
        new PriceField("usd_etched", LowConfidence: true),
        new PriceField("tcgplayer", LowConfidence: true),
        new PriceField("tcgplayer_price", LowConfidence: true),
    ];

    /// <summary>
    /// Evaluates a cached card snapshot against a deterministic reference date.
    /// </summary>
    public static CardPriceEvaluation Evaluate(CardSnapshot snapshot, DateOnly referenceDate)
    {
        return Evaluate(
            snapshot.ReleasedAt,
            snapshot.Prices,
            snapshot.Games,
            snapshot.Language,
            referenceDate);
    }

    /// <summary>
    /// Evaluates catalog card details against a deterministic reference date.
    /// </summary>
    public static CardPriceEvaluation Evaluate(CardInfo card, DateOnly referenceDate)
    {
        return Evaluate(
            card.ReleasedAt,
            card.Prices,
            card.Games,
            card.Language,
            referenceDate);
    }

    /// <summary>
    /// Selects the best released paper printing for budget-sensitive snapshots.
    /// </summary>
    public static CardPrintingSelection SelectPrinting(
        CardInfo canonical,
        IReadOnlyList<CardInfo> printings,
        DateOnly referenceDate)
    {
        return SelectPrinting(
            canonical,
            printings,
            referenceDate,
            new CardPrintingSelectionOptions());
    }

    /// <summary>
    /// Selects the best released paper printing using the supplied pricing policy.
    /// </summary>
    public static CardPrintingSelection SelectPrinting(
        CardInfo canonical,
        IReadOnlyList<CardInfo> printings,
        DateOnly referenceDate,
        CardPrintingSelectionOptions options)
    {
        List<CardInfo> candidates = [];
        AddCandidate(candidates, canonical);
        foreach (CardInfo printing in printings)
        {
            AddCandidate(candidates, printing);
        }

        List<PricedPrintingCandidate> pricedCandidates = [];
        foreach (CardInfo candidate in candidates)
        {
            PricedPrintingCandidate? priced = BuildPricedCandidate(candidate, referenceDate, options);
            if (priced is not null)
            {
                pricedCandidates.Add(priced);
            }
        }

        if (pricedCandidates.Count == 0)
        {
            CardPriceEvaluation evaluation = options.PricingMode == PricingMode.ReleasedIfNeeded
                ? Evaluate(canonical, referenceDate)
                : MissingPrice(
                    "no-matching-priced-printing",
                    $"No released paper printing matched {options.PricingMode} with a usable positive price.");
            if (!evaluation.PriceKnown)
            {
                evaluation.SelectedPrintingReason =
                    "No released paper printing matching the pricing mode had a usable positive price. "
                        + evaluation.SelectedPrintingReason;
            }

            return new CardPrintingSelection(canonical, evaluation, ChangedPrinting: false);
        }

        bool hasEnglish = pricedCandidates.Any(candidate => IsEnglish(candidate.Card));
        if (hasEnglish)
        {
            pricedCandidates = pricedCandidates
                .Where(candidate => IsEnglish(candidate.Card))
                .ToList();
        }

        pricedCandidates.Sort((left, right) => ComparePricedCandidates(left, right, options.PricingMode));
        PricedPrintingCandidate selected = pricedCandidates[0];
        CardPriceEvaluation selectedEvaluation = Evaluate(selected.Card, referenceDate);
        selectedEvaluation.SelectedPrintingReason = BuildSelectionReason(selected, hasEnglish, referenceDate);
        return new CardPrintingSelection(
            selected.Card,
            selectedEvaluation,
            ChangedPrinting: !SamePrinting(canonical, selected.Card));
    }

    /// <summary>
    /// Evaluates price status from release date and normalized provider price fields.
    /// </summary>
    private static CardPriceEvaluation Evaluate(
        DateOnly? releasedAt,
        IReadOnlyDictionary<string, string> prices,
        IReadOnlyList<string> games,
        string? language,
        DateOnly referenceDate)
    {
        if (releasedAt.HasValue && releasedAt.Value > referenceDate)
        {
            return MissingPrice(
                "future",
                $"Printing releases on {releasedAt.Value:yyyy-MM-dd}, after reference date {referenceDate:yyyy-MM-dd}.");
        }

        if (games.Count > 0 && !games.Contains("paper", StringComparer.OrdinalIgnoreCase))
        {
            return MissingPrice(
                "non-paper",
                "Printing is not available in paper according to provider game metadata.");
        }

        foreach (PriceField field in PriceFieldOrder)
        {
            decimal? price = TryReadDecimal(prices, field.Name);
            if (price.HasValue)
            {
                string status = releasedAt.HasValue
                    ? field.LowConfidence ? "released-low-confidence-price" : "released"
                    : field.LowConfidence ? "unknown-release-date-low-confidence-price" : "unknown-release-date";
                string languageText = string.IsNullOrWhiteSpace(language)
                    ? "language unavailable"
                    : $"language {language}";
                return new CardPriceEvaluation
                {
                    Price = price.Value,
                    PriceKnown = true,
                    PriceSource = field.Name,
                    PrintingStatus = status,
                    SelectedPrintingReason = releasedAt.HasValue
                        ? $"Selected {field.Name} price for released printing {releasedAt.Value:yyyy-MM-dd}; {languageText}."
                        : $"Selected {field.Name} price; release date was unavailable; {languageText}."
                };
            }
        }

        return MissingPrice(
            releasedAt.HasValue ? "unpriced" : "unknown-release-date-unpriced",
            releasedAt.HasValue
                ? $"Released printing {releasedAt.Value:yyyy-MM-dd} did not include a usable USD, foil, etched, or TCG price."
                : "Release date and usable USD, foil, etched, or TCG price were unavailable.");
    }

    /// <summary>
    /// Builds a comparable priced candidate when the printing satisfies release and paper constraints.
    /// </summary>
    private static PricedPrintingCandidate? BuildPricedCandidate(
        CardInfo card,
        DateOnly referenceDate,
        CardPrintingSelectionOptions options)
    {
        if (card.ReleasedAt.HasValue && card.ReleasedAt.Value > referenceDate)
        {
            return null;
        }

        if (card.Games.Count > 0 && !card.Games.Contains("paper", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsLegalForFormat(card, options.Format))
        {
            return null;
        }

        PricedPrintingCandidate? bestCandidate = null;
        for (int index = 0; index < PriceFieldOrder.Count; index++)
        {
            PriceField field = PriceFieldOrder[index];
            if (!AllowsPriceField(field, options))
            {
                continue;
            }

            decimal? price = TryReadDecimal(card.Prices, field.Name);
            if (price is > 0)
            {
                PricedPrintingCandidate candidate = new(
                    card,
                    field.Name,
                    field.LowConfidence,
                    index,
                    price.Value);
                if (bestCandidate is null
                    || ComparePricedCandidates(candidate, bestCandidate, options.PricingMode) < 0)
                {
                    bestCandidate = candidate;
                }
            }
        }

        return bestCandidate;
    }

    /// <summary>
    /// Orders candidates by price confidence, lowest known price, and stable printing identity.
    /// </summary>
    private static int ComparePricedCandidates(
        PricedPrintingCandidate left,
        PricedPrintingCandidate right,
        PricingMode pricingMode)
    {
        int comparison = pricingMode == PricingMode.ReleasedIfNeeded
            ? left.PriceTier.CompareTo(right.PriceTier)
            : left.Price.CompareTo(right.Price);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = pricingMode == PricingMode.ReleasedIfNeeded
            ? left.Price.CompareTo(right.Price)
            : left.PriceTier.CompareTo(right.PriceTier);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Nullable.Compare(left.Card.ReleasedAt, right.Card.ReleasedAt);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.Compare(left.Card.Set, right.Card.Set, StringComparison.OrdinalIgnoreCase);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.Compare(left.Card.CollectorNumber, right.Card.CollectorNumber, StringComparison.OrdinalIgnoreCase);
        if (comparison != 0)
        {
            return comparison;
        }

        return string.Compare(left.Card.Id, right.Card.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the selected pricing mode can use one provider price field.
    /// </summary>
    private static bool AllowsPriceField(PriceField field, CardPrintingSelectionOptions options)
    {
        if (options.PricingMode != PricingMode.BudgetPlayable)
        {
            return true;
        }

        return options.AllowAnyFinish || field.Name.Equals("usd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks provider legality only when the requested format is present in source data.
    /// </summary>
    private static bool IsLegalForFormat(CardInfo card, string? format)
    {
        if (string.IsNullOrWhiteSpace(format)
            || !card.Legalities.TryGetValue(format, out string? legality)
            || string.IsNullOrWhiteSpace(legality))
        {
            return true;
        }

        return legality.Equals("legal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a human-readable provenance reason for the selected printing.
    /// </summary>
    private static string BuildSelectionReason(
        PricedPrintingCandidate selected,
        bool englishWasAvailable,
        DateOnly referenceDate)
    {
        List<string> parts =
        [
            $"Selected lowest {selected.PriceSource} price among released paper printings on or before {referenceDate:yyyy-MM-dd}.",
            englishWasAvailable
                ? "English printing was available and preferred."
                : "No English priced printing was available, so language was not used as a filter.",
        ];

        if (selected.LowConfidencePrice)
        {
            parts.Add("Price source is lower confidence because no non-foil USD candidate was selected.");
        }

        if (selected.Card.ReleasedAt.HasValue)
        {
            parts.Add($"Selected printing released {selected.Card.ReleasedAt.Value:yyyy-MM-dd}.");
        }

        if (!string.IsNullOrWhiteSpace(selected.Card.Set) || !string.IsNullOrWhiteSpace(selected.Card.CollectorNumber))
        {
            parts.Add($"Printing: {selected.Card.Set ?? "unknown-set"} #{selected.Card.CollectorNumber ?? "unknown-number"}.");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Adds a candidate if another printing with the same provider id was not already included.
    /// </summary>
    private static void AddCandidate(List<CardInfo> candidates, CardInfo card)
    {
        if (string.IsNullOrWhiteSpace(card.Id)
            || !candidates.Any(candidate => candidate.Id.Equals(card.Id, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(card);
        }
    }

    /// <summary>
    /// Checks whether two card objects describe the same printing.
    /// </summary>
    private static bool SamePrinting(CardInfo left, CardInfo right)
    {
        if (!string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id))
        {
            return left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase);
        }

        return left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Set, right.Set, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.CollectorNumber, right.CollectorNumber, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a printing is English or lacks language metadata.
    /// </summary>
    private static bool IsEnglish(CardInfo card)
    {
        return string.IsNullOrWhiteSpace(card.Language)
            || card.Language.Equals("en", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Parses a decimal value from a case-insensitive dictionary key.
    /// </summary>
    private static decimal? TryReadDecimal(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            && decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal result)
                ? result
                : null;
    }

    /// <summary>
    /// Describes one selected printing and its evaluated price confidence.
    /// </summary>
    public sealed record CardPrintingSelection(
        CardInfo Card,
        CardPriceEvaluation PriceEvaluation,
        bool ChangedPrinting);

    /// <summary>
    /// Stores ordered metadata for one supported price field.
    /// </summary>
    private sealed record PriceField(string Name, bool LowConfidence);

    /// <summary>
    /// Stores comparable data for one print candidate.
    /// </summary>
    private sealed record PricedPrintingCandidate(
        CardInfo Card,
        string PriceSource,
        bool LowConfidencePrice,
        int PriceTier,
        decimal Price);
}
