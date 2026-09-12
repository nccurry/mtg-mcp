using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies resolved on-curve requests have complete bounded mainboard and land-rule data.
/// </summary>
public sealed class OnCurveRequestValidatorTests
{
    /// <summary>
    /// Identifies the stable test deck.
    /// </summary>
    private static readonly Guid DeckId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <summary>
    /// Identifies the stable target entry.
    /// </summary>
    private static readonly Guid TargetEntryId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <summary>
    /// Identifies the stable target printing.
    /// </summary>
    private static readonly Guid TargetPrintingId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>
    /// Identifies a different stable printing for target identity checks.
    /// </summary>
    private static readonly Guid OtherPrintingId = Guid.Parse("33333334-3333-4333-8333-333333333333");

    /// <summary>
    /// Identifies a stable entry that is not in the test mainboard.
    /// </summary>
    private static readonly Guid UnknownEntryId = Guid.Parse("33333335-3333-4333-8333-333333333333");

    /// <summary>
    /// Identifies the stable modeled-land entry.
    /// </summary>
    private static readonly Guid LandEntryId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    /// <summary>
    /// Identifies the stable modeled-land printing.
    /// </summary>
    private static readonly Guid LandPrintingId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    /// <summary>
    /// Identifies a stable noncandidate entry with known-empty produced-mana data.
    /// </summary>
    private static readonly Guid KnownEmptyEntryId = Guid.Parse("66666666-6666-4666-8666-666666666666");

    /// <summary>
    /// Identifies the stable printing for the known-empty noncandidate entry.
    /// </summary>
    private static readonly Guid KnownEmptyPrintingId = Guid.Parse("77777777-7777-4777-8777-777777777777");

    /// <summary>
    /// Identifies a stable noncandidate entry with null produced-mana data.
    /// </summary>
    private static readonly Guid NullEntryId = Guid.Parse("88888888-8888-4888-8888-888888888888");

    /// <summary>
    /// Identifies the stable printing for the null noncandidate entry.
    /// </summary>
    private static readonly Guid NullPrintingId = Guid.Parse("99999999-9999-4999-8999-999999999999");

    /// <summary>
    /// Verifies a fully resolved bounded request is accepted without changing its identity.
    /// </summary>
    [Fact]
    public void Validate_WithCompleteRequest_ReturnsOriginalRequest()
    {
        OnCurveRequest request = ValidRequest();

        OperationResult<OnCurveRequest> result = OnCurveRequestValidator.Validate(request);

        Assert.Same(request, Assert.IsType<OperationSuccess<OnCurveRequest>>(result.Value).Data);
    }

