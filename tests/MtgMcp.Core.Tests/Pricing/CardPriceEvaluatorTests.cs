using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies deterministic card price and printing selection policy.
/// </summary>
public sealed class CardPriceEvaluatorTests
{
    /// <summary>
    /// Verifies that the default price source preserves evaluator semantics.
    /// </summary>
    [Fact]
    public void CatalogPriceSource_EvaluatesNormalizedCatalogPriceFields()
    {
        CardSnapshot snapshot = new()
        {
            ReleasedAt = new DateOnly(2025, 1, 1),
            Games = ["paper"],
            Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["usd_foil"] = "4.25"
            }
        };

        CardPriceEvaluation evaluation = CatalogPriceSource.Instance.Evaluate(
            snapshot,
            new DateOnly(2026, 1, 1));

        evaluation.PriceKnown.Should().BeTrue();
        evaluation.Price.Should().Be(4.25m);
        evaluation.PriceSource.Should().Be("usd_foil");
        evaluation.PrintingStatus.Should().Be("released-low-confidence-price");
    }

    /// <summary>
    /// Verifies that released English paper USD printings beat future canonical snapshots.
    /// </summary>
    [Fact]
    public void SelectPrinting_PrefersReleasedEnglishPaperUsdOverFutureCanonical()
    {
        CardInfo canonical = Card(
            "future",
            "Policy Card",
            releasedAt: new DateOnly(2027, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        CardInfo releasedExpensive = Card(
            "released-expensive",
            "Policy Card",
            releasedAt: new DateOnly(2024, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" });
        CardInfo releasedCheap = Card(
            "released-cheap",
            "Policy Card",
            releasedAt: new DateOnly(2025, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.50" });

        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            canonical,
            [releasedExpensive, releasedCheap],
            new DateOnly(2026, 1, 1));

        selection.Card.Id.Should().Be("released-cheap");
        selection.ChangedPrinting.Should().BeTrue();
        selection.PriceEvaluation.PriceKnown.Should().BeTrue();
        selection.PriceEvaluation.PriceSource.Should().Be("usd");
        selection.PriceEvaluation.PrintingStatus.Should().Be("released");
        selection.PriceEvaluation.SelectedPrintingReason.Should().Contain("released paper printings");
    }

    /// <summary>
    /// Verifies that foil or TCG fallback prices are lower confidence and used only when no USD exists.
    /// </summary>
    [Fact]
    public void SelectPrinting_FallsBackToLowerConfidencePriceWhenNoUsdExists()
    {
        CardInfo canonical = Card(
            "canonical",
            "Foil Only",
            releasedAt: new DateOnly(2025, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        CardInfo foil = Card(
            "foil",
            "Foil Only",
            releasedAt: new DateOnly(2025, 2, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd_foil"] = "2.25" });

        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            canonical,
            [foil],
            new DateOnly(2026, 1, 1));

        selection.Card.Id.Should().Be("foil");
        selection.PriceEvaluation.PriceKnown.Should().BeTrue();
        selection.PriceEvaluation.PriceSource.Should().Be("usd_foil");
        selection.PriceEvaluation.PrintingStatus.Should().Be("released-low-confidence-price");
        selection.PriceEvaluation.SelectedPrintingReason.Should().Contain("lower confidence");
    }

    /// <summary>
    /// Verifies cheapest released paper mode chooses the cheapest supported field instead of the highest-confidence field.
    /// </summary>
    [Fact]
    public void SelectPrinting_CheapestReleasedPaperCanUseAnySupportedFinish()
    {
        CardInfo canonical = Card(
            "canonical",
            "Cheap Foil",
            releasedAt: new DateOnly(2025, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" });
        CardInfo cheapFoil = Card(
            "cheap-foil",
            "Cheap Foil",
            releasedAt: new DateOnly(2025, 2, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd_foil"] = "0.50" });

        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            canonical,
            [cheapFoil],
            new DateOnly(2026, 1, 1),
            new CardPrintingSelectionOptions { PricingMode = PricingMode.CheapestReleasedPaper });

        selection.Card.Id.Should().Be("cheap-foil");
        selection.PriceEvaluation.PriceSource.Should().Be("usd_foil");
        selection.PriceEvaluation.Price.Should().Be(0.50m);
    }

    /// <summary>
    /// Verifies budget-playable mode uses source-backed legality, paper, release, language, and non-foil USD rules.
    /// </summary>
    [Fact]
    public void SelectPrinting_BudgetPlayableRequiresLegalReleasedPaperUsd()
    {
        CardInfo canonical = Card(
            "future",
            "Budget Card",
            releasedAt: new DateOnly(2027, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        CardInfo arenaOnly = Card(
            "arena-only",
            "Budget Card",
            releasedAt: new DateOnly(2024, 1, 1),
            language: "en",
            games: ["arena"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.10" });
        CardInfo illegal = Card(
            "illegal",
            "Budget Card",
            releasedAt: new DateOnly(2024, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.20" });
        illegal.Legalities["commander"] = "not_legal";
        CardInfo foilOnly = Card(
            "foil-only",
            "Budget Card",
            releasedAt: new DateOnly(2024, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd_foil"] = "0.30" });
        CardInfo legalEnglish = Card(
            "legal-english",
            "Budget Card",
            releasedAt: new DateOnly(2024, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.25" });
        legalEnglish.Legalities["commander"] = "legal";

        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            canonical,
            [arenaOnly, illegal, foilOnly, legalEnglish],
            new DateOnly(2026, 1, 1),
            new CardPrintingSelectionOptions
            {
                PricingMode = PricingMode.BudgetPlayable,
                Format = "commander",
            });

        selection.Card.Id.Should().Be("legal-english");
        selection.PriceEvaluation.PriceSource.Should().Be("usd");
        selection.PriceEvaluation.Price.Should().Be(1.25m);
    }

    /// <summary>
    /// Verifies budget-playable mode does not treat foil-only prices as safe non-foil budget prices.
    /// </summary>
    [Fact]
    public void SelectPrinting_BudgetPlayableReturnsUnknownWhenOnlyFoilPricesExist()
    {
        CardInfo canonical = Card(
            "foil-only",
            "Foil Only",
            releasedAt: new DateOnly(2025, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd_foil"] = "0.25" });

        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            canonical,
            [],
            new DateOnly(2026, 1, 1),
            new CardPrintingSelectionOptions { PricingMode = PricingMode.BudgetPlayable });

        selection.PriceEvaluation.PriceKnown.Should().BeFalse();
        selection.PriceEvaluation.PrintingStatus.Should().Be("no-matching-priced-printing");
        selection.PriceEvaluation.SelectedPrintingReason.Should().Contain("usable positive price");
    }

    /// <summary>
    /// Verifies that missing released priced printings remain unknown instead of becoming free.
    /// </summary>
    [Fact]
    public void SelectPrinting_ReturnsUnknownWhenNoReleasedPricedPaperPrintingExists()
    {
        CardInfo canonical = Card(
            "future",
            "No Price",
            releasedAt: new DateOnly(2027, 1, 1),
            language: "en",
            games: ["paper"],
            prices: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        CardPriceEvaluator.CardPrintingSelection selection = CardPriceEvaluator.SelectPrinting(
            canonical,
            [],
            new DateOnly(2026, 1, 1));

        selection.Card.Id.Should().Be("future");
        selection.PriceEvaluation.PriceKnown.Should().BeFalse();
        selection.PriceEvaluation.PrintingStatus.Should().Be("future");
        selection.PriceEvaluation.SelectedPrintingReason.Should().Contain("No released paper printing");
    }

    /// <summary>
    /// Creates compact test card metadata.
    /// </summary>
    private static CardInfo Card(
        string id,
        string name,
        DateOnly releasedAt,
        string language,
        List<string> games,
        Dictionary<string, string> prices)
    {
        return new CardInfo
        {
            Id = id,
            Name = name,
            ReleasedAt = releasedAt,
            Language = language,
            Games = games,
            Prices = prices,
            Set = "tst",
            CollectorNumber = id,
        };
    }
}
