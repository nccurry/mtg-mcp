using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Proves the captured replay fixtures still match the original on-curve estimator they were taken from.
/// </summary>
public sealed class OnCurveReplayTests
{
    /// <summary>
    /// Verifies every captured case returns its recorded success count.
    /// </summary>
    [Fact]
    public void OriginalEstimator_ReplaysEveryCapturedCase()
    {
        IReadOnlyList<ReplayCase> cases = OnCurveReplayFixtures.Load();
        Assert.True(cases.Count >= 16);
        foreach (ReplayCase item in cases)
        {
            List<OnCurveDeckEntry> main = [];
            List<Guid> candidates = [];
            List<OnCurveLandRuleInput> rules = [];
            foreach (ReplayEntry entry in item.Entries)
            {
                Guid id = OnCurveReplayFixtures.Id(entry.Id);
                OnCurveProducedMana mana = entry.Produces is null
                    ? OnCurveTestData.MissingMana()
                    : OnCurveTestData.Mana([.. entry.Produces]);
                main.Add(new OnCurveDeckEntry(id, OnCurveReplayFixtures.Id(1000 + entry.Id), entry.Quantity, mana));
                if (entry.LandRule is not null)
                {
                    candidates.Add(id);
                    rules.Add(new OnCurveLandRuleInput(id, Enum.Parse<OnCurveLandRule>(entry.LandRule)));
                }
            }

            OnCurveRequest request = new(
                OnCurveReplayFixtures.Id(999),
                1,
                new OnCurveTarget(
                    OnCurveReplayFixtures.Id(item.TargetEntry),
                    OnCurveReplayFixtures.Id(1000 + item.TargetEntry),
                    item.TargetManaCost),
                main,
                candidates,
                rules,
                item.TurnLimit,
                item.SampleCount,
                item.OnThePlay,
                item.Seed);

            OperationResult<OnCurveRunReport> result = OnCurveEstimate.Calculate(
                request,
                TestContext.Current.CancellationToken);

            OnCurveRunReport report = Assert.IsType<OperationSuccess<OnCurveRunReport>>(result.Value).Data;
            Assert.True(item.ExpectedSuccessCount == report.SuccessCount, item.Name);
        }
    }
}
