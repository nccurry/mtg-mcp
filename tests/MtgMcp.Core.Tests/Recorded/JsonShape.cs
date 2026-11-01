using System.Text.Json;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Reduces JSON to sorted path and value-kind pairs so a test compares shape instead of exact bytes.
/// </summary>
internal static class JsonShape
{
    /// <summary>
    /// Lists the path and kind of every value; arrays also record an empty, single, or many bucket.
    /// </summary>
    internal static SortedSet<string> Of(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        SortedSet<string> pairs = new(StringComparer.Ordinal);
        Walk(document.RootElement, "$", pairs);
        return pairs;
    }

    /// <summary>
    /// Lists the pairs the recorded JSON has that the actual JSON lacks, so an empty result means a subset match.
    /// </summary>
    internal static IReadOnlyList<string> Missing(string recordedJson, string actualJson)
    {
        SortedSet<string> missing = Of(recordedJson);
        missing.ExceptWith(Of(actualJson));
        return [.. missing];
    }

    /// <summary>
    /// Adds the pairs for one value and its children.
    /// </summary>
    private static void Walk(JsonElement element, string path, ISet<string> pairs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                pairs.Add($"{path}:object");
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Walk(property.Value, $"{path}.{property.Name}", pairs);
                }

                break;
            case JsonValueKind.Array:
                int count = element.GetArrayLength();
                pairs.Add($"{path}:array:{(count == 0 ? "0" : count == 1 ? "1" : "many")}");
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[]", pairs);
                }

                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                pairs.Add($"{path}:boolean");
                break;
            default:
                pairs.Add($"{path}:{element.ValueKind.ToString().ToLowerInvariant()}");
                break;
        }
    }
}
