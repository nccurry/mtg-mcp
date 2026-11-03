using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Converts storage boundary failures into sanitized MCP outcomes without hiding programming defects.
/// </summary>
internal static class ScryfallToolExecution
{
    /// <summary>
    /// Gives the bytes a page keeps for the parts of the result that are not rows.
    /// </summary>
    private const int HeaderBytes = 2_000;

    /// <summary>
    /// Builds the measure that tells a library how many leading rows fit under the result size limit, for pages whose
    /// rows are unbounded raw provider objects.
    /// </summary>
    internal static Func<IReadOnlyList<T>, int> CountFitting<T>()
    {
        return rows => ResultSizeLimit.CountFitting([.. rows.Select(row => ResultSize.Measure(row) + 1)], HeaderBytes);
    }

    /// <summary>
    /// Executes one adapter operation and maps recognized local persistence failures.
    /// </summary>
    internal static async Task<OperationResult<T>> RunAsync<T>(Func<Task<OperationResult<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or SqliteException)
        {
            return new OperationUnavailable(
                "scryfall-storage-unavailable",
                "Stored Scryfall evidence is unavailable or failed integrity validation.");
        }
    }
}
