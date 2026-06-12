using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies deterministic ramp operational fact extraction and context scoring.
/// </summary>
public sealed class RampOperationalFactExtractorTests
{
    /// <summary>
    /// Verifies representative ramp cards are shaped without card-name overrides.
    /// </summary>
    [Theory]
    [MemberData(nameof(RampCards))]
    public void Extract_RecognizesRepresentativeRampShapes(DeckCard card, string expectedKind)
    {
        CardOperationalFacts facts = RampOperationalFactExtractor.Extract(card);

        facts.Role.Should().Be(DeckRoles.Ramp);
        facts.Ramp.Should().NotBeNull();
        facts.Ramp!.Kind.Should().Be(expectedKind);
        facts.Evidence.Should().Contain(evidence => evidence.Kind == "parserDerived");
    }

    /// <summary>
    /// Verifies Wayfarer's Bauble is ramp, but delayed activated tapped-land ramp.
    /// </summary>
    [Fact]
    public void Extract_RecognizesBaubleAsDelayedActivatedTappedLandRamp()
    {
        CardOperationalFacts facts = RampOperationalFactExtractor.Extract(WayfarersBauble());

        facts.Role.Should().Be(DeckRoles.Ramp);
        facts.Ramp.Should().NotBeNull();
        facts.Ramp!.Kind.Should().Be("activatedLandRamp");
        facts.Ramp.CastMana.Should().Be(1);
        facts.Ramp.ActivationMana.Should().Be(2);
        facts.Ramp.RequiresTap.Should().BeTrue();
        facts.Ramp.SacrificesSelf.Should().BeTrue();
        facts.Ramp.EntersTapped.Should().BeTrue();
        facts.Ramp.EarliestManaGainTurn.Should().Be(3);
    }

    /// <summary>
    /// Verifies a renamed same-text Bauble gets the same facts without a card-name table.
    /// </summary>
    [Fact]
    public void Extract_RenamedSameTextBaubleGetsSameOperationalShape()
    {
        DeckCard original = WayfarersBauble();
        DeckCard renamed = Card(
            "Traveler's Bauble, Replica",
            "Artifact",
            "{1}",
            1,
            "{2}, {T}, Sacrifice Traveler's Bauble, Replica: Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.",
            []);

        RampOperationalFacts originalRamp = RampOperationalFactExtractor.Extract(original).Ramp!;
        RampOperationalFacts renamedRamp = RampOperationalFactExtractor.Extract(renamed).Ramp!;

        renamedRamp.Kind.Should().Be(originalRamp.Kind);
        renamedRamp.CastMana.Should().Be(originalRamp.CastMana);
        renamedRamp.ActivationMana.Should().Be(originalRamp.ActivationMana);
        renamedRamp.EarliestManaGainTurn.Should().Be(originalRamp.EarliestManaGainTurn);
    }

