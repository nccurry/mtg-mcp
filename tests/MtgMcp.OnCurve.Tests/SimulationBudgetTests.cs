using System.Diagnostics;
using MtgMcp.Core.Results;
using static MtgMcp.OnCurve.Tests.OnCurveTestData;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Proves that an accepted request has bounded real cost: the mana units and the operation estimate are limited
/// before anything runs, a worst-case shape at the limit finishes quickly, and a long run can be canceled.
/// </summary>
public sealed class SimulationBudgetTests
{
    /// <summary>
    /// Builds the most expensive shape: many units of mana, 20 can-cast checks of 16 symbols, and a target-first
    /// rule over a large group.
    /// </summary>
    private static SimulationRequest Heavy(int samples)
    {
        List<SimulationDeckEntry> deck = [.. Enumerable.Range(1, 150).Select(id => Card(id, 3))];
        string sixteen = string.Concat(Enumerable.Repeat("{W/U}", 16));
        List<SimulationCondition> conditions =
        [
            .. Enumerable.Range(0, 20).Select(index => Condition(
                $"c{index}",
                new SimulationCanCast(sixteen, null, 20, null))),
        ];
        return Request(
            deck,
            [Colored("all", ["W", "U"], [.. Enumerable.Range(1, 150)])],
            [Play("all", SimulationPick.TargetFirst, 5, 1, SimulationManaFrom.SameTurn, sixteen)],
            conditions,
            turns: 20,
            samples: samples);
    }

