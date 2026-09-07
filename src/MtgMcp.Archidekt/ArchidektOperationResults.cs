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
                    exception.Message),
                ArchidektFailureKind.NotFound => new OperationNotFound(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.Conflict => new OperationConflict(
                    exception.ReasonCode,
                    exception.Message),
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
        catch (ArgumentException)
        {
            return new OperationInvalidInput(
                "invalid-archidekt-input",
                "The Archidekt operation input is invalid.");
        }
        catch (InvalidDataException)
        {
            return new OperationUnavailable(
                "baseline-unavailable",
                "The stored Archidekt synchronization baseline is unavailable.");
        }
    }
}
