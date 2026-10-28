using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the <c>on-curve</c> preset of <c>stats_simulate</c>: it replays the original estimator's success counts
/// (SIM-004, cases S05 and S24), states the rules it expanded to, reads only installed card data, and refuses
/// facts it cannot trust.
/// </summary>
public sealed class StatisticsSimulatePresetTests
{
    /// <summary>
    /// Verifies every captured case of the original estimator replays to the same success count through the whole
    /// path: card data, preset expansion, request mapping, and the engine (SIM-004, cases S05 and S24).
    /// </summary>
    [Fact]
    public async Task Preset_ReplaysEveryCaptureOfTheOriginalEstimator()
    {
        IReadOnlyList<ReplayCase> cases = OnCurveReplayFixtures.Load();

        Assert.True(cases.Count >= 16);
        Assert.Contains(cases, item => item.TargetManaCost.Contains('U', StringComparison.Ordinal));
        foreach (ReplayCase item in cases)
        {
            (JsonObject[] cards, DeckCreateRequest deck) = BuildCase(item);
            using SimulateSetup setup = await SimulateSetup.CreateAsync(cards, install: true);
            DeckDocument saved = Assert.IsType<OperationSuccess<DeckDocument>>(
                (await setup.Data.Store.CreateAsync(deck, TestContext.Current.CancellationToken)).Value).Data;

            SimulateToolResult result = SimulateSetup.Success(await setup.Coordinator.SimulateAsync(
                new SimulationDeckInput { DeckId = saved.DeckId },
                null,
                null,
                null,
                null,
                null,
                new SimulationOnCurvePresetInput
                {
                    TargetEntryId = OnCurveReplayFixtures.Id(item.TargetEntry),
                    LandRules = [.. item.Entries.Where(entry => entry.LandRule is not null).Select(entry =>
                        new SimulationLandRuleInput
                        {
                            EntryId = OnCurveReplayFixtures.Id(entry.Id),
                            Rule = RuleName(entry.LandRule!),
                        })],
                    TurnLimit = item.TurnLimit,
                    OnThePlay = item.OnThePlay,
                },
                item.SampleCount,
                0,
                item.Seed,
                TestContext.Current.CancellationToken));

            Assert.True(
                item.ExpectedSuccessCount == result.Results[0].Hits,
                $"{item.Name}: expected {item.ExpectedSuccessCount}, got {result.Results[0].Hits}");
            Assert.Equal("on-curve", result.Results[0].Id);
            Assert.Equal(item.Seed, result.Seed);
        }
    }

