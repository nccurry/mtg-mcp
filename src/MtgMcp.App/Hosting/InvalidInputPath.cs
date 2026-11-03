using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Names the argument of a provider rejection. A provider that refuses a request as invalid does not say which
/// value it refused, so the tool that built the request names the argument it sent the value from.
/// </summary>
internal static class InvalidInputPath
{
    /// <summary>
    /// Returns the result unchanged, except that an invalid-input failure without an argument path gets
    /// <paramref name="path"/>.
    /// </summary>
    internal static OperationResult<T> Default<T>(OperationResult<T> result, string path)
    {
        return result is OperationInvalidInput { Path: null } rejected
            ? new OperationInvalidInput(rejected.ReasonCode, rejected.Message, path)
            : result;
    }

    /// <summary>
    /// Awaits the result and names <paramref name="path"/> on an invalid-input failure that has no argument path.
    /// </summary>
    internal static async Task<OperationResult<T>> DefaultAsync<T>(Task<OperationResult<T>> result, string path)
    {
        return Default(await result.ConfigureAwait(false), path);
    }
}