    /// <summary>
    /// Verifies known-empty and null source states remain valid for noncandidate mainboard entries.
    /// </summary>
    [Fact]
    public void Validate_WithNoncandidateSourceStates_ReturnsOriginalRequest()
    {
        OnCurveRequest request = ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, ListedMana("G")),
                Entry(KnownEmptyEntryId, KnownEmptyPrintingId, 1, ListedMana()),
                Entry(NullEntryId, NullPrintingId, 1, NullMana()),
            ],
        };

        OperationResult<OnCurveRequest> result = OnCurveRequestValidator.Validate(request);

        Assert.Same(request, Assert.IsType<OperationSuccess<OnCurveRequest>>(result.Value).Data);
    }

    /// <summary>
    /// Verifies a resolved values state rejects a null source list before request validation.
    /// </summary>
    [Fact]
    public void ProducedManaValues_WithNullColors_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OnCurveProducedManaValues(null!));
    }

    /// <summary>
    /// Verifies incomplete, malformed, duplicate, and out-of-bound requests return one typed failure.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Validate_WithInvalidRequest_ReturnsInvalidInput(object? value)
    {
        OnCurveRequest? request = value as OnCurveRequest;
        OperationResult<OnCurveRequest> result = OnCurveRequestValidator.Validate(request);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-on-curve-request", invalid.ReasonCode);
        Assert.DoesNotContain("\\", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Supplies representative invalid requests for every phase-one validation rule.
    /// </summary>
    public static TheoryData<object?> InvalidRequests => new()
    {
        null!,
        ValidRequest() with { DeckId = Guid.Empty },
        ValidRequest() with { DeckRevision = 0 },
        ValidRequest() with { DeckRevision = -1 },
        ValidRequest() with { Target = null },
        ValidRequest() with { Target = new OnCurveTarget(Guid.Empty, TargetPrintingId, "{G}") },
        ValidRequest() with { Target = new OnCurveTarget(TargetEntryId, Guid.Empty, "{G}") },
        ValidRequest() with { Target = new OnCurveTarget(TargetEntryId, TargetPrintingId, " ") },
        ValidRequest() with { Target = new OnCurveTarget(TargetEntryId, OtherPrintingId, "{G}") },
        ValidRequest() with { Mainboard = null },
        ValidRequest() with { Mainboard = [] },
        ValidRequest() with { Mainboard = Entries(OnCurveEstimate.MaximumMainboardEntries + 1) },
        ValidRequest() with { CandidateLandEntryIds = null },
        ValidRequest() with { LandRules = null },
        ValidRequest() with { TurnLimit = OnCurveEstimate.MinimumTurnLimit - 1 },
        ValidRequest() with { TurnLimit = OnCurveEstimate.MaximumTurnLimit + 1 },
        ValidRequest() with { SampleCount = OnCurveEstimate.MinimumSampleCount - 1 },
        ValidRequest() with { SampleCount = OnCurveEstimate.MaximumSampleCount + 1 },
        ValidRequest() with { Seed = null! },
        ValidRequest() with { Seed = "invalid" },
        ValidRequest() with { Mainboard = [null!, Entry(TargetEntryId, TargetPrintingId, 7, MissingMana())] },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, Guid.Empty, 7, ListedMana("G")),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 0, ListedMana("G")),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(TargetEntryId, LandPrintingId, 7, ListedMana("G")),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, default),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 500, ListedMana("G")),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, int.MaxValue, ListedMana("G")),
            ],
        },
        ValidRequest() with { Mainboard = [Entry(LandEntryId, LandPrintingId, 7, ListedMana("G"))] },
        ValidRequest() with { CandidateLandEntryIds = [Guid.Empty] },
        ValidRequest() with { CandidateLandEntryIds = [UnknownEntryId] },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, MissingMana()),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, NullMana()),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, ListedMana()),
            ],
        },
        ValidRequest() with
        {
            Mainboard =
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, ListedMana(" ")),
            ],
        },
        ValidRequest() with { CandidateLandEntryIds = [LandEntryId, LandEntryId] },
        ValidRequest() with { LandRules = [null!] },
        ValidRequest() with { LandRules = [new OnCurveLandRuleInput(Guid.Empty, OnCurveLandRule.OneManaSameTurn)] },
        ValidRequest() with { LandRules = [new OnCurveLandRuleInput(LandEntryId, (OnCurveLandRule)99)] },
        ValidRequest() with { LandRules = [new OnCurveLandRuleInput(UnknownEntryId, OnCurveLandRule.OneManaSameTurn)] },
        ValidRequest() with
        {
            LandRules =
            [
                new OnCurveLandRuleInput(LandEntryId, OnCurveLandRule.OneManaSameTurn),
                new OnCurveLandRuleInput(LandEntryId, OnCurveLandRule.NotModeled),
            ],
        },
        ValidRequest() with { LandRules = [] },
    };

    /// <summary>
    /// Creates one complete request with a seven-card source count and one target copy.
    /// </summary>
    private static OnCurveRequest ValidRequest()
    {
        return new OnCurveRequest(
            DeckId,
            1,
            new OnCurveTarget(TargetEntryId, TargetPrintingId, "{G}"),
            [
                Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
                Entry(LandEntryId, LandPrintingId, 7, ListedMana("G")),
            ],
            [LandEntryId],
            [new OnCurveLandRuleInput(LandEntryId, OnCurveLandRule.OneManaSameTurn)],
            1,
            100,
            true,
            "0123456789abcdef");
    }

    /// <summary>
    /// Creates one resolved mainboard entry for a validation fixture.
    /// </summary>
    private static OnCurveDeckEntry Entry(
        Guid entryId,
        Guid printingId,
        int quantity,
        OnCurveProducedMana producedMana)
    {
        return new OnCurveDeckEntry(entryId, printingId, quantity, producedMana);
    }

    /// <summary>
    /// Creates the requested number of distinct valid mainboard entries.
    /// </summary>
    private static IReadOnlyList<OnCurveDeckEntry> Entries(int count)
    {
        List<OnCurveDeckEntry> entries = [];
        for (int index = 0; index < count; index++)
        {
            entries.Add(new OnCurveDeckEntry(StableGuid(1, index), StableGuid(2, index), 1, ListedMana("G")));
        }

        return entries;
    }

    /// <summary>
    /// Creates one stable unique identifier for a count-boundary fixture.
    /// </summary>
    private static Guid StableGuid(int group, int index)
    {
        return new Guid(index + 1, (short)group, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Creates a resolved source state where produced-mana was missing.
    /// </summary>
    private static OnCurveProducedMana MissingMana()
    {
        return new OnCurveProducedManaMissing();
    }

    /// <summary>
    /// Creates a resolved source state where produced-mana was null.
    /// </summary>
    private static OnCurveProducedMana NullMana()
    {
        return new OnCurveProducedManaNull();
    }

    /// <summary>
    /// Creates a resolved source state with the listed mana colors.
    /// </summary>
    private static OnCurveProducedMana ListedMana(params string[] colors)
    {
        return new OnCurveProducedManaValues(colors);
    }
}
