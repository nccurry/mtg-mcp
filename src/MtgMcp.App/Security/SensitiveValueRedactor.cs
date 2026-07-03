namespace MtgMcp.App.Security;

/// <summary>
/// Removes caller-identified credentials, tokens, cookies, and secret paths from diagnostic text.
/// </summary>
internal static class SensitiveValueRedactor
{
    /// <summary>
    /// Replaces each distinct nonempty sensitive value without otherwise changing the text.
    /// </summary>
    internal static string Redact(string text, IEnumerable<string?> sensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sensitiveValues);

        HashSet<string> distinctValues = new(StringComparer.Ordinal);
        foreach (string? sensitiveValue in sensitiveValues)
        {
            if (!string.IsNullOrWhiteSpace(sensitiveValue))
            {
                distinctValues.Add(sensitiveValue);
            }
        }

        List<string> orderedValues = [.. distinctValues];
        orderedValues.Sort(static (left, right) => right.Length.CompareTo(left.Length));

        string redacted = text;
        foreach (string sensitiveValue in orderedValues)
        {
            StringComparison comparison = IsWindowsAbsolutePath(sensitiveValue)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            redacted = redacted.Replace(sensitiveValue, "[redacted]", comparison);
        }

        return redacted;
    }

    /// <summary>
    /// Reports whether a value is an absolute Windows path whose casing is not significant.
    /// </summary>
    private static bool IsWindowsAbsolutePath(string value)
    {
        return value.Length >= 3 &&
            char.IsAsciiLetter(value[0]) &&
            value[1] == ':' &&
            (value[2] == '\\' || value[2] == '/');
    }
}
