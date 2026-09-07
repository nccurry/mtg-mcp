using System.Globalization;

namespace MtgMcp.Spellbook;

/// <summary>
/// Formats and parses the UTC values stored in the Commander Spellbook cache.
/// </summary>
internal static class SpellbookSql
{
    /// <summary>
    /// Formats one UTC instant in a fixed round-trip form for SQLite storage.
    /// </summary>
    internal static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses one cache timestamp and returns it as UTC.
    /// </summary>
    internal static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
    }
}
