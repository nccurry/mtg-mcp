using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Builds the one output schema every tool publishes. Operation results have their own JSON converter, so the SDK
/// cannot generate a schema for them and would publish <c>true</c>, which protocol 2026-07-28 rejects. The shared
/// schema names the one property every result has, <c>kind</c>, and nothing else.
/// </summary>
internal static class OperationResultSchema
{
    /// <summary>
    /// Gets the operation result kinds, read from the result cases in Core so the list cannot drift.
    /// </summary>
    internal static IReadOnlyList<string> Kinds { get; } = ReadKinds();

    /// <summary>
    /// Gets the shared output schema: an object with a required <c>kind</c> that is one of the operation result kinds.
    /// </summary>
    internal static JsonElement Schema { get; } = CreateSchema();

    /// <summary>
    /// Returns the type a tool method hands back to its caller, with any Task or ValueTask wrapper removed.
    /// </summary>
    internal static Type ResultType(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        Type type = method.ReturnType;
        Type? definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    /// <summary>
    /// Reports whether a tool method returns an operation result, the only type the shared schema describes.
    /// </summary>
    internal static bool ReturnsOperationResult(MethodInfo method)
    {
        Type type = ResultType(method);
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(OperationResult<>);
    }

    /// <summary>
    /// Builds the schema document.
    /// </summary>
    private static JsonElement CreateSchema()
    {
        JsonObject schema = new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray([.. Kinds.Select(kind => (JsonNode?)JsonValue.Create(kind))]),
                    ["description"] = "The outcome of the call. Any value other than success is a failure.",
                },
            },
            ["required"] = new JsonArray("kind"),
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>
    /// Reads the serialized kind of every case of the operation result union. The kind is a constant on each case, so
    /// a case made without running its constructor still reports it.
    /// </summary>
    private static IReadOnlyList<string> ReadKinds()
    {
        List<string> kinds = [];
        foreach (ConstructorInfo constructor in typeof(OperationResult<object>).GetConstructors())
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            object caseValue = RuntimeHelpers.GetUninitializedObject(parameters[0].ParameterType);
            PropertyInfo? kind = caseValue.GetType().GetProperty("Kind");
            if (kind?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == "kind" &&
                kind.GetValue(caseValue) is string value)
            {
                kinds.Add(value);
            }
        }

        return kinds.Count == 0
            ? throw new InvalidOperationException("No operation result kinds were found.")
            : kinds;
    }
}
