namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Verifies Playgroup credential files remain strict, private, and subordinate to explicit configuration.
/// </summary>
public sealed class PlaygroupCredentialsTests
{
    /// <summary>Verifies a strict JSON file supplies one trimmed API key.</summary>
    [Fact]
    public void Load_ValidFile_ReturnsConfiguredKey()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "playgroup.json");
            File.WriteAllText(path, "{\"apiKey\":\" file-key \"}");

            PlaygroupCredentials.CredentialLoad loaded = new PlaygroupCredentials(
                PlaygroupOptions.CreateDefault(null, path)).Load();

            Assert.Equal("configured", loaded.State);
            Assert.True(loaded.IsUsable);
            Assert.Equal("file-key", loaded.ApiKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies an explicit key takes precedence without reading an invalid file.</summary>
    [Fact]
    public void Load_ExplicitKey_DoesNotReadFile()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        PlaygroupCredentials.CredentialLoad loaded = new PlaygroupCredentials(
            PlaygroupOptions.CreateDefault("explicit-key", missingPath)).Load();

        Assert.Equal("configured", loaded.State);
        Assert.Equal("explicit-key", loaded.ApiKey);
    }

    /// <summary>Verifies malformed files produce only a redacted error state.</summary>
    [Fact]
    public void Load_MalformedFile_ReturnsRedactedError()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "private-playgroup.json");
            File.WriteAllText(path, "{\"unexpected\":\"private-value\"}");

            PlaygroupCredentials.CredentialLoad loaded = new PlaygroupCredentials(
                PlaygroupOptions.CreateDefault(null, path)).Load();

            Assert.Equal("error", loaded.State);
            Assert.False(loaded.IsUsable);
            Assert.Null(loaded.ApiKey);
            Assert.DoesNotContain(path, loaded.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-value", loaded.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies duplicate key fields are rejected instead of receiving order-dependent meaning.</summary>
    [Fact]
    public void Load_DuplicateKey_ReturnsError()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "playgroup.json");
            File.WriteAllText(path, "{\"apiKey\":\"first\",\"APIKEY\":\"second\"}");

            PlaygroupCredentials.CredentialLoad loaded = new PlaygroupCredentials(
                PlaygroupOptions.CreateDefault(null, path)).Load();

            Assert.Equal("error", loaded.State);
            Assert.False(loaded.IsUsable);
            Assert.Null(loaded.ApiKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates one isolated directory owned by the current test.</summary>
    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mtg-mcp-playgroup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
