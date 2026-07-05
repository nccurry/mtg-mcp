namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Verifies provider configuration and secret-file parsing without exposing credential values.
/// </summary>
public sealed class ArchidektConfigurationTests
{
    /// <summary>
    /// Verifies production defaults and every invalid request-bound configuration branch.
    /// </summary>
    [Fact]
    public void Options_ValidateSafetyBounds()
    {
        ArchidektOptions defaults = ArchidektOptions.CreateDefault();
        defaults.Validate();

        Assert.Throws<ArgumentException>(() => (defaults with { BaseAddress = new Uri("file:///tmp/x") }).Validate());
        Assert.Throws<ArgumentException>(() => (defaults with { BaseAddress = new Uri("http://example.test/") }).Validate());
        (defaults with { BaseAddress = new Uri("http://127.0.0.1:1234/") }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { MinimumRequestInterval = TimeSpan.FromTicks(-1) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { MaximumRequestsPerWindow = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { MaximumRequestsPerOperation = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { RequestWindow = TimeSpan.Zero }).Validate());
    }

    /// <summary>
    /// Verifies explicit, JSON, line-oriented, missing, malformed, incomplete, and cached credential states.
    /// </summary>
    [Fact]
    public void Credentials_LoadStrictFormatsAndReturnOnlyRedactedStates()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-creds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            ArchidektCredentials.CredentialLoad none = Load();
            ArchidektCredentials.CredentialLoad explicitValues = Load(username: " user ", password: " secret ");
            ArchidektCredentials.CredentialLoad incomplete = Load(username: "user");
            ArchidektCredentials.CredentialLoad missing = Load(file: Path.Combine(root, "missing.json"));

            string json = Path.Combine(root, "credentials.json");
            File.WriteAllText(json, "{\"username\":\"json-user\",\"password\":\"json-secret\"}");
            ArchidektCredentials credentials = Create(file: json);
            ArchidektCredentials.CredentialLoad jsonValues = credentials.Load();
            File.WriteAllText(json, "{}");
            Assert.Same(jsonValues, credentials.Load());

            string lines = Path.Combine(root, "credentials.txt");
            File.WriteAllText(lines, "# comment\nusername = line-user\n; ignored\npassword=line-secret\n");
            ArchidektCredentials.CredentialLoad lineValues = Load(file: lines);

            string malformed = Path.Combine(root, "malformed.txt");
            File.WriteAllText(malformed, "unknown=value");
            ArchidektCredentials.CredentialLoad malformedValues = Load(file: malformed);

            string malformedJson = Path.Combine(root, "malformed.json");
            File.WriteAllText(malformedJson, "{\"username\":1}");
            ArchidektCredentials.CredentialLoad malformedJsonValues = Load(file: malformedJson);

            Assert.Equal("not-configured", none.State);
            Assert.Equal("configured", explicitValues.State);
            Assert.True(explicitValues.IsUsable);
            Assert.NotEmpty(explicitValues.PacingKey);
            Assert.Equal("error", incomplete.State);
            Assert.Equal("error", missing.State);
            Assert.Equal("configured", jsonValues.State);
            Assert.Equal("configured", lineValues.State);
            Assert.Equal("error", malformedValues.State);
            Assert.Equal("error", malformedJsonValues.State);
            Assert.DoesNotContain("secret", malformedValues.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Creates and loads one credential source.
    /// </summary>
    private static ArchidektCredentials.CredentialLoad Load(
        string? username = null,
        string? password = null,
        string? file = null)
    {
        return Create(username, password, file).Load();
    }

    /// <summary>
    /// Creates one credential source from production option defaults.
    /// </summary>
    private static ArchidektCredentials Create(
        string? username = null,
        string? password = null,
        string? file = null)
    {
        return new ArchidektCredentials(ArchidektOptions.CreateDefault(username, password, file));
    }
}
