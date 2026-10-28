using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Applies the argument binding and error reporting rules shared by every registered tool.
/// </summary>
internal static class ToolCallPolicy
{
    /// <summary>
    /// Names the reason reported when tool arguments cannot be read into the tool's parameter types.
    /// </summary>
    internal const string InvalidArgumentsReason = "invalid-arguments";

    /// <summary>
    /// Limits how deep the allowed-kind search walks nested argument types.
    /// </summary>
    private const int MaximumKindSearchDepth = 4;

    /// <summary>
    /// Maps each tool name to the method that implements it. The SDK does not expose the bound method to
    /// call-tool filters, so the policy finds it by tool name.
    /// </summary>
    private static readonly FrozenDictionary<string, MethodInfo> ToolMethods =
        IndexToolMethods(typeof(ToolCallPolicy).Assembly.GetTypes());

    /// <summary>
    /// Gets the serializer options for tool arguments and results; union kinds may appear in any property position.
    /// Every tool registration must use these options so the binding diagnosis reads arguments the same way the
    /// SDK does.
    /// </summary>
    internal static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    /// <summary>
    /// Wraps the tool call pipeline so failures carry the MCP error flag and binding failures name their path.
    /// </summary>
    internal static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (context, cancellationToken) =>
        {
            CallToolResult result;
            try
            {
                result = await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException && context.Params is not null)
            {
                // The SDK binds arguments inside the pipeline and lets a binding exception reach this filter.
                OperationInvalidInput? failure = DiagnoseArguments(context.Params.Name, context.Params.Arguments);
                if (failure is null)
                {
                    throw;
                }

                return CreateFailureResult(failure);
            }

            return MarkOperationFailure(result);
        };
    }

    /// <summary>
    /// Sets the MCP error flag on any operation result whose kind is not success.
    /// </summary>
    internal static CallToolResult MarkOperationFailure(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (TryReadKind(result.StructuredContent, out string? kind) &&
            !string.Equals(kind, "success", StringComparison.Ordinal))
        {
            result.IsError = true;
        }

        return result;
    }

    /// <summary>
    /// Re-reads each argument into its parameter type to find the first value the tool could not bind.
    /// </summary>
    internal static OperationInvalidInput? DiagnoseArguments(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        return ToolMethods.TryGetValue(toolName, out MethodInfo? method)
            ? DiagnoseArguments(method, arguments)
            : null;
    }

    /// <summary>
    /// Re-reads each argument into the parameter types of one tool method to find the first unbound value.
    /// </summary>
    internal static OperationInvalidInput? DiagnoseArguments(
        MethodInfo method,
        IDictionary<string, JsonElement>? arguments)
    {
        ArgumentNullException.ThrowIfNull(method);
        foreach (ParameterInfo parameter in method.GetParameters())
        {
            if (!IsArgumentParameter(parameter) || parameter.Name is null)
            {
                continue;
            }

            if (arguments is null || !arguments.TryGetValue(parameter.Name, out JsonElement value))
            {
                if (parameter.HasDefaultValue)
                {
                    continue;
                }

                return new OperationInvalidInput(
                    InvalidArgumentsReason,
                    $"Argument '{parameter.Name}' is required.",
                    parameter.Name);
            }

            OperationInvalidInput? failure = TryBind(parameter, value);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether a parameter is supplied from tool arguments rather than injected by the host.
    /// </summary>
    private static bool IsArgumentParameter(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType;
        return type != typeof(CancellationToken) &&
            type != typeof(IServiceProvider) &&
            type != typeof(McpServer) &&
            !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RequestContext<>));
    }

    /// <summary>
    /// Reads one argument into its parameter type and describes the first failure, if any.
    /// </summary>
    private static OperationInvalidInput? TryBind(ParameterInfo parameter, JsonElement value)
    {
        try
        {
            _ = JsonSerializer.Deserialize(value, parameter.ParameterType, SerializerOptions);
            return null;
        }
        catch (JsonException exception)
        {
            string path = CombinePath(parameter.Name!, exception.Path);
            string message = $"The value at '{path}' could not be read: {TrimPathSuffix(exception.Message)}";
            if (exception.Message.Contains("discriminator", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<string> kinds = FindAllowedKinds(parameter.ParameterType);
                if (kinds.Count > 0)
                {
                    message = $"The value at '{path}' has a missing or unknown kind. " +
                        $"Allowed kinds: {string.Join(", ", kinds)}.";
                }
            }

            return new OperationInvalidInput(InvalidArgumentsReason, message, path);
        }
        catch (NotSupportedException exception)
        {
            return new OperationInvalidInput(
                InvalidArgumentsReason,
                $"The value of '{parameter.Name}' could not be read: {TrimPathSuffix(exception.Message)}",
                parameter.Name);
        }
    }

    /// <summary>
    /// Builds the protocol result for a bounded failure, with the same body as structured content and text.
    /// </summary>
    private static CallToolResult CreateFailureResult(OperationInvalidInput failure)
    {
        OperationResult<object> operation = failure;
        JsonElement body = JsonSerializer.SerializeToElement(operation, SerializerOptions);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = body,
            Content = [new TextContentBlock { Text = body.GetRawText() }],
        };
    }

    /// <summary>
    /// Reads the operation outcome kind from structured content when the result is an operation result.
    /// </summary>
    private static bool TryReadKind(JsonElement? structured, out string? kind)
    {
        kind = null;
        if (structured is not { ValueKind: JsonValueKind.Object } element ||
            !element.TryGetProperty("kind", out JsonElement kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        kind = kindElement.GetString();
        return kind is not null;
    }

    /// <summary>
    /// Joins a parameter name and a serializer path such as <c>$[0].kind</c> into <c>changes[0].kind</c>.
    /// </summary>
    private static string CombinePath(string parameterName, string? serializerPath)
    {
        if (string.IsNullOrEmpty(serializerPath) || serializerPath == "$")
        {
            return parameterName;
        }

        string relative = serializerPath.StartsWith('$') ? serializerPath[1..] : serializerPath;
        return relative.StartsWith('[') || relative.StartsWith('.')
            ? parameterName + relative
            : $"{parameterName}.{relative}";
    }

    /// <summary>
    /// Removes the serializer's trailing location details, which the path field already reports.
    /// </summary>
    private static string TrimPathSuffix(string message)
    {
        int index = message.IndexOf(" Path:", StringComparison.Ordinal);
        return index < 0 ? message : message[..index];
    }

    /// <summary>
    /// Lists the discriminator values of the one polymorphic type reachable from an argument type.
    /// </summary>
    private static IReadOnlyList<string> FindAllowedKinds(Type argumentType)
    {
        List<Type> polymorphic = [];
        HashSet<Type> visited = [];
        CollectPolymorphicTypes(argumentType, 0, visited, polymorphic);
        if (polymorphic.Count != 1)
        {
            return [];
        }

        List<string> kinds = [];
        foreach (JsonDerivedTypeAttribute derived in polymorphic[0].GetCustomAttributes<JsonDerivedTypeAttribute>())
        {
            if (derived.TypeDiscriminator is string kind)
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }

    /// <summary>
    /// Walks element types, generic arguments, and properties to find types declared as JSON unions.
    /// </summary>
    private static void CollectPolymorphicTypes(Type type, int depth, HashSet<Type> visited, List<Type> found)
    {
        if (depth > MaximumKindSearchDepth || type.IsPrimitive || type == typeof(string) || !visited.Add(type))
        {
            return;
        }

        if (type.GetCustomAttribute<JsonPolymorphicAttribute>() is not null)
        {
            found.Add(type);
        }

        if (type.IsArray)
        {
            CollectPolymorphicTypes(type.GetElementType()!, depth + 1, visited, found);
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            CollectPolymorphicTypes(argument, depth + 1, visited, found);
        }

        if (type.Namespace?.StartsWith("MtgMcp", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            CollectPolymorphicTypes(property.PropertyType, depth + 1, visited, found);
        }
    }

    /// <summary>
    /// Finds every tool method declared on the given types by its MCP tool name; a repeated name is a build error.
    /// </summary>
    internal static FrozenDictionary<string, MethodInfo> IndexToolMethods(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        Dictionary<string, MethodInfo> methods = new(StringComparer.Ordinal);
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type type in types)
        {
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { Name: { } name })
                {
                    continue;
                }

                if (!methods.TryAdd(name, method))
                {
                    throw new InvalidOperationException(
                        $"Tool name '{name}' is declared by both {methods[name].DeclaringType?.Name} " +
                        $"and {method.DeclaringType?.Name}.");
                }
            }
        }

        return methods.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Copies the SDK's default options and allows union discriminators after other properties.
    /// </summary>
    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions)
        {
            AllowOutOfOrderMetadataProperties = true,
        };
        options.MakeReadOnly();
        return options;
    }
}
