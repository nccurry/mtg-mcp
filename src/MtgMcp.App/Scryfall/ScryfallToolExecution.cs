using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Converts storage boundary failures into sanitized MCP outcomes without hiding programming defects.
/// </summary>
internal static class ScryfallToolExecution
{
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
