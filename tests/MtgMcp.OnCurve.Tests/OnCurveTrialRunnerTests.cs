using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies the fixed turn model from explicitly ordered card copies.
/// </summary>
public sealed class OnCurveTrialRunnerTests
{
    /// <summary>
    /// Verifies the player on the play does not draw the eighth card on turn one.
    /// </summary>
    [Fact]
    public void RunWithLibraryOrder_OnThePlay_DoesNotDrawOnTurnOne()
    {
        Guid landId = OnCurveTestData.GreenLandEntryId;
        IReadOnlyList<OnCurveLibraryCard> library =
        [
            OnCurveTestData.Card(landId, OnCurveManaSymbol.Green),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
        ];

        OnCurveTrialOutcome outcome = Run(library, Rules((landId, OnCurveLandRule.OneManaSameTurn)), "{G}", 1, true);

        Assert.False(outcome.Succeeded);
        Assert.Equal(OnCurveMissReason.TargetNotDrawn, outcome.MissReason);
    }

    /// <summary>
    /// Verifies the player on the draw can use the turn-one eighth card.
    /// </summary>
    [Fact]
    public void RunWithLibraryOrder_OnTheDraw_DrawsAndCastsOnTurnOne()
    {
        Guid landId = OnCurveTestData.GreenLandEntryId;
        IReadOnlyList<OnCurveLibraryCard> library =
        [
            OnCurveTestData.Card(landId, OnCurveManaSymbol.Green),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
        ];

        OnCurveTrialOutcome outcome = Run(library, Rules((landId, OnCurveLandRule.OneManaSameTurn)), "{G}", 1, false);

        Assert.True(outcome.Succeeded);
        OnCurveTrace trace = Assert.IsType<OnCurveTrace>(outcome.Trace);
        Assert.Contains(
            trace.Events,
            item => item.Kind == OnCurveTraceEventKind.TargetCast && item.Turn == 1);
    }

    /// <summary>
    /// Verifies a next-turn land waits while a same-turn land supplies the second mana source.
    /// </summary>
    [Fact]
    public void RunWithLibraryOrder_WithNextTurnLand_UsesItOnTheFollowingTurn()
    {
        Guid nextTurnLandId = OnCurveTestData.Id(10);
        Guid sameTurnLandId = OnCurveTestData.Id(11);
        IReadOnlyList<OnCurveLibraryCard> library =
        [
            OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
            OnCurveTestData.Card(nextTurnLandId, OnCurveManaSymbol.Green),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(OnCurveTestData.FillerEntryId),
            OnCurveTestData.Card(sameTurnLandId, OnCurveManaSymbol.Green),
        ];

        OnCurveTrialOutcome outcome = Run(
            library,
            Rules(
                (nextTurnLandId, OnCurveLandRule.OneManaNextTurn),
                (sameTurnLandId, OnCurveLandRule.OneManaSameTurn)),
            "{1}{G}",
            2,
            true);

        Assert.True(outcome.Succeeded);
        OnCurveTrace trace = Assert.IsType<OnCurveTrace>(outcome.Trace);
        Assert.Collection(
            trace.Events.Where(item => item.Kind == OnCurveTraceEventKind.LandPlayed),
            item =>
            {
                Assert.Equal(1, item.Turn);
                Assert.Equal(nextTurnLandId, item.EntryId);
                Assert.Equal(OnCurveLandRule.OneManaNextTurn, item.LandRule);
            },
            item =>
            {
                Assert.Equal(2, item.Turn);
                Assert.Equal(sameTurnLandId, item.EntryId);
                Assert.Equal(OnCurveLandRule.OneManaSameTurn, item.LandRule);
            });
    }

    /// <summary>
    /// Verifies equal source choices select the lower stable entry identity.
    /// </summary>
    [Fact]
    public void RunWithLibraryOrder_WithEqualLandChoices_UsesLowerEntryId()
    {
        Guid lowerLandId = OnCurveTestData.Id(20);
        Guid higherLandId = OnCurveTestData.Id(21);
        IReadOnlyList<OnCurveLibraryCard> library = OnCurveTestData.OpeningHand(
            OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
            OnCurveTestData.Card(higherLandId, OnCurveManaSymbol.Green),
            OnCurveTestData.Card(lowerLandId, OnCurveManaSymbol.Green));

        OnCurveTrialOutcome outcome = Run(
            library,
            Rules(
                (higherLandId, OnCurveLandRule.OneManaSameTurn),
                (lowerLandId, OnCurveLandRule.OneManaSameTurn)),
            "{G}",
            1,
            true);

        OnCurveTrace trace = Assert.IsType<OnCurveTrace>(outcome.Trace);
        OnCurveTraceEvent landPlayed = Assert.Single(
            trace.Events.Where(item => item.Kind == OnCurveTraceEventKind.LandPlayed));
        Assert.Equal(lowerLandId, landPlayed.EntryId);
    }

    /// <summary>
    /// Verifies a caller-excluded land stays out of the model and appears once in the trace.
    /// </summary>
    [Fact]
    public void RunWithLibraryOrder_WithNotModeledLand_DoesNotPlayIt()
    {
        Guid excludedLandId = OnCurveTestData.Id(30);
        IReadOnlyList<OnCurveLibraryCard> library = OnCurveTestData.OpeningHand(
            OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
            OnCurveTestData.Card(excludedLandId, OnCurveManaSymbol.Green));

        OnCurveTrialOutcome outcome = Run(
            library,
            Rules((excludedLandId, OnCurveLandRule.NotModeled)),
            "{G}",
            2,
            true);

        Assert.False(outcome.Succeeded);
        Assert.Equal(OnCurveMissReason.NoModeledLandPlayed, outcome.MissReason);
        OnCurveTrace trace = Assert.IsType<OnCurveTrace>(outcome.Trace);
        Assert.Single(
            trace.Events.Where(
                item => item.Kind == OnCurveTraceEventKind.LandNotModeled && item.EntryId == excludedLandId));
        Assert.DoesNotContain(trace.Events, item => item.Kind == OnCurveTraceEventKind.LandPlayed);
    }

