using System.Text.Json;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Covers the shared JSON persistence helper used by file-backed repositories.
/// </summary>
public sealed class JsonFileStoreTests
{
    /// <summary>
    /// Matches the repository serializer settings when writing legacy fixture files.
    /// </summary>
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Verifies that ids with the same legacy sanitized path no longer overwrite each other.
    /// </summary>
    [Fact]
    public async Task SaveAsync_UsesCollisionResistantPathForNonAlphanumericIds()
    {
        string dataDirectory = CreateTempDirectory();
        try
        {
            JsonFileStore<DeckWorkspace> store = new(
                dataDirectory,
                "Workspace",
                static workspace => workspace.Id);

            await store.SaveAsync(
                "ab",
                new DeckWorkspace { Id = "ab", Name = "Plain" },
                TestContext.Current.CancellationToken);
            await store.SaveAsync(
                "a-b",
                new DeckWorkspace { Id = "a-b", Name = "Dashed" },
                TestContext.Current.CancellationToken);

            DeckWorkspace? plain = await store.GetAsync("ab", TestContext.Current.CancellationToken);
            DeckWorkspace? dashed = await store.GetAsync("a-b", TestContext.Current.CancellationToken);

            plain.Should().NotBeNull();
            plain!.Name.Should().Be("Plain");
            dashed.Should().NotBeNull();
            dashed!.Name.Should().Be("Dashed");
            Directory.EnumerateFiles(dataDirectory, "*.json").Should().HaveCount(2);

            bool deletedDashed = await store.DeleteAsync("a-b", TestContext.Current.CancellationToken);
            DeckWorkspace? plainAfterDelete = await store.GetAsync("ab", TestContext.Current.CancellationToken);

            deletedDashed.Should().BeTrue();
            plainAfterDelete.Should().NotBeNull();
            plainAfterDelete!.Name.Should().Be("Plain");
        }
        finally
        {
            DeleteTempDirectory(dataDirectory);
        }
    }

    /// <summary>
    /// Verifies that workspaces saved by the old sanitized-id filename strategy still load.
    /// </summary>
    [Fact]
    public async Task GetAsync_ReadsLegacySanitizedPath()
    {
        string dataDirectory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(dataDirectory);
            DeckWorkspace legacy = new() { Id = "workspace-1", Name = "Legacy" };
            string legacyPath = Path.Combine(dataDirectory, "workspace1.json");
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(legacy, WebJsonOptions),
                TestContext.Current.CancellationToken);

            JsonFileStore<DeckWorkspace> store = new(
                dataDirectory,
                "Workspace",
                static workspace => workspace.Id);

            DeckWorkspace? loaded = await store.GetAsync("workspace-1", TestContext.Current.CancellationToken);

            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Legacy");

            await store.SaveAsync(
                "workspace-1",
                new DeckWorkspace { Id = "workspace-1", Name = "Migrated" },
                TestContext.Current.CancellationToken);

            DeckWorkspace? migrated = await store.GetAsync("workspace-1", TestContext.Current.CancellationToken);

            migrated.Should().NotBeNull();
            migrated!.Name.Should().Be("Migrated");
            File.Exists(legacyPath).Should().BeFalse();
            Directory.EnumerateFiles(dataDirectory, "*.json").Should().ContainSingle();
        }
        finally
        {
            DeleteTempDirectory(dataDirectory);
        }
    }

    /// <summary>
    /// Verifies that unreadable legacy collision files do not block new collision-safe saves.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LeavesCorruptLegacyCollisionFile()
    {
        string dataDirectory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(dataDirectory);
            string legacyCollisionPath = Path.Combine(dataDirectory, "ab.json");
            await File.WriteAllTextAsync(
                legacyCollisionPath,
                "{",
                TestContext.Current.CancellationToken);

            JsonFileStore<DeckWorkspace> store = new(
                dataDirectory,
                "Workspace",
                static workspace => workspace.Id);

            Func<Task> save = () => store.SaveAsync(
                "a-b",
                new DeckWorkspace { Id = "a-b", Name = "Dashed" },
                TestContext.Current.CancellationToken);

            await save.Should().NotThrowAsync();
            DeckWorkspace? dashed = await store.GetAsync("a-b", TestContext.Current.CancellationToken);

            dashed.Should().NotBeNull();
            dashed!.Name.Should().Be("Dashed");
            File.Exists(legacyCollisionPath).Should().BeTrue();
        }
        finally
        {
            DeleteTempDirectory(dataDirectory);
        }
    }

    /// <summary>
    /// Allocates an isolated temporary directory for one persistence test.
    /// </summary>
    private static string CreateTempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "mtg-mcp-store-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Removes a temporary persistence directory when the test created one.
    /// </summary>
    private static void DeleteTempDirectory(string dataDirectory)
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
