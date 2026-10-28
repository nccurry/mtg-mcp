using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Creates every MCP tool the server publishes, so the rules for tool schemas live in one place: shared
/// serializer options, closed input objects that reject unknown properties, and optional item choices for
/// list parameters.
/// </summary>
internal static class ToolRegistration
{
    /// <summary>
    /// Creates every tool declared on the target. Each list parameter whose name is a key of
    /// <paramref name="itemChoices"/> publishes the listed values as a JSON Schema <c>enum</c> on its items,
    /// which <c>[AllowedValues]</c> cannot do for the items of a list.
    /// </summary>
    internal static IReadOnlyList<McpServerTool> Create(
        object target,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? itemChoices = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        List<McpServerTool> tools = [];
        foreach (MethodInfo method in target.GetType().GetMethods(flags))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            tools.Add(Create(method, target, itemChoices, describeNode: null));
        }

        return tools;
    }

    /// <summary>
    /// Creates one tool from a method. The optional <paramref name="describeNode"/> runs on every schema node
    /// the SDK builds, for tools that describe generated union discriminators.
    /// </summary>
    internal static McpServerTool Create(
        MethodInfo method,
        object target,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? itemChoices = null,
        Func<JsonNode, JsonNode>? describeNode = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(target);
        return McpServerTool.Create(method, target, new McpServerToolCreateOptions
        {
            SerializerOptions = ToolCallPolicy.SerializerOptions,
            SchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformSchemaNode = describeNode is null ? null : (_, schema) => describeNode(schema),
                // The transform-options hook is the one that reaches the root and parameter schemas of tool methods.
                TransformOptions = new AIJsonSchemaTransformOptions
                {
                    TransformSchemaNode = (context, schema) =>
                        TransformNode(context.Path, schema, itemChoices, describeNode),
                },
            },
        });
    }

    /// <summary>
    /// Closes the root input object, adds list item choices, and applies the optional description step.
    /// </summary>
    private static JsonNode TransformNode(
        ReadOnlySpan<string> path,
        JsonNode schema,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? itemChoices,
        Func<JsonNode, JsonNode>? describeNode)
    {
        JsonNode result = describeNode is null ? schema : describeNode(schema);
        if (path.Length == 0)
        {
            CloseObject(result);
            return result;
        }

        if (itemChoices is not null &&
            path.Length == 2 &&
            path[0] == "properties" &&
            itemChoices.TryGetValue(path[1], out IReadOnlyList<string>? values))
        {
            AddItemChoices(result, values);
        }

        return result;
    }

    /// <summary>
    /// Makes an object schema reject properties it does not list.
    /// </summary>
    private static void CloseObject(JsonNode schema)
    {
        if (schema is JsonObject node && node["properties"] is JsonObject)
        {
            node["additionalProperties"] = false;
        }
    }

    /// <summary>
    /// Adds the allowed item values to the schema of a list parameter.
    /// </summary>
    private static void AddItemChoices(JsonNode schema, IReadOnlyList<string> values)
    {
        JsonObject? items = FindItems(schema);
        if (items is not null)
        {
            items["enum"] = new JsonArray([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);
        }
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
