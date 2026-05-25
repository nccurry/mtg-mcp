using BenchmarkDotNet.Attributes;
using MtgMcp.Core;

namespace MtgMcp.Benchmarks;

/// <summary>
/// Measures the whole-deck analyzer path used by analysis tools and recommendation context.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class DeckAnalysisBenchmarks
{
    /// <summary>
    /// Keeps the generated workspace outside the measured analyzer call.
    /// </summary>
    private DeckWorkspace workspace = null!;

    /// <summary>
    /// Gets or sets the number of distinct cards in the generated workspace.
    /// </summary>
    [Params(100, 600)]
    public int DistinctCards { get; set; }

    /// <summary>
    /// Builds a deterministic workspace with representative card text and categories.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        workspace = BenchmarkDeckFactory.CreateWideWorkspace(DistinctCards);
    }

    /// <summary>
    /// Runs role, type, curve, and color analysis for the configured workspace.
    /// </summary>
    [Benchmark]
    public int AnalyzeDeck()
    {
        DeckAnalysis analysis = DeckAnalyzer.Analyze(workspace);
        return analysis.IncludedCards
            + analysis.RoleCounts.Count
            + analysis.TagCounts.Count
            + analysis.TypeCounts.Count
            + analysis.ManaCurve.Count;
    }
}

/// <summary>
/// Measures repeated role classification, the shared heuristic used across analysis and simulation.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class DeckRoleClassifierBenchmarks
{
    /// <summary>
    /// Stores representative cards that exercise common role and tag heuristics.
    /// </summary>
    private DeckCard[] cards = [];

    /// <summary>
    /// Gets or sets how many classifier calls are included in one measurement.
    /// </summary>
    [Params(100, 1_000)]
    public int OperationCount { get; set; }

    /// <summary>
    /// Builds the reusable card set classified during the benchmark.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        cards = BenchmarkDeckFactory.CreateRepresentativeCards();
    }

    /// <summary>
    /// Classifies a rotating set of representative cards.
    /// </summary>
    [Benchmark]
    public int ClassifyRepresentativeCards()
    {
        int score = 0;
        for (int index = 0; index < OperationCount; index++)
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(cards[index % cards.Length]);
            score += assignment.PrimaryRole.Length + assignment.Tags.Count;
        }

        return score;
    }
}

/// <summary>
/// Measures the pure Stats Lab performance analyzer used by deck performance and plan comparison tools.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class DeckPerformanceAnalyzerBenchmarks
{
    /// <summary>
    /// Keeps the stable Commander deck outside the measured simulation call.
    /// </summary>
    private DeckWorkspace commanderDeck = null!;

    /// <summary>
    /// Gets or sets the Monte Carlo run count requested from the analyzer.
    /// </summary>
    [Params(100, 1_000)]
    public int Simulations { get; set; }

    /// <summary>
    /// Builds the deterministic Commander deck fixture.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        commanderDeck = BenchmarkDeckFactory.CreateCommanderPerformanceDeck();
    }

    /// <summary>
    /// Runs the full performance analysis report for a typical Commander deck.
    /// </summary>
    [Benchmark]
    public double AnalyzeCommanderPerformance()
    {
        DeckPerformanceAnalysis analysis = DeckPerformanceAnalyzer.Analyze(
            commanderDeck,
            "commander-default",
            Simulations,
            maxTurn: 6,
            seed: 2026,
            includeMulligans: true,
            CancellationToken.None);

        return analysis.OpeningHands.SevenCardKeepRate
            + analysis.Commander.CastByTurn.Count
            + analysis.Scenarios.Count;
    }
}

