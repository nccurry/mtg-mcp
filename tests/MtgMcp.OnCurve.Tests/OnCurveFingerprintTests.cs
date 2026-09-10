namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies the replay fingerprint covers modeled facts and excludes run-only settings.
/// </summary>
public sealed class OnCurveFingerprintTests
{
    /// <summary>
    /// Verifies equivalent mainboard and rule ordering creates the same fingerprint.
    /// </summary>
    [Fact]
    public void Create_WithEquivalentOrdering_ReturnsSameFingerprint()
    {
        OnCurveRequest first = RequestWithFiller();
        OnCurveRequest second = first with
        {
            Mainboard = first.Mainboard!.Reverse().ToArray(),
            LandRules = first.LandRules!.Reverse().ToArray(),
            CandidateLandEntryIds = first.CandidateLandEntryIds!.Reverse().ToArray(),
        };

        Assert.Equal(OnCurveFingerprint.Create(first), OnCurveFingerprint.Create(second));
    }

    /// <summary>
    /// Verifies changing only sample count or seed does not change the modeled-input fingerprint.
    /// </summary>
    [Fact]
    public void Create_WithRunOnlySettingsChanged_ReturnsSameFingerprint()
    {
        OnCurveRequest request = RequestWithFiller();
        OnCurveRequest changed = request with
        {
            SampleCount = 1_000,
            Seed = "ffffffffffffffff",
        };

        Assert.Equal(OnCurveFingerprint.Create(request), OnCurveFingerprint.Create(changed));
    }

    /// <summary>
    /// Verifies a source fact, candidate selection, declared land rule, or filler quantity changes the fingerprint.
    /// </summary>
    [Fact]
    public void Create_WithModeledFactChanged_ReturnsDifferentFingerprint()
    {
        OnCurveRequest request = RequestWithFiller();
        IReadOnlyList<OnCurveDeckEntry> changedFacts =
        [
            request.Mainboard![0],
            request.Mainboard[1] with { ProducedMana = OnCurveTestData.Mana("U") },
            request.Mainboard[2],
        ];
        OnCurveRequest differentSource = request with { Mainboard = changedFacts };
        OnCurveRequest differentNullState = request with
        {
            Mainboard =
            [
                request.Mainboard[0] with { ProducedMana = OnCurveTestData.NullMana() },
                request.Mainboard[1],
                request.Mainboard[2],
            ],
        };
        OnCurveRequest differentRule = request with
        {
            LandRules =
            [
                new OnCurveLandRuleInput(
                    OnCurveTestData.GreenLandEntryId,
                OnCurveLandRule.OneManaNextTurn),
            ],
        };
        OnCurveRequest differentCandidateSelection = request with
        {
            CandidateLandEntryIds = [],
            LandRules = [],
        };
        OnCurveRequest differentQuantity = request with
        {
            Mainboard =
            [
                request.Mainboard![0],
                request.Mainboard[1],
                request.Mainboard[2] with { Quantity = 2 },
            ],
        };

        string fingerprint = OnCurveFingerprint.Create(request);

        Assert.NotEqual(fingerprint, OnCurveFingerprint.Create(differentSource));
        Assert.NotEqual(fingerprint, OnCurveFingerprint.Create(differentNullState));
        Assert.NotEqual(fingerprint, OnCurveFingerprint.Create(differentRule));
        Assert.NotEqual(fingerprint, OnCurveFingerprint.Create(differentCandidateSelection));
        Assert.NotEqual(fingerprint, OnCurveFingerprint.Create(differentQuantity));
    }

    /// <summary>
    /// Verifies a fingerprint is lower-case SHA-256 text rather than user-facing prose.
    /// </summary>
    [Fact]
    public void Create_WithValidRequest_ReturnsLowerCaseSha256()
    {
        string fingerprint = OnCurveFingerprint.Create(RequestWithFiller());

        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }

    /// <summary>
    /// Creates a valid three-entry request that includes a noncandidate filler quantity.
    /// </summary>
    private static OnCurveRequest RequestWithFiller()
    {
        return OnCurveTestData.Request(
            mainboard:
            [
                OnCurveTestData.Entry(
                    OnCurveTestData.TargetEntryId,
                    OnCurveTestData.TargetPrintingId,
                    1,
                    OnCurveTestData.MissingMana()),
                OnCurveTestData.Entry(
                    OnCurveTestData.GreenLandEntryId,
                    OnCurveTestData.GreenLandPrintingId,
                    6,
                    OnCurveTestData.Mana("G")),
                OnCurveTestData.Entry(
                    OnCurveTestData.FillerEntryId,
                    OnCurveTestData.FillerPrintingId,
                    1,
                    OnCurveTestData.MissingMana()),
            ]);
    }
}
