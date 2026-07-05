using System.Text.Json;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Prevents live provider workflows from running concurrently inside the test process.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveAcceptanceSerialGroup
{
    /// <summary>
    /// Identifies the serialized live-acceptance collection.
    /// </summary>
    public const string Name = "Live method acceptance";
}

/// <summary>
/// Defines the exact current MCP surface that requires an explicit acceptance disposition.
/// </summary>
internal static class LiveAcceptanceManifest
{
    /// <summary>
    /// Lists every tool visible in the remote/all profile in deterministic order.
    /// </summary>
    internal static readonly string[] ToolNames =
    [
        "archidekt_auth_status",
        "archidekt_deck_create",
        "archidekt_deck_delete",
        "archidekt_deck_get",
        "archidekt_deck_list",
        "archidekt_folder_create",
        "archidekt_folder_delete",
        "archidekt_folder_get",
        "archidekt_folder_list",
        "archidekt_folder_move_items",
        "archidekt_folder_update",
        "archidekt_pull_apply",
        "archidekt_pull_preview",
        "archidekt_push_apply",
        "archidekt_push_preview",
        "archidekt_snapshot_create",
        "archidekt_snapshot_delete",
        "archidekt_snapshot_get",
        "archidekt_snapshot_list",
        "archidekt_snapshot_restore_apply",
        "archidekt_snapshot_restore_preview",
        "archidekt_snapshot_update",
        "archidekt_sync_diff",
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
    ];

    /// <summary>
    /// Identifies provider writes intentionally excluded from live execution.
    /// </summary>
    internal static readonly string[] FixtureOnlyToolNames =
    [
        "playgroup_game_events_batch_create",
        "playgroup_live_session_create",
    ];
}

/// <summary>
/// Validates opt-in state and owns the explicitly marked persistent acceptance directory.
/// </summary>
internal sealed class LiveAcceptanceEnvironment
{
    /// <summary>
    /// Names the marker that prevents accidental use of an unrelated directory.
    /// </summary>
    private const string MarkerName = ".mtg-mcp-live-acceptance";

    /// <summary>
    /// Creates a validated environment for one explicitly enabled run.
    /// </summary>
    private LiveAcceptanceEnvironment(string rootPath)
    {
        RootPath = rootPath;
        Journal = new LiveAcceptanceJournal(rootPath);
    }

    /// <summary>
    /// Gets the persistent caller-owned acceptance root.
    /// </summary>
    internal string RootPath { get; }

    /// <summary>
    /// Gets the path-free method disposition journal.
    /// </summary>
    internal LiveAcceptanceJournal Journal { get; }

    /// <summary>
    /// Validates all safety gates and marks a new empty acceptance directory.
    /// </summary>
    internal static async Task<LiveAcceptanceEnvironment> RequireAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE=1 to run packaged method acceptance.");
        }

        string? configuredRoot = Environment.GetEnvironmentVariable("MTGMCP_LIVE_ACCEPTANCE_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(configuredRoot));
        string rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
        Assert.True(Directory.Exists(rootPath), "The live acceptance directory must be created explicitly before the run.");

        string repositoryRoot = FindRepositoryRoot();
        Assert.False(IsSameOrChild(rootPath, repositoryRoot), "The live acceptance directory must be outside the repository.");

        string defaultDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mtg-mcp",
            "v0.9");
        Assert.False(
            IsSameOrChild(rootPath, defaultDataRoot),
            "The live acceptance directory must not use the normal application-data root.");

        string markerPath = Path.Combine(rootPath, MarkerName);
        if (!File.Exists(markerPath))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(rootPath));
            await File.WriteAllTextAsync(
                markerPath,
                "This directory is owned by the mtg-mcp live method acceptance harness." + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }

        string? installedCommand = Environment.GetEnvironmentVariable("MTGMCP_E2E_COMMAND");
        Assert.False(string.IsNullOrWhiteSpace(installedCommand));
        if (Path.IsPathRooted(installedCommand))
        {
            Assert.True(File.Exists(installedCommand), "The installed MCP command does not exist.");
        }

        return new LiveAcceptanceEnvironment(rootPath);
    }

    /// <summary>
    /// Creates an empty phase directory after deleting only a known child of the marked root.
    /// </summary>
    internal string PrepareEphemeralPhaseRoot(string phase)
    {
        string phaseRoot = ChildPath(phase);
        if (Directory.Exists(phaseRoot))
        {
            Directory.Delete(phaseRoot, recursive: true);
        }

        Directory.CreateDirectory(phaseRoot);
        return phaseRoot;
    }

    /// <summary>
    /// Gets or creates a persistent phase directory used across provider generations.
    /// </summary>
    internal string PreparePersistentPhaseRoot(string phase)
    {
        string phaseRoot = ChildPath(phase);
        Directory.CreateDirectory(phaseRoot);
        return phaseRoot;
    }

    /// <summary>
    /// Builds the only secret-bearing child-process override without adding it to reports.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> ProviderEnvironment()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MTGMCP__PLAYGROUP__API_KEY"] =
                Environment.GetEnvironmentVariable("MTGMCP__PLAYGROUP__API_KEY"),
        };
    }

    /// <summary>
    /// Resolves and verifies one direct child path beneath the marked root.
    /// </summary>
    private string ChildPath(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        string child = Path.GetFullPath(Path.Combine(RootPath, phase));
        Assert.True(IsSameOrChild(child, RootPath));
        Assert.NotEqual(RootPath, child);
        return child;
    }

    /// <summary>
    /// Tests path containment using platform path comparison rules.
    /// </summary>
    private static bool IsSameOrChild(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
            (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
             !Path.IsPathRooted(relative));
    }

    /// <summary>
    /// Finds the repository boundary used only for destructive-path refusal.
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

