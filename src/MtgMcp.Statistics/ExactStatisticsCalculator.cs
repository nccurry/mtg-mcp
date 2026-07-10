using System.Numerics;
using MtgMcp.Core.Evidence;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Owns provider-independent exact calculations over caller-resolved populations.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Identifies the stable exact engine contract.
    /// </summary>
    internal const string CalculationVersion = "exact-v1";

    /// <summary>
    /// Stores the package implementation version returned with derivations.
    /// </summary>
    private readonly string implementationVersion;

    /// <summary>
    /// Stores the fixed request-wide work limit, overridable only by internal tests.
    /// </summary>
    private readonly long workLimit;

    /// <summary>
    /// Creates a calculator for one package implementation version.
    /// </summary>
    public ExactStatisticsCalculator(string implementationVersion)
        : this(implementationVersion, StatisticsWorkBudget.DefaultLimit)
    {
    }

    /// <summary>
    /// Creates a calculator with an explicit work limit for boundary verification.
    /// </summary>
    internal ExactStatisticsCalculator(string implementationVersion, long workLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workLimit);
        this.implementationVersion = implementationVersion.Trim();
        this.workLimit = workLimit;
    }

    /// <summary>
    /// Calculates one exact hypergeometric event, complement, expectation, and variance.
    /// </summary>
    public OperationResult<StatisticsCalculation<HypergeometricResult>> CalculateHypergeometric(
        HypergeometricRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculateHypergeometric(
                population,
                request,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<HypergeometricResult>>(bounded),
        };
    }

    /// <summary>
    /// Calculates one request after population validation and canonicalization.
    /// </summary>
    private OperationResult<StatisticsCalculation<HypergeometricResult>> CalculateHypergeometric(
        CanonicalPopulation population,
        HypergeometricRequest request,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (!TryValidateExactGroup(request.SuccessGroup, out string successGroup) ||
            !population.HasGroup(successGroup))
        {
            return Invalid<HypergeometricResult>(
                "request.successGroup must be one exact declared population group.");
        }

        if (request.DrawCount < 0 || request.DrawCount > population.TotalCount)
        {
            return Invalid<HypergeometricResult>(
                "request.drawCount must be between zero and the selected population size.");
        }

        if (!TryResolveEvent(
                request.Event,
                request.DrawCount,
                out HypergeometricEventSnapshot? eventSnapshot,
                out string? eventError))
        {
            return Invalid<HypergeometricResult>(eventError!);
        }

        int successCount = population.CountGroup(successGroup);
        if (!TryCalculateHypergeometricFraction(
                population.TotalCount,
                successCount,
                request.DrawCount,
                eventSnapshot!,
                budget,
                cancellationToken,
                out ExactFraction probability,
                out long estimatedWork))
        {
            return Bounded<HypergeometricResult>(
                budget,
                estimatedWork,
                population,
                "Reduce the event range or selected population.");
        }

        ExactFraction complement = ExactFraction.One.Subtract(probability);
        ExactFraction expectation = new(
            (BigInteger)request.DrawCount * successCount,
            population.TotalCount);
        ExactFraction variance = population.TotalCount <= 1
            ? ExactFraction.Zero
            : new ExactFraction(
                (BigInteger)request.DrawCount * successCount *
                    (population.TotalCount - successCount) *
                    (population.TotalCount - request.DrawCount),
                (BigInteger)population.TotalCount * population.TotalCount *
                    (population.TotalCount - 1));
        StatisticsDerivation derivation = CreateDerivation(
            "hypergeometric-range",
            [
                "Population buckets and group membership were supplied explicitly by the caller.",
                "Cards are drawn uniformly without replacement.",
                "Rounded display fields do not participate in the exact calculation.",
            ]);
        HypergeometricResult result = new(
            derivation,
            population.ToSnapshot(),
            successGroup,
            successCount,
            request.DrawCount,
            eventSnapshot!,
            probability.ToProbability(),
            complement.ToProbability(),
            expectation.ToValue(),
            variance.ToValue());
        return Exact(result);
    }

    /// <summary>
    /// Calculates one normalized hypergeometric event while sharing a caller request budget.
    /// </summary>
    private static bool TryCalculateHypergeometricFraction(
        int populationSize,
        int successCount,
        int drawCount,
        HypergeometricEventSnapshot eventSnapshot,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken,
        out ExactFraction probability,
        out long estimatedWork)
    {
        int minimumFeasible = Math.Max(0, drawCount - (populationSize - successCount));
        int maximumFeasible = Math.Min(drawCount, successCount);
        int minimum = Math.Max(eventSnapshot.Minimum, minimumFeasible);
        int maximum = Math.Min(eventSnapshot.Maximum, maximumFeasible);
        int termCount = maximum >= minimum ? maximum - minimum + 1 : 0;
        estimatedWork = termCount;
        probability = ExactFraction.Zero;
        if (termCount > 0 && !budget.TryConsume(termCount))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        CombinationCache combinations = new();
        BigInteger denominator = combinations.Choose(populationSize, drawCount);
        BigInteger numerator = BigInteger.Zero;
        for (int successes = minimum; successes <= maximum; successes++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            numerator += combinations.Choose(successCount, successes) *
                combinations.Choose(populationSize - successCount, drawCount - successes);
        }

        probability = new ExactFraction(numerator, denominator);
        return true;
    }

    /// <summary>
    /// Normalizes one closed hypergeometric event into an inclusive range.
    /// </summary>
    private static bool TryResolveEvent(
        HypergeometricEventInput? input,
        int drawCount,
        out HypergeometricEventSnapshot? snapshot,
        out string? error)
    {
        snapshot = null;
        error = null;
        switch (input)
        {
            case HypergeometricExactlyEvent exactly when exactly.Count >= 0:
                snapshot = new HypergeometricEventSnapshot("exactly", exactly.Count, exactly.Count);
                return true;
            case HypergeometricZeroEvent:
                snapshot = new HypergeometricEventSnapshot("zero", 0, 0);
                return true;
            case HypergeometricAtLeastEvent atLeast when atLeast.Count >= 0:
                snapshot = new HypergeometricEventSnapshot("at-least", atLeast.Count, drawCount);
                return true;
            case HypergeometricAtMostEvent atMost when atMost.Count >= 0:
                snapshot = new HypergeometricEventSnapshot("at-most", 0, atMost.Count);
                return true;
            case HypergeometricRangeEvent range
                when range.Minimum >= 0 && range.Maximum >= range.Minimum:
                snapshot = new HypergeometricEventSnapshot("range", range.Minimum, range.Maximum);
                return true;
            case null:
                error = "request.event is required.";
                return false;
            default:
                error = "request.event contains invalid nonnegative success bounds.";
                return false;
        }
    }

    /// <summary>
    /// Validates one exact nonblank unpadded group reference.
    /// </summary>
    private static bool TryValidateExactGroup(string? rawGroup, out string group)
    {
        group = rawGroup ?? string.Empty;
        return group.Length > 0 &&
            !string.IsNullOrWhiteSpace(group) &&
            string.Equals(group, group.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates one stable exact derivation descriptor.
    /// </summary>
    private StatisticsDerivation CreateDerivation(string formulaId, IReadOnlyList<string> assumptions)
    {
        return new StatisticsDerivation(
            formulaId,
            CalculationVersion,
            implementationVersion,
            new ExactDerivationDescriptor(formulaId, assumptions));
    }

    /// <summary>
    /// Wraps one complete exact result in the shared operation contract.
    /// </summary>
    private static OperationResult<StatisticsCalculation<T>> Exact<T>(T data)
    {
        return new OperationSuccess<StatisticsCalculation<T>>(new StatisticsExact<T>(data));
    }

    /// <summary>
    /// Creates one stable invalid-input operation result.
    /// </summary>
    private static OperationResult<StatisticsCalculation<T>> Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-statistics-request", message);
    }

    /// <summary>
    /// Creates one request-wide work-budget result without a partial exact payload.
    /// </summary>
    private static OperationResult<StatisticsCalculation<T>> Bounded<T>(
        StatisticsWorkBudget budget,
        long estimatedWork,
        CanonicalPopulation population,
        string reductionOption,
        int turnCount = 0,
        int attemptCount = 0)
    {
        StatisticsBoundedUnsupported bounded = new(
            "statistics-work-budget-exceeded",
            "The exact statistics request exceeds the request-wide work budget.",
            new StatisticsLimitDetail(
                "work-units",
                budget.Limit,
                estimatedWork,
                population.TotalCount,
                population.DeclaredGroups.Length,
                turnCount,
                attemptCount,
                [reductionOption]));
        return new OperationSuccess<StatisticsCalculation<T>>(bounded);
    }
}
