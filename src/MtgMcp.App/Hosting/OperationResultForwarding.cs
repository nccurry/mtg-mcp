using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Carries a failed operation result from one result type to another without unsafe casts.
/// </summary>
internal static class OperationResultForwarding
{
    /// <summary>
    /// Returns the same failure as a result of the target type; a success has no failure to carry.
    /// </summary>
    internal static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(OperationResult<TSource> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            _ => new OperationUnavailable(
                "unexpected-operation-state",
                "The operation returned an unexpected state."),
        };
    }
}
