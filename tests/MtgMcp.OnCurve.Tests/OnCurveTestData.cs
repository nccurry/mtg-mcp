using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Builds small, stable simulation requests and runs them with a fixed library order, so a test can state exactly
/// which cards are drawn.
/// </summary>
internal static class OnCurveTestData
{
    /// <summary>
    /// Creates one stable identifier whose lexical and numeric order agree.
    /// </summary>
    internal static Guid Id(int value)
    {
        return new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Creates a deck entry with the stable identifier for a small number.
    /// </summary>
    internal static SimulationDeckEntry Card(int id, int quantity = 1)
    {
        return new SimulationDeckEntry(Id(id), quantity);
    }

    /// <summary>
    /// Creates a group of entries that has no order and adds generic mana only.
    /// </summary>
    internal static SimulationGroup Group(string id, params int[] entryIds)
    {
        return new SimulationGroup(id, [.. entryIds.Select(Id)], false, null);
    }

    /// <summary>
    /// Creates a group of entries in the order the caller listed them.
    /// </summary>
    internal static SimulationGroup Listed(string id, params int[] entryIds)
    {
        return new SimulationGroup(id, [.. entryIds.Select(Id)], true, null);
    }

    /// <summary>
    /// Creates a group whose cards add the default colors, with optional colors for single entries.
    /// </summary>
    internal static SimulationGroup Colored(
        string id,
        string[] defaultColors,
        int[] entryIds,
        params (string[] Colors, int[] EntryIds)[] overrides)
    {
        return new SimulationGroup(
            id,
            [.. entryIds.Select(Id)],
            false,
            new SimulationProduces(
                defaultColors,
                [.. overrides.Select(item => new SimulationProducesOverride(
                    [.. item.EntryIds.Select(Id)],
                    item.Colors))]));
    }

    /// <summary>
    /// Creates a <c>play</c> rule.
    /// </summary>
    internal static SimulationRule Play(
        string group,
        SimulationPick pick = SimulationPick.Random,
        int maxPerTurn = 1,
        int addsMana = 1,
        SimulationManaFrom manaFrom = SimulationManaFrom.SameTurn,
        string? targetCost = null,
        params (SimulationManaFrom ManaFrom, IReadOnlyList<int> EntryIds)[] overrides)
    {
        return new SimulationPlay(
            group,
            pick,
            maxPerTurn,
            addsMana,
            manaFrom,
            [.. overrides.Select(item => new SimulationManaFromOverride([.. item.EntryIds.Select(Id)], item.ManaFrom))],
            targetCost);
    }

    /// <summary>
    /// Creates a <c>cast-if-able</c> rule.
    /// </summary>
    internal static SimulationRule Cast(
        string group,
        int cost,
        SimulationPick pick = SimulationPick.Random,
        int maxPerTurn = 1,
        int addsMana = 1,
        SimulationManaFrom manaFrom = SimulationManaFrom.SameTurn)
    {
        return new SimulationCastIfAble(group, pick, maxPerTurn, cost, addsMana, manaFrom, []);
    }

    /// <summary>
    /// Creates a <c>draw-cards</c> rule.
    /// </summary>
    internal static SimulationRule Draw(
        string group,
        int cost,
        int cards,
        SimulationDrawEffect effect,
        SimulationPick pick = SimulationPick.Random,
        int maxPerTurn = 1)
    {
        return new SimulationDrawCards(group, pick, maxPerTurn, cost, cards, effect);
    }

    /// <summary>
    /// Creates a condition that always holds, for tests that read the sample record instead.
    /// </summary>
    internal static SimulationCondition Always()
    {
        return new SimulationCondition("always", new SimulationManaAtLeast(0, 1));
    }

    /// <summary>
    /// Creates a named condition.
    /// </summary>
    internal static SimulationCondition Condition(string id, SimulationCheck check)
    {
        return new SimulationCondition(id, check);
    }

    /// <summary>
    /// Creates a <c>can-cast</c> check for one turn, with an optional entry that must be in hand.
    /// </summary>
    internal static SimulationCheck CanCastOn(string cost, int turn, int? inHand = null)
    {
        return new SimulationCanCast(cost, turn, null, inHand is null ? null : Id(inHand.Value));
    }

    /// <summary>
    /// Creates a complete valid request that a test adjusts with named arguments.
    /// </summary>
    internal static SimulationRequest Request(
        IReadOnlyList<SimulationDeckEntry> deck,
        IReadOnlyList<SimulationGroup>? groups = null,
        IReadOnlyList<SimulationRule>? rules = null,
        IReadOnlyList<SimulationCondition>? conditions = null,
        int turns = 3,
        int samples = 1,
        int sampleHands = 0,
        ulong seed = 1,
        int handSize = 7,
        bool onThePlay = true,
        SimulationMulligan? mulligan = null)
    {
        return new SimulationRequest(
            deck,
            groups ?? [],
            new SimulationOpeningHand(handSize, onThePlay, mulligan),
            rules ?? [],
            turns,
            conditions ?? [Always()],
            samples,
            sampleHands,
            seed,
            "test");
    }

    /// <summary>
    /// Creates a deck of distinct single cards for entry numbers one through <paramref name="count"/>.
    /// </summary>
    internal static List<SimulationDeckEntry> Distinct(int count)
    {
        return [.. Enumerable.Range(1, count).Select(id => Card(id))];
    }

    /// <summary>
    /// Plays one game with the library in deck order and returns its full record and the trial for condition reads.
    /// </summary>
    internal static (SimulationSampleOutcome Outcome, SimulationTrial Trial) RunOrdered(SimulationRequest request)
    {
        SimulationPlan plan = Plan(request);
        SimulationTrial trial = new(plan, new SplitMix64Random(request.Seed), CancellationToken.None)
        {
            KeepDeckOrder = true,
        };
        return (trial.Run(1, record: true), trial);
    }

    /// <summary>
    /// Returns the compiled plan of a request that must be valid.
    /// </summary>
    internal static SimulationPlan Plan(SimulationRequest request)
    {
        OperationResult<SimulationPlan> result = SimulationPlanner.Plan(request);
        Assert.True(result.Value is OperationSuccess<SimulationPlan>, result.Value.ToString());
        return Assert.IsType<OperationSuccess<SimulationPlan>>(result.Value).Data;
    }

    /// <summary>
    /// Runs a request that must succeed and returns its report.
    /// </summary>
    internal static SimulationReport Report(SimulationRequest request)
    {
        OperationResult<SimulationReport> result = SimulationEngine.Run(request, TestContext.Current.CancellationToken);
        Assert.True(result.Value is OperationSuccess<SimulationReport>, result.Value.ToString());
        return Assert.IsType<OperationSuccess<SimulationReport>>(result.Value).Data;
    }

    /// <summary>
    /// Runs a request that must be refused and returns the failure.
    /// </summary>
    internal static OperationInvalidInput Refused(SimulationRequest request)
    {
        OperationResult<SimulationReport> result = SimulationEngine.Run(request, TestContext.Current.CancellationToken);
        return Assert.IsType<OperationInvalidInput>(result.Value);
    }
}
