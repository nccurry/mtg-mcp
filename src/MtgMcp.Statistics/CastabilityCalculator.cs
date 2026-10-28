using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Asks for the exact chance of paying one mana cost by a turn.
/// </summary>
/// <param name="Key">The caller's name for the cost, such as the cost text. It is repeated in the row.</param>
/// <param name="Requirement">The colored, colorless, and generic mana the cost needs.</param>
/// <param name="Turn">The turn by which the cost must be payable; at least 1.</param>
public sealed record CastabilityCostInput(string Key, ManaRequirementInput Requirement, int Turn);

/// <summary>
/// Describes one castability calculation over a deck population and declared source groups.
/// </summary>
/// <param name="Population">The deck as disjoint buckets with the source groups each card belongs to.</param>
/// <param name="Sources">The source groups and the colors each one produces.</param>
/// <param name="Costs">The distinct costs to check.</param>
/// <param name="OnThePlay">Whether the player goes first, which skips the first draw.</param>
/// <param name="RequireLandDrops">
/// Whether to condition on having seen at least as many sources as the turn number, as Frank Karsten's tables do.
/// </param>
public sealed record CastabilityRequest(
    StatisticsPopulation Population,
    IReadOnlyList<ManaSourceInput> Sources,
    IReadOnlyList<CastabilityCostInput> Costs,
    bool OnThePlay,
    bool RequireLandDrops);

/// <summary>
/// Gives the exact result for one cost.
/// </summary>
/// <param name="Key">The caller's name for the cost.</param>
/// <param name="Turn">The turn checked.</param>
/// <param name="CardsSeen">The cards seen by that turn: 7 plus the turn, less one on the play.</param>
/// <param name="Probability">
/// The exact probability, or null when the conditioning event cannot happen (the deck holds fewer sources than the
/// turn number), so the conditional probability is undefined.
/// </param>
public sealed record CastabilityRow(string Key, int Turn, int CardsSeen, ExactProbability? Probability);

/// <summary>
/// Returns exact castability rows, with the assumptions that were used.
/// </summary>
public sealed record CastabilityResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    bool OnThePlay,
    bool RequireLandDrops,
    IReadOnlyList<ManaSourceInput> Sources,
    IReadOnlyList<CastabilityRow> Rows)
{
    /// <summary>
    /// Gets an immutable copy of the rows.
    /// </summary>
    public IReadOnlyList<CastabilityRow> Rows { get; init; } = Array.AsReadOnly(Rows.ToArray());
}

