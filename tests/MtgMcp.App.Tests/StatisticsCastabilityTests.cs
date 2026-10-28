using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Statistics;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>stats_castability</c> over local decks: shared costs, sorting, skipped cards, source groups, errors,
/// and the published examples (CAST-001, CAST-002, cases K03 to K07).
/// </summary>
public sealed class StatisticsCastabilityTests
{
    /// <summary>
    /// Holds the land card used as the green source.
    /// </summary>
    private static readonly JsonObject Forest = CardJson.Make("Forest", "Basic Land — Forest", null, 0m,
        producedMana: ["G"]);

    /// <summary>
    /// Holds a land card that makes black.
    /// </summary>
    private static readonly JsonObject Swamp = CardJson.Make("Swamp", "Basic Land — Swamp", null, 0m,
        producedMana: ["B"]);

    /// <summary>
    /// Holds two spells that share the cost {2}{B}.
    /// </summary>
    private static readonly JsonObject Doom = CardJson.Make("Doom", "Sorcery", "{2}{B}", 3m, ["B"]);

    /// <summary>
    /// Holds the second spell that shares {2}{B}.
    /// </summary>
    private static readonly JsonObject Gloom = CardJson.Make("Gloom", "Instant", "{2}{B}", 3m, ["B"]);

    /// <summary>
    /// Holds an easy one-mana green spell.
    /// </summary>
    private static readonly JsonObject Sprout = CardJson.Make("Sprout", "Instant", "{G}", 1m, ["G"]);

    /// <summary>
    /// Holds a harder green spell.
    /// </summary>
    private static readonly JsonObject Overgrow = CardJson.Make("Overgrow", "Sorcery", "{1}{G}{G}", 3m, ["G"]);

    /// <summary>
    /// Holds a spell with a variable cost.
    /// </summary>
    private static readonly JsonObject Fireball = CardJson.Make("Fireball", "Sorcery", "{X}{R}", 1m, ["R"]);

    /// <summary>
    /// Holds a hybrid spell.
    /// </summary>
    private static readonly JsonObject Hybrid = CardJson.Make("Hybrid", "Sorcery", "{W/U}", 1m, ["W", "U"]);

    /// <summary>
    /// Holds a Phyrexian spell.
    /// </summary>
    private static readonly JsonObject Phyrexian = CardJson.Make("Phyrexian", "Sorcery", "{G/P}", 1m, ["G"]);

    /// <summary>
    /// Holds a spell with no mana cost.
    /// </summary>
    private static readonly JsonObject Free = CardJson.Make("Free", "Sorcery", null, 0m);

    /// <summary>
    /// Verifies two spells that share a cost give one row that lists both, rows sort by probability with no target or
    /// pass (cases K03 and K05), and the unconditional probability equals the mana availability engine.
    /// </summary>
    [Fact]
    public async Task Calculate_SharedCostsAndSorting_GiveOneRowPerCostLowestFirst()
    {
        using Setup setup = await Setup.CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();

        CastabilityToolResult result = Success(await setup.Run(deck, requireLandDrops: true));

        Assert.Equal(["{2}{B}", "{1}{G}{G}", "{G}"], result.Rows.Select(row => row.Cost));
        Assert.Equal(["Doom", "Gloom"], result.Rows[0].Cards.Select(card => card.Name));
        Assert.Equal([3, 3, 1], result.Rows.Select(row => row.Turn));
        Assert.Equal([9, 9, 7], result.Rows.Select(row => row.CardsSeen));
        Assert.True(result.RequireLandDrops);
        string json = JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions);
        Assert.DoesNotContain("passed", json, StringComparison.Ordinal);
        Assert.DoesNotContain("target", json, StringComparison.Ordinal);
        Assert.Equal(["lands", "rocks"], result.Groups.Select(group => group.Id));
        Assert.Equal(20, result.Groups[0].Cards);

