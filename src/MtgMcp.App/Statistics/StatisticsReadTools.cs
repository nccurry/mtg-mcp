using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Exposes provider-independent exact mathematics and deterministic deck summaries.
/// </summary>
internal sealed class StatisticsReadTools
{
    /// <summary>
    /// Resolves explicit local-deck selections without interpreting deck format.
    /// </summary>
    private readonly StatisticsDeckResolver resolver;

    /// <summary>
    /// Performs the bounded exact calculations.
    /// </summary>
    private readonly ExactStatisticsCalculator calculator;

    /// <summary>
    /// Creates the read tools around one local read boundary and calculator.
    /// </summary>
    internal StatisticsReadTools(
        StatisticsDeckResolver resolver,
        ExactStatisticsCalculator calculator)
    {
        this.resolver = resolver;
        this.calculator = calculator;
    }

    /// <summary>
    /// Calculates one exact hypergeometric event and complement.
    /// </summary>
    [McpServerTool(Name = "stats_hypergeometric", Title = "Exact Hypergeometric Probability", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Calculates an exact univariate draw probability from explicit population and event inputs.")]
    internal Task<OperationResult<StatisticsCalculation<HypergeometricResult>>> HypergeometricAsync(
        [Description("Complete exact hypergeometric request.")] StatisticsHypergeometricToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolvePopulationAsync(
            request.Population,
            population => calculator.CalculateHypergeometric(
                new HypergeometricRequest(population, request.SuccessGroup, request.DrawCount, request.Event),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Calculates one exact conjunction over overlapping group counts.
    /// </summary>
    [McpServerTool(Name = "stats_multivariate", Title = "Exact Multivariate Probability", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Calculates an exact conjunction over explicitly declared, possibly overlapping groups.")]
    internal Task<OperationResult<StatisticsCalculation<MultivariateResult>>> MultivariateAsync(
        [Description("Complete exact multivariate request.")] StatisticsMultivariateToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolvePopulationAsync(
            request.Population,
            population => calculator.CalculateMultivariate(
                new MultivariateRequest(population, request.DrawCount, request.Conditions),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Calculates exact probability rows from only the supplied draw schedule.
    /// </summary>
    [McpServerTool(Name = "stats_turn_table", Title = "Exact Probability By Turn", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Calculates exact probability rows using an explicit cards-seen schedule without turn-rule inference.")]
    internal Task<OperationResult<StatisticsCalculation<TurnTableResult>>> TurnTableAsync(
        [Description("Complete explicit turn-table request.")] StatisticsTurnTableToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolvePopulationAsync(
            request.Population,
            population => calculator.CalculateTurnTable(
                new TurnTableRequest(
                    population,
                    request.SuccessGroup,
                    request.OpeningHandSize,
                    request.DrawsByTurn,
                    request.Event),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Calculates exact availability of one-use declared mana sources.
    /// </summary>
    [McpServerTool(Name = "stats_mana_availability", Title = "Exact Mana Availability", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Calculates exact payment availability from caller-declared source groups and capabilities.")]
    internal Task<OperationResult<StatisticsCalculation<ManaAvailabilityResult>>> ManaAvailabilityAsync(
        [Description("Complete exact mana-availability request.")] StatisticsManaAvailabilityToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolvePopulationAsync(
            request.Population,
            population => calculator.CalculateManaAvailability(
                new ManaAvailabilityRequest(
                    population,
                    request.DrawCount,
                    request.Sources,
                    request.Requirement,
                    request.MaximumUsableSources),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Calculates exact one-use package assembly.
    /// </summary>
    [McpServerTool(Name = "stats_package_assembly", Title = "Exact Package Assembly", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Calculates whether drawn physical copies can fill every caller-declared package slot exactly once.")]
    internal Task<OperationResult<StatisticsCalculation<PackageAssemblyResult>>> PackageAssemblyAsync(
        [Description("Complete exact package-assembly request.")] StatisticsPackageAssemblyToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolvePopulationAsync(
            request.Population,
            population => calculator.CalculatePackageAssembly(
                new PackageAssemblyRequest(population, request.DrawCount, request.Requirements),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Calculates exact independent mulligan attempts.
    /// </summary>
    [McpServerTool(Name = "stats_mulligan", Title = "Exact Mulligan Schedule", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Calculates exact outcomes for caller-supplied independent attempts, keep constraints, and bottom priority.")]
    internal Task<OperationResult<StatisticsCalculation<MulliganResult>>> MulliganAsync(
        [Description("Complete exact mulligan request.")] StatisticsMulliganToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolvePopulationAsync(
            request.Population,
            population => calculator.CalculateMulligan(
                new MulliganRequest(
                    population,
                    request.Attempts,
                    request.KeepConditions,
                    request.BottomPriority,
                    request.FinalConditions),
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Finds the minimum count meeting one exact monotone probability target.
    /// </summary>
    [McpServerTool(Name = "stats_minimum_count", Title = "Exact Minimum Copy Count", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Finds the lowest bounded count meeting an exact target for a supported monotone event.")]
    internal Task<OperationResult<StatisticsCalculation<MinimumCountResult>>> MinimumCountAsync(
        [Description("Complete exact inverse-count request.")] StatisticsMinimumCountToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(calculator.CalculateMinimumCount(
            new MinimumCountRequest(
                request.Event,
                request.TargetNumerator,
                request.TargetDenominator,
                request.MinimumCount,
                request.MaximumCount),
            cancellationToken));
    }

    /// <summary>
    /// Summarizes selected stored deck fields and caller-supplied numeric series.
    /// </summary>
    [McpServerTool(Name = "stats_deck_summary", Title = "Deterministic Deck Summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Summarizes an exact deck revision without provider lookup, format policy, or legality inference.")]
    internal async Task<OperationResult<StatisticsCalculation<DeckSummaryResult>>> DeckSummaryAsync(
        [Description("Revisioned deck selection and caller-supplied summary options.")] DeckSummarySourceInput request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationResult<DeckSummaryRequest> resolved = await resolver.ResolveSummaryAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        return resolved switch
        {
            OperationSuccess<DeckSummaryRequest> success =>
                calculator.CalculateDeckSummary(success.Data, cancellationToken),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Resolves one population and exhaustively propagates every operation result case.
    /// </summary>
    private async Task<OperationResult<StatisticsCalculation<T>>> ResolvePopulationAsync<T>(
        StatisticsPopulationInput populationInput,
        Func<StatisticsPopulation, OperationResult<StatisticsCalculation<T>>> calculate,
        CancellationToken cancellationToken)
    {
        OperationResult<StatisticsPopulation> resolved = await resolver.ResolvePopulationAsync(
            populationInput,
            cancellationToken).ConfigureAwait(false);
        return resolved switch
        {
            OperationSuccess<StatisticsPopulation> success => calculate(success.Data),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}
