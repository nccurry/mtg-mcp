using System.Text.RegularExpressions;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Protects the public tool names: the source prefixes, the naming rule, the old-to-new name map, and the frozen
/// Playgroup list. The expected names are the final names of the focused agent tool surface.
/// </summary>
public sealed partial class ToolSurfaceNamingTests
{
    /// <summary>
    /// Lists the source prefixes every tool name must start with.
    /// </summary>
    private static readonly string[] Prefixes =
        ["local_deck_", "scryfall_", "card_", "stats_", "archidekt_", "playgroup_", "spellbook_"];

    /// <summary>
    /// Lists the kept names that do not follow the full source, object, and action shape.
    /// </summary>
    private static readonly string[] NamingExceptions =
    [
        "scryfall_search",
        "scryfall_autocomplete",
        "card_lists_compare",
        "card_list_query",
        "stats_castability",
        "stats_hypergeometric",
        "stats_mana_availability",
        "stats_minimum_count",
        "stats_mulligan",
        "stats_multivariate",
        "stats_package_assembly",
        "stats_simulate",
        "stats_turn_table",
    ];

    /// <summary>
    /// Lists the sixteen Playgroup tools, which are frozen.
    /// </summary>
    private static readonly string[] FrozenPlaygroupTools =
    [
        "playgroup_auth_status",
        "playgroup_commander_get",
        "playgroup_commander_get_by_name",
        "playgroup_commander_turn_damage_get",
        "playgroup_deck_elo_history_get",
        "playgroup_deck_get",
        "playgroup_game_events_batch_create",
        "playgroup_live_session_create",
        "playgroup_me_get",
        "playgroup_playgroup_game_get",
        "playgroup_playgroup_games_list",
        "playgroup_playgroup_members_list",
        "playgroup_user_decks_list",
        "playgroup_user_get",
        "playgroup_user_playgroup_get",
        "playgroup_user_playgroups_list",
    ];

    /// <summary>
    /// Maps every tool name the server no longer has to the tool or tools that replace it.
    /// </summary>
    private static readonly (string Old, string[] Replacements)[] OldToNewNames =
    [
        ("deck_list", ["local_deck_list"]),
        ("deck_get", ["local_deck_get"]),
        ("deck_validate", ["local_deck_validate"]),
        ("deck_export_bundle", ["local_deck_export_bundle"]),
        ("deck_interchange_formats", ["local_deck_interchange_formats"]),
        ("deck_backup_list", ["local_deck_backup_list"]),
        ("deck_backup_create", ["local_deck_backup_create"]),
        ("deck_backup_restore", ["local_deck_backup_restore"]),
        ("deck_backup_delete", ["local_deck_backup_delete"]),
        ("deck_import_preview", ["local_deck_import_preview"]),
        ("deck_import_create", ["local_deck_import_create"]),
        ("deck_category_rules_preview", ["local_deck_category_rules_preview"]),
        ("deck_category_rules_apply", ["local_deck_category_rules_apply"]),
        ("deck_category_rules_validate", ["local_deck_category_rules_preview"]),
        ("deck_identity_reconcile_preview", ["local_deck_identity_reconcile_preview"]),
        ("deck_identity_reconcile_apply", ["local_deck_identity_reconcile_apply"]),
        ("deck_create", ["local_deck_create"]),
        ("deck_apply_changes", ["local_deck_apply_changes"]),
        ("deck_delete", ["local_deck_delete"]),
        ("deck_on_curve_estimate", ["stats_simulate"]),
        ("local_deck_on_curve_estimate", ["stats_simulate"]),
        ("deck_update", ["local_deck_apply_changes"]),
        ("deck_entry_add", ["local_deck_apply_changes"]),
        ("deck_entry_update", ["local_deck_apply_changes"]),
        ("deck_entry_remove", ["local_deck_apply_changes"]),
        ("deck_category_create", ["local_deck_apply_changes"]),
        ("deck_category_update", ["local_deck_apply_changes"]),
        ("deck_category_delete", ["local_deck_apply_changes"]),
        ("deck_category_assign", ["local_deck_apply_changes"]),
        ("deck_category_unassign", ["local_deck_apply_changes"]),
        ("stats_deck_summary", ["stats_local_deck_summary"]),
        ("scryfall_corpus_status", ["scryfall_card_data_status"]),
        ("scryfall_corpus_sync", ["scryfall_card_data_sync"]),
        ("scryfall_corpus_rollback", ["scryfall_card_data_rollback"]),
        ("scryfall_corpus_delete", ["scryfall_card_data_delete"]),
        ("scryfall_tag_search", ["scryfall_tags_search"]),
        ("scryfall_cards_by_tag", ["scryfall_installed_cards_by_tag"]),
        ("scryfall_snapshot_delete", ["scryfall_search_snapshot_delete"]),
        ("scryfall_card_get", ["scryfall_cards_get"]),
        ("scryfall_card_collection", ["scryfall_cards_get"]),
        ("scryfall_card_prints", ["scryfall_card_details"]),
        ("scryfall_card_rulings", ["scryfall_card_details"]),
        ("scryfall_sets", ["scryfall_reference_get"]),
        ("scryfall_catalog", ["scryfall_reference_get"]),
        ("scryfall_bulk_metadata", ["scryfall_reference_get"]),
        ("scryfall_snapshot_list", ["scryfall_search_snapshots_get"]),
        ("scryfall_snapshot_get", ["scryfall_search_snapshots_get"]),
        ("archidekt_folder_list", ["archidekt_folders_get"]),
        ("archidekt_folder_get", ["archidekt_folders_get"]),
        ("archidekt_snapshot_list", ["archidekt_snapshots_get"]),
        ("archidekt_snapshot_get", ["archidekt_snapshots_get"]),
    ];