        CastabilityToolResult plain = Success(await setup.Run(deck, requireLandDrops: false));
        ExactStatisticsCalculator calculator = new("test");
        ManaAvailabilityResult availability = Assert.IsType<StatisticsExact<ManaAvailabilityResult>>(
            Assert.IsType<OperationSuccess<StatisticsCalculation<ManaAvailabilityResult>>>(
                calculator.CalculateManaAvailability(new ManaAvailabilityRequest(
                    new StatisticsPopulation(
                        [
                            new StatisticsPopulationBucket(20, ["lands"]),
                            new StatisticsPopulationBucket(8, ["rocks"]),
                            new StatisticsPopulationBucket(9, []),
                        ],
                        ["lands", "rocks"]),
                    9,
                    [new ManaSourceInput("lands", ["G"]), new ManaSourceInput("rocks", ["B"])],
                    new ManaRequirementInput(Black: 1, Generic: 2),
                    3)).Value).Data.Value).Data;
        CastabilityRowView doom = plain.Rows.Single(row => row.Cost == "{2}{B}");
        Assert.Equal(availability.Probability.Numerator, doom.Probability!.Numerator);
        Assert.Equal(availability.Probability.Denominator, doom.Probability.Denominator);
    }

    /// <summary>
    /// Verifies cards with no cost, an X, a hybrid symbol, or a Phyrexian symbol are skipped with reasons and get no
    /// row; lands are not spells (case K04).
    /// </summary>
    [Fact]
    public async Task Calculate_UnexpressibleCosts_AreSkippedWithReasons()
    {
        using Setup setup = await Setup.CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();

        CastabilityToolResult result = Success(await setup.Run(deck, requireLandDrops: false));

        Assert.Equal(
            [("Fireball", "x-cost"), ("Free", "no-mana-cost"), ("Hybrid", "hybrid-or-phyrexian-symbol"),
                ("Phyrexian", "hybrid-or-phyrexian-symbol")],
            result.Skipped.Select(item => (item.Name, item.Reason)).Order());
        Assert.DoesNotContain(result.Rows, row => row.Cards.Any(card => card.Name is "Fireball" or "Hybrid" or "Free"));
        Assert.DoesNotContain(result.Rows, row => row.Cards.Any(card => card.Name is "Forest" or "Swamp"));
        Assert.NotEmpty(result.Limits);
    }

    /// <summary>
    /// Verifies a fixed turn checks every spell on that turn, and a backup source reads the deck as it was.
    /// </summary>
    [Fact]
    public async Task Calculate_FixedTurnAndBackupSource_Work()
    {
        using Setup setup = await Setup.CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();
        DeckBackup backup = Success(await setup.Data.Store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        _ = Success(await setup.Data.Store.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpdateDeckMetadataChange("Changed", string.Empty, "commander")],
            TestContext.Current.CancellationToken));

        CastabilityToolResult result = Success(await setup.Coordinator.CalculateAsync(
            new DeckBackupSourceInput(backup.BackupId, deck.DeckId, ["main"]),
            setup.Groups(deck),
            new FixedTurnInput(4),
            onThePlay: false,
            requireLandDrops: true,
            TestContext.Current.CancellationToken));

        Assert.All(result.Rows, row => Assert.Equal((4, 11), (row.Turn, row.CardsSeen)));
        Assert.Equal("deck-backup", result.Sources[0].Kind);
    }

    /// <summary>
    /// Verifies a source that is not a deck or backup is refused naming the allowed kinds, and card data that is not
    /// installed gives not-cached naming the sync tool (case K07).
    /// </summary>
    [Fact]
    public async Task Calculate_WrongSourceKindOrNoCardData_IsRefusedWithTheReason()
    {
        using Setup setup = await Setup.CreateAsync();
        using Setup bare = await Setup.CreateAsync(install: false);
        DeckDocument deck = await setup.CreateDeckAsync();
        DeckDocument bareDeck = await bare.CreateDeckAsync();

        OperationInvalidInput wrong = Assert.IsType<OperationInvalidInput>((await setup.Coordinator.CalculateAsync(
            new CardsSourceInput([Guid.NewGuid()]),
            setup.Groups(deck),
            new ManaValueTurnInput(),
            true,
            true,
            TestContext.Current.CancellationToken)).Value);
        OperationNotCached notCached = Assert.IsType<OperationNotCached>((await bare.Run(bareDeck, true)).Value);

        Assert.Equal("invalid-source-kind", wrong.ReasonCode);
        Assert.Contains("deck or deck-backup", wrong.Message, StringComparison.Ordinal);
        Assert.Contains("scryfall_card_data_sync", notCached.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies bad source groups, turns, and a deck with nothing to check are refused with their paths.
    /// </summary>
    [Fact]
    public async Task Calculate_BadGroupsAndTurns_AreRefusedWithPaths()
    {
        using Setup setup = await Setup.CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();
        Guid landEntry = deck.Entries.First(entry => entry.CardName == "Forest").EntryId;
        DeckSourceInput source = new(deck.DeckId, Zones: ["main"]);
        CastabilityProducesInput green = new(["G"]);

        Task<OperationResult<CastabilityToolResult>> Call(
            IReadOnlyList<CastabilitySourceGroupInput>? groups,
            CastabilityTurnInput? turn = null)
        {
            return setup.Coordinator.CalculateAsync(
                source, groups!, turn ?? new ManaValueTurnInput(), true, true, TestContext.Current.CancellationToken);
        }

        CastabilitySourceGroupInput Group(string id, CastabilitySelectInput select,
            CastabilityProducesInput? produces = null)
        {
            return new CastabilitySourceGroupInput(id, select, produces ?? green);
        }

        CastabilityEntryIdsSelect forest = new([landEntry]);
        OperationInvalidInput none = Failure(await Call(null));
        OperationInvalidInput empty = Failure(await Call([]));
        OperationInvalidInput blank = Failure(await Call([Group(" ", forest)]));
        OperationInvalidInput twice = Failure(await Call([Group("a", forest), Group("a", forest)]));
        OperationInvalidInput overlap = Failure(await Call([Group("a", forest), Group("b", forest)]));
        OperationInvalidInput color = Failure(await Call([Group("a", forest, new CastabilityProducesInput(["Z"]))]));
        OperationInvalidInput unknownEntry = Failure(await Call([Group("a",
            new CastabilityEntryIdsSelect([Guid.NewGuid()]))]));
        OperationInvalidInput unknownCategory = Failure(await Call([Group("a",
            new CastabilityCategoryIdsSelect([Guid.NewGuid()]))]));
        OperationInvalidInput emptySelect = Failure(await Call([Group("a", new CastabilityEntryIdsSelect([]))]));
        OperationInvalidInput badOverride = Failure(await Call([Group(
            "a",
            forest,
            new CastabilityProducesInput(["G"], [new CastabilityProducesOverride([Guid.NewGuid()], ["B"])]))]));
        OperationInvalidInput badOverrideColor = Failure(await Call([Group(
            "a",
            forest,
            new CastabilityProducesInput(["G"], [new CastabilityProducesOverride([landEntry], ["Q"])]))]));
        OperationInvalidInput turn = Failure(await Call([Group("a", forest)], new FixedTurnInput(0)));

        Assert.Equal("sources", none.Path);
        Assert.Equal("sources", empty.Path);
        Assert.Equal("sources[0].id", blank.Path);
        Assert.Equal("sources[1].id", twice.Path);
        Assert.Equal("overlapping-source-groups", overlap.ReasonCode);
        Assert.Equal("sources[0].produces.default", color.Path);
        Assert.Equal("unknown-entry-id", unknownEntry.ReasonCode);
        Assert.Equal("unknown-category-id", unknownCategory.ReasonCode);
        Assert.Equal("sources[0].select", emptySelect.Path);
        Assert.Equal("sources[0].produces.byEntry[0].entryIds", badOverride.Path);
        Assert.Equal("sources[0].produces.byEntry[0].colors", badOverrideColor.Path);
        Assert.Equal("turn.turn", turn.Path);
        OperationInvalidInput noSpells = Failure(await setup.Coordinator.CalculateAsync(
            new DeckSourceInput(deck.DeckId, Zones: ["nowhere"]),
            [Group("a", forest)],
            new ManaValueTurnInput(),
            true,
            true,
            TestContext.Current.CancellationToken));
        Assert.Equal("no-spells-to-check", noSpells.ReasonCode);
    }

    /// <summary>
    /// Verifies a per-entry color override puts those entries in their own source group.
    /// </summary>
    [Fact]
    public async Task Calculate_ByEntryOverride_SplitsTheGroupByColor()
    {
        using Setup setup = await Setup.CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync();
        Guid swamp = deck.Entries.First(entry => entry.CardName == "Swamp").EntryId;
        Guid forest = deck.Entries.First(entry => entry.CardName == "Forest").EntryId;
        CastabilitySourceGroupInput lands = new(
            "lands",
            new CastabilityEntryIdsSelect([forest, swamp]),
            new CastabilityProducesInput(["G"], [new CastabilityProducesOverride([swamp], ["B"])]));

        CastabilityToolResult result = Success(await setup.Coordinator.CalculateAsync(
            new DeckSourceInput(deck.DeckId, Zones: ["main"]),
            [lands],
            new ManaValueTurnInput(),
            true,
            false,
            TestContext.Current.CancellationToken));

        CastabilityRowView doom = result.Rows.Single(row => row.Cost == "{2}{B}");
        Assert.True(BigInteger_Positive(doom.Probability!.Numerator));
        Assert.Equal(28, result.Groups.Single().Cards);
    }

    /// <summary>
    /// Verifies mana costs are read into exact requirements and the unreadable ones give reasons.
    /// </summary>
    [Theory]
    [InlineData("{2}{B}", 0, 0, 1, 0, 0, 0, 2, null)]
    [InlineData("{W}{U}{B}{R}{G}{C}", 1, 1, 1, 1, 1, 1, 0, null)]
    [InlineData("{10}", 0, 0, 0, 0, 0, 0, 10, null)]
    [InlineData("{X}{R}", 0, 0, 0, 0, 0, 0, 0, "x-cost")]
    [InlineData("{W/U}", 0, 0, 0, 0, 0, 0, 0, "hybrid-or-phyrexian-symbol")]
    [InlineData("{2/W}", 0, 0, 0, 0, 0, 0, 0, "hybrid-or-phyrexian-symbol")]
    [InlineData("{S}", 0, 0, 0, 0, 0, 0, 0, "unsupported-symbol")]
    [InlineData("{1}{R} // {1}{U}", 0, 0, 0, 0, 0, 0, 0, "split-card-has-two-costs")]
    [InlineData("", 0, 0, 0, 0, 0, 0, 0, "no-mana-cost")]
    [InlineData(null, 0, 0, 0, 0, 0, 0, 0, "no-mana-cost")]
    public void ManaCost_Parse_ReadsExactRequirementsOrGivesAReason(
        string? cost,
        int white,
        int blue,
        int black,
        int red,
        int green,
        int colorless,
        int generic,
        string? reason)
    {
        (ManaRequirementInput? requirement, string? skipReason) = CastabilityManaCost.Parse(cost);

        Assert.Equal(reason, skipReason);
        if (reason is null)
        {
            Assert.Equal(new ManaRequirementInput(white, blue, black, red, green, colorless, generic), requirement);
        }
        else
        {
            Assert.Null(requirement);
        }
    }

    /// <summary>
    /// Verifies the published examples bind to the tool and run (SCHEMA-006).
    /// </summary>
    [Fact]
    public async Task Examples_BindAndRun()
    {
        using Setup setup = await Setup.CreateAsync();
        DeckDocument deck = await setup.CreateDeckAsync(withPlaceholderIds: true);
        using ToolCatalogFixture fixture = new();
        (string Name, string Json)[] examples = CardListExampleSupport.ReadExamples(
            CardListExampleSupport.DescriptionOf("stats_castability"));

        Assert.Equal(["minimal", "full"], examples.Select(example => example.Name));
        foreach ((string name, string json) in examples)
        {
            string filled = CardListExampleSupport.Fill(json, deck, Guid.NewGuid(), Guid.NewGuid());
            Dictionary<string, JsonElement> arguments = CardListExampleSupport.Arguments(filled);
            Assert.Null(ToolCallPolicy.DiagnoseArguments(fixture.Catalog, "stats_castability", arguments));
            StatisticsCastabilityTools tools = new(setup.Coordinator);

            OperationResult<CastabilityToolResult> result = await tools.CalculateAsync(
                arguments["source"].Deserialize<CardListSourceInput>(ToolCallPolicy.SerializerOptions)!,
                arguments["sources"]
                    .Deserialize<IReadOnlyList<CastabilitySourceGroupInput>>(ToolCallPolicy.SerializerOptions)!,
                arguments["turn"].Deserialize<CastabilityTurnInput>(ToolCallPolicy.SerializerOptions)!,
                arguments["onThePlay"].GetBoolean(),
                arguments["requireLandDrops"].GetBoolean(),
                TestContext.Current.CancellationToken);

            Assert.True(
                result.Value is OperationSuccess<CastabilityToolResult>,
                $"{name}: {JsonSerializer.Serialize(result.Value)}");
        }
    }

    /// <summary>
    /// Reports whether a decimal digit string is not zero.
    /// </summary>
    private static bool BigInteger_Positive(string digits)
    {
        return digits.Any(character => character is >= '1' and <= '9');
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return result.Value is OperationSuccess<T> success
            ? success.Data
            : throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(result.Value));
    }

    /// <summary>
    /// Unwraps an invalid-input result.
    /// </summary>
    private static OperationInvalidInput Failure<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationInvalidInput>(result.Value);
    }

    /// <summary>
    /// Builds the deck, card data, and coordinator the castability tests share.
    /// </summary>
    private sealed class Setup : IDisposable
    {
        /// <summary>
        /// Creates a setup around card data.
        /// </summary>
        private Setup(CardDataFixture data)
        {
            Data = data;
            Coordinator = new StatisticsCastabilityCoordinator(
                new CardListResolver(data.Scryfall, data.Access()),
                data.Scryfall,
                new ExactStatisticsCalculator("test"));
        }

        /// <summary>
        /// Gets the card data fixture.
        /// </summary>
        internal CardDataFixture Data { get; }

        /// <summary>
        /// Gets the coordinator under test.
        /// </summary>
        internal StatisticsCastabilityCoordinator Coordinator { get; }

        /// <summary>
        /// Creates a setup with or without installed card data.
        /// </summary>
        internal static async Task<Setup> CreateAsync(bool install = true)
        {
            JsonObject[] cards = [Forest, Swamp, Doom, Gloom, Sprout, Overgrow, Fireball, Hybrid, Phyrexian, Free];
            return new Setup(await CardDataFixture.CreateAsync(cards, install: install));
        }

        /// <summary>
        /// Creates a 40-card deck: 20 Forests and 8 Swamps as land categories, and the spells.
        /// </summary>
        internal async Task<DeckDocument> CreateDeckAsync(bool withPlaceholderIds = false)
        {
            Guid forestEntry = withPlaceholderIds ? Guid.Parse(DeckToolExamples.PlaceholderEntryId) : Guid.NewGuid();
            Guid swampEntry = Guid.Parse(StatisticsCastabilityTools.ExampleEntryId2);
            Guid category = withPlaceholderIds
                ? Guid.Parse(StatisticsCastabilityTools.ExampleCategoryId)
                : Guid.NewGuid();
            List<DeckEntryDraft> entries =
            [
                new(20, "Forest", PrintingId: CardJson.PrintingOf(Forest), Zone: "main", EntryId: forestEntry),
                new(8, "Swamp", PrintingId: CardJson.PrintingOf(Swamp), Zone: "main", EntryId: swampEntry),
                Spell(Doom),
                Spell(Gloom),
                Spell(Sprout),
                Spell(Overgrow),
                Spell(Fireball),
                Spell(Hybrid),
                Spell(Phyrexian),
                Spell(Free),
                new(1, "Filler", Zone: "main"),
            ];
            DeckCreateRequest request = new(
                "Castable",
                Format: "commander",
                Entries: entries,
                Categories: [new DeckCategoryDraft("Lands", CategoryId: category)],
                CategoryAssignments: [new DeckCategoryAssignment(forestEntry, category, true)]);
            return Success(await Data.Store.CreateAsync(request, TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Gives the two source groups the first tests use: Forests by ID and Swamps by ID.
        /// </summary>
        internal IReadOnlyList<CastabilitySourceGroupInput> Groups(DeckDocument deck)
        {
            Guid forest = deck.Entries.First(entry => entry.CardName == "Forest").EntryId;
            Guid swamp = deck.Entries.First(entry => entry.CardName == "Swamp").EntryId;
            return
            [
                new CastabilitySourceGroupInput("lands", new CastabilityEntryIdsSelect([forest]),
                    new CastabilityProducesInput(["G"])),
                new CastabilitySourceGroupInput("rocks", new CastabilityEntryIdsSelect([swamp]),
                    new CastabilityProducesInput(["B"])),
            ];
        }

        /// <summary>
        /// Runs the tool with the first tests' source groups, a mana value turn, and the play.
        /// </summary>
        internal Task<OperationResult<CastabilityToolResult>> Run(DeckDocument deck, bool requireLandDrops)
        {
            return Coordinator.CalculateAsync(
                new DeckSourceInput(deck.DeckId, Zones: ["main"]),
                Groups(deck),
                new ManaValueTurnInput(),
                onThePlay: true,
                requireLandDrops,
                TestContext.Current.CancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Data.Dispose();
        }

        /// <summary>
        /// Builds a one-copy draft for a spell.
        /// </summary>
        private static DeckEntryDraft Spell(JsonObject card)
        {
            return new DeckEntryDraft(
                1,
                card["name"]!.GetValue<string>(),
                PrintingId: CardJson.PrintingOf(card),
                Zone: "main");
        }
    }
}
