using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Walks every tool's published input schema to enforce rules that apply to the whole tool surface.
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
        List<string> missing = [];
        foreach (McpServerTool tool in CreateTools())
        {
            Walk(tool.ProtocolTool.Name, "$", tool.ProtocolTool.InputSchema, missing);
        }

        Assert.True(missing.Count == 0, "Choice fields without enum:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Verifies the annotated choice parameters are present, so the walk above is not vacuous.
    /// </summary>
    [Fact]
    public void KnownChoiceParameters_PublishEnum()
    {
        Dictionary<string, McpServerTool> tools = CreateTools().ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        Assert.Equal(
            ["default", "cache-only", "refresh"],
            EnumValues(tools["scryfall_search"].ProtocolTool.InputSchema, "freshnessPolicy"));
        Assert.Equal(
            ["auto", "asc", "desc"],
            EnumValues(tools["scryfall_search"].ProtocolTool.InputSchema, "direction"));
        Assert.Contains(
            "moxfield-bulk-edit-v1",
            EnumValues(tools["deck_export_bundle"].ProtocolTool.InputSchema, "formatId"));
    }

    /// <summary>
    /// Verifies every tool description, including its examples, fits the length clients keep for loaded tools.
    /// </summary>
    [Fact]
    public void Descriptions_FitClientLimit()
    {
        List<string> tooLong = CreateTools()
            .Where(tool => (tool.ProtocolTool.Description?.Length ?? 0) > MaximumDescriptionLength)
            .Select(tool => $"{tool.ProtocolTool.Name}: {tool.ProtocolTool.Description!.Length}")
            .ToList();

        Assert.True(tooLong.Count == 0, string.Join("\n", tooLong));
    }

    /// <summary>
    /// Builds every tool declared in the App assembly with the serializer options the server registers.
    /// </summary>
    private static List<McpServerTool> CreateTools()
    {
        List<McpServerTool> tools = [];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type type in typeof(ToolCallPolicy).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
                {
                    continue;
                }

                McpServerToolCreateOptions options = new() { SerializerOptions = ToolCallPolicy.SerializerOptions };
                tools.Add(method.IsStatic
                    ? McpServerTool.Create(method, target: null, options)
                    : McpServerTool.Create(method, _ => throw new InvalidOperationException("Schema only."), options));
            }
        }

        Assert.NotEmpty(tools);
        return tools;
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
            !node.TryGetProperty("enum", out _))
        {
            missing.Add($"{tool} {path}: {description.GetString()}");
        }

        foreach (JsonProperty property in node.EnumerateObject())
        {
            Walk(tool, $"{path}.{property.Name}", property.Value, missing);
        }
    }

    /// <summary>
    /// Reports whether a schema node describes a string, alone or with null.
    /// </summary>
    private static bool IsString(JsonElement node)
    {
        if (!node.TryGetProperty("type", out JsonElement type))
        {
            return false;
        }

        return type.ValueKind == JsonValueKind.String
            ? type.GetString() == "string"
            : type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.GetString() == "string");
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
    /// Matches a value list that starts the description or follows a colon or "one of", such as ": default, cache-only, or refresh".
    /// </summary>
    [GeneratedRegex(@"(?:^|: |one of )[a-z][a-z0-9-]*(?:, [a-z][a-z0-9-]*)*,? or (?!null\b)[a-z][a-z0-9-]*\.?(?:\s|$)")]
    private static partial Regex ListsValues();
}
