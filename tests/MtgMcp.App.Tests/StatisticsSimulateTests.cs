using System.Text.Json;
using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using static MtgMcp.App.Tests.SimulateSetup;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>stats_simulate</c> over a local deck with groups chosen by ID: resolution, the shape checks, the work
/// limit, the size limit, and determinism (SIM-001 to SIM-003, SIM-005, SIM-006, SIM-009, SIZE-001, cases S01 to S04,
/// S11, and S19).
/// </summary>
public sealed class StatisticsSimulateTests
{
    /// <summary>
    /// Writes results indented, so the golden file is readable and differences are line by line.
    /// </summary>
    private static readonly JsonSerializerOptions IndentedOptions = new(ToolCallPolicy.SerializerOptions)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Verifies a run with groups chosen by category reports the S01 counts, the deck read, the applied rules with
    /// their defaults, and the group sizes.
    /// </summary>
    [Fact]
    public async Task Simulate_WithCategoryGroups_ReportsHitsAppliedRulesAndGroups()
    {
        using SimulateSetup setup = await CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();

        SimulateToolResult result = Success(await setup.RunAsync(
            conditions:
            [
                Condition("four-mana", new SimulationManaAtLeastInput { Amount = 4, Turn = 4 }),
                Condition("lands-seen", new SimulationDrawnAtLeastInput { Group = "lands", Count = 5, ByTurn = 3 }),
            ],
            samples: 2_000));

        Assert.Equal("0000000000000001", result.Seed);
        Assert.Equal("splitmix64-v1", result.RandomVersion);
        Assert.Equal("simulation-v1", result.ModelVersion);
        Assert.Equal(2_000, result.Samples);
        Assert.Matches("^[0-9a-f]{64}$", result.Fingerprint);
        Assert.Equal(["four-mana", "lands-seen"], result.Results.Select(item => item.Id));
        Assert.All(result.Results, item => Assert.InRange(item.Rate, item.Wilson95[0], item.Wilson95[1]));
        SimulationGroupView group = Assert.Single(result.Groups);
        Assert.Equal(new SimulationGroupView("lands", 1, 24), group);
        SimulationPlayInput play = Assert.IsType<SimulationPlayInput>(Assert.Single(result.AppliedRules.EachTurn));
        Assert.Equal(1, play.AddsMana);
        Assert.Equal(4, result.AppliedRules.Turns);
        CardListSourceInfo source = Assert.Single(result.Sources);
        Assert.Equal(deck.DeckId, source.DeckId);
        Assert.Equal(deck.Revision, source.Revision);
        Assert.Null(result.Mulligans);
        Assert.Equal(0, result.LibraryEmpty);
        Assert.Null(result.Truncated);
        string json = JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions);
        Assert.DoesNotContain("\"passed\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a category that also holds a commander-zone entry lists that entry as skipped, with its reason, and
    /// the group only counts main-zone cards.
    /// </summary>
    [Fact]
    public async Task Simulate_WithACategoryHoldingAnotherZone_ListsTheSkippedEntry()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        SimulateToolResult result = Success(await setup.RunAsync());

        SimulationSkipped skipped = Assert.Single(result.Skipped);
        Assert.Equal(CommanderEntryId, skipped.EntryId);
        Assert.Equal("Commander", skipped.Name);
        Assert.Equal("lands", skipped.Group);
        Assert.Equal("not-in-main-zone", skipped.Reason);
        Assert.Equal(24, result.Groups[0].Cards);
    }

    /// <summary>
    /// Verifies an entry named by ID that is not in the main zone is refused, not dropped.
    /// </summary>
    [Fact]
    public async Task Simulate_WithAnEntryIdOutsideTheMainZone_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput refused = Invalid(await setup.RunAsync(groups: [Entries("lands", CommanderEntryId)]));

