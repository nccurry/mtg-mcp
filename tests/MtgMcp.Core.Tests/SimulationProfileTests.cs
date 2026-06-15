using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains tests for simulation profile resolution and route predicates.
/// </summary>
public sealed class SimulationProfileTests
{
    /// <summary>
    /// Verifies that Deck Intent v2 parses simulation settings and win routes.
    /// </summary>
    [Fact]
    public void DeckIntentText_ParsesV2SimulationIntent()
    {
        DeckIntentResult result = DeckIntentText.Parse(
            """
            MTG MCP Deck Intent
            Version: 2
            Commander: Abdel Adrian, Gorion's Ward + Candlekeep Sage
            Goal: Turbo dungeon / initiative blink powered by commander loops
            Power Target: strong bracket 3
            Simulation Profile: combo
            Archetype Tags: blink, dungeon, value, tokens
            Target Goldfish Turn: 6

            Build Targets
            Ramp: 8-10
            Blink: 10-14

            Simulation
            Commander Dependency: high
            Mulligan Style: multiplayer-london
            Hold Interaction From Turn: 3
            Minimum Interaction Held: 1
            Prefer Commander On Curve: true
            Preferred Commander Turn: 5
            Preferred Background Turn: 4
            Command Zone Order: Background, Commander
            Accept Shield Down Win Attempt: false

            Win Routes
            Altar Loop: requires commander, repeatable-blink, Altar of the Brood; earliest turn 5; kind combo
            End MTG MCP Deck Intent
            """);

        result.Warnings.Should().BeEmpty();
        result.Intent.Should().NotBeNull();
        DeckIntent intent = result.Intent!;
        intent.Version.Should().Be(2);
        intent.Goal.Should().Contain("Turbo dungeon");
        intent.PowerTarget.Should().Be("strong bracket 3");
        intent.SimulationProfile.Should().Be(SimulationProfileIds.Combo);
        intent.ArchetypeTags.Should().Contain(["blink", "dungeon", "value", "tokens"]);
        intent.TargetGoldfishTurn.Should().Be(6);
        intent.BuildTargets["Blink"].Minimum.Should().Be(10);
        intent.Targets["Blink"].Maximum.Should().Be(14);
        intent.Simulation.HoldInteractionFromTurn.Should().Be(3);
        intent.Simulation.PreferCommanderOnCurve.Should().BeTrue();
        intent.Simulation.PreferredCommanderTurn.Should().Be(5);
        intent.Simulation.PreferredBackgroundTurn.Should().Be(4);
        intent.Simulation.CommandZoneOrder.Should().Equal("Background", "Commander");
        intent.Simulation.AcceptShieldDownWinAttempt.Should().BeFalse();
        intent.WinRoutes.Should().ContainSingle();
        intent.WinRoutes[0].Requirements.Should().Contain(["commander", "repeatable-blink", "Altar of the Brood"]);

        string formatted = DeckIntentText.Format(intent);
        formatted.Should().Contain("Simulation Profile: combo");
        formatted.Should().Contain("Preferred Commander Turn: 5");
        formatted.Should().Contain("Command Zone Order: Background, Commander");
        formatted.Should().Contain("Build Targets");
        formatted.Should().Contain("Win Routes");
        DeckIntentText.Parse(formatted).Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that profile resolution applies the documented precedence order.
    /// </summary>
    [Fact]
    public void SimulationProfileResolver_UsesExplicitIntentAutoThenNeutral()
    {
        SimulationProfileCatalog catalog = SimulationProfileCatalog.CreateDefault();
        DeckWorkspace workspace = EmptyWorkspace();
        DeckIntent intent = new()
        {
            SimulationProfile = SimulationProfileIds.Combo,
            ArchetypeTags = ["blink"],
        };

        catalog.Resolve(workspace, SimulationProfileIds.Control, intent).Source.Should().Be("explicit");
        catalog.Resolve(workspace, "auto", intent).Source.Should().Be("deck-intent");

        intent.SimulationProfile = null;
        intent.Goal = "Fast combo loop";
        ResolvedSimulationProfile inferred = catalog.Resolve(workspace, "auto", intent);
        inferred.Source.Should().Be("auto");
        inferred.Profile.Id.Should().Be(SimulationProfileIds.Combo);

        ResolvedSimulationProfile neutral = catalog.Resolve(workspace, "not-real", new DeckIntent());
        neutral.Profile.Id.Should().Be(SimulationProfileIds.Neutral);
        neutral.Warnings.Should().Contain(warning => warning.Contains("not-real", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies broad auto-profile inference for common Commander play patterns.
    /// </summary>
    [Theory]
    [InlineData("token combat", SimulationProfileIds.Aggro)]
    [InlineData("big mana ramp", SimulationProfileIds.BigMana)]
    [InlineData("control counterspell", SimulationProfileIds.Control)]
    [InlineData("stax prison hatebear", SimulationProfileIds.Stax)]
    public void SimulationProfileResolver_InfersBroadProfilesFromIntentText(string goal, string expectedProfile)
    {
        ResolvedSimulationProfile resolved = SimulationProfileCatalog.CreateDefault()
            .Resolve(EmptyWorkspace(), "auto", new DeckIntent { Goal = goal });

        resolved.Profile.Id.Should().Be(expectedProfile);
        resolved.Candidates.Should().Contain(candidate => candidate.ProfileId == expectedProfile);
    }

    /// <summary>
    /// Verifies that external profile inheritance merges parent settings and surfaces validation warnings.
    /// </summary>
    [Fact]
    public void SimulationProfileCatalog_MergesExternalProfileInheritanceAndWarnings()
    {
        SimulationProfileCatalog catalog = new(
            [
                new SimulationProfile
                {
                    Id = "blink-combo",
                    Name = "Blink Combo",
                    Inherits = [SimulationProfileIds.Combo],
                    ThemeTags = ["blink"],
                    WinRoutes =
                    [
                        new SimulationRouteDefinition
                        {
                            Name = "Bad Route",
                            Requirements = ["mana>=-1"],
                        },
                    ],
                },
            ],
            configurationWarnings: ["configured profile warning"]);

        catalog.ConfigurationWarnings.Should().Contain("configured profile warning");
        catalog.ConfigurationWarnings.Should().Contain(warning => warning.Contains("unsupported requirement", StringComparison.OrdinalIgnoreCase));
        catalog.TryGet("blink-combo", out SimulationProfile profile).Should().BeTrue();
        profile.Sequencing.TutorPriority.Should().Be(1);
        profile.ThemeTags.Should().Contain(["storm", "blink"]);
        profile.WinRoutes.Should().NotContain(route => route.Name == "Bad Route");
        profile.WinRoutes.Should().Contain(route => route.Name == "Aristocrats Drain Clock");

        ResolvedSimulationProfile resolved = catalog.Resolve(EmptyWorkspace(), "blink-combo", new DeckIntent());
        resolved.Warnings.Should().Contain("configured profile warning");
    }

    /// <summary>
    /// Verifies that intent command-zone settings override the resolved simulation profile.
    /// </summary>
    [Fact]
    public void SimulationProfileResolver_AppliesCommandZoneIntentOverrides()
    {
        SimulationProfileCatalog catalog = SimulationProfileCatalog.CreateDefault();
        DeckIntent intent = new()
        {
            Simulation = new DeckIntentSimulationSettings
            {
                PreferCommanderOnCurve = false,
                PreferredCommanderTurn = 6,
                PreferredBackgroundTurn = 4,
                CommandZoneOrder = ["Background", "Commander"],
            },
        };

        ResolvedSimulationProfile resolved = catalog.Resolve(EmptyWorkspace(), "auto", intent);

        resolved.Profile.Sequencing.PreferCommanderOnCurve.Should().BeFalse();
        resolved.Profile.Sequencing.PreferredCommanderTurn.Should().Be(6);
        resolved.Profile.Sequencing.PreferredBackgroundTurn.Should().Be(4);
        resolved.Profile.Sequencing.CommandZoneOrder.Should().Equal("Background", "Commander");
        resolved.Profile.Scenarios.CommanderTurn.Should().Be(6);
    }

    /// <summary>
    /// Verifies that route validation accepts graveyard, reanimation, aristocrats, and held-interaction predicates.
    /// </summary>
    [Theory]
    [InlineData("graveyard>=2")]
    [InlineData("reanimation-target")]
    [InlineData("sac-outlet")]
    [InlineData("drain-payoff")]
    [InlineData("recursive-creature")]
    [InlineData("enchantment-recursion")]
    [InlineData("repeatable-graveyard-recursion")]
    [InlineData("enchantress-engine")]
    [InlineData("engine-payoff")]
    [InlineData("drain-clock")]
    [InlineData("treasure-engine")]
    [InlineData("treasure-payoff")]
    [InlineData("food-bank")]
    [InlineData("artifact-token-bank")]
    [InlineData("lifegain-burst")]
    [InlineData("lifegain-payoff")]
    [InlineData("artifact-leaves-drain")]
    [InlineData("food-combat-alpha")]
    [InlineData("artifact-tokens>=2")]
    [InlineData("food-tokens>=2")]
    [InlineData("lifegain-available>=3")]
    [InlineData("commander-damage-pressure")]
    [InlineData("interaction-held>=1")]
    [InlineData("interactionheld>=1")]
    public void SimulationRouteEvaluator_SupportsExpandedGoldfishPredicates(string requirement)
    {
        SimulationRouteEvaluator.IsSupportedRequirement(requirement).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that route predicates detect an Abdel-style Altar blink route.
    /// </summary>
    [Fact]
    public void SimulationRouteEvaluator_DetectsAbdelAltarBlinkRoute()
    {
        SimulationRouteDefinition route = new()
        {
            Name = "Altar Loop",
            Kind = "combo",
            EarliestTurn = 5,
            Source = "deck-intent",
            Requirements = ["commander", "repeatable-blink", "Altar of the Brood"]
        };
        List<SimulationRouteEvidence> evidence = SimulationRouteEvaluator.EvaluateRoutes(
            [route],
            new SimulationRouteState
            {
                Turn = 5,
                CommanderOnBattlefield = true,
                Battlefield =
                [
                    Card("Abdel Adrian, Gorion's Ward", DeckRoles.Commander, "Legendary Creature", "When Abdel enters, exile any number of permanents you control."),
                    Card("Altar of the Brood", DeckRoles.Wincons, "Artifact", "Whenever another permanent enters the battlefield under your control, each opponent mills a card."),
                    Card("Teleportation Circle", DeckRoles.Synergy, "Enchantment", "At the beginning of your end step, exile up to one target artifact or creature you control, then return that card to the battlefield.")
                ]
            });

        evidence.Should().ContainSingle();
        evidence[0].Matched.Should().BeTrue();
        evidence[0].Evidence.Should().Contain(line => line.Contains("repeatable blink", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that commander-damage routes need pump or evasion support, not commander presence alone.
    /// </summary>
    [Fact]
    public void SimulationRouteEvaluator_RequiresSupportForCommanderDamageRoutes()
    {
        SimulationRouteDefinition route = new()
        {
            Name = "Commander Damage",
            Kind = "commander-damage",
            EarliestTurn = 6,
            Source = "deck-intent",
            Requirements = ["commander", "commander-damage-pressure"]
        };
        DeckCard commander = Card(
            "Aurelia, the Warleader",
            DeckRoles.Commander,
            "Legendary Creature - Angel",
            "Flying, vigilance, haste. Whenever Aurelia attacks for the first time each turn, untap all creatures you control.");
        commander.Snapshot.ManaValue = 4;

        List<SimulationRouteEvidence> weakEvidence = SimulationRouteEvaluator.EvaluateRoutes(
            [route],
            new SimulationRouteState
            {
                Turn = 6,
                CommanderOnBattlefield = true,
                Battlefield = [commander]
            });
        weakEvidence.Single().Matched.Should().BeFalse();
        weakEvidence.Single().MissingRequirements.Should().Contain(line =>
            line.Contains("more than commander presence", StringComparison.OrdinalIgnoreCase));

        List<SimulationRouteEvidence> supportedEvidence = SimulationRouteEvaluator.EvaluateRoutes(
            [route],
            new SimulationRouteState
            {
                Turn = 6,
                CommanderOnBattlefield = true,
                Battlefield =
                [
                    commander,
                    Card(
                        "Blackblade Reforged",
                        DeckDefaults.Mainboard,
                        "Legendary Artifact - Equipment",
                        "Equipped creature gets +1/+1 for each land you control. Equip legendary creature {3}."),
                    Card("Whispersilk Cloak", DeckDefaults.Mainboard, "Artifact - Equipment", "Equipped creature can't be blocked and has shroud.")
                ]
            });
        supportedEvidence.Single().Matched.Should().BeTrue();
        supportedEvidence.Single().Evidence.Should().Contain(line =>
            line.Contains("projected three-turn damage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that slow-engine predicates report matched evidence and missing requirements.
    /// </summary>
    [Fact]
    public void SimulationRouteEvaluator_ExplainsEnchantmentRecursionEnginePredicates()
    {
        SimulationRouteDefinition route = new()
        {
            Name = "Enchantment Engine",
            Kind = "engine-inevitability",
            EarliestTurn = 6,
            Source = "profile-common",
            Requirements = ["commander", "enchantment-recursion", "engine-payoff", "graveyard>=1"]
        };
        List<SimulationRouteEvidence> evidence = SimulationRouteEvaluator.EvaluateRoutes(
            [route],
            new SimulationRouteState
            {
                Turn = 6,
                CommanderOnBattlefield = true,
                Battlefield =
                [
                    Card("Ghen, Arcanum Weaver", DeckRoles.Commander, "Legendary Creature", "{R}{W}{B}, {T}, Sacrifice an enchantment: Return target enchantment card from your graveyard to the battlefield."),
                    Card("Bleeding Pact", DeckRoles.Wincons, "Enchantment", "At the beginning of your end step, each opponent loses 1 life."),
                ],
                Graveyard =
                [
                    Card("Grave Pact", DeckRoles.Payoffs, "Enchantment", "Whenever a creature you control dies, each opponent sacrifices a creature."),
                ],
            });

        SimulationRouteEvidence matched = evidence.Should().ContainSingle().Subject;
        matched.Matched.Should().BeTrue();
        matched.Evidence.Should().Contain(line => line.Contains("enchantment recursion", StringComparison.OrdinalIgnoreCase));
        matched.Evidence.Should().Contain(line => line.Contains("engine payoff", StringComparison.OrdinalIgnoreCase));

        List<SimulationRouteEvidence> missingEvidence = SimulationRouteEvaluator.EvaluateRoutes(
            [route],
            new SimulationRouteState
            {
                Turn = 6,
                CommanderOnBattlefield = true,
                Battlefield =
                [
                    Card("Ghen, Arcanum Weaver", DeckRoles.Commander, "Legendary Creature", "{R}{W}{B}, {T}, Sacrifice an enchantment: Return target enchantment card from your graveyard to the battlefield."),
                ],
            });
        missingEvidence.Single().Matched.Should().BeFalse();
        missingEvidence.Single().MissingRequirements.Should().Contain(line => line.Contains("engine payoff", StringComparison.OrdinalIgnoreCase));
        missingEvidence.Single().MissingRequirements.Should().Contain(line => line.Contains("graveyard count", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a minimal Commander workspace for resolver tests.
    /// </summary>
    private static DeckWorkspace EmptyWorkspace()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Format = "commander",
            Categories = [new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true }],
        };
    }

    /// <summary>
    /// Creates a one-card fixture with snapshot text for route evaluation.
    /// </summary>
    private static DeckCard Card(string name, string category, string typeLine, string oracleText)
    {
        return new DeckCard
        {
            Name = name,
            Quantity = 1,
            PrimaryCategory = category,
            Categories = [category],
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                ManaValue = 2,
                OracleText = oracleText,
            }
        };
    }
}
