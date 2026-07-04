using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.App.Security;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Cli;

/// <summary>
/// Selects the one-shot readiness probe or the long-running stdio MCP host.
/// </summary>
internal static class FoundationCli
{
    /// <summary>
    /// Selects the one-shot process-readiness probe.
    /// </summary>
    private const string SmokeArgument = "--smoke";

    /// <summary>
    /// Runs validated configuration through either the probe or MCP host boundary.
    /// </summary>
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        List<string> configurationArguments = [];
        int smokeArgumentCount = 0;
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, SmokeArgument, StringComparison.Ordinal))
            {
                bool interruptsPair = index > 0 &&
                    arguments[index - 1].StartsWith("--", StringComparison.Ordinal) &&
                    !arguments[index - 1].Contains('=');
                if (interruptsPair)
                {
                    smokeArgumentCount = 2;
                    break;
                }

                smokeArgumentCount++;
            }
            else
            {
                configurationArguments.Add(argument);
            }
        }

        if (smokeArgumentCount > 1)
        {
            error.WriteLine(
                "Configuration contains an unsupported or incomplete command-line option.");
            return 2;
        }

        OperationResult<FoundationConfiguration> configurationResult =
            FoundationConfigurationLoader.Load(configurationArguments);
        if (configurationResult is not OperationSuccess<FoundationConfiguration> configuration)
        {
            string failureMessage = SensitiveValueRedactor.Redact(
                GetFailureMessage(configurationResult),
                configurationArguments);
            error.WriteLine(failureMessage);
            return 2;
        }

        if (smokeArgumentCount == 1)
        {
            output.WriteLine("mtg-mcp foundation process ready");
            return 0;
        }

        try
        {
            await FoundationHost.RunAsync(configuration.Data, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception)
        {
            error.WriteLine("The MCP server stopped because its host became unavailable.");
            return 1;
        }
    }

    /// <summary>
    /// Selects the sanitized public message from a failed configuration result.
    /// </summary>
    private static string GetFailureMessage(OperationResult<FoundationConfiguration> result)
    {
        return result switch
        {
            OperationSuccess<FoundationConfiguration> => "Configuration could not be loaded.",
            OperationNotFound value => value.Message,
            OperationNotCached value => value.Message,
            OperationUnsupported value => value.Message,
            OperationUnavailable value => value.Message,
            OperationConflict value => value.Message,
            OperationInvalidInput value => value.Message,
        };
    }
}
