using System.Xml.Linq;
using FluentAssertions;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Contains tests for project boundary.
/// </summary>
public sealed class ProjectBoundaryTests
{
    /// <summary>
    /// Verifies that source projects respect reference boundaries.
    /// </summary>
    [Fact]
    public void SourceProjects_RespectReferenceBoundaries()
    {
        string root = FindRepoRoot();
        Dictionary<string, string[]> expectedReferences = new(StringComparer.OrdinalIgnoreCase)
        {
            ["src/MtgMcp.Core/MtgMcp.Core.csproj"] = [],
            ["src/MtgMcp.Scryfall/MtgMcp.Scryfall.csproj"] = ["src/MtgMcp.Core/MtgMcp.Core.csproj"],
            ["src/MtgMcp.CommanderSpellbook/MtgMcp.CommanderSpellbook.csproj"] =
            [
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
            ],
            ["src/MtgMcp.Archidekt/MtgMcp.Archidekt.csproj"] =
            [
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
            ],
            ["src/MtgMcp.Decklists/MtgMcp.Decklists.csproj"] =
            [
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
            ],
            ["src/MtgMcp.App/MtgMcp.App.csproj"] =
            [
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
                "src/MtgMcp.Scryfall/MtgMcp.Scryfall.csproj",
                "src/MtgMcp.Archidekt/MtgMcp.Archidekt.csproj",
                "src/MtgMcp.CommanderSpellbook/MtgMcp.CommanderSpellbook.csproj",
                "src/MtgMcp.Decklists/MtgMcp.Decklists.csproj",
            ],
        };

        foreach (KeyValuePair<string, string[]> project in expectedReferences)
        {
            string projectPath = Path.Combine(root, project.Key);
            IReadOnlyList<string> references = ReadProjectReferences(root, projectPath);
            references
                .Should()
                .BeEquivalentTo(
                    project.Value,
                    because: $"{project.Key} should only reference its allowed dependencies"
                );
        }
    }

    /// <summary>
    /// Verifies that core project has no third party packages.
    /// </summary>
    [Fact]
    public void CoreProject_HasNoThirdPartyPackages()
    {
        string root = FindRepoRoot();
        string coreProjectPath = Path.Combine(root, "src/MtgMcp.Core/MtgMcp.Core.csproj");
        XDocument document = XDocument.Load(coreProjectPath);

        document.Descendants("PackageReference").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that deck-heavy Core and App code stays in feature folders.
    /// </summary>
    [Fact]
    public void DeckFeatureFiles_StayInFeatureFolders()
    {
        string root = FindRepoRoot();
        string[] expectedFolders =
        [
            "src/MtgMcp.Core/Workspaces",
            "src/MtgMcp.Core/Analysis",
            "src/MtgMcp.Core/Recommendations",
            "src/MtgMcp.Core/Plans",
            "src/MtgMcp.Core/Intent",
            "src/MtgMcp.Core/Simulation",
            "src/MtgMcp.Core/Models",
            "src/MtgMcp.App/Tools",
            "src/MtgMcp.App/Hosting",
            "src/MtgMcp.App/Cli",
        ];
        string[] removedOmnibusFiles =
        [
            "src/MtgMcp.Core/Models.cs",
            "src/MtgMcp.Core/IntelligenceModels.cs",
            "src/MtgMcp.Core/DeckbuildingModels.cs",
            "src/MtgMcp.Core/DeckWorkspaceService.Intelligence.cs",
            "src/MtgMcp.Core/DeckWorkspaceService.WorkflowPrimitives.cs",
            "src/MtgMcp.Core/Workspaces/DeckWorkspaceService.LegacyFacade.cs",
            "src/MtgMcp.App/IntelligenceTools.cs",
            "tests/MtgMcp.Core.Tests/DeckIntelligenceTests.cs",
            "tests/MtgMcp.Core.Tests/Recommendations/DeckIntelligenceTests.cs",
        ];
        string[] expectedTestFolders =
        [
            "tests/MtgMcp.Core.Tests/Analysis",
            "tests/MtgMcp.Core.Tests/Intent",
            "tests/MtgMcp.Core.Tests/Recommendations",
            "tests/MtgMcp.Core.Tests/Plans",
            "tests/MtgMcp.Core.Tests/Simulation",
            "tests/MtgMcp.Core.Tests/Support",
            "tests/MtgMcp.Core.Tests/Workspaces",
        ];

        expectedFolders
            .Select(path => Directory.Exists(Path.Combine(root, path)))
            .Should()
            .OnlyContain(exists => exists);
        expectedTestFolders
            .Select(path => Directory.Exists(Path.Combine(root, path)))
            .Should()
            .OnlyContain(exists => exists);
        removedOmnibusFiles
            .Select(path => File.Exists(Path.Combine(root, path)))
            .Should()
            .OnlyContain(exists => !exists);
    }

    /// <summary>
    /// Verifies that workspace service files do not regain delegated feature-service tools.
    /// </summary>
    [Fact]
    public void DeckWorkspaceService_DoesNotRecreateFeatureFacade()
    {
        string root = FindRepoRoot();
        string workspaceServiceDirectory = Path.Combine(root, "src/MtgMcp.Core/Workspaces");
        string[] delegatedFeatureMethods =
        [
            "AnalyzeDeckCostAsync",
            "RefreshDeckCardSnapshotsAsync",
            "SummarizeDeckWorkspaceAsync",
            "FindCardUpgradesAsync",
            "PreviewDeckPlanAsync",
            "ApplyDeckPlanAsync",
            "SimulateGoldfishAsync",
            "EstimateWinTurnAsync",
        ];
        string combinedText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(workspaceServiceDirectory, "DeckWorkspaceService*.cs")
                .Select(File.ReadAllText));

        delegatedFeatureMethods
            .Where(method => combinedText.Contains(method, StringComparison.Ordinal))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// Verifies that old tool-facing names do not linger in source or tests.
    /// </summary>
    [Fact]
    public void RenamedToolConcepts_UseCurrentNames()
    {
        string root = FindRepoRoot();
        string[] staleNames =
        [
            "NormalizeDeckCardsAsync",
            "SummarizeDeckPlanAsync",
            "FindPowerUpgradesAsync",
            "find_power_upgrades",
            "power-upgrades",
        ];
        string[] files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("ProjectBoundaryTests.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string combinedText = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        staleNames
            .Where(name => combinedText.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// Verifies that read project references.
    /// </summary>
    private static IReadOnlyList<string> ReadProjectReferences(string root, string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? root;
        List<string> references = [];

        foreach (XElement element in document.Descendants("ProjectReference"))
        {
            string include = (element.Attribute("Include")?.Value ?? "")
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            string absolute = Path.GetFullPath(Path.Combine(projectDirectory, include));
            string relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
            references.Add(relative);
        }

        return references;
    }

    /// <summary>
    /// Verifies that find repo root.
    /// </summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "mtg-mcp.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
