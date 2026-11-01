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
    /// Verifies that only the currently approved production projects exist.
    /// </summary>
    [Fact]
    public void ProductionProjects_ContainOnlyApprovedImplementedModules()
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
                "src/MtgMcp.Archidekt/MtgMcp.Archidekt.csproj",
                "src/MtgMcp.Core/MtgMcp.Core.csproj",
                "src/MtgMcp.Decks/MtgMcp.Decks.csproj",
                "src/MtgMcp.Playgroup/MtgMcp.Playgroup.csproj",
                "src/MtgMcp.Scryfall/MtgMcp.Scryfall.csproj",
                "src/MtgMcp.Simulation/MtgMcp.Simulation.csproj",
                "src/MtgMcp.Spellbook/MtgMcp.Spellbook.csproj",
                "src/MtgMcp.Statistics/MtgMcp.Statistics.csproj",
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
    /// Verifies that App depends only on implemented modules and the approved hosting packages.
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
            ["../MtgMcp.Core/MtgMcp.Core.csproj", "../MtgMcp.Archidekt/MtgMcp.Archidekt.csproj", "../MtgMcp.Decks/MtgMcp.Decks.csproj", "../MtgMcp.Playgroup/MtgMcp.Playgroup.csproj", "../MtgMcp.Scryfall/MtgMcp.Scryfall.csproj", "../MtgMcp.Simulation/MtgMcp.Simulation.csproj", "../MtgMcp.Spellbook/MtgMcp.Spellbook.csproj", "../MtgMcp.Statistics/MtgMcp.Statistics.csproj"],
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
    /// Verifies single-file releases include native SQLite dependencies when the application starts.
    /// </summary>
    [Fact]
    public void AppProject_SingleFileReleaseExtractsNativeLibraries()
    {
        XDocument project = LoadProject("src/MtgMcp.App/MtgMcp.App.csproj");

        Assert.Equal("true", Assert.Single(project.Descendants("PublishSingleFile")).Value);
        Assert.Equal(
            "true",
            Assert.Single(project.Descendants("IncludeNativeLibrariesForSelfExtract")).Value);
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
    /// Verifies Commander Spellbook owns source HTTP and its isolated cache while depending only on Core.
    /// </summary>
    [Fact]
    public void SpellbookProject_ReferencesOnlyCoreAndSqlite()
    {
        XDocument project = LoadProject("src/MtgMcp.Spellbook/MtgMcp.Spellbook.csproj");
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
    /// Verifies Statistics contains only provider-neutral exact logic over Core contracts.
    /// </summary>
    [Fact]
    public void StatisticsProject_ReferencesOnlyCoreAndBcl()
    {
        XDocument project = LoadProject("src/MtgMcp.Statistics/MtgMcp.Statistics.csproj");
        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .ToArray();

        Assert.Equal(["../MtgMcp.Core/MtgMcp.Core.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));

        string source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot, "src", "MtgMcp.Statistics"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(IsAuthoredSource)
                .Select(File.ReadAllText));
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelContextProtocol", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MtgMcp.Decks", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MtgMcp.Scryfall", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Simulation contains only resolved-data calculation work over Core result types.
    /// </summary>
    [Fact]
    public void SimulationProject_ReferencesOnlyCoreAndBcl()
    {
        XDocument project = LoadProject("src/MtgMcp.Simulation/MtgMcp.Simulation.csproj");
        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .ToArray();

        Assert.Equal(["../MtgMcp.Core/MtgMcp.Core.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));

        string source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot, "src", "MtgMcp.Simulation"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(IsAuthoredSource)
                .Select(File.ReadAllText));
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelContextProtocol", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MtgMcp.App", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MtgMcp.Decks", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MtgMcp.Scryfall", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MtgMcp.Statistics", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Archidekt owns provider HTTP concerns while depending only on provider-neutral Core contracts.
    /// </summary>
    [Fact]
    public void ArchidektProject_ReferencesOnlyCoreAndBcl()
    {
        XDocument project = LoadProject("src/MtgMcp.Archidekt/MtgMcp.Archidekt.csproj");
        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .ToArray();

        Assert.Equal(["../MtgMcp.Core/MtgMcp.Core.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));

        string source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot, "src", "MtgMcp.Archidekt"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(IsAuthoredSource)
                .Select(File.ReadAllText));
        Assert.DoesNotContain("bulk", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies Playgroup owns provider HTTP concerns while depending only on provider-neutral Core contracts.
    /// </summary>
    [Fact]
    public void PlaygroupProject_ReferencesOnlyCoreAndBcl()
    {
        XDocument project = LoadProject("src/MtgMcp.Playgroup/MtgMcp.Playgroup.csproj");
        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .OfType<string>()
            .ToArray();

        Assert.Equal(["../MtgMcp.Core/MtgMcp.Core.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));

        string liveTest = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "tests",
            "MtgMcp.Playgroup.Tests",
            "PlaygroupLiveTests.cs"));
        Assert.DoesNotContain("CreateLiveSessionAsync", liveTest, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateGameEventsBatchAsync", liveTest, StringComparison.Ordinal);
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
    /// Verifies every tool is created by the one registration helper, so every tool shares the serializer options
    /// and closes its input schema to unknown properties, and no manifest builds its own schema options.
    /// </summary>
    [Fact]
    public void ToolRegistrations_UseTheSharedRegistrationHelper()
    {
        string appRoot = Path.Combine(RepositoryRoot, "src", "MtgMcp.App");
        string helperPath = Path.Combine(appRoot, "Hosting", "ToolRegistration.cs");
        string helper = File.ReadAllText(helperPath);
        List<string> offenders = [];
        int registrations = 0;
        string[] files = [.. Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories).Where(IsAuthoredSource)];
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string statement in FindStatements(text, ".WithRegisteredTools("))
            {
                registrations++;
                bool viaHelper = statement.Contains("ToolRegistration.Create(", StringComparison.Ordinal) ||
                    statement.Contains("DeckBatchWriteTools.Create(", StringComparison.Ordinal) ||
                    statement.Contains("Create(nameof(", StringComparison.Ordinal);
                if (!viaHelper)
                {
                    offenders.Add($"{ToRepositoryPath(file)}: {statement.ReplaceLineEndings(" ")}");
                }
            }

            bool direct = text.Contains("McpServerTool.Create(", StringComparison.Ordinal) ||
                text.Contains("AIJsonSchemaCreateOptions", StringComparison.Ordinal) ||
                text.Contains("McpServerToolCreateOptions", StringComparison.Ordinal);
            if (direct && !string.Equals(file, helperPath, StringComparison.Ordinal))
            {
                offenders.Add($"{ToRepositoryPath(file)} creates a tool or schema options itself.");
            }
        }

        Assert.True(registrations >= 20, $"Only {registrations} registrations were found.");
        Assert.Empty(offenders);
        Assert.Contains("ToolCallPolicy.SerializerOptions", helper, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\"", helper, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the text of each call that starts at the marker and ends at the next marker or semicolon.
    /// </summary>
    private static IEnumerable<string> FindStatements(string text, string marker)
    {
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        while (start >= 0)
        {
            int end = text.IndexOf(';', start);
            int next = text.IndexOf(marker, start + marker.Length, StringComparison.Ordinal);
            end = next >= 0 && next < end ? next : end;
            yield return text[start..end];
            start = text.IndexOf(marker, end, StringComparison.Ordinal);
        }
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

        Assert.Equal("0.13.2", version);
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
    /// Verifies routine and release test commands use the xUnit Microsoft Testing Platform filters.
    /// </summary>
    [Fact]
    public void TestCommands_UseMicrosoftTestingPlatformFilters()
    {
        string taskfile = File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml"));
        string releaseScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "release.ps1"));

        Assert.DoesNotContain("-- --filter \"", taskfile, StringComparison.Ordinal);
        Assert.Contains("--filter-not-trait", taskfile, StringComparison.Ordinal);
        Assert.Contains("--filter-trait", taskfile, StringComparison.Ordinal);
        Assert.Contains("--filter-query", taskfile, StringComparison.Ordinal);
        Assert.DoesNotContain("\"--filter\"", releaseScript, StringComparison.Ordinal);
        Assert.Contains("\"--filter-query\"", releaseScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the exact one-resource, 86-tool, three-prompt toolset-owned surface.
    /// </summary>
    [Fact]
    public void SourceSurface_ContainsOnlyApprovedCapabilityToolsResourceAndPrompts()
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
        string statisticsManifestSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Statistics", "StatisticsToolsetManifest.cs"));
        string archidektManifestSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Archidekt", "ArchidektToolsetManifest.cs"));
        string playgroupManifestSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Playgroup", "PlaygroupToolsetManifest.cs"));
        string spellbookManifestSource = File.ReadAllText(
            Path.Combine(sourceRoot, "MtgMcp.App", "Spellbook", "SpellbookToolsetManifest.cs"));

        Assert.Equal(1, Regex.Count(source, @"\[McpServerResource\("));
        Assert.Equal(86, Regex.Count(source, @"\[McpServerTool\("));
        Assert.Equal(1, Regex.Count(source, @"\.WithResources\("));
        Assert.Equal(3, Regex.Count(source, @"\[McpServerPrompt\("));
        Assert.Equal(1, Regex.Count(source, @"\.WithPrompts<"));
        Assert.Equal(1, Regex.Count(source, @"\.WithTools\("));
        Assert.Equal(25, Regex.Count(source, @"\.WithRegisteredTools\("));
        Assert.Contains("mtg://server/capabilities", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"Server Capabilities\"", source, StringComparison.Ordinal);
        Assert.Contains("MimeType = \"application/json\"", source, StringComparison.Ordinal);
        string[] toolNames = Regex.Matches(source, "McpServerTool\\(\\s*Name = \\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "archidekt_auth_status",
                "archidekt_deck_create",
                "archidekt_deck_delete",
                "archidekt_deck_get",
                "archidekt_deck_list",
                "archidekt_folder_create",
                "archidekt_folder_delete",
                "archidekt_folder_move_items",
                "archidekt_folder_update",
                "archidekt_folders_get",
                "archidekt_pull_apply",
                "archidekt_pull_preview",
                "archidekt_push_apply",
                "archidekt_push_preview",
                "archidekt_snapshot_create",
                "archidekt_snapshot_delete",
                "archidekt_snapshot_restore_apply",
                "archidekt_snapshot_restore_preview",
                "archidekt_snapshot_update",
                "archidekt_snapshots_get",
                "archidekt_sync_diff",
                "card_list_query",
                "card_lists_compare",
                "local_deck_apply_changes",
                "local_deck_backup_create",
                "local_deck_backup_delete",
                "local_deck_backup_list",
                "local_deck_backup_restore",
                "local_deck_category_rules_apply",
                "local_deck_category_rules_preview",
                "local_deck_create",
                "local_deck_delete",
                "local_deck_duplicates_preview",
                "local_deck_export_bundle",
                "local_deck_get",
                "local_deck_identity_reconcile_apply",
                "local_deck_identity_reconcile_preview",
                "local_deck_import_create",
                "local_deck_import_preview",
                "local_deck_interchange_formats",
                "local_deck_list",
                "local_deck_validate",
                "playgroup_auth_status",
                "playgroup_commander_get",
                "playgroup_commander_get_by_name",
                "playgroup_commander_turn_damage_get",
                "playgroup_deck_elo_history_get",
                "playgroup_deck_get",
                "playgroup_game_events_batch_create",
                "playgroup_games_stats_get",
                "playgroup_live_session_create",
                "playgroup_me_get",
                "playgroup_playgroup_game_get",
                "playgroup_playgroup_games_list",
                "playgroup_playgroup_members_list",
                "playgroup_user_decks_list",
                "playgroup_user_get",
                "playgroup_user_playgroup_get",
                "playgroup_user_playgroups_list",
                "scryfall_autocomplete",
                "scryfall_card_data_delete",
                "scryfall_card_data_rollback",
                "scryfall_card_data_status",
                "scryfall_card_data_sync",
                "scryfall_card_details",
                "scryfall_cards_get",
                "scryfall_installed_cards_by_tag",
                "scryfall_reference_get",
                "scryfall_request_snapshot_delete",
                "scryfall_request_snapshots_get",
                "scryfall_search",
                "scryfall_tags_search",
                "spellbook_deck_bracket_facts_get",
                "spellbook_deck_combos_find",
                "spellbook_variant_get",
                "spellbook_variant_search",
                "stats_castability",
                "stats_hypergeometric",
                "stats_local_deck_summary",
                "stats_mana_availability",
                "stats_minimum_count",
                "stats_mulligan",
                "stats_multivariate",
                "stats_package_assembly",
                "stats_simulate",
                "stats_turn_table",
            ],
            toolNames);
        string manifestSource = string.Join(
            Environment.NewLine,
            deckManifestSource,
            scryfallManifestSource,
            statisticsManifestSource,
            archidektManifestSource,
            playgroupManifestSource,
            spellbookManifestSource);
        string[] assignedToolNames = Regex.Matches(
                manifestSource,
                "\\\"((?:archidekt|card|local_deck|playgroup|scryfall|spellbook|stats)_[^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(toolNames, assignedToolNames);
        Assert.Equal(assignedToolNames.Length, assignedToolNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(".WithTools(", hostSource, StringComparison.Ordinal);
        Assert.Equal(10, Regex.Count(deckManifestSource, @"\.WithRegisteredTools\("));
        Assert.Equal(1, Regex.Count(deckManifestSource, @"DeckBatchWriteTools\.Create\("));
        Assert.Equal(4, Regex.Count(scryfallManifestSource, @"\.WithRegisteredTools\("));
        Assert.Equal(3, Regex.Count(statisticsManifestSource, @"\.WithRegisteredTools\("));
        Assert.Equal(3, Regex.Count(archidektManifestSource, @"\.WithRegisteredTools\("));
        Assert.Equal(3, Regex.Count(playgroupManifestSource, @"\.WithRegisteredTools\("));
        Assert.Equal(2, Regex.Count(spellbookManifestSource, @"\.WithRegisteredTools\("));
        Assert.DoesNotContain("FoundationModuleStatus", source, StringComparison.Ordinal);
        Assert.Contains("capability.Remove(\"listChanged\")", source, StringComparison.Ordinal);
        Assert.Contains("\"resources\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendToolListChanged", source, StringComparison.Ordinal);

        string[] forbiddenMarkers =
        [
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
    /// Verifies the MCP smoke test filter in the Taskfile and in the release script name the same E2E classes, and
    /// that the filter includes the prompt tests.
    /// </summary>
    [Fact]
    public void SmokeFilters_InTaskfileAndReleaseScript_AreTheSame()
    {
        Regex filter = new(@"""(/\*/\*/\([A-Za-z]+Tests\)[^""]*)""");
        string task = File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml"));
        string script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "release.ps1"));

        Match fromTask = filter.Match(task);
        Match fromScript = filter.Match(script);

        Assert.True(fromTask.Success && fromScript.Success, "A smoke filter was not found.");
        Assert.Equal(fromTask.Groups[1].Value, fromScript.Groups[1].Value);
        Assert.Contains("(WorkflowPromptsMcpTests)", fromTask.Groups[1].Value, StringComparison.Ordinal);
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
            "MtgMcp.Archidekt",
            "MtgMcp.Archidekt.Tests",
            "MtgMcp.Architecture.Tests",
            "MtgMcp.Core",
            "MtgMcp.Core.Tests",
            "MtgMcp.Decks",
            "MtgMcp.Decks.Tests",
            "MtgMcp.E2E.Tests",
            "MtgMcp.Playgroup",
            "MtgMcp.Playgroup.Tests",
            "MtgMcp.Scryfall",
            "MtgMcp.Scryfall.Tests",
            "MtgMcp.Simulation",
            "MtgMcp.Simulation.Tests",
            "MtgMcp.Spellbook",
            "MtgMcp.Spellbook.Tests",
            "MtgMcp.Statistics",
            "MtgMcp.Statistics.Tests",
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
            "StatisticsSimulateMcpTests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "StatisticsSimulateMcpTests",
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
        Assert.Contains(
            "MtgMcp.Spellbook.Tests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "MtgMcp.Simulation.Tests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "coverage:simulation",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "coverage:spellbook",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "SpellbookMcpTests",
            File.ReadAllText(Path.Combine(RepositoryRoot, "Taskfile.yml")),
            StringComparison.Ordinal);
        Assert.Contains(
            "SpellbookMcpTests",
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
