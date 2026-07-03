namespace MtgMcp.App.Cli;

/// <summary>
/// Provides the temporary process probe used while the evidence-first host is assembled.
/// </summary>
internal static class FoundationCli
{
    /// <summary>
    /// Selects the temporary process-readiness probe.
    /// </summary>
    private const string SmokeArgument = "--smoke";

    /// <summary>
    /// Runs the foundation command line without advertising an MCP surface that is not implemented yet.
    /// </summary>
    internal static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (arguments.Count == 1 &&
            string.Equals(arguments[0], SmokeArgument, StringComparison.Ordinal))
        {
            output.WriteLine("mtg-mcp foundation process ready");
            return 0;
        }

        error.WriteLine("Only --smoke is available while the 0.9 MCP foundation is under construction.");
        return 2;
    }
}
