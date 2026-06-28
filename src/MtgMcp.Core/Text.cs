namespace MtgMcp.Core;

/// <summary>
/// Provides shared text normalization helpers.
/// </summary>
public static class MtgMcpText
{
    /// <summary>
    /// Returns the first value that is not null, empty, or whitespace.
    /// </summary>
    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
