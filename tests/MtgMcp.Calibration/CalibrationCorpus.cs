using MtgMcp.Core;

namespace MtgMcp.Calibration;

/// <summary>
/// Provides the deterministic offline corpus used by Stats Lab calibration.
/// </summary>
internal static class CalibrationCorpus
{
    /// <summary>
    /// Builds all local calibration fixture decks.
    /// </summary>
    public static List<CalibrationFixture> BuildFixtures()
    {
        return
        [
            Fixture(
                "azorius-casual",
                "Azorius Casual Value",
                "synthetic bracket-2 casual",
                "synthetic-azorius-ladder",
                SimulationProfileIds.Value,
                "Synthetic same-commander lower-power value shell with light ramp, draw, and interaction.",
                CreateAzoriusCasual()),
            Fixture(
                "azorius-upgraded",
                "Azorius Upgraded Value",
                "synthetic bracket-3 upgraded",
                "synthetic-azorius-ladder",
                SimulationProfileIds.Value,
                "Synthetic same-commander upgraded shell with healthier ramp, draw, interaction, and castability.",
                CreateAzoriusUpgraded()),
            Fixture(
                "azorius-cedh-combo",
                "Azorius cEDH Combo",
                "synthetic cEDH combo",
                "synthetic-azorius-ladder",
                SimulationProfileIds.Combo,
                "Synthetic same-commander high-power contrast with dense cheap ramp, tutors, interaction, and combo routes.",
                CreateAzoriusCedhCombo()),
            Fixture(
                "azorius-battlecruiser",
                "Azorius Battlecruiser",
                "synthetic bracket-1 battlecruiser",
                "synthetic-azorius-ladder",
                SimulationProfileIds.BigMana,
                "Synthetic same-commander lower-power shell with many expensive payoffs and high stranded-card risk.",
                CreateAzoriusBattlecruiser()),
        ];
    }

    /// <summary>
    /// Builds expected pairwise metric relationships.
    /// </summary>
    public static List<CalibrationExpectation> BuildExpectations()
    {
        return
        [
            Expectation(
                "upgraded-develops-before-casual",
                "synthetic-azorius-ladder",
                "scorecard:early-development",
                "higher",
                "azorius-upgraded",
                "azorius-casual",
                0.02,
                "The upgraded value shell has more early ramp, draw, and retained resources.",
                ["same-commander", "curve"]),
            Expectation(
                "upgraded-holds-more-interaction-than-casual",
                "synthetic-azorius-ladder",
                "scorecard:interaction-readiness",
                "higher",
                "azorius-upgraded",
                "azorius-casual",
                0.03,
                "The upgraded value shell has more interaction and lower pressure from expensive blanks.",
                ["same-commander", "interaction"]),
            Expectation(
                "cedh-assembles-routes-before-upgraded",
                "synthetic-azorius-ladder",
                "scorecard:route-assembly",
                "higher",
                "azorius-cedh-combo",
                "azorius-upgraded",
                0.05,
                "The cEDH contrast fixture is intentionally tutor- and combo-dense.",
                ["same-commander", "cedh-contrast", "combo"]),
            Expectation(
                "cedh-combo-scenario-before-upgraded",
                "synthetic-azorius-ladder",
                "scenario:combo-or-tutor-assembly-by-turn-5",
                "higher",
                "azorius-cedh-combo",
                "azorius-upgraded",
                0.05,
                "The combo scenario should separate a tutor-dense cEDH shell from the upgraded value shell.",
                ["same-commander", "cedh-contrast", "combo"]),
            Expectation(
                "upgraded-strands-less-than-battlecruiser",
                "synthetic-azorius-ladder",
                "scorecard:stranded-resilience",
                "higher",
                "azorius-upgraded",
                "azorius-battlecruiser",
                0.10,
                "The upgraded deck has a lower curve and should strand fewer cards than the battlecruiser shell.",
                ["same-commander", "curve"]),
            Expectation(
                "battlecruiser-has-higher-stranded-risk",
                "synthetic-azorius-ladder",
                "scenario:stranded-high-mana-risk-by-max-turn",
                "lower",
                "azorius-upgraded",
                "azorius-battlecruiser",
                0.10,
                "The stranded-card risk scenario should flag the battlecruiser shell as riskier.",
                ["same-commander", "curve"]),
        ];
    }

    /// <summary>
    /// Creates one labeled fixture.
    /// </summary>
    private static CalibrationFixture Fixture(
        string fixtureId,
        string name,
        string label,
        string groupId,
        string profile,
        string sourceNote,
        DeckWorkspace workspace)
    {
        workspace.Id = fixtureId;
        workspace.Name = name;
        return new CalibrationFixture
        {
            FixtureId = fixtureId,
            Name = name,
            Label = label,
            GroupId = groupId,
            Profile = profile,
            SourceNote = sourceNote,
            SourceKind = "synthetic",
            CapturedAt = "2026-06-09",
            Workspace = workspace,
        };
    }

