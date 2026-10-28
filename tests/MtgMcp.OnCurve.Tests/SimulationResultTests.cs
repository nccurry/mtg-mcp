using System.Text.Json;
using static MtgMcp.OnCurve.Tests.OnCurveTestData;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Proves what a result states (SIM-003), that one request and seed always give the same result (SIM-002), and that
/// returned samples are the counted ones (SIM-009).
/// </summary>
public sealed class SimulationResultTests
{
    /// <summary>
    /// Builds the S01 request: 40 lands and 59 other cards, a land played each turn, four turns, seed 1.
    /// </summary>
    private static SimulationRequest Fixed(int samples = 10_000, int sampleHands = 0)
    {
        return Request(
            [Card(1, 40), Card(2, 59)],
            [Group("lands", 1)],
            [Play("lands")],
            [
                Condition("four-mana-turn-4", new SimulationManaAtLeast(4, 4)),
                Condition("five-lands-seen-by-turn-3", new SimulationDrawnAtLeast("lands", 5, 3)),
            ],
            turns: 4,
            samples: samples,
            sampleHands: sampleHands,
            seed: 1);
    }

    /// <summary>
    /// Verifies the fixed-seed S01 request gives these counts on every platform (SIM-002 golden values).
    /// </summary>
    [Fact]
    public void FixedSeed_GivesTheRecordedCounts()
    {
        SimulationReport report = Report(Fixed());

        Assert.Equal("0000000000000001", report.Seed);
        Assert.Equal("simulation-v1", report.ModelVersion);
        Assert.Equal("splitmix64-v1", report.RandomVersion);
        Assert.Equal(6365, report.Results[0].Hits);
        Assert.Equal(0.6365, report.Results[0].Rate);
        Assert.Equal(2625, report.Results[1].Hits);
        Assert.Equal(0.253969, report.Results[1].WilsonLower);
    }

    /// <summary>
    /// Verifies the request fingerprint is the recorded value on every platform (SIM-002 golden value).
    /// </summary>
    [Fact]
    public void FixedSeed_GivesTheRecordedFingerprint()
    {
        Assert.Equal("a78e31033e5018a7fec611d7d8651812f38f53e83499896b62bde18227f1d25e", Report(Fixed()).Fingerprint);
    }