    /// <summary>
    /// Verifies the largest sample count the planner accepts for the heaviest shape finishes within seconds.
    /// </summary>
    [Fact]
    public void HeaviestShape_AtTheLimit_FinishesQuickly()
    {
        int low = 1;
        int high = 5_000;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (SimulationPlanner.Plan(Heavy(middle)).Value is OperationSuccess<SimulationPlan>)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        OperationResult<SimulationPlan> planned = SimulationPlanner.Plan(Heavy(low));
        Assert.True(planned.Value is OperationSuccess<SimulationPlan>, planned.Value.ToString());
        Assert.IsType<OperationInvalidInput>(SimulationPlanner.Plan(Heavy(low + 1)).Value);
        Stopwatch stopwatch = Stopwatch.StartNew();
        Report(Heavy(low));
        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0, 30);
    }

    /// <summary>
    /// Builds the draw shape: a group of drawing cards that fire 99 times a turn and a random play rule that fires 99
    /// times a turn, so each turn does thousands of hand scans.
    /// </summary>
    private static SimulationRequest FiringShape(int samples)
    {
        Guid draw = new(1000, 0, 0, new byte[8]);
        List<Guid> others = [.. Enumerable.Range(1, 149).Select(id => new Guid(id, 0, 0, new byte[8]))];
        List<SimulationDeckEntry> deck = [new(draw, 351), .. others.Select(id => new SimulationDeckEntry(id, 1))];
        List<SimulationGroup> groups =
        [
            new("d", [draw], true, null),
            new("t", others, true, new SimulationProduces(["W"], [])),
        ];
        List<SimulationRule> rules =
        [
            new(new SimulationDrawCards("d", SimulationPick.Random, 99, 0, 99, SimulationDrawEffect.Once)),
            new(new SimulationPlay("t", SimulationPick.Random, 99, 0, SimulationManaFrom.SameTurn, [], null)),
        ];
        return new SimulationRequest(
            deck,
            groups,
            new SimulationOpeningHand(15, true, null),
            rules,
            1,
            [new("m", new SimulationCheck(new SimulationManaAtLeast(1, 1)))],
            samples,
            0,
            1,
            "firing");
    }

    /// <summary>
    /// Builds the target-first shape: a rule that fires many times a turn and, at each firing, tests every candidate of
    /// a large group against a cost that can never be paid.
    /// </summary>
    private static SimulationRequest TargetFirstShape(int samples)
    {
        Guid draw = new(1000, 0, 0, new byte[8]);
        List<Guid> others = [.. Enumerable.Range(1, 149).Select(id => new Guid(id, 0, 0, new byte[8]))];
        List<SimulationDeckEntry> deck = [new(draw, 351), .. others.Select(id => new SimulationDeckEntry(id, 1))];
        string cost = string.Concat(Enumerable.Repeat("{U}", 16));
        return new SimulationRequest(
            deck,
            [
                new SimulationGroup("d", [draw], true, null),
                new SimulationGroup("t", others, true, new SimulationProduces(["W"], [])),
            ],
            new SimulationOpeningHand(15, true, null),
            [
                new SimulationRule(
                    new SimulationDrawCards("d", SimulationPick.Random, 99, 0, 99, SimulationDrawEffect.Once)),
                new SimulationRule(
                    new SimulationPlay("t", SimulationPick.TargetFirst, 99, 10, SimulationManaFrom.SameTurn, [], cost)),
            ],
            1,
            [new("c", new SimulationCheck(new SimulationManaAtLeast(1, 1)))],
            samples,
            0,
            1,
            "target-first");
    }

    /// <summary>
    /// Finds the largest sample count the planner accepts for a shape, runs it, and returns the seconds it took.
    /// </summary>
    private static double SecondsAtTheLimit(Func<int, SimulationRequest> shape)
    {
        int low = 1;
        int high = 100_000;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (SimulationPlanner.Plan(shape(middle)).Value is OperationSuccess<SimulationPlan>)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        Assert.True(SimulationPlanner.Plan(shape(low)).Value is OperationSuccess<SimulationPlan>);
        Stopwatch stopwatch = Stopwatch.StartNew();
        _ = SimulationEngine.Run(shape(low));
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Verifies a rule that fires 99 times a turn is charged for every firing, so the largest accepted request still
    /// finishes in seconds (it took 48.8 seconds when firings were not counted).
    /// </summary>
    [Fact]
    public void ManyFiringsPerTurn_AtTheLimit_FinishQuickly()
    {
        Assert.InRange(SecondsAtTheLimit(FiringShape), 0, 6);
    }

    /// <summary>
    /// Verifies a target-first rule that fires many times is charged for the candidate loop at every firing (it took
    /// 23.2 seconds when it was charged once).
    /// </summary>
    [Fact]
    public void TargetFirstFiringManyTimes_AtTheLimit_FinishesQuickly()
    {
        Assert.InRange(SecondsAtTheLimit(TargetFirstShape), 0, 6);
    }

    /// <summary>
    /// Verifies a request that would put more than the limit of mana units in play is refused.
    /// </summary>
    [Fact]
    public void ManaUnits_OverTheLimit_AreRefused()
    {
        SimulationRequest request = Request(
            Distinct(150),
            [Group("g", [.. Enumerable.Range(1, 100)])],
            [Play("g", maxPerTurn: 99, addsMana: 99)],
            turns: 20);

        OperationInvalidInput refused = Refused(request);

        Assert.Equal("simulation-too-large", refused.ReasonCode);
        Assert.Equal("eachTurn", refused.Path);
        Assert.Contains("1000", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the operation estimate refuses a request that is small by samples times turns times cards but has
    /// many costly checks, and the message states the limit.
    /// </summary>
    [Fact]
    public void OperationEstimate_OverTheLimit_IsRefusedWithTheLimit()
    {
        OperationInvalidInput refused = Refused(Heavy(5_000));

        Assert.Equal("simulation-too-large", refused.ReasonCode);
        Assert.Contains("limit of 400000000", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a huge generic cost cannot wrap and report a hit once spent mana is added.
    /// </summary>
    [Fact]
    public void CanCast_WithAHugeGenericCost_IsRefused()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1, 2])],
            [Play("lands"), Cast("lands", 0)],
            [Condition("huge", CanCastOn("{2147483647}", 2))],
            turns: 2);

        OperationInvalidInput refused = Refused(request);

        Assert.Equal("invalid-simulation-check", refused.ReasonCode);
        Assert.Equal("conditions[0].check.manaCost", refused.Path);
    }

    /// <summary>
    /// Verifies ID length, the byTurn path, the missing target cost code, and the manaFromByEntry code.
    /// </summary>
    [Fact]
    public void Refusals_HaveTheRightPathsAndCodes()
    {
        string longId = new('x', 65);

        Assert.Equal("groups[0].id", Refused(Request(Distinct(12), [Group(longId, 1)])).Path);
        Assert.Equal(
            "conditions[0].id",
            Refused(Request(Distinct(12), conditions: [Condition(longId, new SimulationManaAtLeast(0, 1))])).Path);
        OperationInvalidInput byTurn = Refused(Request(
            Distinct(12),
            conditions: [Condition("a", new SimulationCanCast("{G}", null, 9, null))]));
        OperationInvalidInput noCost = Refused(Request(
            Distinct(12),
            [Group("g", 1)],
            [Play("g", SimulationPick.TargetFirst)]));
        OperationInvalidInput timing = Refused(Request(
            Distinct(12),
            [Group("g", 1), Group("h", 2)],
            [
                Play(
                    "g",
                    SimulationPick.Random,
                    1,
                    1,
                    SimulationManaFrom.SameTurn,
                    null,
                    (SimulationManaFrom.NextTurn, [2])),
            ]));

        Assert.Equal("conditions[0].check.byTurn", byTurn.Path);
        Assert.Equal(("missing-field", "eachTurn[0].targetCost"), (noCost.ReasonCode, noCost.Path));
        Assert.Equal("invalid-simulation-rule", timing.ReasonCode);
    }

    /// <summary>
    /// Verifies a run that is canceled while it is sampling stops with a cancellation.
    /// </summary>
    [Fact]
    public async Task Run_CanceledMidRun_Throws()
    {
        using CancellationTokenSource cancellation = new();
        SimulationRequest request = Request(
            [Card(1, 40), Card(2, 59)],
            [Group("lands", 1)],
            [Play("lands")],
            turns: 12,
            samples: 40_000);

        Task<OperationResult<SimulationReport>> run = Task.Run(
            () => SimulationEngine.Run(request, cancellation.Token),
            TestContext.Current.CancellationToken);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
}
