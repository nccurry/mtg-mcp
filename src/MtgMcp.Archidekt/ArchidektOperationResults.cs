using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Converts adapter operation outcomes into the shared result union.
/// </summary>
internal static class ArchidektOperationResults
{
    /// <summary>
    /// Runs one operation with a new request budget.
    /// </summary>
    internal static Task<OperationResult<T>> ExecuteAsync<T>(
        int maximumRequestsPerOperation,
        Func<ArchidektOperationBudget, Task<T>> operation)
    {
        return ExecuteAsync(new ArchidektOperationBudget(maximumRequestsPerOperation), operation);
    }

    /// <summary>
    /// Runs one operation against an existing composed-operation budget.
    /// </summary>
    internal static async Task<OperationResult<T>> ExecuteAsync<T>(
        ArchidektOperationBudget budget,
        Func<ArchidektOperationBudget, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return new OperationSuccess<T>(await operation(budget).ConfigureAwait(false));
        }
        catch (ArchidektProviderException exception)
        {
            return exception.Kind switch
            {
                ArchidektFailureKind.InvalidInput => new OperationInvalidInput(
                    exception.ReasonCode,
                    exception.Message,
                    exception.Path),
                ArchidektFailureKind.NotFound => new OperationNotFound(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.Conflict => new OperationConflict(
                    exception.ReasonCode,
                    exception.Message,
                    exception.CurrentRemoteFingerprint is null && exception.CurrentPreviewFingerprint is null
                        ? null
                        : new ConflictState(
                            RemoteFingerprint: exception.CurrentRemoteFingerprint,
                            PreviewFingerprint: exception.CurrentPreviewFingerprint)),
                ArchidektFailureKind.Unsupported => new OperationUnsupported(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.Unavailable => new OperationUnavailable(
                    exception.ReasonCode,
                    exception.Message),
                _ => new OperationUnavailable(
                    "provider-unavailable",
                    "Archidekt could not complete the operation."),
            };
        }
        catch (ArgumentException exception)
        {
            return new OperationInvalidInput(
                "invalid-archidekt-input",
                "The Archidekt operation input is invalid.",
                ArgumentPath(exception.ParamName));
        }
        catch (InvalidDataException)
        {
            return new OperationUnavailable(
                "baseline-unavailable",
                "The stored Archidekt synchronization baseline is unavailable.");
        }
    }

    /// <summary>
    /// Names the tool argument for the parameter an adapter check refused, in the camelCase the tools publish.
    /// </summary>
    private static string? ArgumentPath(string? parameterName)
    {
        return string.IsNullOrEmpty(parameterName)
            ? null
            : char.ToLowerInvariant(parameterName[0]) + parameterName[1..];
    }

    /// <summary>
    /// Requires an exact non-case-folded confirmation phrase, and refuses with the phrase to pass when it differs.
    /// </summary>
    internal static void RequireConfirmation(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "confirmation-required",
                $"Pass confirmation exactly as: {expected}")
            {
                Path = "confirmation",
            };
        }
    }
}
