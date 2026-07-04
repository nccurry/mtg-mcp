namespace MtgMcp.Decks.Tests;

/// <summary>
/// Owns one isolated directory removed after a deck persistence test.
/// </summary>
internal sealed class TemporaryDeckDirectory : IDisposable
{
    /// <summary>
    /// Creates a unique directory beneath the operating-system temporary root.
    /// </summary>
    internal TemporaryDeckDirectory()
    {
        Path = Directory.CreateTempSubdirectory("mtg-mcp-decks-").FullName;
    }

    /// <summary>
    /// Gets the isolated application-data root.
    /// </summary>
    internal string Path { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
