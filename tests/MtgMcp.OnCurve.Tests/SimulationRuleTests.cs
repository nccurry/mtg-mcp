using static MtgMcp.OnCurve.Tests.OnCurveTestData;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Proves each fixed engine rule one at a time, with the library in deck order so the draws are known: the first
/// seven entries are the opening hand and each later entry is the next draw.
/// </summary>
public sealed class SimulationRuleTests
{
    /// <summary>
    /// Verifies rules apply strictly in the listed order: with mana for one spell, the first listed spell is cast
    /// and the second is not (S07).
    /// </summary>
    [Fact]
    public void Rules_WithManaForOneSpell_FireInListedOrder()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("lands", 1, 2), Group("a", 3), Group("b", 4)],
            [Play("lands", maxPerTurn: 2), Cast("a", 2), Cast("b", 2)],
            turns: 1);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal([0, 0, 1], record.Turns[0].Moved.Select(move => move.Rule));
        Assert.Equal(Id(3), record.Turns[0].Moved[2].EntryId);
        Assert.Equal(2, record.Turns[0].ManaSpent);
    }

    /// <summary>
    /// Verifies reversing the rule order reverses which spell is cast (S07, reversed).
    /// </summary>
    [Fact]
    public void Rules_ListedInReverse_CastTheOtherSpell()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("lands", 1, 2), Group("a", 3), Group("b", 4)],
            [Play("lands", maxPerTurn: 2), Cast("b", 2), Cast("a", 2)],
            turns: 1);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(Id(4), record.Turns[0].Moved[2].EntryId);
    }

    /// <summary>
    /// Verifies <c>maxPerTurn</c> caps the cards one rule moves each turn (S08).
    /// </summary>
    [Fact]
    public void MaxPerTurn_WithThreeQualifyingCards_MovesExactlyTwoPerTurn()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("lands", 1, 2, 3)],
            [Play("lands", maxPerTurn: 2)],
            turns: 3);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal([2, 1, 0], record.Turns.Select(turn => turn.Moved.Count));
    }

    /// <summary>
    /// Verifies mana from a <c>next-turn</c> card counts from the next turn and not the turn it is cast (S09).
    /// </summary>
    [Fact]
    public void ManaFrom_NextTurn_CountsFromTheFollowingTurn()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("lands", 1, 2), Group("ramp", 3)],
            [Play("lands"), Cast("ramp", 2, manaFrom: SimulationManaFrom.NextTurn)],
            turns: 3);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal([1, 2, 3], record.Turns.Select(turn => turn.ManaAvailable));
        Assert.Contains(record.Turns[1].Moved, move => move.EntryId == Id(3));
    }

    /// <summary>
    /// Verifies mana from a <c>same-turn</c> card counts on the turn it is cast (S09, same-turn).
    /// </summary>
    [Fact]
    public void ManaFrom_SameTurn_CountsOnTheTurnItIsCast()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("lands", 1, 2), Group("ramp", 3)],
            [Play("lands"), Cast("ramp", 2, manaFrom: SimulationManaFrom.SameTurn)],
            turns: 3);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal([1, 3, 3], record.Turns.Select(turn => turn.ManaAvailable));
        Assert.Equal(2, record.Turns[1].ManaSpent);
    }

    /// <summary>
    /// Verifies <c>manaFromByEntry</c> lets one rule play cards whose mana arrives at different times.
    /// </summary>
    [Fact]
    public void ManaFromByEntry_OverridesTheRuleDefault()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("lands", 1, 2)],
            [
                Play(
                    "lands",
                    SimulationPick.Random,
                    2,
                    1,
                    SimulationManaFrom.SameTurn,
                    null,
                    (SimulationManaFrom.NextTurn, [2]))],
            turns: 2);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(1, record.Turns[0].ManaAvailable);
        Assert.Equal(2, record.Turns[1].ManaAvailable);
    }

    /// <summary>
    /// Verifies <c>listed-order</c> takes the first listed card that is in hand, on every turn (S10).
    /// </summary>
    [Fact]
    public void Pick_ListedOrder_TakesTheFirstListedCardInHand()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Listed("g", 5, 4, 3)],
            [Play("g", SimulationPick.ListedOrder)],
            turns: 3);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(
            [Id(5), Id(4), Id(3)],
            record.Turns.Select(turn => Assert.Single(turn.Moved).EntryId));
    }

    /// <summary>
    /// Verifies <c>random</c> picks every qualifying card over many games, using the seeded generator.
    /// </summary>
    [Fact]
    public void Pick_Random_ChoosesEachQualifyingCardAboutEquallyOften()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Group("g", 1, 2, 3)],
            [Play("g")],
            turns: 1);
        SimulationPlan plan = Plan(request);
        SimulationTrial trial = new(plan, new SplitMix64Random(7), CancellationToken.None) { KeepDeckOrder = true };
        Dictionary<Guid, int> counts = [];

        for (int sample = 1; sample <= 300; sample++)
        {
            Guid picked = Assert.Single(trial.Run(sample, record: true).Record!.Turns[0].Moved).EntryId;
            counts[picked] = counts.GetValueOrDefault(picked) + 1;
        }

        Assert.Equal(3, counts.Count);
        Assert.All(counts.Values, count => Assert.InRange(count, 70, 130));
    }

    /// <summary>
    /// Verifies <c>target-first</c> plays the land that pays a colored symbol of the target now (S25).
    /// </summary>
    [Fact]
    public void Pick_TargetFirst_PlaysTheLandThatCoversAColoredSymbol()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Colored("lands", ["G"], [1, 2, 3], (["U"], [2]), (["U", "G"], [3]))],
            [
                Play(
                    "lands",
                    SimulationPick.TargetFirst,
                    1,
                    1,
                    SimulationManaFrom.SameTurn,
                    "{1}{U}{U}",
                    (SimulationManaFrom.NextTurn, [3]))],
            turns: 1);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(Id(2), Assert.Single(record.Turns[0].Moved).EntryId);
    }

    /// <summary>
    /// Verifies <c>target-first</c> breaks ties by mana usable this turn, then by the lowest entry ID (S25).
    /// </summary>
    [Fact]
    public void Pick_TargetFirst_BreaksTiesBySameTurnThenLowestEntryId()
    {
        SimulationRequest request = Request(
            Distinct(10),
            [Colored("lands", ["U"], [1, 2, 3, 4, 5])],
            [
                Play(
                    "lands",
                    SimulationPick.TargetFirst,
                    1,
                    1,
                    SimulationManaFrom.SameTurn,
                    "{U}{U}",
                    (SimulationManaFrom.NextTurn, [3]))],
            turns: 5);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(
            [Id(1), Id(2), Id(4), Id(5), Id(3)],
            record.Turns.Select(turn => Assert.Single(turn.Moved).EntryId));
    }

    /// <summary>
    /// Verifies the player on the play skips the draw on turn one and the player on the draw does not.
    /// </summary>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void FirstDraw_IsSkippedOnlyOnThePlay(bool onThePlay, int expectedDrawn)
    {
        SimulationRequest request = Request(Distinct(12), turns: 2, onThePlay: onThePlay);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(expectedDrawn, record.Turns[0].Drawn.Count);
        Assert.Equal(1, record.Turns[1].Drawn.Count);
    }

    /// <summary>
    /// Verifies cards in no group are drawn and held and never move (engine rule 6).
    /// </summary>
    [Fact]
    public void CardsInNoGroup_AreDrawnAndNeverMove()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("lands", 1)],
            [Play("lands")],
            turns: 4);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(Id(1), Assert.Single(record.Turns.SelectMany(turn => turn.Moved)).EntryId);
    }

    /// <summary>
    /// Verifies a land drawn by a <c>draw-cards</c> rule listed before the play rule is played the same turn (S14).
    /// </summary>
    [Fact]
    public void DrawCards_ListedBeforePlay_MakesTheDrawnLandPlayableThatTurn()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("d", 1), Group("lands", 8, 9)],
            [Draw("d", 0, 2, SimulationDrawEffect.Once), Play("lands")],
            turns: 1);

        SimulationTurnRecord turn = RunOrdered(request).Outcome.Record!.Turns[0];

        Assert.Equal([Id(8), Id(9)], turn.Drawn);
        Assert.Contains(turn.Moved, move => move.Rule == 1 && (move.EntryId == Id(8) || move.EntryId == Id(9)));
    }

    /// <summary>
    /// Verifies a rule listed before <c>draw-cards</c> does not run again, so the drawn land waits a turn (S15).
    /// </summary>
    [Fact]
    public void DrawCards_ListedAfterPlay_LeavesTheDrawnLandForTheNextTurn()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("d", 1), Group("lands", 8, 9)],
            [Play("lands"), Draw("d", 0, 2, SimulationDrawEffect.Once)],
            turns: 2);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.DoesNotContain(record.Turns[0].Moved, move => move.Rule == 0);
        Assert.Contains(record.Turns[1].Moved, move => move.Rule == 0);
    }

    /// <summary>
    /// Verifies a <c>once</c> draw card leaves play and does not draw again on later turns.
    /// </summary>
    [Fact]
    public void DrawCards_Once_DrawsOnlyAtCastTime()
    {
        SimulationRequest request = Request(
            Distinct(20),
            [Group("d", 1)],
            [Draw("d", 0, 2, SimulationDrawEffect.Once)],
            turns: 3,
            conditions: [Condition("in-play", new SimulationOnBattlefieldAtLeast("d", 1, 3))]);

        (SimulationSampleOutcome outcome, SimulationTrial trial) = RunOrdered(request);

        Assert.Equal([2, 1, 1], outcome.Record!.Turns.Select(turn => turn.Drawn.Count));
        Assert.False(trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies an <c>each-turn</c> draw card, cast on turn two, adds its draws at every later draw step (S16).
    /// </summary>
    [Fact]
    public void DrawCards_EachTurn_DrawsAtEveryLaterDrawStep()
    {
        SimulationRequest request = Request(
            Distinct(30),
            [Group("lands", 2, 3), Group("d", 1)],
            [Play("lands"), Draw("d", 2, 1, SimulationDrawEffect.EachTurn)],
            turns: 4);

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal([0, 2, 2, 2], record.Turns.Select(turn => turn.Drawn.Count));
    }

    /// <summary>
    /// Verifies that when draws empty the library, drawing stops and the game is counted in <c>libraryEmpty</c>
    /// (S17).
    /// </summary>
    [Fact]
    public void DrawCards_WhenTheLibraryRunsOut_StopsAndCountsTheGame()
    {
        SimulationRequest request = Request(
            Distinct(9),
            [Group("d", 1)],
            [Draw("d", 0, 1, SimulationDrawEffect.EachTurn)],
            turns: 4);

        SimulationSampleOutcome outcome = RunOrdered(request).Outcome;
        SimulationReport report = Report(request with { Samples = 5 });

        Assert.True(outcome.LibraryEmpty);
        Assert.Equal(2, outcome.Record!.Turns.Sum(turn => turn.Drawn.Count));
        Assert.Equal(5, report.LibraryEmpty);
    }

    /// <summary>
    /// Verifies a game whose library never runs out is not counted in <c>libraryEmpty</c>.
    /// </summary>
    [Fact]
    public void LibraryEmpty_WithEnoughCards_IsZero()
    {
        SimulationReport report = Report(Request(Distinct(40), samples: 20, turns: 5));

        Assert.Equal(0, report.LibraryEmpty);
    }

    /// <summary>
    /// Verifies a mulligan that is never satisfied keeps the last hand at the limit (S06).
    /// </summary>
    [Fact]
    public void Mulligan_NeverSatisfied_KeepsEveryHandAtTheLimit()
    {
        SimulationRequest request = Request(
            Distinct(40),
            [Group("x", 1)],
            samples: 30,
            sampleHands: 10,
            mulligan: Mulligan(1, false, ["x"]));

        SimulationReport report = Report(request);

        Assert.Equal(new SimulationMulliganCounts(0, 30), report.Mulligans);
        Assert.All(report.SampleHands, sample =>
        {
            Assert.Equal([0, 1], sample.Hands.Select(hand => hand.Mulligan));
            Assert.Single(sample.Bottomed);
            Assert.Equal(6, sample.Kept.Count);
        });
    }

    /// <summary>
    /// Verifies a hand that passes the keep rule is counted as kept by the rule, with no mulligan.
    /// </summary>
    [Fact]
    public void Mulligan_SatisfiedOnTheFirstHand_CountsAsKeptByRule()
    {
        SimulationRequest request = Request(
            Distinct(40),
            [Group("any", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20)],
            samples: 20,
            sampleHands: 3,
            mulligan: new SimulationMulligan(
                2,
                false,
                new SimulationCountBetween("any", 0, 7),
                []));

        SimulationReport report = Report(request);

        Assert.Equal(new SimulationMulliganCounts(20, 0), report.Mulligans);
        Assert.All(report.SampleHands, sample =>
        {
            Assert.Single(sample.Hands);
            Assert.Empty(sample.Bottomed);
            Assert.Equal(7, sample.Kept.Count);
        });
    }

    /// <summary>
    /// Verifies the first bottomed card comes from the preferred group (S12).
    /// </summary>
    [Fact]
    public void Mulligan_Bottoming_TakesPreferredGroupFirst()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("ramp2", 3), Group("other", 1, 2)],
            mulligan: Mulligan(1, false, ["ramp2"], "ramp2"));

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal([Id(3)], record.Bottomed);
        Assert.DoesNotContain(Id(3), record.Kept);
    }

    /// <summary>
    /// Verifies preferred groups are tried in the order listed, then a random card is bottomed.
    /// </summary>
    [Fact]
    public void Mulligan_Bottoming_TriesGroupsInOrderThenAnyCard()
    {
        SimulationRequest first = Request(
            Distinct(12),
            [Group("ramp2", 3), Group("other", 1, 2)],
            mulligan: Mulligan(1, false, ["ramp2"], "other", "ramp2"));
        SimulationRequest none = Request(
            Distinct(12),
            [Group("ramp2", 10), Group("other", 11)],
            mulligan: Mulligan(1, false, ["ramp2"], "other", "ramp2"));

        Guid fromFirst = Assert.Single(RunOrdered(first).Outcome.Record!.Bottomed);
        Guid fromAny = Assert.Single(RunOrdered(none).Outcome.Record!.Bottomed);

        Assert.Contains(fromFirst, new[] { Id(1), Id(2) });
        Assert.DoesNotContain(fromAny, new[] { Id(10), Id(11) });
        Assert.Contains(fromAny, Enumerable.Range(1, 7).Select(Id));
    }

    /// <summary>
    /// Verifies the free first mulligan bottoms nothing, and the next mulligan bottoms one card (S18).
    /// </summary>
    [Theory]
    [InlineData(1, true, 7)]
    [InlineData(1, false, 6)]
    [InlineData(2, true, 6)]
    [InlineData(2, false, 5)]
    public void Mulligan_FreeFirst_BottomsOneFewerCard(int mulligans, bool free, int expectedKept)
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("x", 1)],
            mulligan: Mulligan(mulligans, free, ["x"]));

        SimulationSampleRecord record = RunOrdered(request).Outcome.Record!;

        Assert.Equal(expectedKept, record.Kept.Count);
        Assert.Equal(7 - expectedKept, record.Bottomed.Count);
    }

    /// <summary>
    /// Verifies <c>all</c> and <c>any</c> combine keep checks.
    /// </summary>
    [Fact]
    public void Mulligan_KeepWhenCombinesChecksWithAllAndAny()
    {
        SimulationCheck both = new SimulationAll(
        [
            new SimulationCountBetween("a", 1, 1),
            new SimulationCountBetween("b", 1, 1),
        ]);
        SimulationCheck either = new SimulationAny(
        [
            new SimulationCountBetween("a", 2, 2),
            new SimulationCountBetween("b", 1, 1),
        ]);
        List<SimulationDeckEntry> deck = Distinct(12);
        SimulationGroup[] groups = [Group("a", 1), Group("b", 2)];

        SimulationCheck neither = new SimulationAny(
        [
            new SimulationCountBetween("a", 2, 2),
            new SimulationCountBetween("b", 2, 2),
        ]);

        SimulationSampleOutcome all = Kept(deck, groups, both);
        SimulationSampleOutcome any = Kept(deck, groups, either);
        SimulationSampleOutcome none = Kept(deck, groups, neither);

        Assert.True(all.KeptByRule);
        Assert.True(any.KeptByRule);
        Assert.True(none.KeptAtLimit);
    }

    /// <summary>
    /// Plays one ordered game with a keep rule and returns how its hand was kept.
    /// </summary>
    private static SimulationSampleOutcome Kept(
        List<SimulationDeckEntry> deck,
        SimulationGroup[] groups,
        SimulationCheck keepWhen)
    {
        return RunOrdered(Request(deck, groups, mulligan: new SimulationMulligan(1, true, keepWhen, []))).Outcome;
    }

    /// <summary>
    /// Verifies a <c>can-cast</c> check passes when the lands make the cost's colors (S20).
    /// </summary>
    [Fact]
    public void CanCast_WithMatchingColors_Holds()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1, 2])],
            [Play("lands")],
            [Condition("gg", CanCastOn("{G}{G}", 2))],
            turns: 2);

        Assert.True(RunOrdered(request).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies a generic-only source does not pay a colored symbol (S20, miss).
    /// </summary>
    [Fact]
    public void CanCast_WithAGenericOnlySource_Misses()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1]), Group("rocks", 2)],
            [Play("lands"), Play("rocks")],
            [Condition("gg", CanCastOn("{G}{G}", 2))],
            turns: 2);

        Assert.False(RunOrdered(request).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies a dual land pays one color while another source pays the other (S21).
    /// </summary>
    [Fact]
    public void CanCast_WithADualLand_UsesOneAssignment()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1, 2], (["B", "G"], [2]))],
            [Play("lands")],
            [Condition("bg", CanCastOn("{B}{G}", 2))],
            turns: 2);

        Assert.True(RunOrdered(request).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies a hybrid symbol accepts either color (S22).
    /// </summary>
    [Theory]
    [InlineData("U", true)]
    [InlineData("W", true)]
    [InlineData("R", false)]
    public void CanCast_WithAHybridSymbol_AcceptsEitherColor(string color, bool expected)
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", [color], [1])],
            [Play("lands")],
            [Condition("hybrid", CanCastOn("{W/U}", 1))],
            turns: 1);

        Assert.Equal(expected, RunOrdered(request).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies a Phyrexian symbol needs its color, and the result states that life payment is not modeled (S23).
    /// </summary>
    [Fact]
    public void CanCast_WithAPhyrexianSymbol_NeedsItsColor()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["U"], [1])],
            [Play("lands")],
            [Condition("phyrexian", CanCastOn("{G/P}", 1))],
            turns: 1);

        bool held = RunOrdered(request).Trial.ConditionHeld(0);
        SimulationReport report = Report(request);

        Assert.False(held);
        Assert.Contains(
            report.Limits,
            limit => limit.Contains("Phyrexian life payment is not modeled", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies <c>can-cast</c> pays the cost together with all mana the rules spent that turn (S26).
    /// </summary>
    [Theory]
    [InlineData("G", "U", "U", true)]
    [InlineData("U", "U", "U", false)]
    public void CanCast_WithManaSpentByRules_NeedsOneJointAssignment(
        string first,
        string second,
        string third,
        bool hit)
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", [first], [1, 2, 3], ([second], [2]), ([third], [3])), Group("spell", 4)],
            [Play("lands", maxPerTurn: 3), Cast("spell", 2, addsMana: 0)],
            [Condition("g", CanCastOn("{G}", 1))],
            turns: 1);

        (SimulationSampleOutcome outcome, SimulationTrial trial) = RunOrdered(request);

        Assert.Equal(2, outcome.Record!.Turns[0].ManaSpent);
        Assert.Equal(hit, trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies two lands cannot pay a one-symbol cost together with two mana already spent (S26, too little).
    /// </summary>
    [Fact]
    public void CanCast_WithTooFewSourcesAfterSpending_Misses()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1, 2]), Group("spell", 4)],
            [Play("lands", maxPerTurn: 2), Cast("spell", 2, addsMana: 0)],
            [Condition("g", CanCastOn("{G}", 1))],
            turns: 1);

        Assert.False(RunOrdered(request).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies <c>inHand</c> needs the named card in hand on the checked turn.
    /// </summary>
    [Fact]
    public void CanCast_WithInHand_NeedsThatCardInHand()
    {
        SimulationRequest present = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1])],
            [Play("lands")],
            [Condition("g", CanCastOn("{G}", 1, inHand: 2))],
            turns: 1);
        SimulationRequest absent = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1])],
            [Play("lands")],
            [Condition("g", CanCastOn("{G}", 1, inHand: 11))],
            turns: 1);

        Assert.True(RunOrdered(present).Trial.ConditionHeld(0));
        Assert.False(RunOrdered(absent).Trial.ConditionHeld(0));
    }

    /// <summary>
    /// Verifies <c>byTurn</c> passes when any turn up to it passes, and <c>turn</c> checks only that turn.
    /// </summary>
    [Fact]
    public void CanCast_ByTurn_PassesOnAnyEarlierTurn()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Colored("lands", ["G"], [1, 2, 3])],
            [Play("lands")],
            [
                Condition("by", new SimulationCanCast("{G}{G}", null, 3, null)),
                Condition("only-1", CanCastOn("{G}{G}", 1)),
                Condition("only-2", CanCastOn("{G}{G}", 2)),
            ],
            turns: 3);

        (SimulationSampleOutcome _, SimulationTrial trial) = RunOrdered(request);

        Assert.True(trial.ConditionHeld(0));
        Assert.False(trial.ConditionHeld(1));
        Assert.True(trial.ConditionHeld(2));
    }

    /// <summary>
    /// Verifies <c>mana-at-least</c> counts unspent mana after a turn's rules.
    /// </summary>
    [Fact]
    public void ManaAtLeast_CountsUnspentManaAfterTheRules()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("lands", 1, 2), Group("spell", 3)],
            [Play("lands", maxPerTurn: 2), Cast("spell", 1, addsMana: 0)],
            [
                Condition("one", new SimulationManaAtLeast(1, 1)),
                Condition("two", new SimulationManaAtLeast(2, 1)),
            ],
            turns: 1);

        (SimulationSampleOutcome _, SimulationTrial trial) = RunOrdered(request);

        Assert.True(trial.ConditionHeld(0));
        Assert.False(trial.ConditionHeld(1));
    }

    /// <summary>
    /// Verifies <c>drawn-at-least</c> counts cards seen in the kept hand and by draws, once each.
    /// </summary>
    [Fact]
    public void DrawnAtLeast_CountsCardsSeenByTheTurn()
    {
        SimulationRequest request = Request(
            Distinct(20),
            [Group("g", 6, 7, 8, 9)],
            conditions:
            [
                Condition("hand-and-draw", new SimulationDrawnAtLeast("g", 3, 2)),
                Condition("too-early", new SimulationDrawnAtLeast("g", 3, 1)),
                Condition("late", new SimulationDrawnAtLeast("g", 4, 3)),
                Condition("never", new SimulationDrawnAtLeast("g", 5, 3)),
            ],
            turns: 3);

        (SimulationSampleOutcome _, SimulationTrial trial) = RunOrdered(request);

        Assert.True(trial.ConditionHeld(0));
        Assert.False(trial.ConditionHeld(1));
        Assert.True(trial.ConditionHeld(2));
        Assert.False(trial.ConditionHeld(3));
    }

    /// <summary>
    /// Verifies <c>on-battlefield-at-least</c> counts the cards that rules moved into play.
    /// </summary>
    [Fact]
    public void OnBattlefieldAtLeast_CountsCardsInPlay()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("lands", 1, 2, 3)],
            [Play("lands")],
            [
                Condition("two-by-2", new SimulationOnBattlefieldAtLeast("lands", 2, 2)),
                Condition("three-by-2", new SimulationOnBattlefieldAtLeast("lands", 3, 2)),
                Condition("three-by-3", new SimulationOnBattlefieldAtLeast("lands", 3, 3)),
            ],
            turns: 3);

        (SimulationSampleOutcome _, SimulationTrial trial) = RunOrdered(request);

        Assert.True(trial.ConditionHeld(0));
        Assert.False(trial.ConditionHeld(1));
        Assert.True(trial.ConditionHeld(2));
    }

    /// <summary>
    /// Verifies <c>all</c> and <c>any</c> combine the results of condition checks.
    /// </summary>
    [Fact]
    public void Conditions_CombineChecksWithAllAndAny()
    {
        SimulationRequest request = Request(
            Distinct(12),
            [Group("lands", 1, 2, 3)],
            [Play("lands")],
            [
                Condition("all-true", new SimulationAll(
                [
                    new SimulationOnBattlefieldAtLeast("lands", 1, 1),
                    new SimulationManaAtLeast(1, 1),
                ])),
                Condition("all-false", new SimulationAll(
                [
                    new SimulationOnBattlefieldAtLeast("lands", 1, 1),
                    new SimulationManaAtLeast(5, 1),
                ])),
                Condition("any-true", new SimulationAny(
                [
                    new SimulationManaAtLeast(5, 1),
                    new SimulationOnBattlefieldAtLeast("lands", 1, 1),
                ])),
                Condition("any-false", new SimulationAny(
                [
                    new SimulationManaAtLeast(5, 1),
                    new SimulationOnBattlefieldAtLeast("lands", 5, 1),
                ])),
            ],
            turns: 1);

        (SimulationSampleOutcome _, SimulationTrial trial) = RunOrdered(request);

        Assert.Equal([true, false, true, false], Enumerable.Range(0, 4).Select(trial.ConditionHeld));
    }

    /// <summary>
    /// Builds a mulligan whose keep rule needs five cards of the first group, which a hand never holds.
    /// </summary>
    private static SimulationMulligan Mulligan(int maximum, bool free, string[] groups, params string[] prefer)
    {
        return new SimulationMulligan(
            maximum,
            free,
            new SimulationCountBetween(groups[0], 5, 5),
            prefer);
    }
}
