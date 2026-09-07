namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Owns one isolated temporary Commander Spellbook cache root for a test.
/// </summary>
internal sealed class TemporarySpellbookDirectory : IDisposable
{
    /// <summary>
    /// Creates a unique test root without creating a database file.
    /// </summary>
    internal TemporarySpellbookDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mtg-mcp-spellbook-tests",
            Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Gets the cache root assigned to the current test.
    /// </summary>
    internal string Path { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