    /// <summary>
    /// Verifies the final miss label applies the required priority order.
    /// </summary>
    [Fact]
    public void RunWithLibraryOrder_WithIncompleteMana_UsesMutuallyExclusiveMissReasons()
    {
        Guid nextTurnLandId = OnCurveTestData.Id(40);
        OnCurveTrialOutcome notDrawn = Run(
            OnCurveTestData.OpeningHand(OnCurveTestData.Card(OnCurveTestData.FillerEntryId))
                .Append(OnCurveTestData.Card(OnCurveTestData.TargetEntryId))
                .ToArray(),
            Rules(),
            "{G}",
            1,
            true);
        OnCurveTrialOutcome noLand = Run(
            OnCurveTestData.OpeningHand(OnCurveTestData.Card(OnCurveTestData.TargetEntryId)),
            Rules(),
            "{G}",
            1,
            true);
        OnCurveTrialOutcome notReady = Run(
            OnCurveTestData.OpeningHand(
                OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
                OnCurveTestData.Card(nextTurnLandId, OnCurveManaSymbol.Green)),
            Rules((nextTurnLandId, OnCurveLandRule.OneManaNextTurn)),
            "{G}",
            1,
            true);
        OnCurveTrialOutcome wrongColor = Run(
            OnCurveTestData.OpeningHand(
                OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
                OnCurveTestData.Card(OnCurveTestData.Id(41), OnCurveManaSymbol.Green),
                OnCurveTestData.Card(OnCurveTestData.Id(42), OnCurveManaSymbol.Blue)),
            Rules(
                (OnCurveTestData.Id(41), OnCurveLandRule.OneManaSameTurn),
                (OnCurveTestData.Id(42), OnCurveLandRule.OneManaSameTurn)),
            "{G}{G}",
            2,
            true);
        OnCurveTrialOutcome shortOnGeneric = Run(
            OnCurveTestData.OpeningHand(
                OnCurveTestData.Card(OnCurveTestData.TargetEntryId),
                OnCurveTestData.Card(OnCurveTestData.Id(43), OnCurveManaSymbol.Green),
                OnCurveTestData.Card(OnCurveTestData.Id(44), OnCurveManaSymbol.Green)),
            Rules(
                (OnCurveTestData.Id(43), OnCurveLandRule.OneManaSameTurn),
                (OnCurveTestData.Id(44), OnCurveLandRule.OneManaSameTurn)),
            "{2}{G}",
            2,
            true);

        Assert.Equal(OnCurveMissReason.TargetNotDrawn, notDrawn.MissReason);
        Assert.Equal(OnCurveMissReason.NoModeledLandPlayed, noLand.MissReason);
        Assert.Equal(OnCurveMissReason.SourceNotReady, notReady.MissReason);
        Assert.Equal(OnCurveMissReason.MissingRequiredColor, wrongColor.MissReason);
        Assert.Equal(OnCurveMissReason.NotEnoughMana, shortOnGeneric.MissReason);
    }

    /// <summary>
    /// Verifies trace storage has a hard event limit and preserves the omitted count.
    /// </summary>
    [Fact]
    public void TraceRecorder_WithMoreThanMaximumEvents_RecordsOmissions()
    {
        OnCurveTraceRecorder recorder = new(1);
        for (int index = 0; index < 65; index++)
        {
            recorder.Add(new OnCurveTraceEvent(OnCurveTraceEventKind.Draw, 1), CancellationToken.None);
        }

        OnCurveTrace trace = recorder.Complete(false);

        Assert.Equal(64, trace.Events.Count);
        Assert.Equal(1, trace.OmittedEventCount);
        Assert.False(trace.Succeeded);
    }

    /// <summary>
    /// Runs one ordered library through the public model with a parsed simple cost.
    /// </summary>
    private static OnCurveTrialOutcome Run(
        IReadOnlyList<OnCurveLibraryCard> library,
        IReadOnlyDictionary<Guid, OnCurveLandRule> rules,
        string manaCost,
        int turnLimit,
        bool onThePlay)
    {
        OnCurveManaCost cost = Assert.IsType<OperationSuccess<OnCurveManaCost>>(
            OnCurveManaSymbols.ReadCost(manaCost).Value).Data;
        return OnCurveTrialRunner.RunWithLibraryOrder(
            library,
            OnCurveTestData.TargetEntryId,
            rules,
            cost,
            turnLimit,
            onThePlay,
            1,
            true,
            CancellationToken.None);
    }

    /// <summary>
    /// Creates one stable land-rule map for an ordered library test.
    /// </summary>
    private static IReadOnlyDictionary<Guid, OnCurveLandRule> Rules(
        params (Guid EntryId, OnCurveLandRule Rule)[] values)
    {
        Dictionary<Guid, OnCurveLandRule> rules = [];
        foreach ((Guid entryId, OnCurveLandRule rule) in values)
        {
            rules.Add(entryId, rule);
        }

        return rules;
    }
}
