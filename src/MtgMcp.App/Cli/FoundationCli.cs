using MtgMcp.App.Configuration;
using MtgMcp.App.Security;
using MtgMcp.Core.Results;

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

        List<string> configurationArguments = [];
        int smokeArgumentCount = 0;
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, SmokeArgument, StringComparison.Ordinal))
            {
                smokeArgumentCount++;
            }
            else
            {
                configurationArguments.Add(argument);
            }
        }

        if (smokeArgumentCount != 1)
        {
            error.WriteLine("Only --smoke is available while the 0.9 MCP foundation is under construction.");
            return 2;
        }

        OperationResult<FoundationConfiguration> configurationResult =
            FoundationConfigurationLoader.Load(configurationArguments);
        if (configurationResult is OperationSuccess<FoundationConfiguration>)
        {
            output.WriteLine("mtg-mcp foundation process ready");
            return 0;
        }

        string failureMessage = SensitiveValueRedactor.Redact(
            GetFailureMessage(configurationResult),
            configurationArguments);
        error.WriteLine(failureMessage);
        return 2;
    }

    /// <summary>
    /// Selects the sanitized public message from a failed configuration result.
    /// </summary>
    private static string GetFailureMessage(OperationResult<FoundationConfiguration> result)
    {
        if (result.Value is OperationInvalidInput invalidInput)
        {
            return invalidInput.Message;
        }

        if (result.Value is OperationUnavailable unavailable)
        {
            return unavailable.Message;
        }

        return "Configuration could not be loaded.";
    }
}