    /// <summary>
    /// Verifies the whole report serializes to the same text twice, including rounded rates and intervals.
    /// </summary>
    [Fact]
    public void SameRequest_SerializesToIdenticalText()
    {
        string first = JsonSerializer.Serialize(Report(Fixed(2_000, 3)));
        string second = JsonSerializer.Serialize(Report(Fixed(2_000, 3)));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Verifies a different seed gives a different result.
    /// </summary>
    [Fact]
    public void DifferentSeeds_GiveDifferentResults()
    {
        SimulationReport first = Report(Fixed());
        SimulationReport second = Report(Fixed() with { Seed = 2 });

        Assert.NotEqual(first.Results[0].Hits, second.Results[0].Hits);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    /// <summary>
    /// Verifies a result states seed, versions, sample count, each condition's hits, rate, and 95 percent interval,
    /// the model limits, and no mulligan counts when the request has no mulligan (SIM-003).
    /// </summary>
    [Fact]
    public void Report_StatesEverythingSim003Requires()
    {
        SimulationReport report = Report(Fixed());

        Assert.Equal(10_000, report.Samples);
        Assert.Matches("^[0-9a-f]{64}$", report.Fingerprint);
        Assert.Equal(["four-mana-turn-4", "five-lands-seen-by-turn-3"], report.Results.Select(result => result.Id));
        foreach (SimulationConditionResult result in report.Results)
        {
            Assert.Equal(Math.Round((double)result.Hits / 10_000, 6), result.Rate);
            Assert.InRange(result.Rate, result.WilsonLower, result.WilsonUpper);
            Assert.InRange(result.WilsonUpper - result.WilsonLower, 0.0001, 0.05);
        }

        Assert.Null(report.Mulligans);
        Assert.Equal(0, report.LibraryEmpty);
        Assert.Empty(report.SampleHands);
        Assert.Contains(report.Limits, limit => limit.Contains("generic mana only", StringComparison.Ordinal));
        Assert.Contains(report.Limits, limit => limit.Contains("Phyrexian", StringComparison.Ordinal));
        Assert.Contains(report.Limits, limit => limit.Contains("Only the stated groups", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies rates and interval bounds are written with at most six decimal places.
    /// </summary>
    [Fact]
    public void Report_RoundsToSixPlaces()
    {
        SimulationReport report = Report(Fixed(7_001));

        foreach (SimulationConditionResult result in report.Results)
        {
            foreach (double value in new[] { result.Rate, result.WilsonLower, result.WilsonUpper })
            {
                Assert.Equal(Math.Round(value, 6), value);
            }
        }
    }

    /// <summary>
    /// Verifies the fingerprint ignores seed, samples, and returned samples but changes with any rule or deck
    /// change (SIM-003).
    /// </summary>
    [Fact]
    public void Fingerprint_IgnoresRunSettingsAndTracksTheModel()
    {
        string baseline = Report(Fixed(500)).Fingerprint;

        Assert.Equal(baseline, Report(Fixed(900, 2) with { Seed = 9 }).Fingerprint);
        Assert.NotEqual(baseline, Report(Fixed(500) with { Turns = 5 }).Fingerprint);
        Assert.NotEqual(baseline, Report(Fixed(500) with { Context = "other revision" }).Fingerprint);
        Assert.NotEqual(
            baseline,
            Report(Fixed(500) with { EachTurn = [Play("lands", maxPerTurn: 2)] }).Fingerprint);
        Assert.NotEqual(
            baseline,
            Report(Fixed(500) with { Deck = [Card(1, 39), Card(2, 60)] }).Fingerprint);
    }

    /// <summary>
    /// Verifies every rule and check kind is part of the fingerprint.
    /// </summary>
    [Fact]
    public void Fingerprint_CoversEveryRuleAndCheckKind()
    {
        SimulationRequest rich = Request(
            Distinct(20),
            [Colored("g", ["G"], [1, 2], (["U"], [2])), Group("d", 3), Listed("l", 4, 5)],
            [
                Play(
                    "g",
                    SimulationPick.TargetFirst,
                    1,
                    1,
                    SimulationManaFrom.SameTurn,
                    "{G}",
                    (SimulationManaFrom.NextTurn, [2])),
                Cast("l", 1, SimulationPick.ListedOrder),
                Draw("d", 0, 1, SimulationDrawEffect.EachTurn),
            ],
            [
                Condition(
                    "all",
                    new SimulationAll([new SimulationManaAtLeast(0, 1), new SimulationDrawnAtLeast("g", 1, 1)])),
                Condition(
                    "any",
                    new SimulationAny(
                        [new SimulationOnBattlefieldAtLeast("g", 1, 1), new SimulationCanCast("{G}", null, 2, Id(1))])),
            ],
            mulligan: new SimulationMulligan(1, true, new SimulationCountBetween("g", 0, 7), ["g"]));

        string baseline = Report(rich).Fingerprint;

        Assert.NotEqual(
            baseline,
            Report(rich with { OpeningHand = rich.OpeningHand with { OnThePlay = false } }).Fingerprint);
        Assert.NotEqual(
            baseline,
            Report(rich with { OpeningHand = rich.OpeningHand with { Mulligan = null } }).Fingerprint);
        Assert.NotEqual(
            baseline,
            Report(rich with { Groups = [Group("g", 1, 2), Group("d", 3), Listed("l", 4, 5)] }).Fingerprint);
        Assert.NotEqual(baseline, Report(rich with { Conditions = [rich.Conditions[0]] }).Fingerprint);
    }

    /// <summary>
    /// Verifies the first returned samples are the first counted samples and that asking for them changes no count
    /// (S19).
    /// </summary>
    [Fact]
    public void SampleHands_AreTheFirstCountedSamples()
    {
        SimulationReport withHands = Report(Fixed(300, 3));
        SimulationReport withoutHands = Report(Fixed(300, 0));
        SimulationReport threeOnly = Report(Fixed(3, 3));

        Assert.Equal(withoutHands.Results, withHands.Results);
        Assert.Equal(3, withHands.SampleHands.Count);
        Assert.Equal([1, 2, 3], withHands.SampleHands.Select(sample => sample.Sample));
        Assert.Equal(
            JsonSerializer.Serialize(withHands.SampleHands),
            JsonSerializer.Serialize(threeOnly.SampleHands));
        int unspentOnTurnOne = threeOnly.SampleHands.Count(sample =>
            sample.Turns[0].ManaAvailable - sample.Turns[0].ManaSpent >= 1);
        SimulationReport check = Report(
            Fixed(3, 0) with { Conditions = [Condition("m", new SimulationManaAtLeast(1, 1))] });
        Assert.Equal(unspentOnTurnOne, check.Results[0].Hits);
    }

    /// <summary>
    /// Verifies a returned sample holds the hands drawn, the kept hand, the bottomed cards, and each turn.
    /// </summary>
    [Fact]
    public void SampleHands_StateEachHandKeptHandAndTurn()
    {
        SimulationReport report = Report(Fixed(5, 2));

        SimulationSampleRecord sample = report.SampleHands[0];
        Assert.Single(sample.Hands);
        Assert.Equal(7, sample.Hands[0].EntryIds.Count);
        Assert.Equal(sample.Hands[0].EntryIds, sample.Kept);
        Assert.Empty(sample.Bottomed);
        Assert.Equal([1, 2, 3, 4], sample.Turns.Select(turn => turn.Turn));
        Assert.Empty(sample.Turns[0].Drawn);
        Assert.Single(sample.Turns[1].Drawn);
    }

    /// <summary>
    /// Verifies asking for no sample hands returns none.
    /// </summary>
    [Fact]
    public void SampleHands_Zero_ReturnsNone()
    {
        Assert.Empty(Report(Fixed(50, 0)).SampleHands);
    }
}