/// <summary>
/// Measures mana-source parsing and payment checks used inside performance simulations.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class PerformanceManaBenchmarks
{
    /// <summary>
    /// Stores spells with fixed, hybrid, colorless, and repeated colored requirements.
    /// </summary>
    private DeckCard[] spells = [];

    /// <summary>
    /// Stores reusable mana sources for payment checks.
    /// </summary>
    private PerformanceManaSource[] sources = [];

    /// <summary>
    /// Stores lands and ramp cards used by produced-mana parsing checks.
    /// </summary>
    private DeckCard[] manaCards = [];

    /// <summary>
    /// Gets or sets how many mana helper calls are included in one measurement.
    /// </summary>
    [Params(100, 1_000)]
    public int OperationCount { get; set; }

    /// <summary>
    /// Builds representative spell and mana-source inputs.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        spells =
        [
            BenchmarkDeckFactory.CreateSpell("Absorb", "{W}{U}{U}", 3, "Counter target spell. You gain 3 life.", ["W", "U"]),
            BenchmarkDeckFactory.CreateSpell("Hybrid Answer", "{W/U}{W/U}", 2, "Counter target spell.", ["W", "U"]),
            BenchmarkDeckFactory.CreateSpell("Colorless Engine", "{C}{C}", 2, "Draw a card.", []),
            BenchmarkDeckFactory.CreateSpell("Green Finisher", "{5}{G}{G}", 7, "Creatures you control get +X/+X.", ["G"]),
        ];
        sources =
        [
            new PerformanceManaSource(["W", "U", "B", "R", "G"]),
            new PerformanceManaSource(["U"]),
            new PerformanceManaSource(["U"]),
            new PerformanceManaSource(["G"]),
            new PerformanceManaSource(["C"]),
            new PerformanceManaSource(["C"]),
            new PerformanceManaSource(["W"]),
        ];
        manaCards =
        [
            BenchmarkDeckFactory.CreateLand("Plains", ["W"]),
            BenchmarkDeckFactory.CreateLand("Command Tower", ["W", "U", "B", "R", "G"]),
            BenchmarkDeckFactory.CreateLand("Hagra Mauling // Hagra Broodpit", ["B"], "Instant // Land", "Destroy target creature. Hagra Broodpit enters tapped."),
            BenchmarkDeckFactory.CreateSpell("Arcane Signet", "{2}", 2, "{T}: Add one mana of any color.", []),
        ];
    }

    /// <summary>
    /// Attempts representative mana payments with flexible and repeated color requirements.
    /// </summary>
    [Benchmark]
    public int TryPayRepresentativeCosts()
    {
        int score = 0;
        for (int index = 0; index < OperationCount; index++)
        {
            DeckCard spell = spells[index % spells.Length];
            if (PerformanceMana.TryPay(spell, sources, out List<PerformanceManaSource> remainingSources))
            {
                score += remainingSources.Count;
            }
        }

        return score;
    }

    /// <summary>
    /// Reads produced mana from explicit snapshots and fallback land text.
    /// </summary>
    [Benchmark]
    public int ReadProducedMana()
    {
        int score = 0;
        for (int index = 0; index < OperationCount; index++)
        {
            score += PerformanceMana.ReadProducedMana(manaCards[index % manaCards.Length]).Count;
        }

        return score;
    }
}

/// <summary>
/// Measures JSON facet predicates used by card-facet filtering tools.
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class FacetPredicateBenchmarks
{
    /// <summary>
    /// Keeps the normalized facet snapshot outside the measured predicate evaluation.
    /// </summary>
    private CardFacetSnapshot card = null!;

    /// <summary>
    /// Stores a representative nested predicate with string and numeric checks.
    /// </summary>
    private string predicateJson = "";

    /// <summary>
    /// Builds the facet snapshot and predicate JSON.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        card = BenchmarkDeckFactory.CreateFacetSnapshot();
        predicateJson = """
        {
          "all": [
            { "facet": "workspace.primary_category", "equals": "Draw" },
            { "facet": "tagger.oracle_tags", "containsAny": ["card draw", "discard"] },
            { "facet": "metadata.mana_value", "lessThanOrEqual": 4 }
          ]
        }
        """;
    }

    /// <summary>
    /// Evaluates a representative nested facet predicate.
    /// </summary>
    [Benchmark]
    public bool EvaluateTypicalPredicate()
    {
        return FacetPredicateEvaluator.Evaluate(card, predicateJson).Matched;
    }
}
