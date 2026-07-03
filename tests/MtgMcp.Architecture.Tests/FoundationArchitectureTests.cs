using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Protects the minimal project and dependency boundaries required by the rewrite foundation.
/// </summary>
public sealed class FoundationArchitectureTests
{
    /// <summary>
    /// Provides the repository root used by project-file assertions.
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// Verifies that only the two approved production projects remain.
    /// </summary>
    [Fact]
    public void ProductionProjects_ContainOnlyCoreAndApp()
    {
        string sourceRoot = Path.Combine(RepositoryRoot, "src");
        string[] projects = Directory
            .GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(ToRepositoryPath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "src/MtgMcp.App/MtgMcp.App.csproj",
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
            ],
            projects);
    }

    /// <summary>
    /// Verifies that Core is BCL-only and independent of App.
    /// </summary>
    [Fact]
    public void CoreProject_HasNoRuntimePackagesOrProjectReferences()
    {
        XDocument project = LoadProject("src/MtgMcp.Core/MtgMcp.Core.csproj");

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
    }

    /// <summary>
    /// Verifies that App depends only on the approved Core project during foundation construction.
    /// </summary>
    [Fact]
    public void AppProject_ReferencesOnlyCoreAndNoRuntimePackages()
    {
        XDocument project = LoadProject("src/MtgMcp.App/MtgMcp.App.csproj");
        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .ToArray();

        Assert.Equal(["../MtgMcp.Core/MtgMcp.Core.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));
    }

    /// <summary>
    /// Verifies that the temporary foundation contains no legacy MCP registration attributes.
    /// </summary>
    [Fact]
    public void SourceSurface_ContainsNoLegacyMcpRegistrations()
    {
        string[] forbiddenMarkers =
        [
            "McpServerTool",
            "McpServerResource",
            "McpServerPrompt",
        ];

        string[] sourceFiles = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src"),
            "*.cs",
            SearchOption.AllDirectories)
            .Where(IsAuthoredSource)
            .ToArray();

        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            foreach (string marker in forbiddenMarkers)
            {
                Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Verifies that repository automation names only projects present in the foundation solution.
    /// </summary>
    [Fact]
    public void RepositoryWiring_UsesOnlyFoundationProjects()
    {
        string[] wiringFiles =
        [
            ".github/workflows/ci.yml",
            "CodeCoverage.runsettings",
            "Taskfile.yml",
            "mtg-mcp.slnx",
            "scripts/coverage.ps1",
        ];
        HashSet<string> allowedProjects =
        [
            "MtgMcp.App",
            "MtgMcp.App.Tests",
            "MtgMcp.Architecture.Tests",
            "MtgMcp.Core",
            "MtgMcp.E2E.Tests",
        ];

        foreach (string wiringFile in wiringFiles)
        {
            string contents = File.ReadAllText(Path.Combine(RepositoryRoot, wiringFile));
            MatchCollection projectNames = Regex.Matches(
                contents,
                @"MtgMcp\.[A-Za-z0-9]+(?:\.Tests)?",
                RegexOptions.CultureInvariant);
            foreach (Match projectName in projectNames)
            {
                Assert.Contains(projectName.Value, allowedProjects);
            }

            MatchCollection projectPaths = Regex.Matches(
                contents,
                @"(?:src|tests)[/\\][^\s\""']+\.csproj",
                RegexOptions.CultureInvariant);
            foreach (Match projectPath in projectPaths)
            {
                string normalizedPath = projectPath.Value.Replace('\\', Path.DirectorySeparatorChar);
                Assert.True(
                    File.Exists(Path.Combine(RepositoryRoot, normalizedPath)),
                    $"Repository wiring references missing project '{projectPath.Value}'.");
            }
        }
    }

    /// <summary>
    /// Loads an SDK project from a repository-relative path.
    /// </summary>
    private static XDocument LoadProject(string relativePath)
    {
        return XDocument.Load(Path.Combine(RepositoryRoot, relativePath));
    }

    /// <summary>
    /// Converts an absolute repository file path into a stable slash-separated path.
    /// </summary>
    private static string ToRepositoryPath(string path)
    {
        return Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
    }

    /// <summary>
    /// Reports whether a source file is authored rather than generated build output.
    /// </summary>
    private static bool IsAuthoredSource(string path)
    {
        string relativePath = ToRepositoryPath(path);
        return !relativePath.Contains("/bin/", StringComparison.Ordinal) &&
            !relativePath.Contains("/obj/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds the repository root from the test output directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mtg-mcp repository root.");
    }
}
