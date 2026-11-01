using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Shows one differing field of a row: its value on each side. A side that does not hold the row has no value, and a
/// side that holds the row with an empty field has a JSON null.
/// </summary>
public sealed record ArchidektDifferenceField(
    string Name,
    JsonElement? Baseline,
    JsonElement? Local,
    JsonElement? Remote);

/// <summary>
/// Describes one comparison path by the fields whose values differ, instead of the whole value of each side.
/// </summary>
public sealed record ArchidektConciseDifference(
    string Path,
    string State,
    IReadOnlyList<ArchidektDifferenceField> Fields)
    : ArchidektDifferenceRow(Path, State)
{
    /// <summary>
    /// Gets the differing fields in the order the entry or category lists them.
    /// </summary>
    public IReadOnlyList<ArchidektDifferenceField> Fields { get; init; } = Array.AsReadOnly(Fields.ToArray());
}

/// <summary>
/// Turns a difference row into its field-level form. The whole values on a row are the JSON of one entry or category
/// (or one text value), so the fields are read back from that JSON and nothing is recomputed from the decks.
/// </summary>
public static class ArchidektConciseDifferences
{
    /// <summary>
    /// Lists the fields of a row whose values are not the same on the baseline, local, and remote side. A side that
    /// lacks the row counts as a different value, so an added or removed row lists the fields of the sides that hold
    /// it. A row for one text value, such as the deck name, lists that one value under its last path segment.
    /// </summary>
    public static ArchidektConciseDifference From(ArchidektDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        bool holdsObject = difference.Path.StartsWith("/entries/", StringComparison.Ordinal) ||
            difference.Path.StartsWith("/categories/", StringComparison.Ordinal);
        return new ArchidektConciseDifference(
            difference.Path,
            difference.State,
            holdsObject ? ObjectFields(difference) : [TextField(difference)]);
    }

    /// <summary>
    /// Builds the one field of a row that holds a single text value.
    /// </summary>
    private static ArchidektDifferenceField TextField(ArchidektDifference difference)
    {
        return new ArchidektDifferenceField(
            difference.Path[(difference.Path.LastIndexOf('/') + 1)..],
            Text(difference.BaselineValue),
            Text(difference.LocalValue),
            Text(difference.RemoteValue));
    }

    /// <summary>
    /// Wraps a text value as a JSON string, or returns null when the side has no value.
    /// </summary>
    private static JsonElement? Text(string? value)
    {
        return value is null ? null : JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// Lists the fields of an entry or category row that differ between its three sides.
    /// </summary>
    private static List<ArchidektDifferenceField> ObjectFields(ArchidektDifference difference)
    {
        Dictionary<string, JsonElement>?[] sides =
        [
            Properties(difference.BaselineValue),
            Properties(difference.LocalValue),
            Properties(difference.RemoteValue),
        ];
        List<string> names = [];
        foreach (Dictionary<string, JsonElement>? side in sides)
        {
            if (side is null)
            {
                continue;
            }

            foreach (string name in side.Keys)
            {
                if (!names.Contains(name, StringComparer.Ordinal))
                {
                    names.Add(name);
                }
            }
        }

        List<ArchidektDifferenceField> fields = [];
        foreach (string name in names)
        {
            JsonElement? baseline = ValueOf(sides[0], name);
            JsonElement? local = ValueOf(sides[1], name);
            JsonElement? remote = ValueOf(sides[2], name);
            if (!SameValue(baseline, local) || !SameValue(local, remote))
            {
                fields.Add(new ArchidektDifferenceField(name, baseline, local, remote));
            }
        }

        return fields;
    }

    /// <summary>
    /// Reads the properties of the JSON object a row side holds, or returns null when the side lacks the row.
    /// </summary>
    private static Dictionary<string, JsonElement>? Properties(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            properties[property.Name] = property.Value.Clone();
        }

        return properties;
    }

    /// <summary>
    /// Gets one property of a row side, or null when the side lacks the row or the property.
    /// </summary>
    private static JsonElement? ValueOf(Dictionary<string, JsonElement>? side, string name)
    {
        return side is not null && side.TryGetValue(name, out JsonElement value) ? value : null;
    }

    /// <summary>
    /// Tells whether two sides hold the same JSON value, or both hold none.
    /// </summary>
    private static bool SameValue(JsonElement? left, JsonElement? right)
    {
        return left is null || right is null
            ? left is null && right is null
            : string.Equals(left.Value.GetRawText(), right.Value.GetRawText(), StringComparison.Ordinal);
    }
}