/// <summary>
/// Adds the exact conditional castability calculation.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Gives the most costs times source groups one castability request may hold.
    /// </summary>
    public const int MaximumCastabilityWork = 480;

    /// <summary>
    /// Gives the number of cards in an opening hand.
    /// </summary>
    private const int OpeningHand = 7;

    /// <summary>
    /// Calculates, for each distinct cost, the exact probability that the cards seen by its turn hold sources that
    /// can pay it, using at most one source per turn. With <see cref="CastabilityRequest.RequireLandDrops"/> the
    /// probability is conditional on having seen at least as many sources as the turn number.
    /// </summary>
    public OperationResult<StatisticsCalculation<CastabilityResult>> CalculateCastability(
        CastabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculateCastability(population, request, cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<CastabilityResult>>(bounded),
        };
    }

    /// <summary>
    /// Evaluates one validated castability request.
    /// </summary>
    private OperationResult<StatisticsCalculation<CastabilityResult>> CalculateCastability(
        CanonicalPopulation population,
        CastabilityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Costs is null || request.Costs.Count == 0)
        {
            return Invalid<CastabilityResult>("request.costs must contain at least one cost.");
        }

        if (!TryPrepareManaSources(
                population,
                request.Sources,
                out ManaSourceInput[] sources,
                out string[] groups,
                out ManaCapability[] capabilities,
                out string? sourceError))
        {
            return Invalid<CastabilityResult>(sourceError!);
        }

        long work = (long)request.Costs.Count * Math.Max(1, groups.Length);
        if (work > MaximumCastabilityWork)
        {
            return Invalid<CastabilityResult>(
                $"The request has {request.Costs.Count} costs and {groups.Length} source groups, which is " +
                $"{work} units of work. The limit is {MaximumCastabilityWork} (costs times source groups). " +
                "Check fewer cards or use fewer source groups.");
        }

        if (MembershipBucketEngine.Project(population, groups).Any(
            bucket => BitOperations.PopCount((uint)bucket.MembershipMask) > 1))
        {
            return Invalid<CastabilityResult>(
                "request.sources must not select the same population bucket through multiple source groups.");
        }

        List<CastabilityRow> rows = [];
        foreach (CastabilityCostInput? cost in request.Costs)
        {
            OperationResult<StatisticsCalculation<CastabilityResult>>? failure = EvaluateCost(
                population,
                request,
                cost,
                groups,
                capabilities,
                rows,
                cancellationToken);
            if (failure is { } rejected)
            {
                return rejected;
            }
        }

        string conditioning = request.RequireLandDrops
            ? "Each probability is conditional on seeing at least as many sources as the turn number."
            : "Each probability is unconditional: it does not require a source for every turn.";
        CastabilityResult result = new(
            CreateDerivation(
                request.RequireLandDrops ? "castability-conditional" : "castability-unconditional",
                [
                    "Source groups and W/U/B/R/G/C capabilities were supplied explicitly.",
                    "At most one source is used per turn, so a cost is paid with at most as many sources as the turn.",
                    "Cards seen are 7 plus the turn, less one on the play.",
                    conditioning,
                    "No land, timing, activation, tapped-state, or alternate-cost rule was inferred.",
                ]),
            population.ToSnapshot(),
            request.OnThePlay,
            request.RequireLandDrops,
            sources,
            rows);
        return Exact(result);
    }

    /// <summary>
    /// Evaluates one cost and adds its row; returns a failure result when the cost or the work cannot be accepted.
    /// </summary>
    private OperationResult<StatisticsCalculation<CastabilityResult>>? EvaluateCost(
        CanonicalPopulation population,
        CastabilityRequest request,
        CastabilityCostInput? cost,
        string[] groups,
        ManaCapability[] capabilities,
        List<CastabilityRow> rows,
        CancellationToken cancellationToken)
    {
        if (cost is null || string.IsNullOrWhiteSpace(cost.Key) || cost.Turn < 1)
        {
            return Invalid<CastabilityResult>("Every cost needs a key and a turn of at least 1.");
        }

        if (!TryPrepareManaRequirement(cost.Requirement, out PreparedManaRequirement? requirement, out string? error))
        {
            return Invalid<CastabilityResult>(error!);
        }

        int seen = OpeningHand + cost.Turn - (request.OnThePlay ? 1 : 0);
        if (seen > population.TotalCount)
        {
            return Invalid<CastabilityResult>(
                $"The deck has {population.TotalCount} cards, fewer than the {seen} cards seen by turn {cost.Turn}.");
        }

        StatisticsWorkBudget budget = new(workLimit);
        int turn = cost.Turn;
        MembershipEnumerationResult payable = MembershipBucketEngine.Evaluate(
            population,
            seen,
            groups,
            budget,
            state => (!request.RequireLandDrops || SourcesSeen(state) >= turn) &&
                ManaPaymentMatcher.CanPay(state, capabilities, requirement!, turn),
            cancellationToken);
        if (payable.IsBounded)
        {
            return Bounded<CastabilityResult>(
                budget,
                payable.EstimatedStates,
                population,
                "Check fewer cards, use fewer source groups, or choose an earlier turn.");
        }

        if (!request.RequireLandDrops)
        {
            rows.Add(new CastabilityRow(
                cost.Key,
                turn,
                seen,
                new ExactFraction(payable.FavorableWeight, payable.TotalWeight).ToProbability()));
            return null;
        }

        MembershipEnumerationResult condition = MembershipBucketEngine.Evaluate(
            population,
            seen,
            groups,
            budget,
            state => SourcesSeen(state) >= turn,
            cancellationToken);
        if (condition.IsBounded)
        {
            return Bounded<CastabilityResult>(
                budget,
                condition.EstimatedStates,
                population,
                "Check fewer cards, use fewer source groups, or choose an earlier turn.");
        }

        rows.Add(new CastabilityRow(
            cost.Key,
            turn,
            seen,
            condition.FavorableWeight.IsZero
                ? null
                : new ExactFraction(payable.FavorableWeight, condition.FavorableWeight).ToProbability()));
        return null;
    }

    /// <summary>
    /// Counts the drawn cards that belong to at least one source group.
    /// </summary>
    private static int SourcesSeen(ProjectedDrawState state)
    {
        int count = 0;
        for (int index = 0; index < state.Buckets.Length; index++)
        {
            if (state.Buckets[index].MembershipMask != 0)
            {
                count += state.DrawnByBucket[index];
            }
        }

        return count;
    }
}
