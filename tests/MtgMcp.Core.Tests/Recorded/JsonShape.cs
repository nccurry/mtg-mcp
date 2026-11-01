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
    /// Lists how the actual JSON departs from a recording, so an empty result means it holds the recorded shape. A
    /// recorded path the actual JSON lacks, a value kind the recording never shows for a path, and an array size
    /// bucket that differs in either direction are reported, as is an ignored path that the recording does not have.
    /// Paths under an ignored path are skipped.
    /// </summary>
    internal static List<string> Divergences(string recordedJson, string actualJson, IReadOnlyCollection<string> ignored)
    {
        Dictionary<string, HashSet<string>> recorded = KindsByPath(Of(recordedJson));
        Dictionary<string, HashSet<string>> actual = KindsByPath(Of(actualJson));
        List<string> problems = [];
        foreach (string path in ignored)
        {
            if (!recorded.Keys.Any(recordedPath => IsUnder(recordedPath, path)))
            {
                problems.Add($"{path} is ignored but the recording has no such path");
            }
        }

        foreach ((string path, HashSet<string> kinds) in recorded)
        {
            if (ignored.Any(prefix => IsUnder(path, prefix)))
            {
                continue;
            }

            if (!actual.TryGetValue(path, out HashSet<string>? seen))
            {
                problems.Add($"{path} is missing; recorded {string.Join("|", kinds)}");
                continue;
            }

            foreach (string kind in seen.Where(kind => !kinds.Contains(kind)))
            {
                problems.Add($"{path} is {kind}; recorded {string.Join("|", kinds)}");
            }

            foreach (string kind in kinds.Where(kind => IsArrayBucket(kind) && !seen.Contains(kind)))
            {
                problems.Add($"{path} never shows {kind}; seen {string.Join("|", seen)}");
            }
        }

        return problems;
    }

    /// <summary>
    /// Tells whether a path is the prefix itself or lies below it.
    /// </summary>
    private static bool IsUnder(string path, string prefix)
    {
        return path == prefix ||
            path.StartsWith(prefix + ".", StringComparison.Ordinal) ||
            path.StartsWith(prefix + "[]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Tells whether a kind is an array size bucket, which must match the recording exactly.
    /// </summary>
    private static bool IsArrayBucket(string kind)
    {
        return kind.StartsWith("array:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Groups shape pairs by path into the value kinds seen there; an array kind carries its size bucket.
    /// </summary>
    private static Dictionary<string, HashSet<string>> KindsByPath(IEnumerable<string> pairs)
    {
        Dictionary<string, HashSet<string>> kinds = new(StringComparer.Ordinal);
        foreach (string pair in pairs)
        {
            int split = pair.IndexOf(':', StringComparison.Ordinal);
            string path = pair[..split];
            if (!kinds.TryGetValue(path, out HashSet<string>? set))
            {
                set = [];
                kinds[path] = set;
            }

            set.Add(pair[(split + 1)..]);
        }

        return kinds;
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
