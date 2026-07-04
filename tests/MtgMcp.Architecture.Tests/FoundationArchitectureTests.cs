using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Protects the project, dependency, and public-surface boundaries of the evidence-first rewrite.
/// </summary>
public sealed class FoundationArchitectureTests
{
    /// <summary>
    /// Provides the repository root used by project-file assertions.
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// Verifies that only the four currently approved production projects exist.
    /// </summary>
    [Fact]
    public void ProductionProjects_ContainOnlyCoreDecksScryfallAndApp()
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
                "src/MtgMcp.Decks/MtgMcp.Decks.csproj",
                "src/MtgMcp.Scryfall/MtgMcp.Scryfall.csproj",
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
    /// Verifies that App depends only on Core, Decks, Scryfall, and the approved hosting packages.
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

        Assert.Equal(
            ["../MtgMcp.Core/MtgMcp.Core.csproj", "../MtgMcp.Decks/MtgMcp.Decks.csproj", "../MtgMcp.Scryfall/MtgMcp.Scryfall.csproj"],
            references);
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
    /// Verifies Decks depends only on Core and the approved SQLite packages.
    /// </summary>
    [Fact]
    public void DecksProject_ReferencesOnlyCoreAndSqlite()
    {
        XDocument project = LoadProject("src/MtgMcp.Decks/MtgMcp.Decks.csproj");
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
        Assert.Equal(["Microsoft.Data.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3"], packages);
    }

    /// <summary>
    /// Verifies Scryfall owns HTTP and SQLite while depending only on provider-neutral Core contracts.
    /// </summary>
    [Fact]
    public void ScryfallProject_ReferencesOnlyCoreAndSqlite()
    {
        XDocument project = LoadProject("src/MtgMcp.Scryfall/MtgMcp.Scryfall.csproj");
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
        Assert.Equal(["Microsoft.Data.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3"], packages);
    }

    /// <summary>
    /// Verifies manual interchange cannot grow an implicit provider or arbitrary filesystem path boundary.
    /// </summary>
    [Fact]
    public void ManualInterchange_ContainsNoNetworkClientOrCallerPathWrites()
    {
        string deckSourceRoot = Path.Combine(RepositoryRoot, "src", "MtgMcp.Decks");
        string interchangeSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(deckSourceRoot, "*Interchange*.cs").Select(File.ReadAllText));
        string artifactWriter = File.ReadAllText(Path.Combine(deckSourceRoot, "DeckArtifactWriter.cs"));
        string parser = File.ReadAllText(Path.Combine(deckSourceRoot, "DeckTextParser.cs"));
        string combined = string.Join(Environment.NewLine, interchangeSource, artifactWriter, parser);

        Assert.DoesNotContain("HttpClient", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Write", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Create", combined, StringComparison.Ordinal);
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
    /// Verifies the exact one-resource, forty-one-tool, zero-prompt toolset-owned surface.
    /// </summary>
    [Fact]
    public void SourceSurface_ContainsOnlyApprovedCapabilityToolsAndResource()
    {
        string sourceRoot = Path.Combine(RepositoryRoot, "src");
        string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsAuthoredSource)
            .ToArray();
        string source = string.Join(Environment.NewLine, sourceFiles.Select(File.ReadAllText));
        string hostSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Hosting", "FoundationHost.cs"));
        string deckManifestSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Decks", "DeckToolsetManifest.cs"));
        string scryfallManifestSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Scryfall", "ScryfallToolsetManifest.cs"));

        Assert.Equal(1, Regex.Count(source, @"\[McpServerResource\("));
        Assert.Equal(41, Regex.Count(source, @"\[McpServerTool\("));
        Assert.Equal(1, Regex.Count(source, @"\.WithResources\("));
        Assert.Equal(6, Regex.Count(source, @"\.WithTools\("));
        Assert.Contains("mtg://server/capabilities", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"Server Capabilities\"", source, StringComparison.Ordinal);
        Assert.Contains("MimeType = \"application/json\"", source, StringComparison.Ordinal);
        string[] toolNames = Regex.Matches(source, "McpServerTool\\(\\s*Name = \\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "deck_apply_changes",
                "deck_backup_create",
                "deck_backup_delete",
                "deck_backup_list",
                "deck_backup_restore",
                "deck_category_assign",
                "deck_category_create",
                "deck_category_delete",
                "deck_category_unassign",
                "deck_category_update",
                "deck_create",
                "deck_delete",
                "deck_entry_add",
                "deck_entry_remove",
                "deck_entry_update",
                "deck_export_bundle",
                "deck_get",
                "deck_import_create",
                "deck_import_preview",
                "deck_interchange_formats",
                "deck_list",
                "deck_update",
                "deck_validate",
                "scryfall_autocomplete",
                "scryfall_bulk_metadata",
                "scryfall_card_collection",
                "scryfall_card_get",
                "scryfall_card_prints",
                "scryfall_card_rulings",
                "scryfall_cards_by_tag",
                "scryfall_catalog",
                "scryfall_corpus_delete",
                "scryfall_corpus_rollback",
                "scryfall_corpus_status",
                "scryfall_corpus_sync",
                "scryfall_search",
                "scryfall_sets",
                "scryfall_snapshot_delete",
                "scryfall_snapshot_get",
                "scryfall_snapshot_list",
                "scryfall_tag_search",
            ],
            toolNames);
        string manifestSource = string.Join(Environment.NewLine, deckManifestSource, scryfallManifestSource);
        string[] assignedToolNames = Regex.Matches(manifestSource, "\\\"((?:deck|scryfall)_[^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(toolNames, assignedToolNames);
        Assert.Equal(assignedToolNames.Length, assignedToolNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(".WithTools(", hostSource, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Count(deckManifestSource, @"\.WithTools\("));
        Assert.Equal(2, Regex.Count(scryfallManifestSource, @"\.WithTools\("));
        Assert.DoesNotContain("FoundationModuleStatus", source, StringComparison.Ordinal);
        Assert.Contains("tools.Remove(\"listChanged\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendToolListChanged", source, StringComparison.Ordinal);

        string[] forbiddenMarkers =
        [
            "McpServerPrompt",
            "WithToolsFromAssembly",
            "WithPromptsFromAssembly",
            "WithResourcesFromAssembly",
            "WithPrompts(",
        ];
        foreach (string marker in forbiddenMarkers)
        {
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("tagger_", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MtgMcp.Tagger", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies capability toolsets remain an App composition concern with no Core or Decks dependency.
    /// </summary>
    [Fact]
    public void CapabilityToolsets_RemainInsideApplicationComposition()
    {
        string coreAndDecksSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(RepositoryRoot, "src", "MtgMcp.Core"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(
                    Path.Combine(RepositoryRoot, "src", "MtgMcp.Decks"),
                    "*.cs",
                    SearchOption.AllDirectories))
                .Where(IsAuthoredSource)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("CapabilityToolset", coreAndDecksSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelContextProtocol", coreAndDecksSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that repository automation names only projects present in the current solution.
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
            "MtgMcp.Decks",
            "MtgMcp.Decks.Tests",
            "MtgMcp.E2E.Tests",
            "MtgMcp.Scryfall",
            "MtgMcp.Scryfall.Tests",
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

        Assert.Contains(
            "DeckInterchangeMcpTests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "DeckInterchangeMcpTests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "release.ps1")),
            StringComparison.Ordinal);
        Assert.Contains(
            "ToolsetNorthStarMcpTests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "ToolsetNorthStarMcpTests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "release.ps1")),
            StringComparison.Ordinal);
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
