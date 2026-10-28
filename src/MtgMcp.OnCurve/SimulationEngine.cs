using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Runs seeded samples of the fixed engine rules and the rules a request states. Every choice is either a rule of the
/// game, a fixed engine rule, or a value in the request.
/// </summary>
public static class SimulationEngine
{
    /// <summary>
    /// Identifies the fixed engine rules, so a saved result states which rules produced it.
    /// </summary>
    public const string ModelVersion = "simulation-v1";

    /// <summary>
    /// Identifies the random-sequence and shuffle contract.
    /// </summary>
    public const string RandomVersion = "splitmix64-v1";

    /// <summary>
    /// Lists the model limits every result states.
    /// </summary>
    private static readonly string[] Limits =
    [
        "Groups without produces add generic mana only.",
        "Mana is used or lost within its turn; unspent mana does not carry over to the next turn.",
        "Phyrexian life payment is not modeled; a Phyrexian symbol needs its color.",
        "Only the stated groups and rules are applied; cards in no group are drawn and held.",
    ];

    /// <summary>
    /// Reports whether text is a valid fixed-width replay seed and returns its value.
    /// </summary>
    public static bool TryParseSeed(string? text, out ulong seed)
    {
        return OnCurveSeed.TryParse(text, out seed);
    }

    /// <summary>
    /// Formats a seed as the 16 lower-case hexadecimal characters used in requests and results.
    /// </summary>
    public static string FormatSeed(ulong seed)
    {
        return OnCurveSeed.Format(seed);
    }

    /// <summary>
    /// Parses a printed mana cost with the engine's own reader, so callers share its supported symbols.
    /// </summary>
    public static OperationInvalidInput? CheckManaCost(string? manaCost, string path)
    {
        OperationResult<OnCurveManaCost> cost = OnCurveManaSymbols.ReadCost(manaCost);
        return cost.Value is OperationUnsupported unsupported
            ? new OperationInvalidInput("invalid-simulation-check", unsupported.Message, path)
            : null;
    }

    /// <summary>
    /// Checks a request, then runs every sample.
    /// </summary>
    public static OperationResult<SimulationReport> Run(
        SimulationRequest? request,
        CancellationToken cancellationToken = default)
    {
        OperationResult<SimulationPlan> planned = SimulationPlanner.Plan(request);
        if (planned is not OperationSuccess<SimulationPlan> success)
        {
            return OperationFailure.Forward<SimulationPlan, SimulationReport>(planned);
        }

        SimulationPlan plan = success.Data;
        SimulationRequest valid = plan.Request;
        SimulationTrial trial = new(plan, new SplitMix64Random(valid.Seed), cancellationToken);
        int[] hits = new int[plan.ConditionIds.Length];
        int keptByRule = 0;
        int keptAtLimit = 0;
        int libraryEmpty = 0;
        List<SimulationSampleRecord> records = [];
        for (int sample = 1; sample <= valid.Samples; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationSampleOutcome outcome = trial.Run(sample, sample <= valid.SampleHands);
            keptByRule += outcome.KeptByRule ? 1 : 0;
            keptAtLimit += outcome.KeptAtLimit ? 1 : 0;
            libraryEmpty += outcome.LibraryEmpty ? 1 : 0;
            if (outcome.Record is not null)
            {
                records.Add(outcome.Record);
            }

            for (int condition = 0; condition < hits.Length; condition++)
            {
                hits[condition] += trial.ConditionHeld(condition) ? 1 : 0;
            }
        }

        return new OperationSuccess<SimulationReport>(new SimulationReport(
            ModelVersion,
            RandomVersion,
            OnCurveSeed.Format(valid.Seed),
            valid.Samples,
            SimulationFingerprint.Create(valid),
            BuildResults(plan, hits),
            valid.OpeningHand.Mulligan is null ? null : new SimulationMulliganCounts(keptByRule, keptAtLimit),
            libraryEmpty,
            records,
            Limits));
    }

    /// <summary>
    /// Turns the hit counts into rates with 95 percent Wilson intervals, rounded to six places.
    /// </summary>
    private static List<SimulationConditionResult> BuildResults(SimulationPlan plan, int[] hits)
    {
        List<SimulationConditionResult> results = [];
        int samples = plan.Request.Samples;
        for (int index = 0; index < hits.Length; index++)
        {
            OnCurveWilsonInterval interval = OnCurveWilsonIntervalCalculator.Calculate(hits[index], samples);
            results.Add(new SimulationConditionResult(
                plan.ConditionIds[index],
                hits[index],
                Round((double)hits[index] / samples),
                Round(interval.LowerBound),
                Round(interval.UpperBound)));
        }

        return results;
    }

    /// <summary>
    /// Rounds to six decimal places, so every platform writes the same digits.
    /// </summary>
    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
