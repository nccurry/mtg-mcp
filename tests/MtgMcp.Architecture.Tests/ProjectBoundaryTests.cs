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
            ["src/MtgMcp.App/MtgMcp.App.csproj"] =
            [
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
                "src/MtgMcp.Scryfall/MtgMcp.Scryfall.csproj",
                "src/MtgMcp.Archidekt/MtgMcp.Archidekt.csproj",
                "src/MtgMcp.CommanderSpellbook/MtgMcp.CommanderSpellbook.csproj",
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
