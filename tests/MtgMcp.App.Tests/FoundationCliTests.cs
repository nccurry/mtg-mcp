using System.Globalization;
using MtgMcp.App.Cli;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the temporary foundation process contract.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class FoundationCliTests
{
    /// <summary>
    /// Verifies that the smoke probe succeeds without claiming MCP capabilities.
    /// </summary>
    [Fact]
    public void Run_WithSmokeArgument_ReportsReadiness()
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = FoundationCli.Run(["--smoke"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal($"mtg-mcp foundation process ready{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>
    /// Verifies that unsupported arguments fail instead of starting a partial server.
    /// </summary>
    [Theory]
    [InlineData()]
    [InlineData("--unknown")]
    [InlineData("--smoke", "--smoke")]
    public void Run_WithUnsupportedArguments_ExplainsTheBoundary(params string[] arguments)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = FoundationCli.Run(arguments, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"Only --smoke is available while the 0.9 MCP foundation is under construction.{Environment.NewLine}",
            error.ToString());
    }

    /// <summary>
    /// Verifies configuration switches are accepted with the smoke probe and normalize successfully.
    /// </summary>
    [Fact]
    public void Run_WithValidConfiguration_ReportsReadiness()
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = FoundationCli.Run(
            ["--smoke", "--mode", "READ-ONLY", "--data-dir", Path.GetTempPath()],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal($"mtg-mcp foundation process ready{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>
    /// Verifies invalid startup configuration fails with a sanitized message.
    /// </summary>
    [Fact]
    public void Run_WithInvalidMode_ReturnsSanitizedFailure()
    {
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = FoundationCli.Run(
            ["--smoke", "--mode", "private-invalid-value"],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"Operation mode must be read-only, local, or remote.{Environment.NewLine}",
            error.ToString());
        Assert.DoesNotContain("private-invalid-value", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies unknown configuration switches fail even when they include a value.
    /// </summary>
    [Fact]
    public void Run_WithUnknownConfigurationSwitch_ReturnsSanitizedFailure()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);

        int exitCode = FoundationCli.Run(
            ["--smoke", "--private-switch", "private-value"],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            $"Configuration contains an unsupported or incomplete command-line option.{Environment.NewLine}",
            error.ToString());
        Assert.DoesNotContain("private-switch", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that required collaborators are validated at the process boundary.
    /// </summary>
    [Fact]
    public void Run_WithNullCollaborators_Throws()
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentNullException>(() => FoundationCli.Run(null!, writer, writer));
        Assert.Throws<ArgumentNullException>(() => FoundationCli.Run([], null!, writer));
        Assert.Throws<ArgumentNullException>(() => FoundationCli.Run([], writer, null!));
    }
}
