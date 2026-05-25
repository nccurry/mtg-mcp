using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck intent parsing, formatting, and workspace persistence tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
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
        DeckWorkspaceService service = CreateWorkspaceService(workspaces, new FakeCardCatalog());
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
        DeckWorkspaceService service = CreateWorkspaceService(workspaces, new FakeCardCatalog());

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
        DeckWorkspaceService service = CreateWorkspaceService(workspaces, new FakeCardCatalog());
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
        DeckWorkspaceService service = CreateWorkspaceService(workspaces, new FakeCardCatalog());
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
        DeckWorkspaceService service = CreateWorkspaceService(workspaces, new FakeCardCatalog(), archidekt);
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
    /// Verifies that Archidekt intent writes use the rich editor shape even when the original description is plain.
    /// </summary>
    [Fact]
    public async Task SetDeckIntent_ArchidektWritebackConvertsPlainDescriptionToQuill()
    {
        InMemoryRepository workspaces = new();
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Name = "Plain Remote Intent",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
                Description = ""
            }
        };
        DeckWorkspaceService service = CreateWorkspaceService(workspaces, new FakeCardCatalog(), archidekt);
        DeckWorkspace workspace = await service.OpenArchidektDeckAsync(
            "123",
            writeBack: true,
            TestContext.Current.CancellationToken);

        DeckIntentChangeResult result = await service.SetDeckIntentAsync(
            workspace.Id,
            "Archetype: blink-combo",
            TestContext.Current.CancellationToken);

        result.Persistence.Should().Be(DeckPersistence.ArchidektWriteBack);
        archidekt.PersistedMetadataRequests.Should().Be(1);
        archidekt.ImportedDeck.Description.Should().Contain("\"ops\"");
        DeckIntentText.ToPlainText(archidekt.ImportedDeck.Description).Should().Contain("blink-combo");
    }
}
