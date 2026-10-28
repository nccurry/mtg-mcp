using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Walks every registered tool's published input schema to enforce rules that apply to the whole tool surface.
/// </summary>
public sealed partial class ToolSchemaSurfaceTests
{
    /// <summary>
    /// Limits each tool description to what Claude Code keeps for tools it loads at session start.
    /// </summary>
    private const int MaximumDescriptionLength = 4096;

    /// <summary>
    /// Verifies every string field whose description lists its allowed values publishes them as a schema enum.
    /// </summary>
    [Fact]
    public void ChoiceFields_PublishEnum()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> missing = [];
        foreach (McpServerTool tool in registered.Tools)
        {
            Walk(tool.ProtocolTool.Name, "$", tool.ProtocolTool.InputSchema, missing);
        }

        Assert.True(missing.Count == 0, "Choice fields without enum:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Verifies the walk reports a described value list that publishes no enum, in every way a string can be typed.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"string","description":"Mode: x, y, or z."}""")]
    [InlineData("""{"type":["string","null"],"description":"Mode: x, y, or z."}""")]
    [InlineData("""{"anyOf":[{"type":"null"},{"type":"string"}],"description":"Mode: x, y, or z."}""")]
    [InlineData("""{"oneOf":[{"type":"string"},{"type":"null"}],"description":"x, y, or z"}""")]
    public void Walk_ListedValuesWithoutEnum_AreReported(string schemaJson)
    {
        using JsonDocument schema = JsonDocument.Parse(schemaJson);
        List<string> missing = [];

        Walk("fixture", "$", schema.RootElement, missing);

        Assert.Single(missing);
    }

    /// <summary>
    /// Verifies the walk accepts the same fields once the enum is published, on the node or on a string member.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"string","enum":["x","y","z"],"description":"Mode: x, y, or z."}""")]
    [InlineData("""{"anyOf":[{"type":"null"},{"type":"string","enum":["x","y","z"]}],"description":"x, y, or z"}""")]
    public void Walk_ListedValuesWithEnum_AreNotReported(string schemaJson)
    {
        using JsonDocument schema = JsonDocument.Parse(schemaJson);
        List<string> missing = [];

        Walk("fixture", "$", schema.RootElement, missing);

        Assert.Empty(missing);
    }

    /// <summary>
    /// Verifies the annotated choice parameters are present, so the walk above is not vacuous.
    /// </summary>
    [Fact]
    public void KnownChoiceParameters_PublishEnum()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        Dictionary<string, McpServerTool> tools = registered.Tools
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        Assert.Equal(
            ["default", "cache-only", "refresh"],
            EnumValues(tools["scryfall_search"].ProtocolTool.InputSchema, "freshnessPolicy"));
        Assert.Equal(
            ["auto", "asc", "desc"],
            EnumValues(tools["scryfall_search"].ProtocolTool.InputSchema, "direction"));
        Assert.Contains(
            "moxfield-bulk-edit-v1",
            EnumValues(tools["local_deck_export_bundle"].ProtocolTool.InputSchema, "formatId"));
        Assert.Equal(
            ["concise", "standard", "full"],
            EnumValues(tools["scryfall_search"].ProtocolTool.InputSchema, "responseFormat"));
        Assert.Equal(
            ["cards", "tags"],
            ItemEnumValues(tools["local_deck_get"].ProtocolTool.InputSchema, "include"));
        Assert.Equal(
            ScryfallConciseFields.All,
            ItemEnumValues(tools["scryfall_search"].ProtocolTool.InputSchema, "fields"));
    }

    /// <summary>
    /// Verifies every tool description, including its examples, fits the length clients keep for loaded tools.
    /// </summary>
    [Fact]
    public void Descriptions_FitClientLimit()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> tooLong = registered.Tools
            .Where(tool => (tool.ProtocolTool.Description?.Length ?? 0) > MaximumDescriptionLength)
            .Select(tool => $"{tool.ProtocolTool.Name}: {tool.ProtocolTool.Description!.Length}")
            .ToList();

        Assert.True(tooLong.Count == 0, string.Join("\n", tooLong));
    }

    /// <summary>
    /// Verifies the registration this suite inspects holds the full tool surface, so no rule is checked on a subset.
    /// </summary>
    [Fact]
    public void RegisteredToolSet_HoldsEveryToolOnce()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);

        string[] names = [.. registered.Tools.Select(tool => tool.ProtocolTool.Name)];

        Assert.Equal(80, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Records every string schema node that describes a closed list of values but publishes no enum.
    /// </summary>
    private static void Walk(string tool, string path, JsonElement node, List<string> missing)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in node.EnumerateArray())
            {
                Walk(tool, $"{path}[{index++}]", item, missing);
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (IsString(node) &&
            node.TryGetProperty("description", out JsonElement description) &&
            ListsValues().IsMatch(description.GetString() ?? string.Empty) &&
            !PublishesEnum(node))
        {
            missing.Add($"{tool} {path}: {description.GetString()}");
        }

        foreach (JsonProperty property in node.EnumerateObject())
        {
            Walk(tool, $"{path}.{property.Name}", property.Value, missing);
        }
    }

    /// <summary>
    /// Reports whether a schema node describes a string, alone, with null, or as one member of anyOf or oneOf.
    /// </summary>
    private static bool IsString(JsonElement node)
    {
        if (node.TryGetProperty("type", out JsonElement type))
        {
            return type.ValueKind == JsonValueKind.String
                ? type.GetString() == "string"
                : type.ValueKind == JsonValueKind.Array &&
                    type.EnumerateArray().Any(value => value.GetString() == "string");
        }

        return Members(node).Any(IsString);
    }

    /// <summary>
    /// Reports whether a schema node, or one string member of its anyOf or oneOf, publishes an enum.
    /// </summary>
    private static bool PublishesEnum(JsonElement node)
    {
        return node.TryGetProperty("enum", out _) || Members(node).Any(PublishesEnum);
    }

    /// <summary>
    /// Lists the alternatives of an anyOf or oneOf node.
    /// </summary>
    private static IEnumerable<JsonElement> Members(JsonElement node)
    {
        foreach (string keyword in new[] { "anyOf", "oneOf" })
        {
            if (node.TryGetProperty(keyword, out JsonElement members) && members.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement member in members.EnumerateArray())
                {
                    yield return member;
                }
            }
        }
    }

    /// <summary>
    /// Reads the enum values published for the items of one list input property.
    /// </summary>
    private static string[] ItemEnumValues(JsonElement schema, string property)
    {
        JsonElement node = schema.GetProperty("properties").GetProperty(property);
        JsonElement items = node.TryGetProperty("items", out JsonElement direct)
            ? direct
            : node.GetProperty("anyOf").EnumerateArray().First(value => value.TryGetProperty("items", out _))
                .GetProperty("items");
        return [.. items.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>
    /// Reads the enum values published for one top-level input property.
    /// </summary>
    private static string?[] EnumValues(JsonElement schema, string property)
    {
        return schema.GetProperty("properties").GetProperty(property).GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
    }

    /// <summary>
    /// Matches a value list that starts the description or follows a colon or "one of",
    /// such as ": default, cache-only, or refresh". Descriptions that name allowed values some other way are not found.
    /// </summary>
    [GeneratedRegex(@"(?:^|: |one of )[a-z][a-z0-9-]*(?:, [a-z][a-z0-9-]*)*,? or (?!null\b)[a-z][a-z0-9-]*\.?(?:\s|$)")]
    private static partial Regex ListsValues();
}
