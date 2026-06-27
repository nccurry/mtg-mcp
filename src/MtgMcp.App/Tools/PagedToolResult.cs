using System.Globalization;
using System.Text;

namespace MtgMcp.App;

/// <summary>
/// Represents one cursor-paged MCP tool result.
/// </summary>
public sealed record PagedToolResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    int Limit,
    int TotalCount);

/// <summary>
/// Applies a shared cursor contract to bounded in-memory tool lists.
/// </summary>
public static class ToolPagination
{
    /// <summary>
    /// Maximum page size accepted by list-style MCP tools.
    /// </summary>
    public const int MaxLimit = 200;

    /// <summary>
    /// Returns a bounded page and an opaque cursor for the next page.
    /// </summary>
    public static PagedToolResult<T> Page<T>(IReadOnlyList<T> values, int limit, string? cursor)
    {
        int pageSize = Math.Clamp(limit, 1, MaxLimit);
        int offset = DecodeCursor(cursor);
        if (offset > values.Count)
        {
            offset = values.Count;
        }

        int pageCount = Math.Min(pageSize, values.Count - offset);
        List<T> items = new(pageCount);
        for (int index = 0; index < pageCount; index++)
        {
            items.Add(values[offset + index]);
        }

        int nextOffset = offset + pageCount;
        string? nextCursor = nextOffset < values.Count ? EncodeCursor(nextOffset) : null;
        return new PagedToolResult<T>(items, nextCursor, pageSize, values.Count);
    }

    /// <summary>
    /// Encodes a zero-based item offset as an opaque cursor.
    /// </summary>
    private static string EncodeCursor(int offset)
    {
        string value = Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString("D", CultureInfo.InvariantCulture)));
        return value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decodes an opaque cursor returned by a previous page.
    /// </summary>
    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        string padded = cursor.Trim().Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        try
        {
            string value = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) && offset >= 0)
            {
                return offset;
            }
        }
        catch (FormatException exception)
        {
            throw CreateInvalidCursorException(nameof(cursor), exception);
        }

        throw CreateInvalidCursorException(nameof(cursor));
    }

    /// <summary>
    /// Creates the public validation error used for malformed cursor values.
    /// </summary>
    private static ArgumentException CreateInvalidCursorException(string paramName, Exception? innerException = null)
    {
        return new ArgumentException(
            "cursor must be a nextCursor value returned by a previous page.",
            paramName,
            innerException);
    }
}
