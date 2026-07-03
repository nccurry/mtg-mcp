namespace MtgMcp.App.Tests;

/// <summary>
/// Owns a uniquely named temporary directory and removes it after a test.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>
    /// Stores the created directory and its verified temporary path.
    /// </summary>
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("mtg-mcp-test-");

    /// <summary>
    /// Gets the absolute temporary directory path.
    /// </summary>
    internal string Path => directory.FullName;

    /// <summary>
    /// Removes the owned temporary directory and its test-created contents.
    /// </summary>
    public void Dispose()
    {
        directory.Refresh();
        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