    /// <summary>
    /// Verifies the preset reproduces the original tool's success count for the old App fixture deck, and reports
    /// the facts it read and the rules it expanded to.
    /// </summary>
    [Fact]
    public async Task Preset_WithTheAppFixtureDeck_ReplaysTheOriginalToolAndStatesItsRules()
    {
        using PresetFixture fixture = await PresetFixture.CreateAsync();

        SimulateToolResult result = SimulateSetup.Success(await fixture.RunAsync(fixture.Request()));

        Assert.Equal(OnCurveReplayFixtures.AppFixtureSuccessCount(), result.Results[0].Hits);
        SimulationPresetFacts facts = result.Preset!;
        Assert.Equal("on-curve", facts.Kind);
        Assert.Equal("Fixture Growth", facts.Target.Name);
        Assert.Equal("{1}{G}", facts.Target.ManaCost);
        Assert.Equal(DeckOnCurveSourceFixture.TargetPrintingId, facts.Target.PrintingId);
        SimulationPresetLand forest = Assert.Single(facts.CandidateLands);
        Assert.Equal(DeckOnCurveSourceFixture.GreenLandPrintingId, forest.PrintingId);
        Assert.Equal(["G"], forest.ProducedMana);
        Assert.Equal("one-mana-same-turn", forest.Rule);
        Assert.Equal(DeckOnCurveSourceFixture.KnownEmptyLandPrintingId, Assert.Single(facts.KnownEmptyLands).PrintingId);
        Assert.Equal(DeckOnCurveSourceFixture.MultiFacePrintingId, Assert.Single(facts.MultiFaceCards).PrintingId);
        Assert.Equal("installed-scryfall-card-data", facts.Source);
        SimulationPlayInput play = Assert.IsType<SimulationPlayInput>(Assert.Single(result.AppliedRules.EachTurn));
        Assert.Equal(("target-first", 1, "same-turn", "{1}{G}"), (play.Pick, play.MaxPerTurn, play.ManaFrom, play.TargetCost));
        SimulationCanCastInput cast = Assert.IsType<SimulationCanCastInput>(
            Assert.Single(result.AppliedRules.Conditions).Check);
        Assert.Equal((2, "{1}{G}", fixture.TargetEntryId), (cast.ByTurn, cast.ManaCost, cast.InHand));
        Assert.Null(result.AppliedRules.OpeningHand.Mulligan);
        Assert.Equal(7, result.AppliedRules.OpeningHand.Size);
        Assert.Contains(result.Limits, limit => limit.Contains("on-curve preset plays at most one land", StringComparison.Ordinal));
        Assert.Equal(100, result.Samples);
        string json = JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions);
        Assert.DoesNotContain("\"passed\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a land set to <c>not-modeled</c> leaves the group, and a preset with no modeled land never casts.
    /// </summary>
    [Fact]
    public async Task Preset_WithEveryLandNotModeled_HasNoGroupAndNoHits()
    {
        using PresetFixture fixture = await PresetFixture.CreateAsync();

        SimulateToolResult result = SimulateSetup.Success(await fixture.RunAsync(
            fixture.Request(rule: "not-modeled")));

        Assert.Equal(0, result.Results[0].Hits);
        Assert.Empty(result.Groups);
        Assert.Empty(result.AppliedRules.EachTurn);
        Assert.Equal("not-modeled", Assert.Single(result.Preset!.CandidateLands).Rule);
    }

    /// <summary>
    /// Verifies land rules must name every eligible land once, and each rule must be one of the three allowed
    /// names.
    /// </summary>
    [Fact]
    public async Task Preset_WithBadLandRules_IsRefused()
    {
        using PresetFixture fixture = await PresetFixture.CreateAsync();

        OperationInvalidInput missing = SimulateSetup.Invalid(await fixture.RunAsync(fixture.Request(landRules: [])));
        OperationInvalidInput twice = SimulateSetup.Invalid(await fixture.RunAsync(fixture.Request(
            landRules:
            [
                new SimulationLandRuleInput { EntryId = fixture.ForestEntryId, Rule = "not-modeled" },
                new SimulationLandRuleInput { EntryId = fixture.ForestEntryId, Rule = "not-modeled" },
            ])));
        OperationInvalidInput notALand = SimulateSetup.Invalid(await fixture.RunAsync(fixture.Request(
            landRules:
            [
                new SimulationLandRuleInput { EntryId = fixture.ForestEntryId, Rule = "not-modeled" },
                new SimulationLandRuleInput { EntryId = fixture.TargetEntryId, Rule = "not-modeled" },
            ])));
        OperationInvalidInput unknownRule = SimulateSetup.Invalid(await fixture.RunAsync(fixture.Request(rule: "sometimes")));

        Assert.Equal("invalid-land-rules", missing.ReasonCode);
        Assert.Contains(fixture.ForestEntryId.ToString("D"), missing.Message, StringComparison.Ordinal);
        Assert.Equal("preset.landRules[1].entryId", twice.Path);
        Assert.Equal("preset.landRules[1].entryId", notALand.Path);
        Assert.Equal("preset.landRules[0]", unknownRule.Path);
    }

    /// <summary>
    /// Verifies the target must be in the main zone and in the deck, and must have a cost the engine can read.
    /// </summary>
    [Fact]
    public async Task Preset_WithABadTarget_IsReported()
    {
        using PresetFixture fixture = await PresetFixture.CreateAsync();

        OperationResult<SimulateToolResult> unknown = await fixture.RunAsync(fixture.Request(target: Guid.NewGuid()));
        OperationResult<SimulateToolResult> multiFace = await fixture.RunAsync(
            fixture.Request(target: fixture.MultiFaceEntryId));
        OperationResult<SimulateToolResult> noCost = await fixture.RunAsync(
            fixture.Request(target: fixture.ForestEntryId));

        Assert.IsType<OperationNotFound>(unknown.Value);
        Assert.Equal("unsupported-on-curve-multiface-target", Assert.IsType<OperationUnsupported>(multiFace.Value).ReasonCode);
        OperationInvalidInput cost = Assert.IsType<OperationInvalidInput>(noCost.Value);
        Assert.Equal("unsupported-mana-cost", cost.ReasonCode);
        Assert.Equal("preset.targetEntryId", cost.Path);
    }

    /// <summary>
    /// Verifies a target in another zone is refused with a plain reason.
    /// </summary>
    [Fact]
    public async Task Preset_WithATargetInAnotherZone_IsRefused()
    {
        using PresetFixture fixture = await PresetFixture.CreateAsync(commander: true);

        OperationInvalidInput refused = SimulateSetup.Invalid(await fixture.RunAsync(
            fixture.Request(target: fixture.CommanderEntryId)));

        Assert.Equal("target-not-in-main-zone", refused.ReasonCode);
    }

    /// <summary>
    /// Verifies a single-faced land with no produced-mana fact stops the preset, and so does an entry with no
    /// installed card data, naming the entry.
    /// </summary>
    [Fact]
    public async Task Preset_WithUntrustedFacts_IsUnavailable()
    {
        using PresetFixture nullLand = await PresetFixture.CreateAsync(extraLand: DeckOnCurveSourceFixture.NullLandPrintingId);
        using PresetFixture missingLand = await PresetFixture.CreateAsync(extraLand: DeckOnCurveSourceFixture.MissingLandPrintingId);
        using PresetFixture noData = await PresetFixture.CreateAsync(unidentified: true);

        OperationUnavailable nullFact = Assert.IsType<OperationUnavailable>((await nullLand.RunAsync(nullLand.Request())).Value);
        OperationUnavailable missingFact = Assert.IsType<OperationUnavailable>((await missingLand.RunAsync(missingLand.Request())).Value);
        OperationUnavailable none = Assert.IsType<OperationUnavailable>((await noData.RunAsync(noData.Request())).Value);

        Assert.Equal("on-curve-land-produced-mana-unavailable", nullFact.ReasonCode);
        Assert.Equal("on-curve-land-produced-mana-unavailable", missingFact.ReasonCode);
        Assert.Equal("on-curve-card-fact-unavailable", none.ReasonCode);
        Assert.Contains("Mystery", none.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the preset points to the sync tool when card data is not installed.
    /// </summary>
    [Fact]
    public async Task Preset_WithoutInstalledCardData_IsNotCached()
    {
        using PresetFixture fixture = await PresetFixture.CreateAsync(install: false);

        OperationNotCached notCached = Assert.IsType<OperationNotCached>((await fixture.RunAsync(fixture.Request())).Value);

        Assert.Equal("card-data-not-installed", notCached.ReasonCode);
        Assert.Contains("scryfall_card_data_sync", notCached.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a main-zone deck of 150 land entries gives a preset result under the size limit, cuts the long land
    /// list with a stated omitted count, and still states the rules it expanded to (SIZE-001).
    /// </summary>
    [Fact]
    public async Task Preset_WithTheLargestDeck_StaysUnderTheSizeLimit()
    {
        List<JsonObject> cards = [CardJson.Make("Target", "Creature", "{2}{G}", 3m)];
        string[][] colors = [["G"], ["U"], ["B", "G"], ["W", "U"], ["R"], ["C"]];
        for (int index = 0; index < 149; index++)
        {
            cards.Add(CardJson.Make($"Land with a longer name {index}", "Land", null, 0m, producedMana: colors[index % colors.Length]));
        }

        using SimulateSetup setup = await SimulateSetup.CreateAsync(cards, install: true);
        List<DeckEntryDraft> entries = [.. cards.Select((card, index) => new DeckEntryDraft(
            1,
            card["name"]!.GetValue<string>(),
            PrintingId: CardJson.PrintingOf(card),
            Zone: "main",
            EntryId: Guid.Parse($"0193e97a-ef95-7700-8000-{index + 1:D12}")))];
        Guid deckId = Assert.IsType<OperationSuccess<DeckDocument>>((await setup.Data.Store.CreateAsync(
            new DeckCreateRequest("All lands", Entries: entries),
            TestContext.Current.CancellationToken)).Value).Data.DeckId;

        SimulateToolResult result = SimulateSetup.Success(await setup.Coordinator.SimulateAsync(
            new SimulationDeckInput { DeckId = deckId },
            null,
            null,
            null,
            null,
            null,
            new SimulationOnCurvePresetInput
            {
                TargetEntryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000001"),
                LandRules = [.. Enumerable.Range(2, 149).Select(number => new SimulationLandRuleInput
                {
                    EntryId = Guid.Parse($"0193e97a-ef95-7700-8000-{number:D12}"),
                    Rule = number % 3 == 0 ? "one-mana-next-turn" : "one-mana-same-turn",
                })],
                TurnLimit = 5,
                OnThePlay = true,
            },
            1_000,
            0,
            "0000000000000007",
            TestContext.Current.CancellationToken));

        Assert.Equal(149, result.Preset!.CandidateLands.Count + result.Preset.CandidateLandsOmitted.GetValueOrDefault());
        Assert.True(result.Truncated);
        Assert.Contains("candidate lands", result.Narrow, StringComparison.Ordinal);
        int size = ResultSize.Measure(new OperationSuccess<SimulateToolResult>(result));
        Assert.InRange(size, 1, 39_999);
    }

    /// <summary>
    /// Verifies the published examples bind to the tool and run, including one that uses the preset (SCHEMA-006).
    /// </summary>
    [Fact]
    public async Task Examples_BindAndRun()
    {
        JsonObject forest = CardJson.Make("Forest", "Basic Land — Forest", null, 0m, producedMana: ["G"]);
        JsonObject spell = CardJson.Make("Spell", "Creature", "{1}{G}", 2m);
        JsonObject filler = CardJson.Make("Filler", "Creature", "{2}", 2m);
        using SimulateSetup setup = await SimulateSetup.CreateAsync([forest, spell, filler], install: true);
        Guid forestEntry = Guid.Parse(MtgMcp.App.Decks.DeckToolExamples.PlaceholderEntryId);
        Guid spellEntry = Guid.Parse(StatisticsCastabilityTools.ExampleEntryId2);
        Guid category = Guid.Parse(StatisticsCastabilityTools.ExampleCategoryId);
        DeckDocument deck = Assert.IsType<OperationSuccess<DeckDocument>>((await setup.Data.Store.CreateAsync(
            new DeckCreateRequest(
                "Examples",
                Entries:
                [
                    new DeckEntryDraft(24, "Forest", PrintingId: CardJson.PrintingOf(forest), Zone: "main", EntryId: forestEntry),
                    new DeckEntryDraft(2, "Spell", PrintingId: CardJson.PrintingOf(spell), Zone: "main", EntryId: spellEntry),
                    new DeckEntryDraft(34, "Filler", PrintingId: CardJson.PrintingOf(filler), Zone: "main"),
                ],
                Categories: [new DeckCategoryDraft("Lands", CategoryId: category)],
                CategoryAssignments: [new DeckCategoryAssignment(forestEntry, category, true)]),
            TestContext.Current.CancellationToken)).Value).Data;
        using ToolCatalogFixture fixture = new();
        (string Name, string Json)[] examples = CardListExampleSupport.ReadExamples(
            CardListExampleSupport.DescriptionOf("stats_simulate"));

        Assert.Equal(["minimal", "full", "preset"], examples.Select(example => example.Name));
        foreach ((string name, string json) in examples)
        {
            string filled = CardListExampleSupport.Fill(json, deck, Guid.NewGuid(), Guid.NewGuid());
            Dictionary<string, JsonElement> arguments = CardListExampleSupport.Arguments(filled);
            Assert.Null(ToolCallPolicy.DiagnoseArguments(fixture.Catalog, "stats_simulate", arguments));
            Assert.Null(ToolCallPolicy.FindUnknownArgument(fixture.Catalog, "stats_simulate", arguments));

            OperationResult<SimulateToolResult> result = await setup.Tools.SimulateAsync(
                Read<SimulationDeckInput>(arguments, "deck")!,
                arguments["samples"].GetInt32(),
                arguments["sampleHands"].GetInt32(),
                Read<IReadOnlyList<SimulationGroupInput>>(arguments, "groups"),
                Read<SimulationOpeningHandInput>(arguments, "openingHand"),
                Read<IReadOnlyList<SimulationRuleInput>>(arguments, "eachTurn"),
                arguments.TryGetValue("turns", out JsonElement turns) ? turns.GetInt32() : null,
                Read<IReadOnlyList<SimulationConditionInput>>(arguments, "conditions"),
                Read<SimulationPresetInput>(arguments, "preset"),
                arguments.TryGetValue("seed", out JsonElement seed) ? seed.GetString() : null,
                TestContext.Current.CancellationToken);

            Assert.True(
                result.Value is OperationSuccess<SimulateToolResult>,
                $"{name}: {JsonSerializer.Serialize(result.Value)}");
        }
    }

    /// <summary>
    /// Verifies the tool description states the fixed rules, stays within the length limit, and names its source.
    /// </summary>
    [Fact]
    public void Description_StatesTheFixedRulesWithinTheLimit()
    {
        string description = CardListExampleSupport.DescriptionOf("stats_simulate");

        Assert.StartsWith("Seeded sampling", description, StringComparison.Ordinal);
        Assert.InRange(description.Length, 1, 4_096);
        foreach (string word in new[] { "London mulligan", "target-first", "listed-order", "draw-cards", "libraryEmpty", "sampleHands", "50,000,000", "Phyrexian", UntrustedTextNotes.Marker })
        {
            Assert.Contains(word, description, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Reads an optional argument into its type.
    /// </summary>
    private static T? Read<T>(Dictionary<string, JsonElement> arguments, string name)
    {
        return arguments.TryGetValue(name, out JsonElement value)
            ? value.Deserialize<T>(ToolCallPolicy.SerializerOptions)
            : default;
    }

    /// <summary>
    /// Turns a rule name of the captured fixtures into the tool's text.
    /// </summary>
    private static string RuleName(string captured)
    {
        return captured switch
        {
            "OneManaSameTurn" => "one-mana-same-turn",
            "OneManaNextTurn" => "one-mana-next-turn",
            _ => "not-modeled",
        };
    }

    /// <summary>
    /// Builds the card data and the saved deck of one captured case: a target with its cost, lands with their
    /// colors, and filler, keeping the entry IDs of the capture.
    /// </summary>
    private static (JsonObject[] Cards, DeckCreateRequest Deck) BuildCase(ReplayCase item)
    {
        List<JsonObject> cards = [];
        List<DeckEntryDraft> entries = [];
        foreach (ReplayEntry entry in item.Entries)
        {
            Guid printing = Guid.Parse($"00000000-0000-4000-8000-{entry.Id:D12}");
            JsonObject card = entry.Id == item.TargetEntry
                ? CardJson.Make("Target", "Creature", item.TargetManaCost, 3m, printingId: printing)
                : entry.Produces is not null
                    ? CardJson.Make($"Land {entry.Id}", "Land", null, 0m, producedMana: [.. entry.Produces], printingId: printing)
                    : CardJson.Make($"Filler {entry.Id}", "Creature", "{1}", 1m, printingId: printing);
            cards.Add(card);
            entries.Add(new DeckEntryDraft(
                entry.Quantity,
                card["name"]!.GetValue<string>(),
                PrintingId: printing,
                Zone: "main",
                SortOrder: entries.Count,
                EntryId: OnCurveReplayFixtures.Id(entry.Id)));
        }

        return ([.. cards], new DeckCreateRequest(item.Name, Format: "custom", Entries: entries));
    }

    /// <summary>
    /// Builds the card data, saved deck, and coordinator of the old App fixture: a target that costs {1}{G}, seven
    /// Forests, a multi-face land, and a land that lists no colors.
    /// </summary>
    private sealed class PresetFixture : IDisposable
    {
        /// <summary>
        /// Holds the folder of the stores.
        /// </summary>
        private readonly TemporaryDirectory folder = new();

        /// <summary>
        /// Holds the Scryfall service.
        /// </summary>
        private ScryfallService scryfall = null!;

        /// <summary>
        /// Holds the deck store.
        /// </summary>
        private SqliteDeckStore store = null!;

        /// <summary>
        /// Gets the target entry.
        /// </summary>
        internal Guid TargetEntryId { get; private set; }

        /// <summary>
        /// Gets the Forest entry.
        /// </summary>
        internal Guid ForestEntryId { get; private set; }

        /// <summary>
        /// Gets the multi-face entry.
        /// </summary>
        internal Guid MultiFaceEntryId { get; private set; }

        /// <summary>
        /// Gets the commander entry, when the deck has one.
        /// </summary>
        internal Guid CommanderEntryId { get; private set; }

        /// <summary>
        /// Gets the saved deck.
        /// </summary>
        internal Guid DeckId { get; private set; }

        /// <summary>
        /// Creates the fixture.
        /// </summary>
        internal static async Task<PresetFixture> CreateAsync(
            bool install = true,
            bool commander = false,
            Guid? extraLand = null,
            bool unidentified = false)
        {
            PresetFixture fixture = new();
            DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
            fixture.scryfall = DeckOnCurveSourceFixture.CreateService(fixture.folder.Path, handler);
            if (install)
            {
                _ = await fixture.scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken);
            }

            fixture.store = new SqliteDeckStore(fixture.folder.Path, "test");
            List<DeckEntryDraft> entries =
            [
                new(1, "Fixture Growth", PrintingId: DeckOnCurveSourceFixture.TargetPrintingId, Zone: "main"),
                new(7, "Fixture Forest", PrintingId: DeckOnCurveSourceFixture.GreenLandPrintingId, Zone: "main"),
                new(1, "Fixture Front // Fixture Back", PrintingId: DeckOnCurveSourceFixture.MultiFacePrintingId, Zone: "main"),
                new(1, "Fixture Empty Land", PrintingId: DeckOnCurveSourceFixture.KnownEmptyLandPrintingId, Zone: "main"),
            ];
            if (extraLand is Guid land)
            {
                entries.Add(new DeckEntryDraft(1, "Fixture Extra Land", PrintingId: land, Zone: "main"));
            }

            if (unidentified)
            {
                entries.Add(new DeckEntryDraft(1, "Mystery", Zone: "main"));
            }

            if (commander)
            {
                entries.Add(new DeckEntryDraft(1, "Fixture Commander", PrintingId: DeckOnCurveSourceFixture.TargetPrintingId, Zone: "commander"));
            }

            DeckDocument deck = Assert.IsType<OperationSuccess<DeckDocument>>((await fixture.store.CreateAsync(
                new DeckCreateRequest("Preset", Format: "custom", Entries: entries),
                TestContext.Current.CancellationToken)).Value).Data;
            fixture.DeckId = deck.DeckId;
            fixture.TargetEntryId = deck.Entries.Single(entry => entry.CardName == "Fixture Growth").EntryId;
            fixture.ForestEntryId = deck.Entries.Single(entry => entry.CardName == "Fixture Forest").EntryId;
            fixture.MultiFaceEntryId = deck.Entries.Single(entry => entry.CardName.StartsWith("Fixture Front", StringComparison.Ordinal)).EntryId;
            fixture.CommanderEntryId = deck.Entries.SingleOrDefault(entry => entry.CardName == "Fixture Commander")?.EntryId ?? Guid.Empty;
            return fixture;
        }

        /// <summary>
        /// Builds the preset request of the old App test: turn 2, 100 samples on the play, one same-turn Forest rule.
        /// </summary>
        internal SimulationOnCurvePresetInput Request(
            Guid? target = null,
            IReadOnlyList<SimulationLandRuleInput>? landRules = null,
            string rule = "one-mana-same-turn")
        {
            return new SimulationOnCurvePresetInput
            {
                TargetEntryId = target ?? TargetEntryId,
                LandRules = landRules ?? [new SimulationLandRuleInput { EntryId = ForestEntryId, Rule = rule }],
                TurnLimit = 2,
                OnThePlay = true,
            };
        }

        /// <summary>
        /// Runs the preset.
        /// </summary>
        internal Task<OperationResult<SimulateToolResult>> RunAsync(SimulationOnCurvePresetInput preset)
        {
            StatisticsSimulateCoordinator coordinator = new(
                new CardListResolver(scryfall, new CardListAccess(store, null)));
            return coordinator.SimulateAsync(
                new SimulationDeckInput { DeckId = DeckId },
                null,
                null,
                null,
                null,
                null,
                preset,
                100,
                0,
                "0123456789abcdef",
                TestContext.Current.CancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            scryfall.Dispose();
            store.Dispose();
            folder.Dispose();
        }
    }
}