    /// <summary>
    /// Provides the repository root used to read source and the changelog.
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// Verifies every tool name starts with a source prefix and fits the 64-character limit.
    /// </summary>
    [Fact]
    public void ToolNames_StartWithASourcePrefixAndFitTheLengthLimit()
    {
        string[] names = ReadToolNames();

        Assert.Equal(86, names.Length);
        Assert.All(names, name =>
        {
            Assert.True(Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)), name);
            Assert.True(name.Length <= 64, name);
            Assert.Matches("^[a-z]+(_[a-z]+)+$", name);
        });
    }

    /// <summary>
    /// Verifies every name outside the listed exceptions and the frozen Playgroup set has at least a source, an
    /// object, and an action, and that every listed exception is a real tool.
    /// </summary>
    [Fact]
    public void ToolNames_FollowTheSourceObjectActionShapeExceptForTheListedNames()
    {
        string[] names = ReadToolNames();

        foreach (string name in names)
        {
            bool exempt = NamingExceptions.Contains(name, StringComparer.Ordinal) ||
                name.StartsWith("playgroup_", StringComparison.Ordinal);
            if (!exempt)
            {
                Assert.True(name.Split('_').Length >= 3, $"{name} has fewer than three parts.");
            }
        }

        Assert.All(NamingExceptions, name => Assert.Contains(name, names));
    }

    /// <summary>
    /// Verifies the 16 Playgroup tools are exactly the frozen list and belong to the Playgroup toolset.
    /// </summary>
    [Fact]
    public void PlaygroupTools_AreTheFrozenSixteenPlusTheGameStatisticsTool()
    {
        string[] playgroup =
        [
            .. ReadToolNames().Where(name => name.StartsWith("playgroup_", StringComparison.Ordinal)),
        ];
        string manifest = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "MtgMcp.App", "Playgroup", "PlaygroupToolsetManifest.cs"));
        string[] assigned = [.. ToolNameInQuotes().Matches(manifest)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)];

        string[] expected = [.. FrozenPlaygroupTools, "playgroup_games_stats_get"];
        Array.Sort(expected, StringComparer.Ordinal);
        Assert.Equal(expected, playgroup);
        Assert.Equal(expected, assigned);
    }

    /// <summary>
    /// Verifies every removed or renamed tool name is gone, every replacement exists, and the changelog lists
    /// the old name with its replacement.
    /// </summary>
    [Fact]
    public void OldToolNames_AreGoneWithTheirReplacementsAndAreListedInTheChangelog()
    {
        string[] names = ReadToolNames();
        string release = ReadReleaseChangelog();

        foreach ((string oldName, string[] replacements) in OldToNewNames)
        {
            Assert.DoesNotContain(oldName, names);
            Assert.All(replacements, replacement => Assert.Contains(replacement, names));
            Assert.Contains($"`{oldName}`", release, StringComparison.Ordinal);
            Assert.All(
                replacements,
                replacement => Assert.Contains($"`{replacement}`", release, StringComparison.Ordinal));
        }

        Assert.Equal(OldToNewNames.Length, OldToNewNames.Select(entry => entry.Old).Distinct().Count());
    }

    /// <summary>
    /// Reads the tool names declared in source, in ordinal order.
    /// </summary>
    private static string[] ReadToolNames()
    {
        string source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Select(File.ReadAllText));
        return [.. DeclaredToolName().Matches(source)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads the 0.12.0 and 0.11.0 sections of the changelog, the two releases of the tool surface changes.
    /// </summary>
    private static string ReadReleaseChangelog()
    {
        string changelog = File.ReadAllText(Path.Combine(RepositoryRoot, "CHANGELOG.md"));
        int start = changelog.IndexOf("## 0.12.0 - ", StringComparison.Ordinal);
        Assert.True(start >= 0, "CHANGELOG.md has no 0.12.0 section.");
        int first = changelog.IndexOf("## 0.11.0 - ", start, StringComparison.Ordinal);
        Assert.True(first >= 0, "CHANGELOG.md has no 0.11.0 section.");
        int next = changelog.IndexOf("\n## ", first + 1, StringComparison.Ordinal);
        return next < 0 ? changelog[start..] : changelog[start..next];
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

    /// <summary>
    /// Matches the name in a tool attribute, written on one line or split after the opening parenthesis.
    /// </summary>
    [GeneratedRegex("McpServerTool\\(\\s*Name = \"([^\"]+)\"")]
    private static partial Regex DeclaredToolName();

    /// <summary>
    /// Matches a quoted Playgroup tool name in a manifest.
    /// </summary>
    [GeneratedRegex("\"(playgroup_[a-z_]+)\"")]
    private static partial Regex ToolNameInQuotes();
}
