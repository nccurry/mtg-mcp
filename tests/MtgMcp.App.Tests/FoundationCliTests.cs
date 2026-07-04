using System.Globalization;
using MtgMcp.App.Cli;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the one-shot foundation process contract and sanitized startup failures.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class FoundationCliTests
{
    /// <summary>
    /// Verifies that the smoke probe succeeds without opening the long-running MCP host.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithSmokeArgument_ReportsReadiness()
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = await FoundationCli.RunAsync(
            ["--smoke"],
            output,
            error,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(0, exitCode);
        Assert.Equal($"mtg-mcp process ready{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>
    /// Verifies both supported configuration switch forms use the same smoke boundary.
    /// </summary>
    [Theory]
    [InlineData("--mode", "READ-ONLY")]
    [InlineData("--mode=READ-ONLY")]
    [InlineData("--toolsets", "decks")]
    [InlineData("--toolsets=decks")]
    public async Task RunAsync_WithValidConfiguration_ReportsReadiness(params string[] configurationArguments)
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        List<string> arguments = ["--smoke", .. configurationArguments];

        int exitCode = await FoundationCli.RunAsync(
            arguments,
            output,
            error,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(0, exitCode);
        Assert.Equal($"mtg-mcp process ready{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>
    /// Verifies ambiguous and unsupported arguments fail with one stable sanitized diagnostic.
    /// </summary>
    [Theory]
    [InlineData("--smoke", "--smoke")]
    [InlineData("--smoke", "--unknown")]
    [InlineData("--mode", "--smoke", "local")]
    [InlineData("--smoke", "--mode", "local", "--mode", "remote")]
    [InlineData("--smoke", "--toolsets", "decks", "--toolsets", "none")]
    public async Task RunAsync_WithAmbiguousArguments_ReturnsStableFailure(params string[] arguments)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = await FoundationCli.RunAsync(
            arguments,
            output,
            error,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"Configuration contains an unsupported or incomplete command-line option.{Environment.NewLine}",
            error.ToString());
        Assert.DoesNotContain("unknown", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies invalid startup configuration fails without echoing the rejected value.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithInvalidMode_ReturnsSanitizedFailure()
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = await FoundationCli.RunAsync(
            ["--smoke", "--mode", "private-invalid-value"],
            output,
            error,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"Operation mode must be read-only, local, or remote.{Environment.NewLine}",
            error.ToString());
        Assert.DoesNotContain("private-invalid-value", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an invalid toolset selection fails without echoing its rejected value.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithInvalidToolsets_ReturnsSanitizedFailure()
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        const string rejectedValue = "private-provider";

        int exitCode = await FoundationCli.RunAsync(
            ["--smoke", "--toolsets", rejectedValue],
            output,
            error,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("implemented lowercase capabilities", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(rejectedValue, error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies required collaborators fail through the asynchronous argument boundary.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithNullCollaborators_Throws()
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FoundationCli.RunAsync(
                null!,
                writer,
                writer,
                TestContext.Current.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FoundationCli.RunAsync(
                [],
                null!,
                writer,
                TestContext.Current.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FoundationCli.RunAsync(
                [],
                writer,
                null!,
                TestContext.Current.CancellationToken)).ConfigureAwait(false);
    }
}
