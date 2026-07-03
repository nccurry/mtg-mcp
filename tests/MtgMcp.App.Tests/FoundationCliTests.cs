using System.Globalization;
using MtgMcp.App.Cli;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the temporary foundation process contract.
/// </summary>
public sealed class FoundationCliTests
{
    /// <summary>
    /// Verifies that the smoke probe succeeds without claiming MCP capabilities.
    /// </summary>
    [Fact]
    public void Run_WithSmokeArgument_ReportsReadiness()
    {
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
