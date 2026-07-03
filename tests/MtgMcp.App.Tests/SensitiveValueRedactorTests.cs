using MtgMcp.App.Security;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies credential, token, cookie, and path removal from diagnostic text.
/// </summary>
public sealed class SensitiveValueRedactorTests
{
    /// <summary>
    /// Verifies all representative sensitive values are removed, including case-varied Windows paths.
    /// </summary>
    [Fact]
    public void Redact_RepresentativeSecrets_RemovesEveryValue()
    {
        const string token = "secret-token";
        const string cookie = "session-cookie";
        const string windowsPath = "C:\\Secrets\\archidekt.json";
        const string unixPath = "/home/user/.config/mtg-mcp/credentials.json";
        string diagnostic =
            $"token={token}; cookie={cookie}; file=C:\\SECRETS\\ARCHIDEKT.JSON; other={unixPath}";

        string redacted = SensitiveValueRedactor.Redact(
            diagnostic,
            [token, cookie, windowsPath, unixPath]);

        Assert.DoesNotContain(token, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("ARCHIDEKT.JSON", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unixPath, redacted, StringComparison.Ordinal);
        Assert.Equal(4, redacted.Split("[redacted]", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// Verifies empty and overlapping values do not corrupt the remaining diagnostic text.
    /// </summary>
    [Fact]
    public void Redact_EmptyAndOverlappingValues_PreservesNonsensitiveText()
    {
        string redacted = SensitiveValueRedactor.Redact(
            "prefix secret-token suffix",
            [null, string.Empty, "secret", "secret-token"]);

        Assert.Equal("prefix [redacted] suffix", redacted);
    }

    /// <summary>
    /// Verifies required redaction inputs are validated.
    /// </summary>
    [Fact]
    public void Redact_NullInputs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SensitiveValueRedactor.Redact(null!, []));
        Assert.Throws<ArgumentNullException>(() => SensitiveValueRedactor.Redact("text", null!));
    }
}
