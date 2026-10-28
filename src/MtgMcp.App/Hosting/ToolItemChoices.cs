using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Creates tools whose list parameters publish their allowed item values as a JSON Schema <c>enum</c>, which
/// <c>[AllowedValues]</c> cannot do for the items of a list.
/// </summary>
internal static class ToolItemChoices
{
    /// <summary>
    /// Creates every tool declared on the target, publishing the listed allowed values on the items of each list
    /// parameter whose name is a key of <paramref name="itemChoices"/>.
    /// </summary>
    internal static IReadOnlyList<McpServerTool> Create(
        object target,
        IReadOnlyDictionary<string, IReadOnlyList<string>> itemChoices)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(itemChoices);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        List<McpServerTool> tools = [];
        foreach (MethodInfo method in target.GetType().GetMethods(flags))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            tools.Add(McpServerTool.Create(method, target, new McpServerToolCreateOptions
            {
                SerializerOptions = ToolCallPolicy.SerializerOptions,
                SchemaCreateOptions = new AIJsonSchemaCreateOptions
                {
                    TransformSchemaNode = (context, schema) => AddItemChoices(context.Path, schema, itemChoices),
                    TransformOptions = new AIJsonSchemaTransformOptions
                    {
                        TransformSchemaNode = (context, schema) => AddItemChoices(context.Path, schema, itemChoices),
                    },
                },
            }));
        }

        return tools;
    }

    /// <summary>
    /// Adds the allowed item values to the schema of a named list parameter and leaves every other node alone.
    /// </summary>
    private static JsonNode AddItemChoices(
        ReadOnlySpan<string> path,
        JsonNode schema,
        IReadOnlyDictionary<string, IReadOnlyList<string>> itemChoices)
    {
        if (path.Length != 2 ||
            path[0] != "properties" ||
            !itemChoices.TryGetValue(path[1], out IReadOnlyList<string>? values))
        {
            return schema;
        }

        JsonObject? items = FindItems(schema);
        if (items is not null)
        {
            items["enum"] = new JsonArray([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);
        }

        return schema;
    }

    /// <summary>
    /// Finds the schema of the list items, directly or inside an anyOf that also allows null.
    /// </summary>
    private static JsonObject? FindItems(JsonNode schema)
    {
        if (schema is not JsonObject node)
        {
            return null;
        }

        if (node["items"] is JsonObject items)
        {
            return items;
        }

        if (node["anyOf"] is JsonArray alternatives)
        {
            foreach (JsonNode? alternative in alternatives)
            {
                if (alternative is not null && FindItems(alternative) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