    /// <summary>
    /// Creates a low-power Azorius value fixture.
    /// </summary>
    private static DeckWorkspace CreateAzoriusCasual()
    {
        return CommanderFixture(
            BenchmarkAzoriusCommander(),
            [
                Land("Casual Plains", 18, ["W"]),
                Land("Casual Island", 18, ["U"]),
                Card("Casual Mana Rock", 4, DeckRoles.Ramp, "Artifact", "{3}", 3, "{T}: Add one mana of any color.", [], ["W", "U"]),
                Card("Casual Draw Spell", 4, DeckRoles.Draw, "Sorcery", "{3}{U}", 4, "Draw two cards.", ["U"]),
                Card(
                    "Casual Counterspell",
                    2,
                    DeckRoles.Interaction,
                    "Instant",
                    "{2}{U}",
                    3,
                    "Counter target spell unless its controller pays {2}.",
                    ["U"]),
                Card(
                    "Casual Boots",
                    2,
                    DeckRoles.Protection,
                    "Artifact - Equipment",
                    "{2}",
                    2,
                    "Equipped creature has hexproof.",
                    []),
                Card(
                    "Casual Finale",
                    2,
                    DeckRoles.Wincons,
                    "Sorcery",
                    "{6}{W}{U}",
                    8,
                    "Creatures you control get +4/+4 until end of turn.",
                    ["W", "U"]),
                Card("Casual Filler", 49, DeckRoles.Utility, "Sorcery", "{4}", 4, "Scry 1.", []),
            ]);
    }

    /// <summary>
    /// Creates an upgraded Azorius value fixture.
    /// </summary>
    private static DeckWorkspace CreateAzoriusUpgraded()
    {
        return CommanderFixture(
            BenchmarkAzoriusCommander(),
            [
                Land("Upgraded Plains", 18, ["W"]),
                Land("Upgraded Island", 18, ["U"]),
                Card(
                    "Arcane Signet Package",
                    8,
                    DeckRoles.Ramp,
                    "Artifact",
                    "{2}",
                    2,
                    "{T}: Add one mana of any color in your commander's color identity.",
                    [],
                    ["W", "U"]),
                Card("Efficient Draw Package", 8, DeckRoles.Draw, "Sorcery", "{2}{U}", 3, "Draw two cards.", ["U"]),
                Card(
                    "Efficient Interaction Package",
                    16,
                    DeckRoles.Interaction,
                    "Instant",
                    "{U}{U}",
                    2,
                    "Counter target spell.",
                    ["U"]),
                Card(
                    "Efficient Protection Package",
                    4,
                    DeckRoles.Protection,
                    "Artifact - Equipment",
                    "{2}",
                    2,
                    "Equipped creature has hexproof and haste.",
                    []),
                Card(
                    "Value Combo Engine",
                    3,
                    DeckRoles.Synergy,
                    "Artifact",
                    "{3}",
                    3,
                    "Combo engine. Untap another artifact. Add {U}.",
                    ["U"],
                    ["U"]),
                Card(
                    "Value Table Finisher",
                    3,
                    DeckRoles.Wincons,
                    "Sorcery",
                    "{5}{W}{U}",
                    7,
                    "Each opponent loses half their life.",
                    ["W", "U"]),
                Card("Upgraded Utility", 21, DeckRoles.Utility, "Sorcery", "{3}", 3, "Scry 1.", []),
            ]);
    }

    /// <summary>
    /// Creates a high-power Azorius combo contrast fixture.
    /// </summary>
    private static DeckWorkspace CreateAzoriusCedhCombo()
    {
        return CommanderFixture(
            BenchmarkAzoriusCommander(),
            [
                Land("cEDH Dual Land", 28, ["W", "U"]),
                Card("Zero Mana Rock", 8, DeckRoles.Ramp, "Artifact", "{0}", 0, "{T}: Add one mana of any color.", [], ["W", "U"]),
                Card("One Mana Rock", 7, DeckRoles.Ramp, "Artifact", "{1}", 1, "{T}: Add one mana of any color.", [], ["W", "U"]),
                Card(
                    "Efficient Tutor Package",
                    14,
                    DeckRoles.Tutors,
                    "Instant",
                    "{U}",
                    1,
                    "Search your library for a combo card, reveal it, put it into your hand, then shuffle.",
                    ["U"]),
                Card("Cheap Draw Package", 12, DeckRoles.Draw, "Instant", "{U}", 1, "Draw a card.", ["U"]),
                Card("Free Interaction Package", 14, DeckRoles.Interaction, "Instant", "{U}", 1, "Counter target spell.", ["U"]),
                Card(
                    "Compact Combo Piece A",
                    5,
                    DeckRoles.Synergy,
                    "Artifact",
                    "{1}",
                    1,
                    "Combo. Untap target permanent. Copy target activated ability.",
                    []),
                Card(
                    "Compact Combo Piece B",
                    5,
                    DeckRoles.Synergy,
                    "Artifact",
                    "{1}",
                    1,
                    "Whenever an ability is copied, untap target permanent.",
                    []),
                Card(
                    "Cheap Protection Package",
                    6,
                    DeckRoles.Protection,
                    "Instant",
                    "{W}",
                    1,
                    "Target permanent gains hexproof until end of turn.",
                    ["W"]),
            ]);
    }

