using System.Globalization;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Provides small JSON readers for adapter-owned payload mapping code.
/// </summary>
public static class MtgMcpJson
{
    /// <summary>
    /// Lists the response envelopes shared by the adapters that expose paged collections.
    /// </summary>
    private static readonly string[] CommonCollectionProperties = ["results", "data"];

    /// <summary>
    /// Enumerates a root array or the first array-valued envelope property.
    /// </summary>
    public static IEnumerable<JsonElement> EnumerateCollection(
        JsonElement root,
        params string[] propertyNames)
    {
        JsonElement collection;
        if (root.ValueKind == JsonValueKind.Array)
        {
            collection = root;
        }
        else if (!TryGetCollection(root, propertyNames.Length == 0 ? CommonCollectionProperties : propertyNames, out collection))
        {
            yield break;
        }

        foreach (JsonElement item in collection.EnumerateArray())
        {
            yield return item;
        }
    }

    /// <summary>
    /// Reads a string-like property, preserving non-string JSON scalars as raw text.
    /// </summary>
    public static string? GetString(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    /// <summary>
    /// Reads an Int32 property from a numeric value or, by default, from numeric text.
    /// </summary>
    public static int? GetInt(JsonElement element, string propertyName, bool allowString = true)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
        {
            return value;
        }

        return
            allowString
            && property.ValueKind == JsonValueKind.String
            && int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Reads an Int64 property from a numeric value or, by default, from numeric text.
    /// </summary>
    public static long? GetLong(JsonElement element, string propertyName, bool allowString = true)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long value))
        {
            return value;
        }

        return
            allowString
            && property.ValueKind == JsonValueKind.String
            && long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Reads a double property from a numeric value or, by default, from numeric text.
    /// </summary>
    public static double? GetDouble(JsonElement element, string propertyName, bool allowString = true)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double value))
        {
            return value;
        }

        return
            allowString
            && property.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Reads a boolean property from a bool value or, by default, from boolean text.
    /// </summary>
    public static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out bool value) => value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads a boolean property with a caller-owned fallback value.
    /// </summary>
    public static bool GetBool(JsonElement element, string propertyName, bool defaultValue)
    {
        return GetBool(element, propertyName) ?? defaultValue;
    }

    /// <summary>
    /// Reads a string-like property from a nested object.
    /// </summary>
    public static string? GetNestedString(
        JsonElement element,
        string propertyName,
        string nestedPropertyName)
    {
        return TryGetObjectProperty(element, propertyName, out JsonElement nested)
            ? GetString(nested, nestedPropertyName)
            : null;
    }

    /// <summary>
    /// Reads an Int32 property from a nested object.
    /// </summary>
    public static int? GetNestedInt(
        JsonElement element,
        string propertyName,
        string nestedPropertyName,
        bool allowString = true)
    {
        return TryGetObjectProperty(element, propertyName, out JsonElement nested)
            ? GetInt(nested, nestedPropertyName, allowString)
            : null;
    }

    /// <summary>
    /// Reads an Int64 property from a nested object.
    /// </summary>
    public static long? GetNestedLong(
        JsonElement element,
        string propertyName,
        string nestedPropertyName,
        bool allowString = true)
    {
        return TryGetObjectProperty(element, propertyName, out JsonElement nested)
            ? GetLong(nested, nestedPropertyName, allowString)
            : null;
    }

    /// <summary>
    /// Reads a double property from a nested object.
    /// </summary>
    public static double? GetNestedDouble(
        JsonElement element,
        string propertyName,
        string nestedPropertyName,
        bool allowString = true)
    {
        return TryGetObjectProperty(element, propertyName, out JsonElement nested)
            ? GetDouble(nested, nestedPropertyName, allowString)
            : null;
    }

    /// <summary>
    /// Reads array entries as strings, preserving non-string values as raw text.
    /// </summary>
    public static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in property.EnumerateArray())
        {
            string? value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Finds the first configured envelope property that contains an array.
    /// </summary>
    private static bool TryGetCollection(
        JsonElement root,
        IReadOnlyList<string> propertyNames,
        out JsonElement collection)
    {
        foreach (string propertyName in propertyNames)
        {
            if (
                root.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.Array
            )
            {
                collection = property;
                return true;
            }
        }

        collection = default;
        return false;
    }

    /// <summary>
    /// Reads a nested object property before nested scalar readers inspect its fields.
    /// </summary>
    private static bool TryGetObjectProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        return
            element.TryGetProperty(propertyName, out property)
            && property.ValueKind == JsonValueKind.Object;
    }
}
