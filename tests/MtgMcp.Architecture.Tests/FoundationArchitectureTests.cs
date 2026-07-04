using System.Text.Json;
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
    /// Verifies that App depends only on Core and the approved foundation hosting packages.
    /// </summary>
    [Fact]
    public void AppProject_ReferencesOnlyApprovedFoundationDependencies()
    {
        XDocument project = LoadProject("src/MtgMcp.App/MtgMcp.App.csproj");
        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .ToArray();
        string[] packages = project
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["../MtgMcp.Core/MtgMcp.Core.csproj"], references);
        Assert.Equal(
            [
                "Microsoft.Extensions.Configuration.CommandLine",
                "Microsoft.Extensions.Configuration.EnvironmentVariables",
                "Microsoft.Extensions.Configuration.Json",
                "Microsoft.Extensions.Hosting",
                "ModelContextProtocol",
            ],
            packages);
    }

    /// <summary>
    /// Verifies that E2E tests use only the official MCP Core client package.
    /// </summary>
    [Fact]
    public void E2eProject_ReferencesOnlyApprovedClientDependency()
    {
        XDocument project = LoadProject("tests/MtgMcp.E2E.Tests/MtgMcp.E2E.Tests.csproj");
        string[] packages = project
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Microsoft.NET.Test.Sdk",
                "ModelContextProtocol.Core",
                "xunit.runner.visualstudio",
                "xunit.v3",
            ],
            packages);
    }

    /// <summary>
    /// Verifies the application project is the sole authored default package-version source.
    /// </summary>
    [Fact]
    public void Versioning_UsesOnlyApplicationProjectDefault()
    {
        XDocument project = LoadProject("src/MtgMcp.App/MtgMcp.App.csproj");
        string version = Assert.Single(project.Descendants("Version")).Value;
        string taskfile = File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml"));
        string scripts = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(RepositoryRoot, "scripts"), "*.ps1")
                .Select(File.ReadAllText));
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot, "server.json")));

        Assert.Equal("0.9.0-preview.1", version);
        Assert.Empty(project.Descendants("PackageVersion"));
        Assert.Contains("VERSION: ''", taskfile, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PackageVersion", taskfile, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PackageVersion", scripts, StringComparison.Ordinal);
        Assert.Equal(version, manifest.RootElement.GetProperty("version").GetString());
        Assert.Equal(
            version,
            Assert.Single(manifest.RootElement.GetProperty("packages").EnumerateArray())
                .GetProperty("version")
                .GetString());
    }

    /// <summary>
    /// Verifies the exact one-resource, zero-tool, zero-prompt foundation surface.
    /// </summary>
    [Fact]
    public void SourceSurface_ContainsOnlyApprovedCapabilityResource()
    {
        string sourceRoot = Path.Combine(RepositoryRoot, "src");
        string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsAuthoredSource)
            .ToArray();
        string source = string.Join(Environment.NewLine, sourceFiles.Select(File.ReadAllText));

        Assert.Equal(1, Regex.Count(source, @"\[McpServerResource\("));
        Assert.Equal(1, Regex.Count(source, @"\.WithResources\("));
        Assert.Contains("mtg://server/capabilities", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"Server Capabilities\"", source, StringComparison.Ordinal);
        Assert.Contains("MimeType = \"application/json\"", source, StringComparison.Ordinal);

        string[] forbiddenMarkers =
        [
            "McpServerTool",
            "McpServerPrompt",
            "WithToolsFromAssembly",
            "WithPromptsFromAssembly",
            "WithResourcesFromAssembly",
            "WithTools(",
            "WithPrompts(",
        ];
        foreach (string marker in forbiddenMarkers)
        {
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
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
            "MtgMcp.Core.Tests",
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
