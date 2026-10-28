using MtgMcp.Core.Results;
using static MtgMcp.OnCurve.Tests.OnCurveTestData;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Proves every fixed limit and engine rule a request must satisfy, and that each refusal names the JSON path.
/// </summary>
public sealed class SimulationValidationTests
{
    /// <summary>
    /// Verifies a condition that names no declared group is refused (S02).
    /// </summary>
    [Fact]
    public void Run_WithAnUnknownGroup_IsRefusedWithTheGroupPath()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("lands", 1)],
            [Play("lands")],
            [Condition("x", new SimulationDrawnAtLeast("nope", 1, 1))]);

        AssertRefused(request, "invalid-simulation-group", "conditions[0].check.group");
    }

    /// <summary>
    /// Verifies a rule that names no declared group is refused.
    /// </summary>
    [Fact]
    public void Run_WithARuleOnAnUnknownGroup_IsRefused()
    {
        AssertRefused(
            Request(Distinct(12), [Group("lands", 1)], [Play("land")]),
            "invalid-simulation-group",
            "eachTurn[0].group");
    }

    /// <summary>
    /// Verifies a mulligan that prefers an unknown group is refused.
    /// </summary>
    [Fact]
    public void Run_WithAnUnknownPreferredGroup_IsRefused()
    {
        SimulationMulligan mulligan = new(1, true, new SimulationCountBetween("g", 0, 7), ["missing"]);

        AssertRefused(
            Request(Distinct(12), [Group("g", 1)], mulligan: mulligan),
            "invalid-simulation-group",
            "openingHand.mulligan.bottom.preferGroups[0]");
    }

    /// <summary>
    /// Verifies a request over the work limit is refused and the message states the limit (S03).
    /// </summary>
    [Fact]
    public void Run_OverTheWorkLimit_IsRefusedWithTheLimit()
    {
        SimulationRequest request = Request(
            [Card(1, 100)],
            samples: 100_000,
            turns: 20);

        OperationInvalidInput refused = Refused(request);

        Assert.Equal("simulation-too-large", refused.ReasonCode);
        Assert.Equal("samples", refused.Path);
        Assert.Contains("200000000", refused.Message, StringComparison.Ordinal);
        Assert.Contains("limit of 50000000", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a request exactly at the work limit is accepted by the planner, and one card more is refused.
    /// </summary>
    [Fact]
    public void Plan_AtTheWorkLimit_IsAcceptedAndOneCardOverIsNot()
    {
        SimulationRequest atLimit = Request([Card(1, 100)], samples: 100_000, turns: 5);
        SimulationRequest over = Request([Card(1, 101)], samples: 100_000, turns: 5);

        Assert.IsType<OperationSuccess<SimulationPlan>>(SimulationPlanner.Plan(atLimit).Value);
        Assert.Equal(
            "simulation-too-large",
            Assert.IsType<OperationInvalidInput>(SimulationPlanner.Plan(over).Value).ReasonCode);
    }

    /// <summary>
    /// Verifies huge numbers are refused by their own bound before any product could overflow.
    /// </summary>
    [Fact]
    public void Run_WithHugeNumbers_IsRefusedWithoutOverflow()
    {
        AssertRefused(Request(Distinct(10), samples: int.MaxValue), "invalid-simulation-value", "samples");
        AssertRefused(Request(Distinct(10), turns: int.MaxValue), "invalid-simulation-value", "turns");
        AssertRefused(Request([Card(1, int.MaxValue)]), "invalid-simulation-value", "deck");
        AssertRefused(Request([Card(1, 500), Card(2, int.MaxValue)]), "invalid-simulation-value", "deck");
        AssertRefused(
            Request(Distinct(10), [Group("g", 1)], [Play("g", maxPerTurn: int.MaxValue)]),
            "invalid-simulation-value",
            "eachTurn[0].maxPerTurn");
        AssertRefused(
            Request(Distinct(10), [Group("g", 1)], [Cast("g", int.MaxValue)]),
            "invalid-simulation-value",
            "eachTurn[0].cost");
    }

    /// <summary>
    /// Verifies a card in two groups is refused and names the entry and both groups (S04).
    /// </summary>
    [Fact]
    public void Run_WithOverlappingGroups_NamesTheEntry()
    {
        SimulationRequest request = Request(Distinct(12), [Group("a", 1, 2), Group("b", 2, 3)]);

        OperationInvalidInput refused = Refused(request);

        Assert.Equal("overlapping-groups", refused.ReasonCode);
        Assert.Contains(Id(2).ToString("D"), refused.Message, StringComparison.Ordinal);
        Assert.Contains("group a and group b", refused.Message, StringComparison.Ordinal);
        Assert.Equal("groups[1].select", refused.Path);
    }

    /// <summary>
    /// Verifies group IDs must be unique, entries must be in the deck, and an entry cannot repeat in a group.
    /// </summary>
    [Fact]
    public void Run_WithBadGroups_IsRefused()
    {
        AssertRefused(
            Request(Distinct(12), [Group("a", 1), Group("a", 2)]),
            "invalid-simulation-group",
            "groups[1].id");
        AssertRefused(Request(Distinct(12), [Group(" ", 1)]), "invalid-simulation-group", "groups[0].id");
        AssertRefused(Request(Distinct(12), [Group("a", 99)]), "unknown-entry-id", "groups[0].select");
        AssertRefused(Request(Distinct(12), [Group("a", 1, 1)]), "invalid-simulation-group", "groups[0].select");
    }

    /// <summary>
    /// Verifies <c>pick: listed-order</c> needs a group chosen by entry IDs (S11).
    /// </summary>
    [Fact]
    public void Run_WithListedOrderOnAnUnorderedGroup_IsRefused()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("g", 1, 2)],
            [Play("g", SimulationPick.ListedOrder)]);

        AssertRefused(request, "invalid-simulation-rule", "eachTurn[0].pick");
    }

    /// <summary>
    /// Verifies <c>target-first</c> needs a readable target cost, belongs to <c>play</c>, and is not allowed
    /// without that pick.
    /// </summary>
    [Fact]
    public void Run_WithTargetFirstMisused_IsRefused()
    {
        SimulationGroup[] groups = [Group("g", 1, 2)];

        AssertRefused(
            Request(Distinct(12), groups, [Play("g", SimulationPick.TargetFirst)]),
            "missing-field",
            "eachTurn[0].targetCost");
        AssertRefused(
            Request(Distinct(12), groups, [Play("g", SimulationPick.TargetFirst, targetCost: "{X}")]),
            "invalid-simulation-check",
            "eachTurn[0].targetCost");
        AssertRefused(
            Request(Distinct(12), groups, [Play("g", targetCost: "{G}")]),
            "field-not-allowed",
            "eachTurn[0].targetCost");
        AssertRefused(
            Request(Distinct(12), groups, [Cast("g", 1, SimulationPick.TargetFirst)]),
            "invalid-simulation-rule",
            "eachTurn[0].pick");
    }

    /// <summary>
    /// Verifies group colors must be W, U, B, R, G, C, or <c>any</c>, and <c>byEntry</c> must name group entries once.
    /// </summary>
    [Fact]
    public void Run_WithBadProduces_IsRefused()
    {
        AssertRefused(
            Request(Distinct(12), [Colored("g", ["X"], [1])]),
            "invalid-colors",
            "groups[0].produces.default");
        AssertRefused(
            Request(Distinct(12), [Colored("g", [], [1])]),
            "invalid-colors",
            "groups[0].produces.default");
        AssertRefused(
            Request(Distinct(12), [Colored("g", ["G"], [1], (["R", "?"], [1]))]),
            "invalid-colors",
            "groups[0].produces.byEntry[0].colors");
        AssertRefused(
            Request(Distinct(12), [Colored("g", ["G"], [1], (["R"], [2]))]),
            "invalid-simulation-group",
            "groups[0].produces.byEntry[0].entryIds");
        AssertRefused(
            Request(Distinct(12), [Colored("g", ["G"], [1, 2], (["R"], [1]), (["U"], [1]))]),
            "invalid-simulation-group",
            "groups[0].produces.byEntry[1].entryIds");
    }

    /// <summary>
    /// Verifies <c>any</c> adds one mana of any of the five colors and not colorless.
    /// </summary>
    [Theory]
    [InlineData("{W}", true)]
    [InlineData("{G}", true)]
    [InlineData("{C}", false)]
    public void Run_WithAnyColor_AddsTheFiveColors(string cost, bool expected)
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["any"], [1])],
            [Play("lands")],
            [Condition("c", CanCastOn(cost, 1))],
            turns: 1);

        Assert.Equal(expected, RunOrdered(request).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies <c>manaFromByEntry</c> must name entries of the rule's group, once each.
    /// </summary>
    [Fact]
    public void Run_WithBadManaFromOverride_IsRefused()
    {
        AssertRefused(
            Request(
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
                        (SimulationManaFrom.NextTurn, [2]))]),
            "invalid-simulation-rule",
            "eachTurn[0].manaFromByEntry[0].entryIds");
    }

    /// <summary>
    /// Verifies counts of rules, groups, conditions, and the opening hand are bounded.
    /// </summary>
    [Fact]
    public void Run_WithTooManyItems_IsRefused()
    {
        List<SimulationDeckEntry> deck = Distinct(40);
        List<SimulationGroup> groups = [.. Enumerable.Range(1, 21).Select(index => Group($"g{index}", index))];
        List<SimulationRule> rules = [.. Enumerable.Range(1, 21).Select(_ => Play("g1"))];
        List<SimulationCondition> conditions = [
            .. Enumerable.Range(1, 21).Select(index => Condition($"c{index}", new SimulationManaAtLeast(0, 1)))];

        AssertRefused(Request(deck, groups), "invalid-simulation-value", "groups");
        AssertRefused(Request(deck, [Group("g1", 1)], rules), "invalid-simulation-value", "eachTurn");
        AssertRefused(Request(deck, conditions: conditions), "invalid-simulation-value", "conditions");
        AssertRefused(Request(deck, conditions: []), "invalid-simulation-value", "conditions");
        AssertRefused(Request(deck, handSize: 0), "invalid-simulation-value", "openingHand.size");
        AssertRefused(Request(deck, handSize: 16), "invalid-simulation-value", "openingHand.size");
        AssertRefused(Request(Distinct(5)), "invalid-simulation-value", "openingHand.size");
    }

    /// <summary>
    /// Verifies samples, returned samples, and turns are bounded.
    /// </summary>
    [Fact]
    public void Run_WithSamplesOutOfRange_IsRefused()
    {
        List<SimulationDeckEntry> deck = Distinct(10);

        AssertRefused(Request(deck, samples: 0), "invalid-simulation-value", "samples");
        AssertRefused(Request(deck, samples: 100_001), "invalid-simulation-value", "samples");
        AssertRefused(Request(deck, sampleHands: -1), "invalid-simulation-value", "sampleHands");
        AssertRefused(Request(deck, sampleHands: 11, samples: 20), "invalid-simulation-value", "sampleHands");
        AssertRefused(Request(deck, sampleHands: 3, samples: 2), "invalid-simulation-value", "sampleHands");
        AssertRefused(Request(deck, turns: 0), "invalid-simulation-value", "turns");
        AssertRefused(Request(deck, turns: 21), "invalid-simulation-value", "turns");
    }

    /// <summary>
    /// Verifies the deck must have unique entries and positive quantities.
    /// </summary>
    [Fact]
    public void Run_WithABadDeck_IsRefused()
    {
        AssertRefused(Request([]), "invalid-simulation-value", "deck");
        AssertRefused(Request([Card(1, 8), Card(1, 2)]), "invalid-simulation-value", "deck");
        AssertRefused(Request([Card(1, 0), Card(2, 9)]), "invalid-simulation-value", "deck");
        AssertRefused(Request([new SimulationDeckEntry(Guid.Empty, 9)]), "invalid-simulation-value", "deck");
        AssertRefused(Request([Card(1, 500), Card(2, 1)]), "invalid-simulation-value", "deck");
    }

    /// <summary>
    /// Verifies the small counts of a rule are bounded.
    /// </summary>
    [Fact]
    public void Run_WithRuleNumbersOutOfRange_IsRefused()
    {
        List<SimulationDeckEntry> deck = Distinct(12);
        SimulationGroup[] groups = [Group("g", 1)];

        AssertRefused(
            Request(deck, groups, [Play("g", maxPerTurn: 0)]),
            "invalid-simulation-value",
            "eachTurn[0].maxPerTurn");
        AssertRefused(
            Request(deck, groups, [Play("g", addsMana: -1)]),
            "invalid-simulation-value",
            "eachTurn[0].addsMana");
        AssertRefused(Request(deck, groups, [Cast("g", -1)]), "invalid-simulation-value", "eachTurn[0].cost");
        AssertRefused(
            Request(deck, groups, [Draw("g", 1, 0, SimulationDrawEffect.Once)]),
            "invalid-simulation-value",
            "eachTurn[0].cards");
        AssertRefused(
            Request(deck, groups, [Draw("g", 1, 100, SimulationDrawEffect.Once)]),
            "invalid-simulation-value",
            "eachTurn[0].cards");
        AssertRefused(
            Request(deck, groups, [Draw("g", 1, 1, (SimulationDrawEffect)9)]),
            "invalid-simulation-value",
            "eachTurn[0].effect");
        AssertRefused(
            Request(deck, groups, [Play("g", (SimulationPick)9)]),
            "invalid-simulation-value",
            "eachTurn[0].pick");
        AssertRefused(
            Request(deck, groups, [Play("g", manaFrom: (SimulationManaFrom)9)]),
            "invalid-simulation-value",
            "eachTurn[0].manaFrom");
    }

    /// <summary>
    /// Verifies mulligan limits must leave at least one card in the kept hand.
    /// </summary>
    [Fact]
    public void Run_WithMulliganOutOfRange_IsRefused()
    {
        List<SimulationDeckEntry> deck = Distinct(12);
        SimulationGroup[] groups = [Group("g", 1)];
        SimulationCheck keep = new SimulationCountBetween("g", 0, 7);

        AssertRefused(
            Request(deck, groups, mulligan: new SimulationMulligan(-1, true, keep, [])),
            "invalid-simulation-value",
            "openingHand.mulligan.maxMulligans");
        AssertRefused(
            Request(deck, groups, mulligan: new SimulationMulligan(11, true, keep, [])),
            "invalid-simulation-value",
            "openingHand.mulligan.maxMulligans");
        AssertRefused(
            Request(deck, groups, handSize: 3, mulligan: new SimulationMulligan(3, false, keep, [])),
            "invalid-simulation-value",
            "openingHand.mulligan.maxMulligans");
        Assert.IsType<OperationSuccess<SimulationReport>>(
            SimulationEngine.Run(
                Request(deck, groups, handSize: 3, mulligan: new SimulationMulligan(3, true, keep, []))).Value);
    }

    /// <summary>
    /// Verifies a keep rule accepts only <c>count-between</c>, <c>all</c>, and <c>any</c>, and a condition accepts
    /// every kind but <c>count-between</c>.
    /// </summary>
    [Fact]
    public void Run_WithTheWrongCheckKind_IsRefused()
    {
        SimulationGroup[] groups = [Group("g", 1)];

        AssertRefused(
            Request(
                Distinct(12),
                groups,
                mulligan: new SimulationMulligan(1, true, new SimulationManaAtLeast(1, 1), [])),
            "invalid-simulation-check",
            "openingHand.mulligan.keepWhen");
        AssertRefused(
            Request(Distinct(12), groups, conditions: [Condition("c", new SimulationCountBetween("g", 0, 1))]),
            "invalid-simulation-check",
            "conditions[0].check");
        AssertRefused(
            Request(
                Distinct(12),
                groups,
                mulligan: new SimulationMulligan(
                    1,
                    true,
                    new SimulationAll([new SimulationCountBetween("g", 0, 1), new SimulationDrawnAtLeast("g", 1, 1)]),
                    [])),
            "invalid-simulation-check",
            "openingHand.mulligan.keepWhen.checks[1]");
    }

    /// <summary>
    /// Verifies <c>all</c> and <c>any</c> are bounded in depth and size and cannot be empty.
    /// </summary>
    [Fact]
    public void Run_WithBadNesting_IsRefused()
    {
        SimulationCheck deep = new SimulationManaAtLeast(0, 1);
        for (int level = 0; level < 5; level++)
        {
            deep = new SimulationAll([deep]);
        }

        SimulationCheck wide = new SimulationAny(
            [.. Enumerable.Range(0, 21).Select(_ => (SimulationCheck)new SimulationManaAtLeast(0, 1))]);

        AssertRefused(Request(Distinct(12), conditions: [Condition("deep", deep)]), "invalid-simulation-check", null);
        AssertRefused(Request(Distinct(12), conditions: [Condition("wide", wide)]), "invalid-simulation-check", null);
        AssertRefused(
            Request(Distinct(12), conditions: [Condition("empty", new SimulationAll([]))]),
            "invalid-simulation-check",
            "conditions[0].check");
    }

    /// <summary>
    /// Verifies condition IDs must be unique and non-blank.
    /// </summary>
    [Fact]
    public void Run_WithBadConditionIds_IsRefused()
    {
        SimulationCondition one = Condition("same", new SimulationManaAtLeast(0, 1));

        AssertRefused(Request(Distinct(12), conditions: [one, one]), "invalid-simulation-check", "conditions[1].id");
        AssertRefused(
            Request(Distinct(12), conditions: [Condition(" ", new SimulationManaAtLeast(0, 1))]),
            "invalid-simulation-check",
            "conditions[0].id");
    }

    /// <summary>
    /// Verifies the numbers and turns inside checks are bounded by the request.
    /// </summary>
    [Fact]
    public void Run_WithCheckNumbersOutOfRange_IsRefused()
    {
        SimulationGroup[] groups = [Group("g", 1)];

        AssertRefused(
            Request(Distinct(12), groups, conditions: [Condition("a", new SimulationManaAtLeast(-1, 1))]),
            "invalid-simulation-value",
            "conditions[0].check.amount");
        AssertRefused(
            Request(Distinct(12), groups, conditions: [Condition("a", new SimulationManaAtLeast(1, 4))]),
            "invalid-simulation-value",
            "conditions[0].check.turn");
        AssertRefused(
            Request(Distinct(12), groups, conditions: [Condition("a", new SimulationDrawnAtLeast("g", 0, 1))]),
            "invalid-simulation-value",
            "conditions[0].check.count");
        AssertRefused(
            Request(Distinct(12), groups, conditions: [Condition("a", new SimulationDrawnAtLeast("g", 1, 0))]),
            "invalid-simulation-value",
            "conditions[0].check.byTurn");
        AssertRefused(
            Request(
                Distinct(12),
                groups,
                conditions: [Condition("a", new SimulationOnBattlefieldAtLeast("g", 100, 1))]),
            "invalid-simulation-value",
            "conditions[0].check.count");
        AssertRefused(
            Request(
                Distinct(12),
                groups,
                mulligan: new SimulationMulligan(1, true, new SimulationCountBetween("g", 3, 2), [])),
            "invalid-simulation-value",
            "openingHand.mulligan.keepWhen");
    }

    /// <summary>
    /// Verifies <c>can-cast</c> needs exactly one of <c>turn</c> and <c>byTurn</c>, a readable cost, and a deck entry
    /// for <c>inHand</c>.
    /// </summary>
    [Fact]
    public void Run_WithABadCanCast_IsRefused()
    {
        AssertRefused(
            Request(Distinct(12), conditions: [Condition("a", new SimulationCanCast("{G}", null, null, null))]),
            "invalid-simulation-check",
            "conditions[0].check");
        AssertRefused(
            Request(Distinct(12), conditions: [Condition("a", new SimulationCanCast("{G}", 1, 2, null))]),
            "invalid-simulation-check",
            "conditions[0].check");
        AssertRefused(
            Request(Distinct(12), conditions: [Condition("a", new SimulationCanCast("{G}", 9, null, null))]),
            "invalid-simulation-value",
            "conditions[0].check.turn");
        AssertRefused(
            Request(Distinct(12), conditions: [Condition("a", new SimulationCanCast("{X}{G}", 1, null, null))]),
            "invalid-simulation-check",
            "conditions[0].check.manaCost");
        AssertRefused(
            Request(Distinct(12), conditions: [Condition("a", new SimulationCanCast("{G}", 1, null, Id(99)))]),
            "unknown-entry-id",
            "conditions[0].check.inHand");
    }

    /// <summary>
    /// Verifies a missing request is refused rather than thrown.
    /// </summary>
    [Fact]
    public void Run_WithNoRequest_IsRefused()
    {
        OperationResult<SimulationReport> result = SimulationEngine.Run(null);

        Assert.Equal("invalid-simulation-value", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a canceled token stops the run.
    /// </summary>
    [Fact]
    public void Run_WithACanceledToken_Throws()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => SimulationEngine.Run(Request(Distinct(12), samples: 10), cancellation.Token));
    }

    /// <summary>
    /// Verifies the mana cost reader used by callers reports its failures with the given path.
    /// </summary>
    [Fact]
    public void CheckManaCost_ReportsUnreadableCosts()
    {
        Assert.Null(SimulationEngine.CheckManaCost("{2}{B}{G}", "p"));
        OperationInvalidInput refused = SimulationEngine.CheckManaCost("{X}", "p")!;
        Assert.Equal("p", refused.Path);
    }

    /// <summary>
    /// Verifies a request is refused with the given reason and path.
    /// </summary>
    private static void AssertRefused(SimulationRequest request, string reason, string? path)
    {
        OperationInvalidInput refused = Refused(request);

        Assert.Equal(reason, refused.ReasonCode);
        if (path is not null)
        {
            Assert.Equal(path, refused.Path);
        }
    }
}
