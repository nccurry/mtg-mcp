using System.Globalization;

namespace MtgMcp.Scryfall;

/// <summary>
/// Converts fixed UUID and UTC values between C# and SQLite.
/// </summary>
internal static class ScryfallSql
{
    /// <summary>
    /// Formats one UUID for stable SQLite comparisons.
    /// </summary>
    internal static string FormatGuid(Guid value)
    {
        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts an optional UUID to a SQLite parameter value.
    /// </summary>
    internal static object OptionalGuid(Guid? value)
    {
        return value is Guid id ? FormatGuid(id) : DBNull.Value;
    }

    /// <summary>
    /// Parses one stored UUID or reports corrupt SQLite data.
    /// </summary>
    internal static Guid ParseGuid(string value)
    {
        return Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new InvalidDataException("The Scryfall database contains an invalid UUID.");
    }

    /// <summary>
    /// Formats one timestamp in canonical UTC round-trip form.
    /// </summary>
    internal static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses one stored timestamp or reports corrupt SQLite data.
    /// </summary>
    internal static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : throw new InvalidDataException("The Scryfall database contains an invalid timestamp.");
    }
}
