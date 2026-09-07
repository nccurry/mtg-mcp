using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Converts Archidekt provider identifiers without losing numeric or opaque forms.
/// </summary>
internal static class ArchidektProviderId
{
    /// <summary>
    /// Preserves numeric provider IDs as numbers and all other explicit IDs as strings.
    /// </summary>
    internal static object? Parse(string? value)
    {
        string? normalized = ArchidektContract.Optional(value);
        return long.TryParse(normalized, out long number) ? number : normalized;
    }

    /// <summary>
    /// Reads one provider ID stored as either a JSON string or number.
    /// </summary>
    internal static bool TryRead(JsonElement value, string propertyName, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        result = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(result);
    }
}
