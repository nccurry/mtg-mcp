using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains tests for deck intelligence workflows.
/// </summary>
public sealed class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies that normalize deck cards populates extended snapshot.
    /// </summary>
    [Fact]
    public async Task NormalizeDeckCards_PopulatesExtendedSnapshot()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Normalize",
            Cards = [new DeckCard { Name = "Sol Ring" }]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckNormalizationResult result = await service.NormalizeDeckCardsAsync(
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
    /// Verifies that normalize deck cards handles legacy null snapshots.
    /// </summary>
    [Fact]
    public async Task NormalizeDeckCards_HandlesLegacyNullSnapshots()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Legacy",
            Cards = [new DeckCard { Name = "Sol Ring", Snapshot = null! }]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckNormalizationResult result = await service.NormalizeDeckCardsAsync(
            workspace.Id,
            "missing",
            TestContext.Current.CancellationToken);

        result.UpdatedCards.Should().Be(1);
        result.Workspace.Cards.Single().Snapshot.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that deck intent is parsed from Archidekt-style description text.
    /// </summary>
    [Fact]
    public async Task GetDeckIntent_ReadsHumanReadableSectionFromDescription()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Intent",
            Description =
                """
                {"ops":[{"insert":"Primer text\n\nMTG MCP Deck Intent\nVersion: 1\nArchetype: discard-control\nBudget: prefer cheaper swaps; avoid cards over $15 unless core\n\nTargets\nRamp: 8-10\nDraw: 9-11\n\nPrefer\n- repeatable discard\n\nAvoid\n- hard stax\n\nProtect\n- Tinybones, Trinket Thief\nEnd MTG MCP Deck Intent\n"}]}
                """
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());
        string originalText = DeckIntentText.ToPlainText(workspace.Description);

        DeckIntentResult result = await service.GetDeckIntentAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Intent.Should().NotBeNull();
        result.Intent!.Archetype.Should().Be("discard-control");
        result.Intent.Budget.MaxCardPrice.Should().Be(15);
        result.Intent.Targets[DeckRoles.Ramp].Minimum.Should().Be(8);
        result.Intent.Prefer.Should().Contain("repeatable discard");
        result.Intent.Avoid.Should().Contain("hard stax");
        result.Intent.Protect.Should().Contain("Tinybones, Trinket Thief");
    }

    /// <summary>
    /// Verifies that newer deck intent fields normalize supported vocabulary.
    /// </summary>
    [Fact]
    public void DeckIntentText_ParsesHeuristicFieldsAndPackages()
    {
        DeckIntentResult result = DeckIntentText.Parse(
            """
            MTG MCP Deck Intent
            Version: 1
            Power Level: cEDH
            Heuristic Profile: Command Zone Template
            Package Template: package_8x8
            Local Meta: go-wide tokens, graveyards; artifact decks

            Packages
            Ramp: 8
            Draw: 8
            Interaction: 8
            End MTG MCP Deck Intent
            """);

        result.Warnings.Should().BeEmpty();
        result.Intent.Should().NotBeNull();
        result.Intent!.PowerLevel.Should().Be("cedh");
        result.Intent.HeuristicProfile.Should().Be("command-zone-template");
        result.Intent.PackageTemplate.Should().Be("8x8");
        result.Intent.LocalMeta.Should().Contain(["go-wide tokens", "graveyards", "artifact decks"]);
        result.Intent.Packages[DeckRoles.Ramp].Minimum.Should().Be(8);
        result.Intent.Packages[DeckRoles.Ramp].Maximum.Should().Be(8);
    }

    /// <summary>
    /// Verifies that unknown vocabulary remains readable but produces warnings.
    /// </summary>
    [Fact]
    public void DeckIntentText_KeepsUnknownVocabularyWithWarnings()
    {
        DeckIntentResult result = DeckIntentText.Parse(
            """
            MTG MCP Deck Intent
            Power Level: kitchen table
            Heuristic Profile: personal brew
            Package Template: cube
            End MTG MCP Deck Intent
            """);

        result.Intent.Should().NotBeNull();
        result.Intent!.PowerLevel.Should().Be("kitchen table");
        result.Intent.HeuristicProfile.Should().Be("personal brew");
        result.Intent.PackageTemplate.Should().Be("cube");
        result.Warnings.Should().HaveCount(3);
    }

    /// <summary>
    /// Verifies that documented power-level aliases normalize to supported values.
    /// </summary>
    [Theory]
    [InlineData("High Power", "high-power")]
    [InlineData("high_power", "high-power")]
    [InlineData("upgraded-precon", "casual")]
    [InlineData("mid-power", "tuned-casual")]
    [InlineData("optimized", "high-power")]
    [InlineData("competitive", "cedh")]
    public void DeckIntentVocabulary_NormalizesPowerLevels(string value, string expected)
    {
        DeckIntentVocabulary.TryNormalizePowerLevel(value, out string normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that documented heuristic profile aliases normalize to supported values.
    /// </summary>
    [Theory]
    [InlineData("Command Zone Template", "command-zone-template")]
    [InlineData("package_8x8", "package-8x8")]
    [InlineData("75%", "seventy-five-percent")]
    [InlineData("cedh midrange", "cedh-midrange")]
    public void DeckIntentVocabulary_NormalizesHeuristicProfiles(string value, string expected)
    {
        DeckIntentVocabulary.TryNormalizeHeuristicProfile(value, out string normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that suggested intent includes normalized heuristic guidance.
    /// </summary>
    [Fact]
    public async Task SuggestDeckIntent_IncludesHeuristicProfile()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Intent Suggestion",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Teysa Karlov",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander]
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckIntentResult result = await service.SuggestDeckIntentAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        result.Intent.Should().NotBeNull();
        result.Intent!.HeuristicProfile.Should().Be("auto");
        result.IntentText.Should().Contain("Heuristic Profile: auto");
        DeckIntentText.Parse(result.IntentText).Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the README documents all supported deck intent vocabulary.
    /// </summary>
    [Fact]
    public void Readme_DocumentsSupportedDeckIntentVocabulary()
    {
        string readme = ReadRepoFile("README.md");

        foreach (string powerLevel in DeckIntentVocabulary.PowerLevels)
        {
            readme.Should().Contain($"`{powerLevel}`");
        }

        foreach (string profile in DeckIntentVocabulary.HeuristicProfiles)
        {
            readme.Should().Contain($"`{profile}`");
        }

        foreach (string packageTemplate in DeckIntentVocabulary.PackageTemplates)
        {
            readme.Should().Contain($"`{packageTemplate}`");
        }
    }

    /// <summary>
    /// Verifies that the README's deck intent example stays parseable.
    /// </summary>
    [Fact]
    public void Readme_DeckIntentExampleParses()
    {
        string readme = ReadRepoFile("README.md");
        string example = readme
            .Split("```", StringSplitOptions.None)
            .First(block => block.Contains("MTG MCP Deck Intent", StringComparison.Ordinal)
                && block.Contains("Heuristic Profile", StringComparison.Ordinal));

        DeckIntentResult result = DeckIntentText.Parse(example);

        result.Intent.Should().NotBeNull();
        result.Intent!.HeuristicProfile.Should().Be("command-zone-template");
        result.Intent.PowerLevel.Should().Be("tuned-casual");
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that set and clear deck intent preserve surrounding description text.
    /// </summary>
    [Fact]
    public async Task SetAndClearDeckIntent_PreserveSurroundingDescriptionText()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Intent Update",
            Description = """{"ops":[{"insert":"Primer before\n"}]}"""
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());
        string originalText = DeckIntentText.ToPlainText(workspace.Description);

        DeckIntentChangeResult set = await service.SetDeckIntentAsync(
            workspace.Id,
            """
            Archetype: discard-control

            Targets
            Ramp: 8-10
            """,
            TestContext.Current.CancellationToken);
        string setText = DeckIntentText.ToPlainText(set.Workspace.Description);

        set.Persistence.Should().Be(DeckPersistence.LocalOnly);
        set.Intent.Found.Should().BeTrue();
        setText.Should().Contain("Primer before");
        setText.Should().Contain("MTG MCP Deck Intent");
        setText.Should().Contain("End MTG MCP Deck Intent");

        DeckIntentChangeResult cleared = await service.ClearDeckIntentAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);
        string clearedText = DeckIntentText.ToPlainText(cleared.Workspace.Description);

        cleared.Intent.Found.Should().BeFalse();
        clearedText.Should().Contain("Primer before");
        clearedText.Should().NotContain("MTG MCP Deck Intent");
    }

    /// <summary>
    /// Verifies that intent edits preserve rich Quill description content.
    /// </summary>
    [Fact]
    public async Task SetAndClearDeckIntent_PreserveQuillFormattingAndEmbeds()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Rich Intent Update",
            Description =
                """
                {"ops":[{"insert":"Primer","attributes":{"bold":true}},{"insert":" before\n"},{"insert":{"image":"https://example.test/card.jpg"}},{"insert":"\nPrimer after\n","attributes":{"italic":true}}]}
                """
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());
        string originalText = DeckIntentText.ToPlainText(workspace.Description);

        DeckIntentChangeResult set = await service.SetDeckIntentAsync(
            workspace.Id,
            "Archetype: discard-control",
            TestContext.Current.CancellationToken);
        string setText = DeckIntentText.ToPlainText(set.Workspace.Description);

        setText.Should().Contain("Primer before");
        setText.Should().Contain("Primer after");
        setText.Should().Contain("discard-control");
        setText.Should().NotContain("https://example.test/card.jpg");
        AssertRichQuillContent(set.Workspace.Description!);

        DeckIntentChangeResult cleared = await service.ClearDeckIntentAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);
        string clearedText = DeckIntentText.ToPlainText(cleared.Workspace.Description);

        clearedText.Should().Contain("Primer before");
        clearedText.Should().Contain("Primer after");
        clearedText.Should().NotContain("MTG MCP Deck Intent");
        clearedText.Should().Be(originalText);
        AssertRichQuillContent(cleared.Workspace.Description!);
    }

    /// <summary>
    /// Verifies that description edits ignore loose marker mentions and incomplete blocks.
    /// </summary>
    [Fact]
    public void DeckIntentDescriptionEdits_IgnoreLooseMentionsAndIncompleteBlocks()
    {
        string description =
            """
            Intro
            This primer mentions MTG MCP Deck Intent in prose.
            MTG MCP Deck Intent
            Archetype: unfinished
            Keep this footer.
            """;

        DeckIntentText.ClearDescription(description).Should().Be(description);

        string updated = DeckIntentText.UpsertDescription(description, "Archetype: discard-control");
        DeckIntentResult result = DeckIntentText.Extract(updated);

        updated.Should().Contain("Keep this footer.");
        updated.Should().Contain("Archetype: unfinished");
        result.Found.Should().BeTrue();
        result.Intent.Should().NotBeNull();
        result.Intent!.Archetype.Should().Be("discard-control");
        result.IntentText.Should().NotContain("unfinished");

        string cleared = DeckIntentText.ClearDescription(updated);
        cleared.Should().Contain("Keep this footer.");
        cleared.Should().Contain("Archetype: unfinished");
        cleared.Should().NotContain("discard-control");
    }

    /// <summary>
    /// Verifies that setting deck intent writes through Archidekt metadata writeback.
    /// </summary>
    [Fact]
    public async Task SetDeckIntent_ArchidektWritebackPersistsMetadata()
    {
        InMemoryRepository workspaces = new();
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Name = "Remote Intent",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
                Description = """{"ops":[{"insert":"Primer before\n"}]}"""
            }
        };
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidekt);
        DeckWorkspace workspace = await service.OpenArchidektDeckAsync(
            "123",
            writeBack: true,
            TestContext.Current.CancellationToken);

        DeckIntentChangeResult result = await service.SetDeckIntentAsync(
            workspace.Id,
            "Archetype: discard-control",
            TestContext.Current.CancellationToken);

        result.Persistence.Should().Be(DeckPersistence.ArchidektWriteBack);
        archidekt.PersistedMetadataRequests.Should().Be(1);
        DeckIntentText.ToPlainText(archidekt.ImportedDeck.Description).Should().Contain("discard-control");
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

        DeckRoleClassifier.Classify(Card(
                "Tourach, Dread Cantor",
                "Legendary Creature",
                "Kicker {B}{B}. Protection from white. Whenever an opponent discards a card, put a +1/+1 counter on Tourach."))
            .PrimaryRole
            .Should()
            .NotBe(DeckRoles.Protection);

        DeckRoleClassifier.Classify(Card(
                "Swiftfoot Boots",
                "Artifact — Equipment",
                "Equipped creature has hexproof and haste. Equip {1}."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Protection);
        DeckRoleClassifier.Classify(Card(
                "Lightning Greaves",
                "Artifact — Equipment",
                "Equipped creature has haste and shroud. Equip {0}."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Protection);
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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckBestPracticeAnalysis analysis = await service.AnalyzeDeckBestPracticesAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        analysis.RecommendedProfile.Should().Be("cedh-turbo");
        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Lands).Status.Should().Be("high");
        analysis.NeedProfile.RoleNeeds.Single(need => need.Target == DeckRoles.Ramp).Minimum.Should().Be(14);
        analysis.HeuristicComparisons.Should().Contain(comparison => comparison.ProfileId == "cedh-turbo");
    }

    /// <summary>
    /// Verifies that goal recommendations create a previewable plan.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_CreatesPlanForTableInteraction()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Goal",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add a few cards that interact with the whole table",
            count: 2,
            maxPrice: 5,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation => operation.Operation == DeckEditOperations.AddCard);
        result.Suggestions.Should().Contain(suggestion => suggestion.Tags.Contains(DeckTags.TableInteraction));
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that goal recommendations understand politics and table-wide interaction requests.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_CreatesPlanForPoliticsInteraction()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Politics Goal",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add politics or monarch cards that interact with the whole table",
            count: 1,
            maxPrice: 5,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion =>
            suggestion.CardName == "Court of Ambition"
            && suggestion.Tags.Contains(DeckTags.Politics)
            && suggestion.Tags.Contains(DeckTags.TableInteraction));
    }

    /// <summary>
    /// Verifies that goal recommendations understand commander protection requests.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_CreatesPlanForCommanderProtection()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Protection Goal",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "ensure I have commander protection",
            count: 1,
            maxPrice: 10,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard
            && operation.CardName == "Lightning Greaves"
            && operation.Category == DeckRoles.Protection);
        result.Suggestions.Should().Contain(suggestion =>
            suggestion.CardName == "Lightning Greaves"
            && suggestion.Role == DeckRoles.Protection);
    }

    /// <summary>
    /// Verifies that new-card radar uses release filters and deck fit.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_UsesCatalogTrendFallback()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "New Cards",
            Description =
                """
                MTG MCP Deck Intent
                Archetype: tokens
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckWorkspaceService service = new(workspaces, catalog);

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "tst",
            limit: 3,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        catalog.SearchQueries.Should().Contain(query => query.Contains("date>=2026-01-01", StringComparison.OrdinalIgnoreCase));
        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Season of Loss");
    }

    /// <summary>
    /// Verifies that release radar defaults to a recent window when since is omitted.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_DefaultsToRecentReleaseWindow()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Default Release Radar",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckWorkspaceService service = new(
            workspaces,
            catalog,
            currentDateOverride: new DateOnly(2026, 5, 10));

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: null,
            setCode: null,
            limit: 3,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        catalog.SearchQueries.Should().Contain(query => query.Contains("date>=2025-05-10", StringComparison.OrdinalIgnoreCase));
        result.Notes.Should().Contain(note => note.Contains("2025-05-10", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that combo and goldfish projections return explainable estimates.
    /// </summary>
    [Fact]
    public async Task GoldfishAndComboTools_ReturnHeuristicEstimates()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Goldfish",
            Cards =
            [
                new DeckCard { Name = "Forest", Quantity = 40, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 12, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "{T}: Add {G}." } },
                new DeckCard { Name = "Token Maker", Quantity = 16, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Creature", ManaValue = 3, OracleText = "When this enters, create two 1/1 creature tokens." } },
                new DeckCard { Name = "Craterhoof Behemoth", Quantity = 3, PrimaryCategory = DeckRoles.Wincons, Categories = [DeckRoles.Wincons], Snapshot = new CardSnapshot { TypeLine = "Creature", ManaValue = 8, OracleText = "Creatures you control get +X/+X and gain trample until end of turn." } },
                new DeckCard { Name = "Combo A", Quantity = 1, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "Untap target permanent. Copy target activated ability." } },
                new DeckCard { Name = "Combo B", Quantity = 1, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "Whenever an ability is copied, untap target permanent." } }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 5,
            simulations: 200,
            seed: 9,
            mulligan: true,
            TestContext.Current.CancellationToken);
        ComboPressureEstimate pressure = await service.EstimateComboPressureAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        goldfish.TurnSummaries.Should().HaveCount(5);
        goldfish.TurnSummaries.Last().MedianManaSources.Should().BeGreaterThan(0);
        goldfish.WinEstimate.Routes.Should().NotBeEmpty();
        pressure.Level.Should().NotBe("low");
    }

    /// <summary>
    /// Verifies that budgeted goal packages do not treat unknown prices as free.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_ExcludesUnpricedCardsWhenBudgeted()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Budget Goal",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new GoalBudgetCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add a few cards that interact with the whole table",
            count: 2,
            maxPrice: 1,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Syphon Mind");
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Mystery Table Spell");
    }

    /// <summary>
    /// Verifies that lower-salt goal packages cut high-pressure cards rather than only adding value.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_LessSaltyAddsCutsForHighPressureCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Salty Deck",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard
                {
                    Name = "Mana Crypt",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add {C}{C}.", ColorIdentity = [] }
                },
                new DeckCard
                {
                    Name = "Demonic Tutor",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Tutors,
                    Categories = [DeckRoles.Tutors],
                    Snapshot = new CardSnapshot { TypeLine = "Sorcery", OracleText = "Search your library for a card.", ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "make this deck less salty",
            count: 2,
            maxPrice: 5,
            strategy: "casual",
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Phyrexian Arena");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Mana Crypt");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Demonic Tutor");
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Ayara, First of Locthwain");
    }

    /// <summary>
    /// Verifies that new-card fallback keeps searched print metadata.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_PreservesSearchPrintMetadataAndExcludesUnpricedCards()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Release Radar",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new TrendMetadataCatalog());

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "new",
            limit: 5,
            maxPrice: 1,
            TestContext.Current.CancellationToken);

        NewCardSuggestion suggestion = result.Suggestions.Should().ContainSingle(card => card.CardName == "Reprinted Drain").Subject;
        suggestion.Set.Should().Be("new");
        suggestion.ReleasedAt.Should().Be(new DateOnly(2026, 2, 1));
        result.Suggestions.Should().NotContain(card => card.CardName == "Unpriced New Card");
    }

    /// <summary>
    /// Verifies that card trend providers are optional and failure-tolerant.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_FallsBackWhenTrendProviderFails()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Trend Fallback",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(
            workspaces,
            new FakeCardCatalog(),
            cardTrendProvider: new ThrowingCardTrendProvider());

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "tst",
            limit: 3,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Season of Loss");
        result.Notes.Should().Contain(note => note.Contains("provider failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that Commander meta providers are optional and failure-tolerant.
    /// </summary>
    [Fact]
    public async Task CompareToCommanderMeta_FallsBackWhenProviderFails()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Meta Fallback",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(
            workspaces,
            new FakeCardCatalog(),
            commanderMetaProvider: new ThrowingCommanderMetaProvider());

        CommanderMetaReport result = await service.CompareToCommanderMetaAsync(
            workspace.Id,
            limit: 5,
            TestContext.Current.CancellationToken);

        result.MissingPopularCards.Should().NotBeEmpty();
        result.Notes.Should().Contain(note => note.Contains("provider failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that provider new-card suggestions are filtered through deck fit rules.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_FiltersProviderSuggestionsThroughDeckRules()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Provider Filtering",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(
            workspaces,
            new FakeCardCatalog(),
            cardTrendProvider: new FixedCardTrendProvider(
            [
                new NewCardSuggestion { CardName = "Blasphemous Act", Score = 1, Price = 3 },
                new NewCardSuggestion { CardName = "Season of Loss", Score = 0.8, Price = 2, ReleasedAt = new DateOnly(2026, 2, 1), Set = "tst" }
            ]));

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: null,
            setCode: null,
            limit: 5,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Season of Loss");
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Blasphemous Act");
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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), comboCatalog: new ThrowingComboCatalog());

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckComboReport completed = await service.FindDeckCombosAsync(workspace.Id, TestContext.Current.CancellationToken);
        DeckComboReport nearMisses = await service.FindNearMissCombosAsync(workspace.Id, TestContext.Current.CancellationToken);

        completed.Combos.Should().BeEmpty();
        nearMisses.NearMisses.Should().ContainSingle(combo => combo.Cards.Contains("Combo A"));
    }

    /// <summary>
    /// Verifies that weak decks report no likely goldfish win route.
    /// </summary>
    [Fact]
    public async Task EstimateWinTurn_ReturnsNoRouteForDeckWithoutWinCondition()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "No Wincon",
            Cards =
            [
                new DeckCard { Name = "Forest", Quantity = 42, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 10, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "{T}: Add {G}." } }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        WinTurnEstimate estimate = await service.EstimateWinTurnAsync(
            workspace.Id,
            maxTurn: 7,
            simulations: 100,
            seed: 17,
            TestContext.Current.CancellationToken);

        estimate.MedianWinTurn.Should().BeNull();
        estimate.Routes.Should().BeEmpty();
        estimate.Notes.Should().Contain(note => note.Contains("No likely win", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that brainstorming explicitly persists one previewable goal plan.
    /// </summary>
    [Fact]
    public async Task BrainstormDeckImprovements_PersistsOnePreviewableGoalPlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Brainstorm",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        BrainstormDeckImprovementsResult result = await service.BrainstormDeckImprovementsAsync(
            workspace.Id,
            "add a few cards that interact with the whole table",
            budget: 5,
            targetPower: "balanced",
            TestContext.Current.CancellationToken);

        IReadOnlyList<DeckEditPlan> savedPlans = await plans.ListAsync(workspace.Id, TestContext.Current.CancellationToken);
        savedPlans.Should().ContainSingle();
        result.Notes.Should().Contain(note => note.Contains(savedPlans.Single().PlanId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that card-selection recommendations use a specific search query.
    /// </summary>
    [Fact]
    public void RoleClassifier_QueryForRole_CoversCardSelection()
    {
        string query = DeckRoleClassifier.QueryForRole(DeckTags.CardSelection, "commander", maxPrice: 2);

        query.Should().Contain("scry");
        query.Should().Contain("legal:commander");
        query.Should().Contain("usd<=2");
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
    public async Task SummarizeDeckPlan_ReturnsRoleCountsAndRisks()
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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckPlanSummary summary = await service.SummarizeDeckPlanAsync(workspace.Id, TestContext.Current.CancellationToken);

        summary.RoleCounts[DeckRoles.Ramp].Should().Be(1);
        summary.RoleCounts[DeckRoles.Draw].Should().Be(1);
        summary.RoleCounts[DeckRoles.BoardWipes].Should().Be(1);
        summary.Risks.Should().Contain(note => note.Contains("Land count", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that summarize deck plan uses intent thresholds when present.
    /// </summary>
    [Fact]
    public async Task SummarizeDeckPlan_UsesIntentThresholdsWhenPresent()
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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckPlanSummary summary = await service.SummarizeDeckPlanAsync(
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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

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
    /// Verifies that find budget replacements creates persisted plan without mutating deck.
    /// </summary>
    [Fact]
    public async Task FindBudgetReplacements_CreatesPersistedPlanWithoutMutatingDeck()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Budget",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle();
        result.Plan.Operations.Should().HaveCount(2);
        result.Plan.Operations[0].Operation.Should().Be(DeckEditOperations.RemoveCard);
        result.Plan.Operations[1].Operation.Should().Be(DeckEditOperations.AddCard);
        workspaces.Workspaces[workspace.Id].Cards.Single().Name.Should().Be("Mana Crypt");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that budget replacements respect protected cards from intent.
    /// </summary>
    [Fact]
    public async Task FindBudgetReplacements_RespectsIntentProtectedCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Protected Budget",
            Description =
                """
                MTG MCP Deck Intent

                Protect
                - Mana Crypt
                End MTG MCP Deck Intent
                """,
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().BeEmpty();
        result.Plan.Warnings.Should().Contain(warning => warning.Contains("intent", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that find card upgrades creates persisted upgrade plan.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_CreatesPersistedUpgradePlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Upgrades",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        EdhrecRank = 20_000,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: new ReplacementWeights { Role = 2, Power = 1, Price = 1 },
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Phyrexian Arena");
        result.Plan.Operations.Should().Contain(operation => operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Phyrexian Arena");
    }

    /// <summary>
    /// Verifies that power upgrade focus supplies default weights.
    /// </summary>
    [Fact]
    public async Task FindPowerUpgrades_FocusSuppliesDefaultWeights()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Speed Upgrades",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        EdhrecRank = 20_000,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindPowerUpgradesAsync(
            workspace.Id,
            focus: "speed",
            maxPrice: null,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Plan.Rationale.Should().Contain("role=0.35");
        result.Plan.Rationale.Should().Contain("power=0.5");
        result.Plan.Rationale.Should().Contain("price=0.15");
    }

    /// <summary>
    /// Verifies that find card upgrades rejects off color and wrong format candidates.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_RejectsOffColorAndWrongFormatCandidates()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Modern Mono Black",
            Format = "modern",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Phyrexian Arena");
        result.Suggestions.Select(suggestion => suggestion.WithCard)
            .Should()
            .NotContain(["Rhystic Study", "Necropotence"]);
    }

    /// <summary>
    /// Verifies that budget recommendations never replace the commander.
    /// </summary>
    [Fact]
    public async Task FindBudgetReplacements_SkipsCommanderCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Commander Budget",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        OracleText = "Whenever an opponent discards a card, that player loses 2 life.",
                        ColorIdentity = ["B"],
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "25.00" }
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().BeEmpty();
        result.Plan.Warnings.Should().ContainSingle().Which.Should().Contain("No replacements");
    }

    /// <summary>
    /// Verifies that candidate cards are scored from their own text instead of inheriting the replaced card's category.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_ScoresCandidateRoleFromCandidateText()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Interaction Upgrade",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Edict",
                    PrimaryCategory = DeckRoles.Interaction,
                    Categories = [DeckRoles.Interaction],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        OracleText = "Each opponent sacrifices a creature.",
                        ManaValue = 5,
                        ColorIdentity = ["B"],
                        EdhrecRank = 20_000
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Hero's Downfall");
    }

    /// <summary>
    /// Verifies that generic utility cards do not produce broad staple upgrade suggestions.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_SkipsGenericUtilityCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Generic Utility",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Unclear Utility",
                    PrimaryCategory = DeckRoles.Utility,
                    Categories = [DeckRoles.Utility],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        OracleText = "This card has no useful role signal.",
                        ManaValue = 4,
                        ColorIdentity = [],
                        EdhrecRank = 25_000
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().BeEmpty();
        result.Plan.Warnings.Should().ContainSingle().Which.Should().Contain("No replacements");
    }

    /// <summary>
    /// Verifies that suggest deck categories persists move plan.
    /// </summary>
    [Fact]
    public async Task SuggestDeckCategories_PersistsMovePlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Categories",
            Cards =
            [
                new DeckCard
                {
                    Name = "Arcane Signet",
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add one mana of any color." }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        CategoryPlanResult result = await service.SuggestDeckCategoriesAsync(workspace.Id, TestContext.Current.CancellationToken);

        result.Suggestions.Single().SuggestedPrimaryRole.Should().Be(DeckRoles.Ramp);
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.MoveCard
            && operation.ToCategory == DeckRoles.Ramp);
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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

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
        DeckWorkspaceService service = new(
            workspaces,
            new FakeCardCatalog { ThrowOnGameChangerSearch = true });

        Func<Task> estimate = () => service.EstimateCommanderBracketAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        await estimate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Unable to fetch live Commander Game Changer data from Scryfall.");
    }

    /// <summary>
    /// Verifies that preview deck plan applies operations only to a clone.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_DoesNotMutateWorkspace()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Swap",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.RemoveCard,
                    CardName = "Mana Crypt",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Before.Cost.IncludedTotal.Should().Be(180);
        preview.After.Cost.IncludedTotal.Should().Be(1);
        workspaces.Workspaces[workspace.Id].Cards.Should().ContainSingle().Which.Name.Should().Be("Mana Crypt");
    }

    /// <summary>
    /// Verifies that preview deck plan applies card-category operations on the clone.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_AppliesCardCategoryOperations()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview Categories",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Move to maybeboard",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCardCategory,
                    CardName = "Mana Crypt",
                    Category = DeckDefaults.Maybeboard
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.SetPrimaryCardCategory,
                    CardName = "Mana Crypt",
                    Category = DeckDefaults.Maybeboard
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.RemoveCardCategory,
                    CardName = "Mana Crypt",
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Warnings.Should().BeEmpty();
        preview.After.Cost.IncludedTotal.Should().Be(0);
        preview.After.Cost.MaybeboardTotal.Should().Be(180);
        DeckCard original = workspaces.Workspaces[workspace.Id].Cards.Single();
        original.PrimaryCategory.Should().Be(DeckRoles.Ramp);
        original.Categories.Should().NotContain(DeckDefaults.Maybeboard);
    }

    /// <summary>
    /// Verifies that preview and apply agree for delete-category replacement.
    /// </summary>
    [Fact]
    public async Task PreviewAndApplyDeckPlan_DeleteCategoryReplacementAgree()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Delete Category Preview",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true, IncludedInPrice = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false, IncludedInPrice = true },
            ],
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Move ramp to sideboard",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.DeleteCategory,
                    Category = DeckRoles.Ramp,
                    ToCategory = DeckDefaults.Sideboard
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);
        DeckEditPlanApplyResult applied = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: false,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        preview.Warnings.Should().BeEmpty();
        preview.After.Cost.IncludedTotal.Should().Be(0);
        preview.After.Analysis.IncludedCards.Should().Be(0);
        DeckCard appliedCard = applied.Workspace.Cards.Single();
        appliedCard.PrimaryCategory.Should().Be(DeckDefaults.Sideboard);
        appliedCard.Categories.Should().Equal(DeckDefaults.Sideboard);
        DeckAnalyzer.Analyze(applied.Workspace).IncludedCards.Should().Be(preview.After.Analysis.IncludedCards);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);
        opened.Cards.Single().PrimaryCategory.Should().Be(DeckDefaults.Sideboard);
    }

    /// <summary>
    /// Verifies that bracket reduction creates a persisted non-mutating plan.
    /// </summary>
    [Fact]
    public async Task FindBracketReductionCandidates_CreatesPersistedPlanWithoutMutatingDeck()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Bracket Plan",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBracketReductionCandidatesAsync(
            workspace.Id,
            targetBracket: 2,
            limit: 5,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Mana Crypt");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Arcane Signet");
        workspaces.Workspaces[workspace.Id].Cards.Single().Name.Should().Be("Mana Crypt");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that consistency improvements create add-card plans.
    /// </summary>
    [Fact]
    public async Task FindConsistencyImprovements_CreatesAddPlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Consistency",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindConsistencyImprovementsAsync(
            workspace.Id,
            focus: "balanced",
            maxPrice: 10,
            limit: 3,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Arcane Signet");
        workspaces.Workspaces[workspace.Id].Cards.Should().ContainSingle().Which.Name.Should().Be("Swamp");
    }

    /// <summary>
    /// Verifies that card-selection consistency does not fall back to unrelated legal cards.
    /// </summary>
    [Fact]
    public async Task FindConsistencyImprovements_CardSelectionAddsMatchingCard()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Selection",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckWorkspaceService service = new(workspaces, catalog, archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindConsistencyImprovementsAsync(
            workspace.Id,
            focus: "selection",
            maxPrice: 10,
            limit: 3,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Opt");
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Lightning Greaves");
        catalog.SearchQueries.Should().Contain(query => query.Contains("scry", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that mana base improvements can replace tapped lands.
    /// </summary>
    [Fact]
    public async Task FindManaBaseImprovements_ReplacesTappedLands()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Mana Plan",
            Cards =
            [
                new DeckCard
                {
                    Name = "Temple of Deceit",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        OracleText = "Temple of Deceit enters the battlefield tapped.",
                        ProducedMana = ["U", "B"],
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                    }
                },
                new DeckCard
                {
                    Name = "Barren Moor",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        OracleText = "Barren Moor enters the battlefield tapped.",
                        ProducedMana = ["B"],
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindManaBaseImprovementsAsync(
            workspace.Id,
            maxPrice: 10,
            limit: 3,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Command Tower");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Temple of Deceit");
        result.Plan.Operations.Count(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Command Tower").Should().Be(1);
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Temple of Silence");
    }

    /// <summary>
    /// Verifies that apply deck plan applies local mutations.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_LocalPlan_AppliesThroughMutationPath()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace { Name = "Apply" }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Add card",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Sol Ring",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.AppliedOperations.Should().Be(1);
        result.Workspace.Cards.Single().Name.Should().Be("Sol Ring");
        result.Persistence.Should().Be(DeckPersistence.LocalOnly);

        Func<Task> reapply = () => service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);
        await reapply.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been applied*");
    }

    /// <summary>
    /// Verifies that apply deck plan handles local category quantity and metadata operations.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_LocalPlan_AppliesCategoryQuantityAndMetadataOperations()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Apply Many",
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add {C}{C}." }
                },
                new DeckCard
                {
                    Name = "Lightning Bolt",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Instant", OracleText = "Deal 3 damage." }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Many edits",
            Operations =
            [
                new DeckEditOperation { Operation = DeckEditOperations.CreateCategory, Category = DeckRoles.Ramp, IncludedInDeck = true, IncludedInPrice = true },
                new DeckEditOperation { Operation = DeckEditOperations.MoveCard, CardName = "Sol Ring", FromCategory = DeckDefaults.Mainboard, ToCategory = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.AddCardCategory, CardName = "Sol Ring", Category = "Testing" },
                new DeckEditOperation { Operation = DeckEditOperations.SetPrimaryCardCategory, CardName = "Sol Ring", Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.RemoveCardCategory, CardName = "Sol Ring", Category = "Testing" },
                new DeckEditOperation { Operation = DeckEditOperations.SetCardQuantity, CardName = "Sol Ring", Quantity = 2, Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.UpdateDeckMetadata, Name = "Updated", Format = "commander", Description = "Edited" },
                new DeckEditOperation { Operation = DeckEditOperations.RenameCategory, FromCategory = DeckRoles.Ramp, ToCategory = "Mana" },
                new DeckEditOperation { Operation = DeckEditOperations.DeleteCategory, Category = "Mana", ToCategory = DeckDefaults.Mainboard },
                new DeckEditOperation { Operation = DeckEditOperations.RemoveCard, CardName = "Lightning Bolt", Quantity = 1, Category = DeckDefaults.Mainboard }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Workspace.Name.Should().Be("Updated");
        result.Workspace.Description.Should().Be("Edited");
        result.Workspace.Cards.Should().ContainSingle(card => card.Name == "Sol Ring" && card.Quantity == 2);
        result.Workspace.Cards.Should().NotContain(card => card.Name == "Lightning Bolt");
        result.Workspace.Categories.Should().NotContain(category => category.Name == "Mana");
    }

    /// <summary>
    /// Verifies that Archidekt writeback plans require and create checkpoints.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_ArchidektWritebackRequiresAndCreatesCheckpointForMultiEditPlans()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        FakeArchidektGateway archidekt = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Remote",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        }, TestContext.Current.CancellationToken);
        archidekt.ImportedDeck = workspace;
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Remote edits",
            Operations =
            [
                new DeckEditOperation { Operation = DeckEditOperations.AddCard, CardName = "Sol Ring", Quantity = 1, Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.AddCard, CardName = "Arcane Signet", Quantity = 1, Category = DeckRoles.Ramp }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidekt, plans);

        Func<Task> withoutCheckpoint = () => service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: false,
            checkpointName: null,
            TestContext.Current.CancellationToken);
        await withoutCheckpoint.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires a checkpoint*");

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: "Before remote edits",
            TestContext.Current.CancellationToken);

        result.CheckpointId.Should().Be("checkpoint-1");
        archidekt.CreatedCheckpoints.Should().ContainSingle().Which.Should().Be("Before remote edits");
    }

    /// <summary>
    /// Verifies that json deck plan repository saves lists gets and deletes plans.
    /// </summary>
    [Fact]
    public async Task JsonDeckPlanRepository_SavesListsGetsAndDeletesPlans()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), $"mtg-mcp-plans-{Guid.NewGuid():N}");
        try
        {
            JsonDeckPlanRepository repository = new(dataDirectory);
            DeckEditPlan saved = await repository.SaveAsync(new DeckEditPlan
            {
                WorkspaceId = "workspace-1",
                Name = "Plan"
            }, TestContext.Current.CancellationToken);

            (await repository.GetAsync(saved.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
            IReadOnlyList<DeckEditPlan> listed = await repository.ListAsync("workspace-1", TestContext.Current.CancellationToken);
            listed.Should().ContainSingle(plan => plan.PlanId == saved.PlanId);

            await repository.DeleteAsync(saved.PlanId, TestContext.Current.CancellationToken);
            (await repository.GetAsync(saved.PlanId, TestContext.Current.CancellationToken)).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Creates a deck card fixture.
    /// </summary>
    private static DeckCard Card(string name, string typeLine, string oracleText)
    {
        return new DeckCard
        {
            Name = name,
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                OracleText = oracleText
            }
        };
    }

    /// <summary>
    /// Reads a file from the repository root.
    /// </summary>
    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }

    /// <summary>
    /// Creates an expensive ramp fixture.
    /// </summary>
    private static DeckCard ExpensiveRamp()
    {
        return new DeckCard
        {
            Name = "Mana Crypt",
            Quantity = 1,
            PrimaryCategory = DeckRoles.Ramp,
            Categories = [DeckRoles.Ramp],
            Snapshot = new CardSnapshot
            {
                TypeLine = "Artifact",
                OracleText = "{T}: Add two colorless mana.",
                ManaValue = 0,
                EdhrecRank = 20,
                Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["usd"] = "180"
                }
            }
        };
    }

    /// <summary>
    /// Verifies that a Quill description still contains rich content.
    /// </summary>
    private static void AssertRichQuillContent(string description)
    {
        using JsonDocument document = JsonDocument.Parse(description);
        JsonElement ops = document.RootElement.GetProperty("ops");

        ops.GetArrayLength().Should().BeGreaterThan(3);
        ops.EnumerateArray().Any(HasBoldAttribute).Should().BeTrue();
        ops.EnumerateArray().Any(HasItalicAttribute).Should().BeTrue();
        ops.EnumerateArray().Any(HasImageInsert).Should().BeTrue();
    }

    /// <summary>
    /// Checks whether an op has a bold attribute.
    /// </summary>
    private static bool HasBoldAttribute(JsonElement op)
    {
        return op.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("bold", out JsonElement bold)
            && bold.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Checks whether an op has an italic attribute.
    /// </summary>
    private static bool HasItalicAttribute(JsonElement op)
    {
        return op.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("italic", out JsonElement italic)
            && italic.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Checks whether an op has an image insert.
    /// </summary>
    private static bool HasImageInsert(JsonElement op)
    {
        return op.TryGetProperty("insert", out JsonElement insert)
            && insert.ValueKind == JsonValueKind.Object
            && insert.TryGetProperty("image", out JsonElement image)
            && image.GetString() == "https://example.test/card.jpg";
    }

    /// <summary>
    /// Provides fake card catalog behavior.
    /// </summary>
    /// <summary>
    /// Provides card data for budget filtering tests.
    /// </summary>
    private sealed class GoalBudgetCatalog : ICardCatalog
    {
        /// <summary>
        /// Searches budget goal candidates.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results =
            [
                new CardSearchResult { Name = "Mystery Table Spell" },
                new CardSearchResult { Name = "Syphon Mind" }
            ];
            return Task.FromResult(results);
        }

        /// <summary>
        /// Gets a budget goal card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets budget goal cards by name.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, CreateCard, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets no fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets no fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests no fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a budget test card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            return name.Equals("Syphon Mind", StringComparison.OrdinalIgnoreCase)
                ? new CardInfo
                {
                    Name = "Syphon Mind",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Sorcery",
                    OracleText = "Each opponent discards a card. You draw a card for each card discarded this way.",
                    ColorIdentity = ["B"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                }
                : new CardInfo
                {
                    Name = "Mystery Table Spell",
                    ManaCost = "{2}{B}",
                    ManaValue = 3,
                    TypeLine = "Sorcery",
                    OracleText = "Each opponent loses 2 life. Each opponent sacrifices a creature.",
                    ColorIdentity = ["B"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" }
                };
        }
    }

    /// <summary>
    /// Provides card data for release metadata tests.
    /// </summary>
    private sealed class TrendMetadataCatalog : ICardCatalog
    {
        /// <summary>
        /// Searches recent cards with explicit print metadata.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results =
            [
                new CardSearchResult { Name = "Reprinted Drain", Set = "new", ReleasedAt = new DateOnly(2026, 2, 1) },
                new CardSearchResult { Name = "Unpriced New Card", Set = "new", ReleasedAt = new DateOnly(2026, 2, 1) }
            ];
            return Task.FromResult(results);
        }

        /// <summary>
        /// Gets a recent card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets recent cards by name.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, CreateCard, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets no fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets no fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests no fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a release metadata test card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            Dictionary<string, string> prices = name.Equals("Reprinted Drain", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new CardInfo
            {
                Name = name,
                ManaCost = "{1}{B}",
                ManaValue = 2,
                TypeLine = "Sorcery",
                OracleText = "Each opponent loses life and you create a token.",
                Set = "old",
                ReleasedAt = new DateOnly(2020, 1, 1),
                ColorIdentity = ["B"],
                Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                Prices = prices
            };
        }
    }

    /// <summary>
    /// Provides a failing trend provider.
    /// </summary>
    private sealed class ThrowingCardTrendProvider : ICardTrendProvider
    {
        /// <summary>
        /// Throws for trend lookup.
        /// </summary>
        public Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
            CardTrendQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("trend unavailable");
        }
    }

    /// <summary>
    /// Provides a failing Commander meta provider.
    /// </summary>
    private sealed class ThrowingCommanderMetaProvider : ICommanderMetaProvider
    {
        /// <summary>
        /// Throws for Commander meta lookup.
        /// </summary>
        public Task<CommanderMetaReport> GetCommanderMetaAsync(
            CommanderMetaQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("meta unavailable");
        }
    }

    /// <summary>
    /// Provides fixed trend suggestions.
    /// </summary>
    private sealed class FixedCardTrendProvider : ICardTrendProvider
    {
        /// <summary>
        /// Stores fixed trend suggestions.
        /// </summary>
        private readonly IReadOnlyList<NewCardSuggestion> suggestions;

        /// <summary>
        /// Creates a fixed trend provider.
        /// </summary>
        public FixedCardTrendProvider(IReadOnlyList<NewCardSuggestion> suggestions)
        {
            this.suggestions = suggestions;
        }

        /// <summary>
        /// Returns fixed trend suggestions.
        /// </summary>
        public Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
            CardTrendQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(suggestions);
        }
    }

    /// <summary>
    /// Provides a failing combo catalog.
    /// </summary>
    private sealed class ThrowingComboCatalog : IComboCatalog
    {
        /// <summary>
        /// Throws for combo lookup.
        /// </summary>
        public Task<DeckComboReport> FindCombosAsync(
            ComboCatalogQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("combo unavailable");
        }
    }

    /// <summary>
    /// Provides card data for deck intelligence tests.
    /// </summary>
    private sealed class FakeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Gets search queries sent to the fake catalog.
        /// </summary>
        public List<string> SearchQueries { get; } = [];

        /// <summary>
        /// Gets or sets whether Game Changer search throws.
        /// </summary>
        public bool ThrowOnGameChangerSearch { get; init; }

        /// <summary>
        /// Searches fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            SearchQueries.Add(query);
            IReadOnlyList<CardSearchResult> results;
            if (query.Contains("is:game-changer", StringComparison.OrdinalIgnoreCase))
            {
                if (ThrowOnGameChangerSearch)
                {
                    throw new HttpRequestException("Scryfall unavailable.");
                }

                results = [new CardSearchResult { Name = "Mana Crypt" }];
            }
            else if (query.Contains("t:land", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Temple of Silence" },
                    new CardSearchResult { Name = "Command Tower" }
                ];
            }
            else if (query.Contains("add", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Arcane Signet" }];
            }
            else if (query.Contains("scry", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Lightning Greaves" },
                    new CardSearchResult { Name = "Opt" }
                ];
            }
            else if (query.Contains("draw", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Rhystic Study" },
                    new CardSearchResult { Name = "Necropotence" },
                    new CardSearchResult { Name = "Phyrexian Arena" }
                ];
            }
            else if (query.Contains("destroy target", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Lightning Greaves" },
                    new CardSearchResult { Name = "Hero's Downfall" }
                ];
            }
            else if (query.Contains("goad", StringComparison.OrdinalIgnoreCase)
                || query.Contains("monarch", StringComparison.OrdinalIgnoreCase)
                || query.Contains("vote", StringComparison.OrdinalIgnoreCase)
                || query.Contains("tempting offer", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Court of Ambition" },
                    new CardSearchResult { Name = "Syphon Mind" }
                ];
            }
            else if (query.Contains("each opponent", StringComparison.OrdinalIgnoreCase)
                || query.Contains("each player", StringComparison.OrdinalIgnoreCase)
                || query.Contains("each creature", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Syphon Mind" },
                    new CardSearchResult { Name = "Blasphemous Act" }
                ];
            }
            else if (query.Contains("destroy all tokens", StringComparison.OrdinalIgnoreCase)
                || query.Contains("creatures can't attack", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Illness in the Ranks" },
                    new CardSearchResult { Name = "Crawlspace" }
                ];
            }
            else if (query.Contains("date>=", StringComparison.OrdinalIgnoreCase)
                || query.Contains("set:", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Season of Loss" }];
            }
            else if (query.Contains("legal:commander", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Lightning Greaves" }];
            }
            else
            {
                results = [];
            }

            return Task.FromResult(results);
        }

        /// <summary>
        /// Gets a fake card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets fake cards by names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            Dictionary<string, CardInfo> cards = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                cards[name] = CreateCard(name);
            }

            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(cards);
        }

        /// <summary>
        /// Gets fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a fake card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            return name switch
            {
                "Arcane Signet" => new CardInfo
                {
                    Id = "arcane-signet",
                    OracleId = "oracle-arcane-signet",
                    Name = "Arcane Signet",
                    ManaCost = "{2}",
                    ManaValue = 2,
                    TypeLine = "Artifact",
                    OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "U", "B", "R", "G"],
                    EdhrecRank = 5,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Phyrexian Arena" => new CardInfo
                {
                    Id = "phyrexian-arena",
                    OracleId = "oracle-phyrexian-arena",
                    Name = "Phyrexian Arena",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "At the beginning of your upkeep, you draw a card and you lose 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 250,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                        ["modern"] = "legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                },
                "Rhystic Study" => new CardInfo
                {
                    Id = "rhystic-study",
                    OracleId = "oracle-rhystic-study",
                    Name = "Rhystic Study",
                    ManaCost = "{2}{U}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.",
                    ColorIdentity = ["U"],
                    EdhrecRank = 20,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                        ["modern"] = "not_legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "40.00" }
                },
                "Necropotence" => new CardInfo
                {
                    Id = "necropotence",
                    OracleId = "oracle-necropotence",
                    Name = "Necropotence",
                    ManaCost = "{B}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "Skip your draw step. Pay 1 life: Exile the top card of your library face down.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 30,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "not_legal",
                        ["modern"] = "not_legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "25.00" }
                },
                "Lightning Greaves" => new CardInfo
                {
                    Id = "lightning-greaves",
                    OracleId = "oracle-lightning-greaves",
                    Name = "Lightning Greaves",
                    ManaCost = "{2}",
                    ManaValue = 2,
                    TypeLine = "Artifact — Equipment",
                    OracleText = "Equipped creature has haste and shroud. Equip {0}.",
                    ColorIdentity = [],
                    EdhrecRank = 40,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "6.00" }
                },
                "Hero's Downfall" => new CardInfo
                {
                    Id = "heros-downfall",
                    OracleId = "oracle-heros-downfall",
                    Name = "Hero's Downfall",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Instant",
                    OracleText = "Destroy target creature or planeswalker.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 3_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                },
                "Command Tower" => new CardInfo
                {
                    Id = "command-tower",
                    OracleId = "oracle-command-tower",
                    Name = "Command Tower",
                    TypeLine = "Land",
                    OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "U", "B", "R", "G"],
                    EdhrecRank = 10,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.50" }
                },
                "Temple of Silence" => new CardInfo
                {
                    Id = "temple-of-silence",
                    OracleId = "oracle-temple-of-silence",
                    Name = "Temple of Silence",
                    TypeLine = "Land",
                    OracleText = "Temple of Silence enters the battlefield tapped. When it enters, scry 1. {T}: Add {W} or {B}.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "B"],
                    EdhrecRank = 1,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.20" }
                },
                "Opt" => new CardInfo
                {
                    Id = "opt",
                    OracleId = "oracle-opt",
                    Name = "Opt",
                    ManaCost = "{U}",
                    ManaValue = 1,
                    TypeLine = "Instant",
                    OracleText = "Scry 1. Draw a card.",
                    ColorIdentity = ["U"],
                    EdhrecRank = 100,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.10" }
                },
                "Syphon Mind" => new CardInfo
                {
                    Id = "syphon-mind",
                    OracleId = "oracle-syphon-mind",
                    Name = "Syphon Mind",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Sorcery",
                    OracleText = "Each other player discards a card. You draw a card for each card discarded this way.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 2_500,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                },
                "Court of Ambition" => new CardInfo
                {
                    Id = "court-of-ambition",
                    OracleId = "oracle-court-of-ambition",
                    Name = "Court of Ambition",
                    ManaCost = "{2}{B}{B}",
                    ManaValue = 4,
                    TypeLine = "Enchantment",
                    OracleText = "When Court of Ambition enters the battlefield, you become the monarch. At the beginning of your upkeep, each opponent loses 3 life unless they discard a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 2_100,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.00" }
                },
                "Blasphemous Act" => new CardInfo
                {
                    Id = "blasphemous-act",
                    OracleId = "oracle-blasphemous-act",
                    Name = "Blasphemous Act",
                    ManaCost = "{8}{R}",
                    ManaValue = 9,
                    TypeLine = "Sorcery",
                    OracleText = "Blasphemous Act deals 13 damage to each creature.",
                    ColorIdentity = ["R"],
                    EdhrecRank = 300,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                },
                "Illness in the Ranks" => new CardInfo
                {
                    Id = "illness-in-the-ranks",
                    OracleId = "oracle-illness-in-the-ranks",
                    Name = "Illness in the Ranks",
                    ManaCost = "{B}",
                    ManaValue = 1,
                    TypeLine = "Enchantment",
                    OracleText = "Creature tokens get -1/-1.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 8_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Crawlspace" => new CardInfo
                {
                    Id = "crawlspace",
                    OracleId = "oracle-crawlspace",
                    Name = "Crawlspace",
                    ManaCost = "{3}",
                    ManaValue = 3,
                    TypeLine = "Artifact",
                    OracleText = "No more than two creatures can attack you each combat.",
                    ColorIdentity = [],
                    EdhrecRank = 2_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.00" }
                },
                "Season of Loss" => new CardInfo
                {
                    Id = "season-of-loss",
                    OracleId = "oracle-season-of-loss",
                    Name = "Season of Loss",
                    ManaCost = "{3}{B}{B}",
                    ManaValue = 5,
                    TypeLine = "Sorcery",
                    OracleText = "Choose modes. Each opponent sacrifices a creature. Create two tapped creature tokens. You draw two cards.",
                    Set = "tst",
                    ReleasedAt = new DateOnly(2026, 2, 1),
                    ColorIdentity = ["B"],
                    EdhrecRank = 1_500,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "2.00" }
                },
                _ => new CardInfo
                {
                    Id = name.ToLowerInvariant().Replace(' ', '-'),
                    OracleId = $"oracle-{name}",
                    Name = name,
                    ManaCost = "{1}",
                    ManaValue = name.Equals("Sol Ring", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    TypeLine = "Artifact",
                    OracleText = "{T}: Add {C}{C}.",
                    ColorIdentity = [],
                    ProducedMana = ["C"],
                    EdhrecRank = 1,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.25" }
                }
            };
        }
    }

    /// <summary>
    /// Provides fake Archidekt gateway behavior.
    /// </summary>
    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        /// <summary>
        /// Gets or sets the imported deck.
        /// </summary>
        public DeckWorkspace ImportedDeck { get; set; } = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        };

        /// <summary>
        /// Gets created checkpoints.
        /// </summary>
        public List<string> CreatedCheckpoints { get; } = [];

        /// <summary>
        /// Gets persisted metadata count.
        /// </summary>
        public int PersistedMetadataRequests { get; private set; }

        /// <summary>
        /// Gets fake auth status.
        /// </summary>
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasJwt = true });
        }

        /// <summary>
        /// Lists fake decks.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>([]);
        }

        /// <summary>
        /// Imports a fake deck.
        /// </summary>
        public Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken)
        {
            ImportedDeck.Mode = WorkspaceMode.Archidekt;
            ImportedDeck.WriteBack = writeBack;
            ImportedDeck.ArchidektDeckId = "123";
            return Task.FromResult(ImportedDeck);
        }

        /// <summary>
        /// Persists fake card changes.
        /// </summary>
        public Task PersistCardsAsync(
            DeckWorkspace workspace,
            IReadOnlyList<DeckCard> upsertedCards,
            IReadOnlyList<DeckCard> removedCards,
            CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Persists a fake category.
        /// </summary>
        public Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes a fake category.
        /// </summary>
        public Task DeleteCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Persists fake metadata.
        /// </summary>
        public Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            PersistedMetadataRequests++;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> CreateCheckpointAsync(
            DeckWorkspace workspace,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            CreatedCheckpoints.Add(name);
            return Task.FromResult(new DeckCheckpoint
            {
                Id = "checkpoint-1",
                DeckId = workspace.ArchidektDeckId ?? "",
                Name = name,
                Description = description
            });
        }

        /// <summary>
        /// Lists fake checkpoints.
        /// </summary>
        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([]);
        }

        /// <summary>
        /// Gets a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> GetCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = "Checkpoint" });
        }

        /// <summary>
        /// Renames a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> RenameCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = name, Description = description });
        }

        /// <summary>
        /// Deletes a fake checkpoint.
        /// </summary>
        public Task DeleteCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Provides in-memory workspace repository behavior.
    /// </summary>
    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Gets workspaces.
        /// </summary>
        public Dictionary<string, DeckWorkspace> Workspaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a workspace.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            Workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets a workspace.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            Workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Lists workspaces.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(Workspaces.Values.ToList());
        }
    }

    /// <summary>
    /// Provides in-memory plan repository behavior.
    /// </summary>
    private sealed class InMemoryPlanRepository : IDeckPlanRepository
    {
        /// <summary>
        /// Stores plans.
        /// </summary>
        private readonly Dictionary<string, DeckEditPlan> plans = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a plan.
        /// </summary>
        public Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
        {
            plans[plan.PlanId] = plan;
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Gets a plan.
        /// </summary>
        public Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
        {
            plans.TryGetValue(planId, out DeckEditPlan? plan);
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Lists plans.
        /// </summary>
        public Task<IReadOnlyList<DeckEditPlan>> ListAsync(string? workspaceId, CancellationToken cancellationToken)
        {
            IReadOnlyList<DeckEditPlan> result = plans.Values
                .Where(plan => string.IsNullOrWhiteSpace(workspaceId)
                    || plan.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(result);
        }

        /// <summary>
        /// Deletes a plan.
        /// </summary>
        public Task DeleteAsync(string planId, CancellationToken cancellationToken)
        {
            plans.Remove(planId);
            return Task.CompletedTask;
        }
    }
}