    /// <summary>
    /// Verifies Tagger ramp evidence remains visible when the parser cannot shape timing.
    /// </summary>
    [Fact]
    public void Extract_TaggerRampWithoutShapeReturnsUnknownShapeWarning()
    {
        DeckCard card = Card("Tagged Ramp", "Artifact", "{2}", 2, "", []);
        card.Metadata[CardFacetNames.TaggerOracleTags] = "ramp";

        CardOperationalFacts facts = RampOperationalFactExtractor.Extract(card);

        facts.Ramp.Should().NotBeNull();
        facts.Ramp!.Kind.Should().Be("unknownShape");
        facts.Evidence.Should().Contain(evidence => evidence.Kind == "sourceBacked");
        facts.Warnings.Should().Contain(warning => warning.StartsWith("unknownShape", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies sacrifice costs only mark the card as self-sacrificing when the cost names the card or says this.
    /// </summary>
    [Fact]
    public void Extract_SacrificeAnotherDoesNotMarkRampAsSelfSacrifice()
    {
        DeckCard card = Card(
            "Proxy Bauble",
            "Artifact",
            "{1}",
            1,
            "{2}, Sacrifice another artifact: Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.",
            []);

        CardOperationalFacts facts = RampOperationalFactExtractor.Extract(card);

        facts.Ramp.Should().NotBeNull();
        facts.Ramp!.Kind.Should().Be("activatedLandRamp");
        facts.Ramp.SacrificesSelf.Should().BeFalse();
    }

    /// <summary>
    /// Verifies contextual scoring ranks delayed activated ramp below faster alternatives in Simic.
    /// </summary>
    [Fact]
    public void Evaluate_RanksBaubleBelowFastRampInGreenBlueCommanderDeck()
    {
        DeckWorkspace workspace = CreateRampContextDeck();
        RampContextEvaluation bauble = RampContextScorer.Evaluate(
            workspace,
            WayfarersBauble(),
            RampOperationalFactExtractor.Extract(WayfarersBauble()));
        DeckCard nature = Card(
            "Nature's Lore",
            "Sorcery",
            "{1}{G}",
            2,
            "Search your library for a Forest card, put that card onto the battlefield, then shuffle.",
            ["G"]);
        RampContextEvaluation natureEvaluation = RampContextScorer.Evaluate(
            workspace,
            nature,
            RampOperationalFactExtractor.Extract(nature));
        DeckCard signet = Card(
            "Arcane Signet",
            "Artifact",
            "{2}",
            2,
            "{T}: Add one mana of any color in your commander's color identity.",
            [],
            ["W", "U", "B", "R", "G"]);
        RampContextEvaluation signetEvaluation = RampContextScorer.Evaluate(
            workspace,
            signet,
            RampOperationalFactExtractor.Extract(signet));

        bauble.Score.Should().BeLessThan(natureEvaluation.Score);
        bauble.Score.Should().BeLessThan(signetEvaluation.Score);
        bauble.TopIssues.Should().Contain(issue => issue.Contains("future activation mana", StringComparison.OrdinalIgnoreCase));
        bauble.TopIssues.Should().Contain(issue => issue.Contains("enters tapped", StringComparison.OrdinalIgnoreCase));
        natureEvaluation.TopStrengths.Should().Contain(strength => strength.Contains("commander", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies replacement-style ramp comparisons expose timing differences in a five-mana commander deck.
    /// </summary>
    [Fact]
    public void Evaluate_SeparatesBaubleTwoManaRockAndCostReducerForFiveManaCommander()
    {
        DeckWorkspace workspace = CreateFiveManaCommanderDeck();
        RampContextEvaluation bauble = RampContextScorer.Evaluate(
            workspace,
            WayfarersBauble(),
            RampOperationalFactExtractor.Extract(WayfarersBauble()));
        DeckCard signet = Card(
            "Arcane Signet",
            "Artifact",
            "{2}",
            2,
            "{T}: Add one mana of any color in your commander's color identity.",
            [],
            ["W", "U", "B", "R", "G"]);
        RampContextEvaluation signetEvaluation = RampContextScorer.Evaluate(
            workspace,
            signet,
            RampOperationalFactExtractor.Extract(signet));
        DeckCard reducer = Card(
            "Banner of Kinship",
            "Artifact",
            "{2}",
            2,
            "Creature spells you cast cost {1} less to cast.",
            []);
        RampContextEvaluation reducerEvaluation = RampContextScorer.Evaluate(
            workspace,
            reducer,
            RampOperationalFactExtractor.Extract(reducer));

        signetEvaluation.RampKind.Should().Be("manaRock");
        reducerEvaluation.RampKind.Should().Be("costReducer");
        signetEvaluation.Score.Should().BeGreaterThan(bauble.Score);
        reducerEvaluation.SubScores["helpsCommanderOnCurve"].Should().Be(20);
        bauble.TopIssues.Should().Contain(issue => issue.Contains("future activation mana", StringComparison.OrdinalIgnoreCase));
        signetEvaluation.TopStrengths.Should().Contain(strength => strength.Contains("commander", StringComparison.OrdinalIgnoreCase));
        reducerEvaluation.TopStrengths.Should().Contain(strength => strength.Contains("commander", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Provides representative ramp cards and expected operational kinds.
    /// </summary>
    public static TheoryData<DeckCard, string> RampCards()
    {
        return new TheoryData<DeckCard, string>
        {
            { Card("Arcane Signet", "Artifact", "{2}", 2, "{T}: Add one mana of any color in your commander's color identity.", [], ["W", "U", "B", "R", "G"]), "manaRock" },
            { Card("Nature's Lore", "Sorcery", "{1}{G}", 2, "Search your library for a Forest card, put that card onto the battlefield, then shuffle.", ["G"]), "spellLandRampUntapped" },
            { Card("Rampant Growth", "Sorcery", "{1}{G}", 2, "Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.", ["G"]), "spellLandRampTapped" },
            { Card("Sakura-Tribe Elder", "Creature - Snake Shaman", "{1}{G}", 2, "Sacrifice Sakura-Tribe Elder: Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.", ["G"]), "creatureSacrificeLandRamp" },
            { Card("Burnished Hart", "Artifact Creature - Elk", "{3}", 3, "{3}, Sacrifice Burnished Hart: Search your library for up to two basic land cards, put them onto the battlefield tapped, then shuffle.", []), "creatureSacrificeLandRamp" },
            { Card("Sol Ring", "Artifact", "{0}", 0, "{T}: Add {C}{C}.", [], ["C"]), "manaRock" },
            { Card("Sky Diamond", "Artifact", "{2}", 2, "Sky Diamond enters the battlefield tapped. {T}: Add {U}.", [], ["U"]), "manaRock" },
            { Card("Farseek", "Sorcery", "{1}{G}", 2, "Search your library for a Plains, Island, Swamp, or Mountain card, put it onto the battlefield tapped, then shuffle.", ["G"]), "spellLandRampTapped" },
            { Card("Three Visits", "Sorcery", "{1}{G}", 2, "Search your library for a Forest card, put that card onto the battlefield. Then shuffle.", ["G"]), "spellLandRampUntapped" },
            { Card("Llanowar Elves", "Creature - Elf Druid", "{G}", 1, "{T}: Add {G}.", ["G"], ["G"]), "manaDork" },
            { Card("Unexpected Windfall", "Instant", "{2}{R}{R}", 4, "As an additional cost to cast this spell, discard a card. Draw two cards and create two Treasure tokens.", ["R"]), "treasureBurst" },
            { Card("Dark Ritual", "Instant", "{B}", 1, "Add {B}{B}{B}.", ["B"]), "ritual" },
            { Card("Goblin Electromancer", "Creature - Goblin Wizard", "{U}{R}", 2, "Instant and sorcery spells you cast cost {1} less to cast.", ["U", "R"]), "costReducer" },
        };
    }

    /// <summary>
    /// Creates a Wayfarer's Bauble fixture.
    /// </summary>
    private static DeckCard WayfarersBauble()
    {
        return Card(
            "Wayfarer's Bauble",
            "Artifact",
            "{1}",
            1,
            "{2}, {T}, Sacrifice Wayfarer's Bauble: Search your library for a basic land card, put that card onto the battlefield tapped, then shuffle.",
            []);
    }

    /// <summary>
    /// Creates a compact deck context for Simic ramp evaluation.
    /// </summary>
    private static DeckWorkspace CreateRampContextDeck()
    {
        return new DeckWorkspace
        {
            Id = "ramp-context",
            Name = "Ramp Context",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Kenessos Test Commander", "Legendary Creature - Merfolk", "{1}{G}{U}", 3, "", ["G", "U"]),
                Card("Forest", "Basic Land - Forest", null, 0, "{T}: Add {G}.", [], ["G"]),
                Card("Island", "Basic Land - Island", null, 0, "{T}: Add {U}.", [], ["U"]),
            ],
        };
    }

    /// <summary>
    /// Creates a five-mana commander deck context for replacement comparison tests.
    /// </summary>
    private static DeckWorkspace CreateFiveManaCommanderDeck()
    {
        return new DeckWorkspace
        {
            Id = "five-mana-ramp-context",
            Name = "Five Mana Ramp Context",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Five Mana Commander", "Legendary Creature - Avatar", "{3}{G}{U}", 5, "", ["G", "U"]),
                Card("Forest", "Basic Land - Forest", null, 0, "{T}: Add {G}.", [], ["G"]),
                Card("Island", "Basic Land - Island", null, 0, "{T}: Add {U}.", [], ["U"]),
            ],
        };
    }

    /// <summary>
    /// Creates a deck card with cached Scryfall-like facts.
    /// </summary>
    private static DeckCard Card(
        string name,
        string typeLine,
        string? manaCost,
        double manaValue,
        string oracleText,
        List<string> colorIdentity,
        List<string>? producedMana = null)
    {
        string category = typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)
            ? DeckRoles.Lands
            : DeckDefaults.Mainboard;
        if (typeLine.Contains("Legendary Creature", StringComparison.OrdinalIgnoreCase))
        {
            category = DeckRoles.Commander;
        }

        return new DeckCard
        {
            Name = name,
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