        Assert.Equal("entry-not-in-main-zone", refused.ReasonCode);
        Assert.Equal("groups[0].select.ids", refused.Path);
        Assert.Contains("Commander", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the same request and seed give byte-identical results, and another seed does not (SIM-002).
    /// </summary>
    [Fact]
    public async Task Simulate_WithTheSameSeed_GivesIdenticalText()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        string first = Serialize(Success(await setup.RunAsync(samples: 3_000, sampleHands: 3)));
        string second = Serialize(Success(await setup.RunAsync(samples: 3_000, sampleHands: 3)));
        string other = Serialize(
            Success(await setup.RunAsync(samples: 3_000, sampleHands: 3, seed: "0000000000000002")));

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    /// <summary>
    /// Verifies the fixed-seed result serializes to the checked-in text on every platform (SIM-002, case S01). CI
    /// runs this test on Linux as well as Windows.
    /// </summary>
    [Fact]
    public async Task Simulate_WithAFixedSeed_MatchesTheGoldenResult()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        SimulateToolResult result = Success(await setup.RunAsync(
            groups: [Lands(), Entries("ramp", RampEntryId)],
            eachTurn:
            [
                Play("lands"),
                new SimulationCastIfAbleInput
                {
                    Group = "ramp",
                    Pick = "listed-order",
                    MaxPerTurn = 1,
                    Cost = 2,
                    ManaFrom = "next-turn",
                },
            ],
            conditions:
            [
                Condition("four-mana", new SimulationManaAtLeastInput { Amount = 4, Turn = 4 }),
                Condition("ramp-seen", new SimulationDrawnAtLeastInput { Group = "ramp", Count = 1, ByTurn = 2 }),
            ],
            openingHand: Hand(mulligan: new SimulationMulliganInput
            {
                Rule = "london",
                FreeFirstMulligan = true,
                MaxMulligans = 2,
                KeepWhen = new SimulationKeepCountBetweenInput { Group = "lands", Min = 2, Max = 5 },
                Bottom = new SimulationBottomInput { PreferGroups = ["ramp"], Otherwise = "random" },
            }),
            samples: 4_000,
            sampleHands: 2,
            seed: "9f3c1a7e5b2d4c60"));

        string golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "simulate-golden.json"))
            .ReplaceLineEndings("\n")
            .TrimEnd();
        Assert.Equal(golden, Serialize(result));
    }

    /// <summary>
    /// Verifies <c>sampleHands</c> returns the first samples with a legend of card names, and asking for them changes
    /// no count (S19).
    /// </summary>
    [Fact]
    public async Task Simulate_WithSampleHands_ReturnsTheCountedSamplesAndNames()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        SimulateToolResult withHands = Success(await setup.RunAsync(samples: 500, sampleHands: 3));
        SimulateToolResult without = Success(await setup.RunAsync(samples: 500, sampleHands: 0));

        Assert.Equal(without.Results.Select(item => item.Hits), withHands.Results.Select(item => item.Hits));
        Assert.Equal(3, withHands.SampleHands.Count);
        Assert.Empty(without.SampleHands);
        Assert.Empty(without.SampleCards);
        HashSet<Guid> named = [.. withHands.SampleCards.Select(card => card.EntryId)];
        Assert.All(
            withHands.SampleHands.SelectMany(sample => sample.Kept),
            id => Assert.Contains(id, named));
        Assert.Equal("Forest", withHands.SampleCards.Single(card => card.EntryId == LandEntryId).Name);
        Assert.Null(withHands.SampleHandsOmitted);
    }

    /// <summary>
    /// Verifies a group that names no declared group in a rule or condition is refused with the path (S02).
    /// </summary>
    [Fact]
    public async Task Simulate_WithAnUnknownGroup_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput refused = Invalid(await setup.RunAsync(
            conditions: [Condition("x", new SimulationDrawnAtLeastInput { Group = "nope", Count = 1, ByTurn = 1 })]));

        Assert.Equal("invalid-simulation-group", refused.ReasonCode);
        Assert.Equal("conditions[0].check.group", refused.Path);
    }

    /// <summary>
    /// Verifies a request over the work limit is refused and states the limit (S03).
    /// </summary>
    [Fact]
    public async Task Simulate_OverTheWorkLimit_IsRefusedWithTheLimit()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput refused = Invalid(await setup.RunAsync(samples: 100_000, turns: 20));

        Assert.Equal("simulation-too-large", refused.ReasonCode);
        Assert.Contains("limit of 50000000", refused.Message, StringComparison.Ordinal);
        Assert.Contains("120000000", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a card in two groups is refused and names the entry, for groups chosen by entry and by category (S04).
    /// </summary>
    [Fact]
    public async Task Simulate_WithOverlappingGroups_NamesTheEntry()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput byEntry = Invalid(await setup.RunAsync(
            groups: [Entries("a", LandEntryId, RampEntryId), Entries("b", RampEntryId)]));
        OperationInvalidInput byCategory = Invalid(await setup.RunAsync(
            groups: [Lands("a"), Entries("b", LandEntryId)]));

        Assert.Equal("overlapping-groups", byEntry.ReasonCode);
        Assert.Contains(RampEntryId.ToString("D"), byEntry.Message, StringComparison.Ordinal);
        Assert.Equal("overlapping-groups", byCategory.ReasonCode);
        Assert.Contains(LandEntryId.ToString("D"), byCategory.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <c>listed-order</c> on a group chosen by category is refused (S11).
    /// </summary>
    [Fact]
    public async Task Simulate_WithListedOrderOnACategoryGroup_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput refused = Invalid(await setup.RunAsync(eachTurn: [Play("lands", "listed-order")]));

        Assert.Equal("invalid-simulation-rule", refused.ReasonCode);
        Assert.Equal("eachTurn[0].pick", refused.Path);
    }

    /// <summary>
    /// Verifies <c>listed-order</c> on a group chosen by entry IDs runs.
    /// </summary>
    [Fact]
    public async Task Simulate_WithListedOrderOnAnEntryGroup_Runs()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        Success(await setup.RunAsync(
            groups: [Entries("lands", LandEntryId)],
            eachTurn: [Play("lands", "listed-order")]));
    }

    /// <summary>
    /// Verifies a missing or unknown deck, a stale revision, and an empty main zone are reported plainly.
    /// </summary>
    [Fact]
    public async Task Simulate_WithABadDeck_IsReported()
    {
        using SimulateSetup setup = await CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();

        OperationResult<SimulateToolResult> unknown = await setup.RunAsync(
            deck: new SimulationDeckInput { DeckId = Guid.NewGuid() });
        OperationResult<SimulateToolResult> stale = await setup.RunAsync(
            deck: new SimulationDeckInput { DeckId = deck.DeckId, ExpectedRevision = deck.Revision + 5 });

        Assert.IsType<OperationNotFound>(unknown.Value);
        OperationConflict conflict = Assert.IsType<OperationConflict>(stale.Value);
        Assert.Equal(deck.Revision, conflict.Current!.Revision);
    }

    /// <summary>
    /// Verifies a deck with nothing in the main zone is refused.
    /// </summary>
    [Fact]
    public async Task Simulate_WithAnEmptyMainZone_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        OperationResult<DeckDocument> created = await setup.Data.Store.CreateAsync(
            new DeckCreateRequest("Only a commander", Entries: [new DeckEntryDraft(1, "Commander", Zone: "commander")]),
            TestContext.Current.CancellationToken);
        Guid id = Assert.IsType<OperationSuccess<DeckDocument>>(created.Value).Data.DeckId;

        OperationInvalidInput refused = Invalid(await setup.RunAsync(
            deck: new SimulationDeckInput { DeckId = id },
            groups: []));

        Assert.Equal("empty-main-zone", refused.ReasonCode);
    }

    /// <summary>
    /// Verifies unknown category and entry IDs in a select are refused with the select's path.
    /// </summary>
    [Fact]
    public async Task Simulate_WithUnknownSelectIds_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput entry = Invalid(await setup.RunAsync(groups: [Entries("x", Guid.NewGuid())]));
        OperationInvalidInput category = Invalid(await setup.RunAsync(
            groups: [
                new SimulationGroupInput { Id = "x", Select = new CastabilityCategoryIdsSelect([Guid.NewGuid()]) }]));

        Assert.Equal("unknown-entry-id", entry.ReasonCode);
        Assert.Equal("groups[0].select.ids", entry.Path);
        Assert.Equal("unknown-category-id", category.ReasonCode);
    }

    /// <summary>
    /// Verifies the request must give a preset or every rule part, never both.
    /// </summary>
    [Fact]
    public async Task Simulate_WithAMissingOrExtraPart_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();
        SimulationOnCurvePresetInput preset = new()
        {
            TargetEntryId = SpellEntryId,
            LandRules = [],
            TurnLimit = 3,
            OnThePlay = true,
        };
        SimulationDeckInput deck = new() { DeckId = DeckId };

        OperationInvalidInput noRules = Invalid(await setup.Coordinator.SimulateAsync(
            deck, null, null, null, null, null, null, 100, 0, null, TestContext.Current.CancellationToken));
        OperationInvalidInput noTurns = Invalid(await setup.Coordinator.SimulateAsync(
            deck,
            [],
            Hand(),
            [],
            null,
            [Condition("c", new SimulationManaAtLeastInput { Amount = 1, Turn = 1 })],
            null,
            100,
            0,
            null, TestContext.Current.CancellationToken));
        OperationInvalidInput both = Invalid(await setup.Coordinator.SimulateAsync(
            deck, [], null, null, null, null, preset, 100, 0, null, TestContext.Current.CancellationToken));
        OperationInvalidInput noDeck = Invalid(await setup.Coordinator.SimulateAsync(
            null!, null, null, null, null, null, null, 100, 0, null, TestContext.Current.CancellationToken));

        Assert.Equal(("missing-field", "openingHand"), (noRules.ReasonCode, noRules.Path));
        Assert.Equal(("missing-field", "turns"), (noTurns.ReasonCode, noTurns.Path));
        Assert.Equal(("field-not-allowed", "groups"), (both.ReasonCode, both.Path));
        Assert.Equal("deck", noDeck.Path);
    }

    /// <summary>
    /// Verifies a seed must be 16 hexadecimal characters and a missing seed is generated and returned.
    /// </summary>
    [Fact]
    public async Task Simulate_Seed_IsCheckedAndGenerated()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput bad = Invalid(await setup.RunAsync(seed: "xyz"));
        SimulateToolResult generated = Success(await setup.RunAsync(seed: null));
        SimulateToolResult again = Success(await setup.RunAsync(seed: generated.Seed));

        Assert.Equal("invalid-seed", bad.ReasonCode);
        Assert.Matches("^[0-9a-f]{16}$", generated.Seed);
        Assert.Equal(generated.Results.Select(item => item.Hits), again.Results.Select(item => item.Hits));
    }

    /// <summary>
    /// Verifies null values inside lists and required objects are reported as missing, never thrown.
    /// </summary>
    [Fact]
    public async Task Simulate_WithNullPieces_ReportsMissingFields()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput group = Invalid(await setup.RunAsync(groups: [null!]));
        OperationInvalidInput rule = Invalid(await setup.RunAsync(eachTurn: [null!]));
        OperationInvalidInput condition = Invalid(await setup.RunAsync(conditions: [null!]));
        OperationInvalidInput check = Invalid(await setup.RunAsync(
            conditions: [new SimulationConditionInput { Id = "c", Check = null! }]));
        OperationInvalidInput nested = Invalid(await setup.RunAsync(
            conditions: [Condition("c", new SimulationAllInput { Checks = [null!] })]));
        OperationInvalidInput ruleGroup = Invalid(await setup.RunAsync(
            eachTurn: [
                new SimulationPlayInput { Group = null!, Pick = "random", MaxPerTurn = 1, ManaFrom = "same-turn" }]));

        OperationInvalidInput[] failures = [group, rule, condition, check, nested, ruleGroup];
        Assert.Equal(
            [
                "groups[0]",
                "eachTurn[0]",
                "conditions[0]",
                "conditions[0].check",
                "conditions[0].check.checks[0]",
                "eachTurn[0].group"],
            failures.Select(item => item.Path));
        Assert.All(failures, item => Assert.Equal("missing-field", item.ReasonCode));
    }

    /// <summary>
    /// Verifies closed text values that the schema lists are checked at run time too.
    /// </summary>
    [Fact]
    public async Task Simulate_WithAnUnknownClosedValue_IsRefusedWithTheAllowedValues()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();

        OperationInvalidInput pick = Invalid(await setup.RunAsync(eachTurn: [Play("lands", "best")]));
        OperationInvalidInput manaFrom = Invalid(await setup.RunAsync(eachTurn: [Play("lands", manaFrom: "later")]));
        OperationInvalidInput effect = Invalid(await setup.RunAsync(
            eachTurn: [
                new SimulationDrawCardsInput {
                     Group = "lands",
                    Pick = "random",
                    MaxPerTurn = 1,
                    Cost = 0,
                    Cards = 1,
                    Effect = "forever" }]));
        OperationInvalidInput rule = Invalid(
            await setup.RunAsync(openingHand: Hand(mulligan: Mulligan(rule: "vancouver"))));
        OperationInvalidInput otherwise = Invalid(await setup.RunAsync(
            openingHand: Hand(mulligan: Mulligan(otherwise: "worst"))));

        Assert.Equal(
            [
                "eachTurn[0].pick",
                "eachTurn[0].manaFrom",
                "eachTurn[0].effect",
                "openingHand.mulligan.rule",
                "openingHand.mulligan.bottom.otherwise",
            ],
            new[] { pick, manaFrom, effect, rule, otherwise }.Select(item => item.Path));
        Assert.Contains("random or listed-order or target-first", pick.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the number of IDs a request lists is bounded, so the echoed rules stay small.
    /// </summary>
    [Fact]
    public async Task Simulate_WithTooManyIds_IsRefused()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();
        Guid[] ids = [.. Enumerable.Range(0, 151).Select(_ => Guid.NewGuid())];

        OperationInvalidInput oneSelect = Invalid(await setup.RunAsync(groups: [Entries("big", ids)]));
        OperationInvalidInput perRequest = Invalid(await setup.RunAsync(
            groups: [.. Enumerable.Range(0, 3).Select(index => Entries($"g{index}", [.. ids.Take(110)]))]));

        Assert.Equal("too-many-ids", oneSelect.ReasonCode);
        Assert.Equal("too-many-ids", perRequest.ReasonCode);
    }

    /// <summary>
    /// Verifies <c>byEntry</c> colors and <c>manaFromByEntry</c> carry through to a hit.
    /// </summary>
    [Fact]
    public async Task Simulate_WithColorsAndTiming_PaysTheCost()
    {
        using SimulateSetup setup = await CreateAsync();
        await setup.CreateDeckAsync();
        SimulationGroupInput colored = new()
        {
            Id = "lands",
            Select = new CastabilityEntryIdsSelect([LandEntryId]),
            Produces = new SimulationProducesInput
            {
                Default = ["G"],
                ByEntry = [new SimulationProducesOverrideInput { EntryIds = [LandEntryId], Colors = ["B", "G"] }],
            },
        };

        SimulateToolResult result = Success(await setup.RunAsync(
            groups: [colored],
            eachTurn:
            [
                new SimulationPlayInput
                {
                    Group = "lands",
                    Pick = "random",
                    MaxPerTurn = 2,
                    ManaFrom = "same-turn",
                    ManaFromByEntry = [
                        new SimulationManaFromOverrideInput { EntryIds = [LandEntryId], ManaFrom = "same-turn" }],
                },
            ],
            conditions: [Condition("bg", new SimulationCanCastInput { ManaCost = "{B}{G}", ByTurn = 4 })],
            samples: 300));

        Assert.True(result.Results[0].Hits > 250);
    }

    /// <summary>
    /// Verifies the result of a request that asks for the most it can stays under the size limit with the sample
    /// hands cut, and says what was cut and how to narrow the request (SIZE-001).
    /// </summary>
    [Fact]
    public async Task Simulate_WithTheLargestSampleHands_CutsToTheSizeLimit()
    {
        using SimulateSetup setup = await CreateAsync();
        OperationResult<DeckDocument> created = await setup.Data.Store.CreateAsync(
            new DeckCreateRequest(
                "Wide",
                Entries: [.. Enumerable.Range(1, 100).Select(index => new DeckEntryDraft(
                    1,
                    $"Card {index}",
                    Zone: "main",
                    EntryId: Guid.Parse($"0193e97a-ef95-7700-8000-{index:D12}")))]),
            TestContext.Current.CancellationToken);
        Guid wide = Assert.IsType<OperationSuccess<DeckDocument>>(created.Value).Data.DeckId;
        Guid[] all = [.. Enumerable.Range(1, 100).Select(index => Guid.Parse($"0193e97a-ef95-7700-8000-{index:D12}"))];

        SimulateToolResult cut = Success(await setup.RunAsync(
            deck: new SimulationDeckInput { DeckId = wide },
            groups: [Entries("all", all)],
            eachTurn: [Play("all", maxPerTurn: 3)],
            turns: 20,
            samples: 50,
            sampleHands: 10));
        SimulateToolResult small = Success(await setup.RunAsync(
            deck: new SimulationDeckInput { DeckId = wide },
            groups: [Entries("all", all)],
            eachTurn: [Play("all")],
            turns: 2,
            samples: 50,
            sampleHands: 2));

        Assert.True(cut.Truncated);
        Assert.InRange(cut.SampleHandsOmitted!.Value, 1, 10);
        Assert.Equal(10 - cut.SampleHandsOmitted, cut.SampleHands.Count);
        Assert.Contains("fewer sampleHands", cut.Narrow, StringComparison.Ordinal);
        Assert.InRange(Size(cut), 1, 39_999);
        Assert.Null(small.Truncated);
        Assert.Null(small.Narrow);
        Assert.Equal(2, small.SampleHands.Count);
        Assert.InRange(Size(small), 1, 39_999);
    }

    /// <summary>
    /// Verifies a request with the most groups, conditions, and ids stays under the size limit even with every
    /// sample hand cut.
    /// </summary>
    [Fact]
    public async Task Simulate_WithTheLargestRuleSet_StaysUnderTheSizeLimit()
    {
        using SimulateSetup setup = await CreateAsync();
        OperationResult<DeckDocument> created = await setup.Data.Store.CreateAsync(
            new DeckCreateRequest(
                "Widest",
                Entries: [.. Enumerable.Range(1, 150).Select(index => new DeckEntryDraft(
                    1,
                    $"A fairly long card name number {index}",
                    Zone: "main",
                    EntryId: Guid.Parse($"0193e97a-ef95-7700-8000-{index:D12}")))]),
            TestContext.Current.CancellationToken);
        Guid widest = Assert.IsType<OperationSuccess<DeckDocument>>(created.Value).Data.DeckId;
        List<SimulationGroupInput> groups = [];
        List<SimulationRuleInput> rules = [];
        for (int index = 0; index < 20; index++)
        {
            Guid[] ids = [
                .. Enumerable.Range(index * 7 + 1, 7).Select(
                    number => Guid.Parse($"0193e97a-ef95-7700-8000-{number:D12}"))];
            groups.Add(Entries($"group-number-{index}", ids));
            rules.Add(Play($"group-number-{index}", maxPerTurn: 2));
        }

        SimulateToolResult result = Success(await setup.RunAsync(
            deck: new SimulationDeckInput { DeckId = widest },
            groups: groups,
            eachTurn: rules,
            conditions: [.. Enumerable.Range(0, 20).Select(index => Condition(
                $"condition-with-a-long-name-{index}",
                new SimulationDrawnAtLeastInput { Group = $"group-number-{index}", Count = 1, ByTurn = 5 }))],
            turns: 20,
            samples: 20,
            sampleHands: 10));

        Assert.InRange(Size(result), 1, 39_999);
        Assert.Equal(20, result.Results.Count);
    }

    /// <summary>
    /// Verifies a failing request never changes the saved deck.
    /// </summary>
    [Fact]
    public async Task Simulate_NeverChangesTheDeck()
    {
        using SimulateSetup setup = await CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();

        Success(await setup.RunAsync(samples: 100));
        _ = await setup.RunAsync(groups: [Entries("x", Guid.NewGuid())]);

        DeckDocument after = Assert.IsType<OperationSuccess<DeckDocument>>(
            (await setup.Data.Store.GetAsync(DeckId, TestContext.Current.CancellationToken)).Value).Data;
        Assert.Equal(deck.Revision, after.Revision);
    }

    /// <summary>
    /// Measures a result as the server writes it.
    /// </summary>
    private static int Size(SimulateToolResult result)
    {
        return ResultSize.Measure(new OperationSuccess<SimulateToolResult>(result));
    }

    /// <summary>
    /// Writes a result the way the server does.
    /// </summary>
    private static string Serialize(SimulateToolResult result)
    {
        return JsonSerializer.Serialize(result, IndentedOptions).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Builds a mulligan that keeps a hand with two to five lands.
    /// </summary>
    private static SimulationMulliganInput Mulligan(string rule = "london", string otherwise = "random")
    {
        return new SimulationMulliganInput
        {
            Rule = rule,
            FreeFirstMulligan = true,
            MaxMulligans = 1,
            KeepWhen = new SimulationKeepCountBetweenInput { Group = "lands", Min = 2, Max = 5 },
            Bottom = new SimulationBottomInput { PreferGroups = [], Otherwise = otherwise },
        };
    }
}
