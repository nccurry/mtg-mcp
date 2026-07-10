using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Adds bounded inverse copy-count solving over engine-proven monotone events.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Finds the lowest in-range copy count meeting one exact target probability.
    /// </summary>
    public OperationResult<StatisticsCalculation<MinimumCountResult>> CalculateMinimumCount(
        MinimumCountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryParseTarget(
                request.TargetNumerator,
                request.TargetDenominator,
                out ExactFraction target))
        {
            return Invalid<MinimumCountResult>(
                "request target must be a base-10 rational probability with a positive denominator.");
        }

        PreparedMinimumCountEvent preparation = PrepareMinimumEvent(request.Event);
        return preparation switch
        {
            PreparedHypergeometricMinimum hypergeometric => SolveMinimumCount(
                request,
                target,
                hypergeometric,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            PreparedManaMinimum mana => SolveMinimumCount(
                request,
                target,
                mana,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
        };
    }

    /// <summary>
    /// Solves one prepared hypergeometric or explicit-turn monotone event.
    /// </summary>
    private OperationResult<StatisticsCalculation<MinimumCountResult>> SolveMinimumCount(
        MinimumCountRequest request,
        ExactFraction target,
        PreparedHypergeometricMinimum prepared,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCountRange(
                request.MinimumCount,
                request.MaximumCount,
                prepared.PopulationSize,
                out string? rangeError))
        {
            return Invalid<MinimumCountResult>(rangeError!);
        }

        HypergeometricEventSnapshot eventSnapshot = new(
            "at-least",
            prepared.RequiredSuccesses,
            prepared.DrawCount);
        CanonicalPopulation population = CreateRepeatedPopulation(
            prepared.PopulationSize,
            request.MinimumCount);
        ExactFraction? previous = null;
        int? previousCount = null;
        ExactFraction highest = ExactFraction.Zero;
        int highestCount = request.MinimumCount;
        for (int count = request.MinimumCount; count <= request.MaximumCount; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.TryConsume(1))
            {
                return Bounded<MinimumCountResult>(
                    budget,
                    request.MaximumCount - request.MinimumCount + 1L,
                    population,
                    "Narrow the copy-count range or reduce the draw size.");
            }

            if (!TryCalculateHypergeometricFraction(
                    prepared.PopulationSize,
                    count,
                    prepared.DrawCount,
                    eventSnapshot,
                    budget,
                    cancellationToken,
                    out ExactFraction probability,
                    out long estimatedWork))
            {
                return Bounded<MinimumCountResult>(
                    budget,
                    StatisticsWorkBudget.SaturatingMultiply(
                        estimatedWork,
                        request.MaximumCount - request.MinimumCount + 1L),
                    population,
                    "Narrow the copy-count range or reduce the draw size.");
            }

            highest = probability;
            highestCount = count;
            if (probability.CompareTo(target) >= 0)
            {
                return Exact(CreateMinimumResult(
                    prepared.Kind,
                    request,
                    target,
                    found: true,
                    count,
                    probability,
                    previousCount,
                    previous,
                    highestCount,
                    highest));
            }

            previous = probability;
            previousCount = count;
        }

        return Exact(CreateMinimumResult(
            prepared.Kind,
            request,
            target,
            found: false,
            count: null,
            probability: null,
            previousCount: null,
            previousProbability: null,
            highestCount,
            highest));
    }

    /// <summary>
    /// Solves one prepared repeated-source mana availability event.
    /// </summary>
    private OperationResult<StatisticsCalculation<MinimumCountResult>> SolveMinimumCount(
        MinimumCountRequest request,
        ExactFraction target,
        PreparedManaMinimum prepared,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCountRange(
                request.MinimumCount,
                request.MaximumCount,
                prepared.PopulationSize,
                out string? rangeError))
        {
            return Invalid<MinimumCountResult>(rangeError!);
        }

        ExactFraction? previous = null;
        int? previousCount = null;
        ExactFraction highest = ExactFraction.Zero;
        int highestCount = request.MinimumCount;
        CanonicalPopulation population = CreateRepeatedPopulation(
            prepared.PopulationSize,
            request.MinimumCount);
        for (int count = request.MinimumCount; count <= request.MaximumCount; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            population = CreateRepeatedPopulation(prepared.PopulationSize, count);
            if (!budget.TryConsume(1))
            {
                return Bounded<MinimumCountResult>(
                    budget,
                    request.MaximumCount - request.MinimumCount + 1L,
                    population,
                    "Narrow the copy-count range or reduce the draw size.");
            }

            if (!TryCalculateManaFraction(
                    population,
                    prepared.DrawCount,
                    ["source"],
                    [prepared.Capability],
                    prepared.Requirement,
                    prepared.MaximumUsableSources,
                    budget,
                    cancellationToken,
                    out ExactFraction probability,
                    out long estimatedWork))
            {
                return Bounded<MinimumCountResult>(
                    budget,
                    StatisticsWorkBudget.SaturatingMultiply(
                        estimatedWork,
                        request.MaximumCount - request.MinimumCount + 1L),
                    population,
                    "Narrow the copy-count range or reduce the draw size.");
            }

            highest = probability;
            highestCount = count;
            if (probability.CompareTo(target) >= 0)
            {
                return Exact(CreateMinimumResult(
                    "mana-availability",
                    request,
                    target,
                    found: true,
                    count,
                    probability,
                    previousCount,
                    previous,
                    highestCount,
                    highest));
            }

            previous = probability;
            previousCount = count;
        }

        return Exact(CreateMinimumResult(
            "mana-availability",
            request,
            target,
            found: false,
            count: null,
            probability: null,
            previousCount: null,
            previousProbability: null,
            highestCount,
            highest));
    }

    /// <summary>
    /// Creates one exact inverse result with stable neighboring evidence.
    /// </summary>
    private MinimumCountResult CreateMinimumResult(
        string eventKind,
        MinimumCountRequest request,
        ExactFraction target,
        bool found,
        int? count,
        ExactFraction? probability,
        int? previousCount,
        ExactFraction? previousProbability,
        int highestCount,
        ExactFraction highestProbability)
    {
        return new MinimumCountResult(
            CreateDerivation(
                "minimum-monotone-count",
                [
                    "The event belongs to an engine-owned monotone case.",
                    "Only exact rational comparisons determine whether the target is met.",
                ]),
            eventKind,
            request.MinimumCount,
            request.MaximumCount,
            target.ToProbability(),
            found,
            count,
            probability?.ToProbability(),
            previousCount,
            previousProbability?.ToProbability(),
            highestCount,
            highestProbability.ToProbability());
    }

    /// <summary>
    /// Parses and validates one exact target probability.
    /// </summary>
    private static bool TryParseTarget(
        string? numeratorText,
        string? denominatorText,
        out ExactFraction target)
    {
        target = ExactFraction.Zero;
        if (!BigInteger.TryParse(
                numeratorText,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out BigInteger numerator) ||
            !BigInteger.TryParse(
                denominatorText,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out BigInteger denominator) ||
            denominator.Sign <= 0)
        {
            return false;
        }

        target = new ExactFraction(numerator, denominator);
        return target.CompareTo(ExactFraction.Zero) >= 0 &&
            target.CompareTo(ExactFraction.One) <= 0;
    }

    /// <summary>
    /// Validates an inclusive variable copy-count range inside a fixed population.
    /// </summary>
    private static bool TryValidateCountRange(
        int minimum,
        int maximum,
        int population,
        out string? error)
    {
        error = null;
        if (minimum < 0 || maximum < minimum || maximum > population)
        {
            error = "request minimumCount and maximumCount must define an inclusive range inside the population.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates and normalizes one closed inverse event.
    /// </summary>
    private static PreparedMinimumCountEvent PrepareMinimumEvent(MinimumCountEventInput? input)
    {
        switch (input)
        {
            case MinimumHypergeometricCountEvent hypergeometric
                when IsValidFixedPopulation(
                    hypergeometric.PopulationSize,
                    hypergeometric.DrawCount,
                    hypergeometric.RequiredSuccesses):
                return new PreparedHypergeometricMinimum(
                    "hypergeometric-at-least",
                    hypergeometric.PopulationSize,
                    hypergeometric.DrawCount,
                    hypergeometric.RequiredSuccesses);
            case MinimumTurnCountEvent turn:
                return PrepareTurnMinimum(turn);
            case MinimumManaCountEvent mana:
                return PrepareManaMinimum(mana);
            case null:
                return new OperationInvalidInput(
                    "invalid-statistics-request",
                    "request.event is required.");
            default:
                return new OperationInvalidInput(
                    "invalid-statistics-request",
                    "request.event contains invalid fixed population or success values.");
        }
    }

    /// <summary>
    /// Normalizes one explicit turn inverse event to its cumulative draw count.
    /// </summary>
    private static PreparedMinimumCountEvent PrepareTurnMinimum(MinimumTurnCountEvent input)
    {
        if (input.PopulationSize is < 1 or > StatisticsPopulationValidator.MaximumPopulation ||
            input.OpeningHandSize < 0 ||
            input.OpeningHandSize > input.PopulationSize ||
            input.RequiredSuccesses < 0 ||
            !TryPrepareDrawSchedule(
                input.DrawsByTurn,
                input.OpeningHandSize,
                input.PopulationSize,
                out TurnDrawInput[] schedule,
                out _))
        {
            return new OperationInvalidInput(
                "invalid-statistics-request",
                "request.event contains an invalid explicit turn schedule.");
        }

        int cardsSeen = input.OpeningHandSize;
        foreach (TurnDrawInput row in schedule)
        {
            cardsSeen += row.Draws;
            if (row.Turn == input.Turn)
            {
                return new PreparedHypergeometricMinimum(
                    "turn-at-least",
                    input.PopulationSize,
                    cardsSeen,
                    input.RequiredSuccesses);
            }
        }

        return new OperationInvalidInput(
            "invalid-statistics-request",
            "request.event.turn must identify one supplied draw schedule row.");
    }

    /// <summary>
    /// Validates one repeated-source inverse event.
    /// </summary>
    private static PreparedMinimumCountEvent PrepareManaMinimum(MinimumManaCountEvent input)
    {
        if (input.PopulationSize is < 1 or > StatisticsPopulationValidator.MaximumPopulation ||
            input.DrawCount < 0 ||
            input.DrawCount > input.PopulationSize ||
            input.MaximumUsableSources < 0 ||
            input.MaximumUsableSources > input.PopulationSize ||
            !TryParseCapabilities(input.SourceCapabilities, out ManaCapability capability, out _) ||
            !TryPrepareManaRequirement(input.Requirement, out PreparedManaRequirement? requirement, out _))
        {
            return new OperationInvalidInput(
                "invalid-statistics-request",
                "request.event contains an invalid repeated-source mana case.");
        }

        return new PreparedManaMinimum(
            input.PopulationSize,
            input.DrawCount,
            capability,
            requirement!,
            input.MaximumUsableSources);
    }

    /// <summary>
    /// Reports whether fixed hypergeometric inputs fit the exact stable bounds.
    /// </summary>
    private static bool IsValidFixedPopulation(int population, int drawCount, int requiredSuccesses)
    {
        return population is >= 1 and <= StatisticsPopulationValidator.MaximumPopulation &&
            drawCount >= 0 &&
            drawCount <= population &&
            requiredSuccesses >= 0;
    }

    /// <summary>
    /// Creates one canonical two-bucket population for a repeated variable copy count.
    /// </summary>
    private static CanonicalPopulation CreateRepeatedPopulation(int population, int count)
    {
        List<CanonicalPopulationBucket> buckets = [];
        if (population - count > 0)
        {
            buckets.Add(new CanonicalPopulationBucket(population - count, []));
        }

        if (count > 0)
        {
            buckets.Add(new CanonicalPopulationBucket(count, ["source"]));
        }

        return new CanonicalPopulation(population, [.. buckets], ["source"], null);
    }
}

/// <summary>
/// Represents one normalized inverse event or its input/bound failure.
/// </summary>
internal readonly union PreparedMinimumCountEvent(
    PreparedHypergeometricMinimum,
    PreparedManaMinimum,
    OperationInvalidInput);

/// <summary>
/// Stores a fixed hypergeometric at-least event, including explicit-turn normalization.
/// </summary>
internal sealed record PreparedHypergeometricMinimum(
    string Kind,
    int PopulationSize,
    int DrawCount,
    int RequiredSuccesses);

/// <summary>
/// Stores one fixed repeated-source mana event.
/// </summary>
internal sealed record PreparedManaMinimum(
    int PopulationSize,
    int DrawCount,
    ManaCapability Capability,
    PreparedManaRequirement Requirement,
    int MaximumUsableSources);
