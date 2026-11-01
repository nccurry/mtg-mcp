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
    string CapturedAt,
    string Sha256,
    string Scrubbed)
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
                item.GetProperty("capturedAt").GetString()!,
                item.GetProperty("sha256").GetString()!,
                item.GetProperty("scrubbed").GetString()!));
        }

        return entries;
    }
}