    /// <summary>
    /// Creates a high-curve Azorius battlecruiser fixture.
    /// </summary>
    private static DeckWorkspace CreateAzoriusBattlecruiser()
    {
        return CommanderFixture(
            BenchmarkAzoriusCommander(),
            [
                Land("Battlecruiser Plains", 17, ["W"]),
                Land("Battlecruiser Island", 17, ["U"]),
                Card("Slow Mana Rock", 4, DeckRoles.Ramp, "Artifact", "{3}", 3, "{T}: Add one mana of any color.", [], ["W", "U"]),
                Card("Expensive Draw Spell", 4, DeckRoles.Draw, "Sorcery", "{4}{U}", 5, "Draw three cards.", ["U"]),
                Card("Expensive Interaction", 3, DeckRoles.Interaction, "Instant", "{3}{U}", 4, "Counter target spell.", ["U"]),
                Card("Seven Mana Angel", 18, DeckRoles.Utility, "Creature - Angel", "{5}{W}{W}", 7, "Flying, vigilance.", ["W"]),
                Card(
                    "Eight Mana Leviathan",
                    18,
                    DeckRoles.Utility,
                    "Creature - Leviathan",
                    "{6}{U}{U}",
                    8,
                    "Draw a card when this attacks.",
                    ["U"]),
                Card(
                    "Battlecruiser Finale",
                    18,
                    DeckRoles.Wincons,
                    "Sorcery",
                    "{7}{W}{U}",
                    9,
                    "Creatures you control get +7/+7 and gain flying.",
                    ["W", "U"]),
            ]);
    }

    /// <summary>
    /// Creates the shared commander used to compare same-commander fixture shells.
    /// </summary>
    private static DeckCard BenchmarkAzoriusCommander()
    {
        return Card(
            "Benchmark Azorius Commander",
            1,
            DeckRoles.Commander,
            "Legendary Creature - Advisor",
            "{2}{W}{U}",
            4,
            "Whenever you draw your second card each turn, create a 1/1 creature token.",
            ["W", "U"]);
    }

    /// <summary>
    /// Creates a Commander workspace around a commander and quantified packages.
    /// </summary>
    private static DeckWorkspace CommanderFixture(DeckCard commander, List<DeckCard> cards)
    {
        DeckWorkspace workspace = new()
        {
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Interaction, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Protection, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Synergy, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Tutors, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Wincons, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Utility, IncludedInDeck = true },
            ],
            Cards = [commander],
        };
        workspace.Cards.AddRange(cards);
        return workspace;
    }

    /// <summary>
    /// Creates one expectation definition.
    /// </summary>
    private static CalibrationExpectation Expectation(
        string expectationId,
        string groupId,
        string metric,
        string direction,
        string preferredFixtureId,
        string otherFixtureId,
        double minimumDelta,
        string rationale,
        List<string> tags)
    {
        return new CalibrationExpectation
        {
            ExpectationId = expectationId,
            GroupId = groupId,
            Metric = metric,
            Severity = CalibrationExpectationSeverity.Required,
            Tags = tags,
            Direction = direction,
            PreferredFixtureId = preferredFixtureId,
            OtherFixtureId = otherFixtureId,
            MinimumDelta = minimumDelta,
            Rationale = rationale,
        };
    }

    /// <summary>
    /// Creates a land package.
    /// </summary>
    private static DeckCard Land(string name, int quantity, List<string> producedMana)
    {
        return Card(name, quantity, DeckRoles.Lands, "Land", null, 0, "{T}: Add mana.", [], producedMana);
    }

    /// <summary>
    /// Creates a card with a cached snapshot.
    /// </summary>
    private static DeckCard Card(
        string name,
        int quantity,
        string category,
        string typeLine,
        string? manaCost,
        double manaValue,
        string oracleText,
        List<string> colorIdentity,
        List<string>? producedMana = null)
    {
        return new DeckCard
        {
            Name = name,
            Quantity = quantity,
            PrimaryCategory = category,
            Categories = [category],
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                ManaCost = manaCost,
                ManaValue = manaValue,
                OracleText = oracleText,
                ColorIdentity = colorIdentity,
                ProducedMana = producedMana ?? [],
            },
        };
    }
}
