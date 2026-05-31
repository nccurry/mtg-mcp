using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck analysis, summary, combo, odds, and role-classifier tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies that snapshot refresh populates extended metadata.
    /// </summary>
    [Fact]
    public async Task RefreshDeckCardSnapshots_PopulatesExtendedSnapshot()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Normalize",
            Cards = [new DeckCard { Name = "Sol Ring" }]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckNormalizationResult result = await service.RefreshDeckCardSnapshotsAsync(
            workspace.Id,
            "all",
            TestContext.Current.CancellationToken);

        result.UpdatedCards.Should().Be(1);
        DeckCard card = result.Workspace.Cards.Single();
        card.Snapshot.OracleText.Should().Contain("Add");
        card.Snapshot.EdhrecRank.Should().Be(1);
        card.Snapshot.ProducedMana.Should().Contain("C");
        card.Snapshot.Prices["usd"].Should().Be("1.25");
        card.Snapshot.Legalities["commander"].Should().Be("legal");
    }

    /// <summary>
    /// Verifies that included-scope refresh follows the ordered primary category rather than the legacy mirror.
    /// </summary>
    [Fact]
    public async Task RefreshDeckCardSnapshots_IncludedScopeUsesOrderedPrimaryCategory()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Ordered Primary",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false }
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckRoles.Ramp, DeckDefaults.Maybeboard]
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckNormalizationResult result = await service.RefreshDeckCardSnapshotsAsync(
            workspace.Id,
            "included",
            TestContext.Current.CancellationToken);

        result.RequestedCards.Should().Be(1);
        result.UpdatedCards.Should().Be(1);
        result.Workspace.Cards.Single().ScryfallId.Should().Be("sol-ring");
    }

    /// <summary>
    /// Verifies that snapshot refresh handles legacy null snapshots.
    /// </summary>
    [Fact]
    public async Task RefreshDeckCardSnapshots_HandlesLegacyNullSnapshots()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Legacy",
            Cards = [new DeckCard { Name = "Sol Ring", Snapshot = null! }]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckNormalizationResult result = await service.RefreshDeckCardSnapshotsAsync(
            workspace.Id,
            "missing",
            TestContext.Current.CancellationToken);

        result.UpdatedCards.Should().Be(1);
        result.Workspace.Cards.Single().Snapshot.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that role classifier classifies common deck roles and tags.
    /// </summary>
    [Fact]
    public void RoleClassifier_ClassifiesCommonDeckRolesAndTags()
    {
        DeckRoleClassifier.Classify(Card("Arcane Signet", "Artifact", "{T}: Add one mana of any color."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Ramp);
        DeckRoleClassifier.Classify(Card("Toxic Deluge", "Sorcery", "All creatures get -X/-X until end of turn."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.BoardWipes);

        CardRoleAssignment tinybones = DeckRoleClassifier.Classify(Card(
            "Tinybones, Trinket Thief",
            "Legendary Creature",
            "Whenever an opponent discards a card, you draw a card."));
        tinybones.PrimaryRole.Should().Be(DeckRoles.Draw);
        tinybones.Tags.Should().Contain(DeckTags.Discard);

        DeckRoleClassifier.Classify(Card(
                "Aclazotz, Deepest Betrayal // Temple of the Dead",
                "Legendary Creature — Bat God // Land",
                "Whenever Aclazotz attacks, each opponent discards a card. For each opponent who can't, you draw a card."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Draw);

        DeckRoleClassifier.Classify(Card(
                "Malakir Rebirth // Malakir Mire",
                "Instant // Land",
                "Choose target creature. You lose 2 life. Until end of turn, that creature gains when this creature dies, return it to the battlefield tapped."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Utility);
        DeckRoleClassifier.Classify(new DeckCard
        {
            Name = "Chart a Course",
            PrimaryCategory = DeckRoles.Draw,
            Categories = [DeckRoles.Draw, DeckRoles.Lands],
            Snapshot = new CardSnapshot
            {
                TypeLine = "Sorcery",
                OracleText = "Draw two cards. Then discard a card unless you attacked this turn."
            }
        })
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Draw);

        DeckRoleClassifier.Classify(Card(
                "Tourach, Dread Cantor",
                "Legendary Creature",
                "Kicker {B}{B}. Protection from white. Whenever an opponent discards a card, put a +1/+1 counter on Tourach."))
            .PrimaryRole
            .Should()
            .NotBe(DeckRoles.Protection);

        DeckRoleClassifier.Classify(Card(
                "Swiftfoot Boots",
                "Artifact - Equipment",
                "Equipped creature has hexproof and haste. Equip {1}."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Protection);
        DeckRoleClassifier.Classify(Card(
                "Lightning Greaves",
                "Artifact - Equipment",
                "Equipped creature has haste and shroud. Equip {0}."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Protection);
    }

    /// <summary>
    /// Verifies that saved Scryfall Tagger oracle annotations drive role classification before text heuristics.
    /// </summary>
    [Fact]
    public void RoleClassifier_UsesCanonicalTaggerOracleTags()
    {
        DeckCard treasureEngine = new()
        {
            Name = "Mystery Engine",
            Snapshot = new CardSnapshot
            {
                TypeLine = "Enchantment",
                OracleText = "At the beginning of your upkeep, choose one."
            },
            Metadata =
            {
                [CardFacetNames.TaggerOracleTags] = "repeatable-treasures"
            }
        };

        CardRoleAssignment assignment = DeckRoleClassifier.Classify(treasureEngine);

        assignment.PrimaryRole.Should().Be(DeckRoles.Ramp);
        assignment.Tags.Should().Contain(DeckTags.ManaFixing);
    }

    /// <summary>
    /// Verifies that the runtime Tagger taxonomy only names slugs present as oracle-card tags in the saved snapshot.
    /// </summary>
    [Fact]
    public void TaggerTaxonomy_UsesOnlySnapshotOracleCardTags()
    {
        using JsonDocument document = JsonDocument.Parse(ReadRepoFile("docs/reference/scryfall-tagger-tags-2026-05-23.json"));
        HashSet<string> oracleCardSlugs = document.RootElement
            .GetProperty("tags")
            .EnumerateArray()
            .Where(tag => tag.GetProperty("namespace").GetString() == "card")
            .Select(tag => tag.GetProperty("slug").GetString() ?? "")
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        DeckTaggerTaxonomy.Rules.Select(rule => rule.Slug)
            .Should()
            .OnlyContain(slug => oracleCardSlugs.Contains(slug));
    }

    /// <summary>
    /// Verifies that deterministic role ordering matches the public category taxonomy.
    /// </summary>
    [Fact]
    public void DeckRoles_Primary_UsesCanonicalCategoryOrder()
    {
        DeckRoles.Primary.Should().Equal(
            DeckRoles.Maybeboard,
            DeckRoles.Commander,
            DeckRoles.Lands,
            DeckRoles.Ramp,
            DeckRoles.Draw,
            DeckRoles.Tutors,
            DeckRoles.BoardWipes,
            DeckRoles.Interaction,
            DeckRoles.Protection,
            DeckRoles.Recursion,
            DeckRoles.Wincons,
            DeckRoles.Payoffs,
            DeckRoles.Synergy,
            DeckRoles.Utility);
    }

    /// <summary>
    /// Verifies that role classifier recognizes expanded theorycrafting tags.
    /// </summary>
    [Fact]
    public void RoleClassifier_ClassifiesExpandedTheorycraftingTags()
    {
        DeckRoleClassifier.Classify(Card(
                "Ghostly Prison",
                "Enchantment",
                "Creatures can't attack you unless their controller pays {2} for each creature they control that's attacking you."))
            .Tags
            .Should()
            .Contain([DeckTags.Pillowfort, DeckTags.GoWideProtection]);
        DeckRoleClassifier.Classify(Card(
                "Illness in the Ranks",
                "Enchantment",
                "Creature tokens get -1/-1."))
            .Tags
            .Should()
            .Contain(DeckTags.TokenHate);
        DeckRoleClassifier.Classify(Card(
                "Bane of Progress",
                "Creature",
                "When Bane of Progress enters the battlefield, destroy all artifacts and enchantments."))
            .Tags
            .Should()
            .Contain(DeckTags.ArtifactEnchantmentHate);

        CardRoleAssignment viciousRumors = DeckRoleClassifier.Classify(Card(
            "Vicious Rumors",
            "Sorcery",
            "Vicious Rumors deals 1 damage to each opponent. Each opponent discards a card."));
        viciousRumors.PrimaryRole.Should().NotBe(DeckRoles.Wincons);
        viciousRumors.Tags.Should().NotContain(DeckTags.Finishers);

        CardRoleAssignment leechriddenSwamp = DeckRoleClassifier.Classify(Card(
            "Leechridden Swamp",
            "Land",
            "{B}, {T}: Each opponent loses 1 life. Activate only if you control two or more black permanents."));
        leechriddenSwamp.PrimaryRole.Should().NotBe(DeckRoles.Wincons);
        leechriddenSwamp.Tags.Should().NotContain(DeckTags.Finishers);

        DeckRoleClassifier.Classify(Card(
                "Torment of Hailfire",
                "Sorcery",
                "Repeat the following process X times. Each opponent loses 3 life unless they sacrifice a nonland permanent or discard a card."))
            .Tags
            .Should()
            .Contain(DeckTags.Finishers);
        DeckRoleClassifier.Classify(Card(
                "Gray Merchant of Asphodel",
                "Creature",
                "When Gray Merchant enters, each opponent loses X life, where X is your devotion to black. You gain life equal to the life lost this way."))
            .Tags
            .Should()
            .Contain(DeckTags.Finishers);
    }

    /// <summary>
    /// Verifies that best-practice analysis reports role gaps.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckBestPractices_ReturnsNeedProfileAndGaps()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Gaps",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 32, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Signet", Quantity = 4, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                Card("Doom Blade", "Instant", "Destroy target nonblack creature.")
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckBestPracticeAnalysis analysis = await service.AnalyzeDeckBestPracticesAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Ramp).Status.Should().Be("low");
        analysis.RecommendedProfile.Should().Be("commander-baseline");
        analysis.HeuristicComparisons.Should().Contain(comparison => comparison.ProfileId == "command-zone-template");
        analysis.Risks.Should().Contain(risk => risk.Contains("Win", StringComparison.OrdinalIgnoreCase)
            || risk.Contains("win condition", StringComparison.OrdinalIgnoreCase));
        analysis.Citations.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that best-practice analysis honors deck intent heuristic profiles.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckBestPractices_UsesIntentHeuristicProfile()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Command Zone Profile",
            Description =
                """
                MTG MCP Deck Intent
                Heuristic Profile: command-zone-template
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard { Name = "Land", Quantity = 36, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 8, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Draw", Quantity = 9, PrimaryCategory = DeckRoles.Draw, Categories = [DeckRoles.Draw] },
                new DeckCard { Name = "Removal", Quantity = 8, PrimaryCategory = DeckRoles.Interaction, Categories = [DeckRoles.Interaction] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckBestPracticeAnalysis analysis = await service.AnalyzeDeckBestPracticesAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.RecommendedProfile.Should().Be("command-zone-template");
        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Ramp).Minimum.Should().Be(10);
        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Draw).Minimum.Should().Be(10);
        analysis.NeedProfile.Notes.Should().Contain(note => note.Contains("Command Zone template", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that best-practice analysis can use cEDH profiles from power intent.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckBestPractices_InfersCedhProfile()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Turbo Profile",
            Description =
                """
                MTG MCP Deck Intent
                Power Level: cEDH
                Archetype: turbo combo
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard { Name = "Land", Quantity = 36, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 10, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Tutor", Quantity = 4, PrimaryCategory = DeckRoles.Tutors, Categories = [DeckRoles.Tutors] },
                new DeckCard { Name = "Interaction", Quantity = 8, PrimaryCategory = DeckRoles.Interaction, Categories = [DeckRoles.Interaction] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckBestPracticeAnalysis analysis = await service.AnalyzeDeckBestPracticesAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.RecommendedProfile.Should().Be("cedh-turbo");
        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Lands).Status.Should().Be("high");
        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Ramp).Minimum.Should().Be(14);
        analysis.HeuristicComparisons.Should().Contain(comparison => comparison.ProfileId == "cedh-turbo");
    }

    /// <summary>
    /// Verifies that combo catalog failure falls back to local heuristics.
    /// </summary>
    [Fact]
    public async Task FindDeckCombos_FallsBackWhenComboCatalogFails()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Combo Fallback",
            Cards =
            [
                new DeckCard { Name = "Exquisite Blood", Quantity = 1, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy] },
                new DeckCard { Name = "Sanguine Bond", Quantity = 1, PrimaryCategory = DeckRoles.Wincons, Categories = [DeckRoles.Wincons] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog(), comboCatalog: new ThrowingComboCatalog());

        DeckComboReport result = await service.FindDeckCombosAsync(workspace.Id, TestContext.Current.CancellationToken);

        result.Combos.Should().Contain(combo => combo.Name.Contains("Exquisite Blood", StringComparison.OrdinalIgnoreCase));
        result.Notes.Should().Contain(note => note.Contains("catalog failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that duplicate copies of one combo-tagged card do not form a completed combo.
    /// </summary>
    [Fact]
    public async Task FindDeckCombos_DoesNotTreatDuplicateSingleCardAsCompletedCombo()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Duplicate Combo",
            Cards =
            [
                new DeckCard { Name = "Combo A", Quantity = 2, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "Untap target permanent. Copy target activated ability." } }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckComboReport completed = await service.FindDeckCombosAsync(workspace.Id, TestContext.Current.CancellationToken);
        DeckComboReport nearMisses = await service.FindNearMissCombosAsync(workspace.Id, TestContext.Current.CancellationToken);

        completed.Combos.Should().BeEmpty();
        nearMisses.NearMisses.Should().ContainSingle(combo => combo.Cards.Contains("Combo A"));
    }

    /// <summary>
    /// Verifies that card-selection recommendations request the intended search role.
    /// </summary>
    [Fact]
    public void RoleClassifier_SearchRequestForRole_CoversCardSelection()
    {
        CardSearchRequest request = DeckRoleClassifier.SearchRequestForRole(DeckTags.CardSelection, "commander", maxPrice: 2);

        request.Preset.Should().Be(CardSearchPreset.Role);
        request.Role.Should().Be(DeckTags.CardSelection);
        request.Format.Should().Be("commander");
        request.MaxPrice.Should().Be(2);
    }

    /// <summary>
    /// Verifies that analyze draw odds uses hypergeometric odds.
    /// </summary>
    [Fact]
    public void AnalyzeDrawOdds_UsesHypergeometricOdds()
    {
        DeckWorkspace workspace = new()
        {
            Cards =
            [
                new DeckCard { Name = "Ramp A", Quantity = 2, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Spell A", Quantity = 8, PrimaryCategory = DeckRoles.Utility, Categories = [DeckRoles.Utility] }
            ]
        };

        DeckOddsAnalysis analysis = DeckStatistics.AnalyzeDrawOdds(
            workspace,
            [DeckRoles.Ramp],
            turn: 1,
            openingHandSize: 5,
            simulations: 1_000,
            seed: 42);

        analysis.Rows.Single().HypergeometricAtLeastOne.Should().BeApproximately(0.777777, 0.0005);
        analysis.Rows.Single().MonteCarloAtLeastOne.Should().BeApproximately(0.809, 0.001);
    }

    /// <summary>
    /// Verifies that summarize deck plan returns role counts and risks.
    /// </summary>
    [Fact]
    public async Task SummarizeDeckWorkspace_ReturnsRoleCountsAndRisks()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Summary",
            Cards =
            [
                Card("Tinybones, Trinket Thief", "Legendary Creature", "Whenever an opponent discards a card, you draw a card."),
                Card("Arcane Signet", "Artifact", "{T}: Add one mana of any color."),
                Card("Toxic Deluge", "Sorcery", "All creatures get -X/-X until end of turn.")
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckPlanSummary summary = await service.SummarizeDeckWorkspaceAsync(workspace.Id, TestContext.Current.CancellationToken);

        summary.RoleCounts[DeckRoles.Ramp].Should().Be(1);
        summary.RoleCounts[DeckRoles.Draw].Should().Be(1);
        summary.RoleCounts[DeckRoles.BoardWipes].Should().Be(1);
        summary.Risks.Should().Contain(note => note.Contains("Land count", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that summarize deck plan uses intent thresholds when present.
    /// </summary>
    [Fact]
    public async Task SummarizeDeckWorkspace_UsesIntentThresholdsWhenPresent()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Intent Summary",
            Description =
                """
                MTG MCP Deck Intent
                Archetype: discard-control

                Targets
                Ramp: 4
                Draw: 5
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard { Name = "Land", Quantity = 36, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 4, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Draw", Quantity = 5, PrimaryCategory = DeckRoles.Draw, Categories = [DeckRoles.Draw] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckPlanSummary summary = await service.SummarizeDeckWorkspaceAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        summary.Intent.Should().NotBeNull();
        summary.IntentNotes.Should().Contain(note => note.Contains("discard-control", StringComparison.OrdinalIgnoreCase));
        summary.Risks.Should().NotContain(risk => risk.Contains("Ramp", StringComparison.OrdinalIgnoreCase));
        summary.Risks.Should().NotContain(risk => risk.Contains("draw", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that analyze draw odds uses default targets.
    /// </summary>
    [Fact]
    public async Task AnalyzeDrawOddsAsync_UsesDefaultTargets()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 36, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Signet", Quantity = 8, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Spell", Quantity = 56, PrimaryCategory = DeckRoles.Utility, Categories = [DeckRoles.Utility] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckOddsAnalysis analysis = await service.AnalyzeDrawOddsAsync(
            workspace.Id,
            targets: null,
            turn: 3,
            openingHandSize: 7,
            simulations: 500,
            seed: 7,
            TestContext.Current.CancellationToken);

        analysis.Rows.Select(row => row.Target).Should().Contain([DeckRoles.Lands, DeckRoles.Ramp, DeckRoles.Draw]);
        analysis.Rows.Single(row => row.Target == DeckRoles.Lands).SuccessesInDeck.Should().Be(36);
    }

    /// <summary>
    /// Verifies that land-drop odds include exact and deterministic simulation rows.
    /// </summary>
    [Fact]
    public async Task AnalyzeLandDropOddsAsync_ReturnsTurnByTurnMissRisk()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 30, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Spell", Quantity = 69, PrimaryCategory = DeckRoles.Utility, Categories = [DeckRoles.Utility] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        LandDropOddsAnalysis analysis = await service.AnalyzeLandDropOddsAsync(
            workspace.Id,
            turn: 3,
            openingHandSize: 7,
            onThePlay: true,
            includeMulligans: true,
            simulations: 500,
            seed: 42,
            TestContext.Current.CancellationToken);

        analysis.LandCount.Should().Be(30);
        analysis.Rows.Should().HaveCount(3);
        analysis.Rows.Single(row => row.Turn == 3).CardsSeen.Should().Be(9);
        analysis.Rows.Single(row => row.Turn == 3).MonteCarloMissLandDrop.Should().BeGreaterThan(0);
        analysis.FailureDrivers.Should().Contain(driver => driver.Contains("Land density", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that route classification emits only approved route labels.
    /// </summary>
    [Fact]
    public async Task ClassifyWinRoutesAsync_EmitsOnlyApprovedRouteLabels()
    {
        DeckAnalysisService service = CreateAnalysisService(new InMemoryRepository(), new FakeCardCatalog());

        WinRouteClassificationResult result = await service.ClassifyWinRoutesAsync(
            cardNames: null,
            workspaceId: null,
            comboId: null,
            producedFeatures:
            [
                "Infinite colorless mana",
                "Infinite storm count",
                "Draw your deck"
            ],
            format: "commander",
            TestContext.Current.CancellationToken);

        WinRouteClassification classification = result.Classifications.Single();
        classification.RouteTypes.Should().OnlyContain(route => WinRouteLabels.All.Contains(route));
        classification.RouteTypes.Should().Contain([WinRouteLabels.InfiniteMana, WinRouteLabels.Storm, WinRouteLabels.DrawDeck]);
        classification.NeedsPayoff.Should().BeTrue();
        classification.PayoffKindsNeeded.Should().Contain("mana-sink");
    }

    /// <summary>
    /// Verifies that unrecognized produced features do not get a fuzzy fallback route.
    /// </summary>
    [Fact]
    public async Task ClassifyWinRoutesAsync_DoesNotInventFallbackRoutes()
    {
        DeckAnalysisService service = CreateAnalysisService(new InMemoryRepository(), new FakeCardCatalog());

        WinRouteClassificationResult result = await service.ClassifyWinRoutesAsync(
            cardNames: null,
            workspaceId: null,
            comboId: null,
            producedFeatures: ["Untap target permanent"],
            format: "commander",
            TestContext.Current.CancellationToken);

        WinRouteClassification classification = result.Classifications.Single();
        classification.RouteTypes.Should().BeEmpty();
        classification.Terminal.Should().BeFalse();
        classification.NeedsPayoff.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that route classification requires one deterministic evidence input.
    /// </summary>
    [Fact]
    public async Task ClassifyWinRoutesAsync_RequiresExactlyOneInput()
    {
        DeckAnalysisService service = CreateAnalysisService(new InMemoryRepository(), new FakeCardCatalog());

        Func<Task> act = () => service.ClassifyWinRoutesAsync(
            cardNames: ["Blood Artist"],
            workspaceId: "workspace-1",
            comboId: null,
            producedFeatures: null,
            format: "commander",
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*exactly one*");
    }

    /// <summary>
    /// Verifies that prevention text is not treated as an alternate win condition.
    /// </summary>
    [Fact]
    public void WinRouteClassifier_DoesNotTreatCantLoseTextAsTerminal()
    {
        WinRouteClassification classification = WinRouteClassifier.ClassifyCard(new CardInfo
        {
            Name = "Platinum Angel",
            TypeLine = "Artifact Creature",
            OracleText = "You can't lose the game and your opponents can't win the game."
        });

        classification.RouteTypes.Should().NotContain(WinRouteLabels.AlternateWin);
        classification.Terminal.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that analyze deck cost returns totals and drivers from cached prices.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckCost_ReturnsTotalsAndDrivers()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Cost",
            Cards =
            [
                ExpensiveRamp(),
                new DeckCard
                {
                    Name = "Maybe Draw",
                    Quantity = 2,
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckDefaults.Maybeboard],
                    Snapshot = new CardSnapshot
                    {
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                    }
                },
                new DeckCard { Name = "Unknown Price", PrimaryCategory = DeckRoles.Draw, Categories = [DeckRoles.Draw] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        DeckCostAnalysis analysis = await service.AnalyzeDeckCostAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.IncludedTotal.Should().Be(180);
        analysis.MaybeboardTotal.Should().Be(6);
        analysis.MissingPriceCards.Should().Contain("Unknown Price");
        analysis.TopCostDrivers.Should().ContainSingle().Which.CardName.Should().Be("Mana Crypt");
    }

    /// <summary>
    /// Verifies that mana base and consistency analysis return useful signals.
    /// </summary>
    [Fact]
    public async Task AnalyzeManaBaseAndConsistency_ReturnSignals()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Signals",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 32,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                },
                new DeckCard
                {
                    Name = "Temple of Deceit",
                    Quantity = 4,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        OracleText = "Temple of Deceit enters the battlefield tapped.",
                        ProducedMana = ["U", "B"]
                    }
                },
                new DeckCard
                {
                    Name = "Arcane Signet",
                    Quantity = 2,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        OracleText = "{T}: Add one mana of any color.",
                        ProducedMana = ["W", "U", "B", "R", "G"],
                        ManaValue = 2
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        ManaBaseAnalysis manaBase = await service.AnalyzeManaBaseAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);
        DeckConsistencyAnalysis consistency = await service.AnalyzeDeckConsistencyAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        manaBase.LandCount.Should().Be(36);
        manaBase.ColorSources["B"].Should().Be(36);
        manaBase.TappedLandCount.Should().Be(4);
        consistency.RampCount.Should().Be(2);
        consistency.Risks.Should().Contain(note => note.Contains("Ramp", StringComparison.OrdinalIgnoreCase));
        consistency.KeyOdds.Rows.Should().Contain(row => row.Target == DeckRoles.Ramp);
    }

    /// <summary>
    /// Verifies that colorless utility lands do not make mono-color decks look multicolor.
    /// </summary>
    [Fact]
    public async Task AnalyzeManaBase_MonoBlackWithColorlessLandDoesNotWarnAboutMulticolorFixing()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "War Room",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                },
                new DeckCard
                {
                    Name = "War Room",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Land", OracleText = "{T}: Add {C}.", ProducedMana = ["C"] }
                },
                new DeckCard
                {
                    Name = "Command Tower",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                        ProducedMana = ["W", "U", "B", "R", "G"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        ManaBaseAnalysis analysis = await service.AnalyzeManaBaseAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.ColorSources.Should().ContainKey("B");
        analysis.ColorSources.Should().ContainKey("C");
        analysis.Risks.Should().NotContain(risk => risk.Contains("Multicolor", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that multicolor commander identity still receives fixing guidance.
    /// </summary>
    [Fact]
    public async Task AnalyzeManaBase_MulticolorCommanderWarnsWhenFixingIsLow()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Two Color",
            Cards =
            [
                new DeckCard
                {
                    Name = "Krydle of Baldur's Gate",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["U", "B"] }
                },
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                },
                new DeckCard
                {
                    Name = "Temple of Deceit",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Land", ProducedMana = ["U", "B"] }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        ManaBaseAnalysis analysis = await service.AnalyzeManaBaseAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.Risks.Should().Contain(risk => risk.Contains("Multicolor", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that MDFC lands in land categories are represented as land slots.
    /// </summary>
    [Fact]
    public async Task AnalyzeManaBase_MdfcLandCategoryReportsLandSlotAndManaProducingCounts()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "MDFC Lands",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 37,
                    PrimaryCategory = "Land",
                    Categories = ["Land"],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                },
                new DeckCard
                {
                    Name = "Malakir Rebirth // Malakir Mire",
                    Quantity = 1,
                    PrimaryCategory = "Land",
                    Categories = ["Land"],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Instant // Land",
                        ColorIdentity = ["B"],
                        OracleText = "Choose target creature. You lose 2 life. Until end of turn, that creature gains when this creature dies, return it to the battlefield tapped."
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        ManaBaseAnalysis analysis = await service.AnalyzeManaBaseAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.LandCount.Should().Be(38);
        analysis.LandSlotCount.Should().Be(38);
        analysis.ModalDoubleFacedLandCount.Should().Be(1);
        analysis.ManaProducingLandCount.Should().Be(38);
        analysis.ColorSources["B"].Should().Be(38);
        analysis.TappedLandCount.Should().Be(1);
        analysis.UntappedLandCount.Should().Be(37);
    }

    /// <summary>
    /// Verifies that commander bracket estimates use live Game Changer search results.
    /// </summary>
    [Fact]
    public async Task EstimateCommanderBracket_UsesGameChangers()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Bracket",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(workspaces, new FakeCardCatalog());

        CommanderBracketEstimate estimate = await service.EstimateCommanderBracketAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        estimate.GameChangers.Should().Contain("Mana Crypt");
        estimate.EstimatedBracket.Should().BeGreaterThanOrEqualTo(3);
        estimate.Signals.Should().Contain(signal => signal.Signal == "game-changer");
    }

    /// <summary>
    /// Verifies that unavailable Game Changer data fails clearly.
    /// </summary>
    [Fact]
    public async Task EstimateCommanderBracket_FailsClearlyWhenGameChangersUnavailable()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Bracket",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(
            workspaces,
            new FakeCardCatalog { ThrowOnGameChangerSearch = true });

        Func<Task> estimate = () => service.EstimateCommanderBracketAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        await estimate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Unable to fetch live Commander Game Changer data from Scryfall.");
    }

    /// <summary>
    /// Verifies that caller cancellation during Game Changer search is not converted into an outage error.
    /// </summary>
    [Fact]
    public async Task EstimateCommanderBracket_PropagatesCallerCancellationDuringGameChangerSearch()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Bracket",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckAnalysisService service = CreateAnalysisService(
            workspaces,
            new FakeCardCatalog { CancelGameChangerSearch = true });
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> estimate = () => service.EstimateCommanderBracketAsync(
            workspace.Id,
            cancellation.Token);

        await estimate.Should().ThrowAsync<OperationCanceledException>();
    }
}
