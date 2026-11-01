using System.Security.Cryptography;
using System.Text.Json;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Describes one recorded provider response listed in an adapter manifest.
/// </summary>
internal sealed record RecordedEntry(
    string Id,
    string Method,
    string Url,
    string Route,
    int Status,
    string File,
    string Sha256)
{
    /// <summary>
    /// Reads the stored bytes of the recording.
    /// </summary>
    internal byte[] ReadBytes()
    {
        return System.IO.File.ReadAllBytes(RecordedManifest.PathOf(File));
    }

    /// <summary>
    /// Reads the stored recording as text.
    /// </summary>
    internal string ReadText()
    {
        return System.Text.Encoding.UTF8.GetString(ReadBytes());
    }

    /// <summary>
    /// Hashes the stored bytes the way the refresh script does, in lowercase hexadecimal.
    /// </summary>
    internal string ComputeSha256()
    {
        return Convert.ToHexStringLower(SHA256.HashData(ReadBytes()));
    }
}

/// <summary>
/// Loads the recorded-response manifest copied to the test output of the adapter that owns it.
/// </summary>
internal static class RecordedManifest
{
    /// <summary>
    /// Reads the manifest once per test process.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<RecordedEntry>> Loaded = new(Read);

    /// <summary>
    /// Gets the directory that holds the manifest and every recording.
    /// </summary>
    internal static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "recorded");

    /// <summary>
    /// Gets every manifest entry in file order.
    /// </summary>
    internal static IReadOnlyList<RecordedEntry> Entries => Loaded.Value;

    /// <summary>
    /// Gets the manifest ids as theory data, so each recording is checked by its own test case.
    /// </summary>
    internal static TheoryData<string> Ids()
    {
        TheoryData<string> ids = [];
        foreach (RecordedEntry entry in Entries)
        {
            ids.Add(entry.Id);
        }

        return ids;
    }

    /// <summary>
    /// Finds one entry by id.
    /// </summary>
    internal static RecordedEntry Get(string id)
    {
        return Entries.Single(entry => entry.Id == id);
    }

    /// <summary>
    /// Resolves a manifest-relative recording path to a full path.
    /// </summary>
    internal static string PathOf(string file)
    {
        return Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Finds the repository root by walking up from the test output to the solution file.
    /// </summary>
    internal static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("mtg-mcp.slnx was not found above the test output.");
    }

    /// <summary>
    /// Lists recording files in the source <c>Fixtures/recorded</c> folder of this test project that the manifest does
    /// not list, so a stale copy in the output folder cannot hide one.
    /// </summary>
    internal static string[] UnlistedFiles()
    {
        string source = Path.Combine(
            RepositoryRoot(),
            "tests",
            typeof(RecordedManifest).Assembly.GetName().Name!,
            "Fixtures",
            "recorded");
        HashSet<string> listed = [.. Entries.Select(entry => Path.GetFullPath(Path.Combine(source, entry.File)))];
        List<string> unlisted = [];
        foreach (string path in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) != "manifest.json" && !listed.Contains(path))
            {
                unlisted.Add(path);
            }
        }

        return [.. unlisted];
    }

    /// <summary>
    /// Parses the manifest file.
    /// </summary>
    private static IReadOnlyList<RecordedEntry> Read()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        List<RecordedEntry> entries = [];
        foreach (JsonElement item in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new RecordedEntry(
                item.GetProperty("id").GetString()!,
                item.GetProperty("method").GetString()!,
                item.GetProperty("url").GetString()!,
                item.GetProperty("route").GetString()!,
                item.GetProperty("status").GetInt32(),
                item.GetProperty("file").GetString()!,
                item.GetProperty("sha256").GetString()!));
        }

        return entries;
    }
}