/// <summary>
/// Persists only method names, dispositions, timestamps, and non-sensitive note codes.
/// </summary>
internal sealed class LiveAcceptanceJournal
{
    /// <summary>
    /// Lists every valid journal disposition.
    /// </summary>
    private static readonly string[] ValidStatuses =
    [
        "live-pass",
        "fixture-only-owner-approved",
        "pending-provider-generation",
        "fixture-unavailable",
        "blocked",
        "failed",
    ];

    /// <summary>
    /// Configures deterministic readable output in the untracked acceptance directory.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Stores the untracked journal file.
    /// </summary>
    private readonly string journalPath;

    /// <summary>
    /// Pins every retained method result to one clean repository commit.
    /// </summary>
    private readonly string testedCommit;

    /// <summary>
    /// Pins every retained method result to one installed package version.
    /// </summary>
    private readonly string packageVersion;

    /// <summary>
    /// Creates a journal under the validated caller-owned root.
    /// </summary>
    internal LiveAcceptanceJournal(string rootPath)
    {
        journalPath = Path.Combine(rootPath, "live-method-results.json");
        testedCommit = Environment.GetEnvironmentVariable("MTGMCP_LIVE_ACCEPTANCE_COMMIT") ?? string.Empty;
        packageVersion = Environment.GetEnvironmentVariable("MTGMCP_E2E_VERSION") ?? string.Empty;
        Assert.Matches("^[0-9a-f]{40}$", testedCommit);
        Assert.False(string.IsNullOrWhiteSpace(packageVersion));
    }

    /// <summary>
    /// Records one tool disposition without provider payloads or identifiers.
    /// </summary>
    internal async Task RecordAsync(
        string toolName,
        string status,
        string note,
        CancellationToken cancellationToken)
    {
        Assert.Contains(toolName, LiveAcceptanceManifest.ToolNames);
        Assert.Contains(status, ValidStatuses);

        LiveAcceptanceReport current = await ReadAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, LiveAcceptanceRecord> records = current.Records.ToDictionary(
            value => value.Tool,
            StringComparer.Ordinal);
        records[toolName] = new LiveAcceptanceRecord(
            toolName,
            status,
            note,
            DateTimeOffset.UtcNow);
        LiveAcceptanceReport report = new(
            1,
            testedCommit,
            packageVersion,
            current.CapabilityResourceStatus,
            records.Values.OrderBy(value => value.Tool, StringComparer.Ordinal).ToArray());
        string json = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(
            journalPath,
            json + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records successful initialization, discovery, and capability-resource validation.
    /// </summary>
    internal async Task RecordCapabilityResourceAsync(CancellationToken cancellationToken)
    {
        LiveAcceptanceReport current = await ReadAsync(cancellationToken).ConfigureAwait(false);
        LiveAcceptanceReport report = current with { CapabilityResourceStatus = "live-pass" };
        string json = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(
            journalPath,
            json + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the two reviewed fixture-only Playgroup writes without invoking them.
    /// </summary>
    internal async Task RecordFixtureOnlyWritesAsync(CancellationToken cancellationToken)
    {
        foreach (string toolName in LiveAcceptanceManifest.FixtureOnlyToolNames)
        {
            await RecordAsync(
                toolName,
                "fixture-only-owner-approved",
                "public-api-has-no-cleanup-operation",
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads prior phase results so multi-day corpus acceptance can resume.
    /// </summary>
    private async Task<LiveAcceptanceReport> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(journalPath))
        {
            return EmptyReport();
        }

        string json = await File.ReadAllTextAsync(journalPath, cancellationToken).ConfigureAwait(false);
        LiveAcceptanceReport? report = JsonSerializer.Deserialize<LiveAcceptanceReport>(json);
        return report is not null &&
            string.Equals(report.TestedCommit, testedCommit, StringComparison.Ordinal) &&
            string.Equals(report.PackageVersion, packageVersion, StringComparison.Ordinal)
            ? report
            : EmptyReport();
    }

    /// <summary>
    /// Creates one empty report for the exact package build under test.
    /// </summary>
    private LiveAcceptanceReport EmptyReport()
    {
        return new LiveAcceptanceReport(1, testedCommit, packageVersion, "not-run", []);
    }
}

/// <summary>
/// Represents one path-free live method disposition.
/// </summary>
internal sealed record LiveAcceptanceRecord(
    string Tool,
    string Status,
    string Note,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Represents the versioned untracked method journal.
/// </summary>
internal sealed record LiveAcceptanceReport(
    int SchemaVersion,
    string TestedCommit,
    string PackageVersion,
    string CapabilityResourceStatus,
    IReadOnlyList<LiveAcceptanceRecord> Records);
