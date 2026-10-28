using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Applies the argument binding and error reporting rules shared by every registered tool.
/// </summary>
internal static partial class ToolCallPolicy
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
    /// Limits how much of an unknown argument name an error message repeats.
    /// </summary>
    private const int MaximumEchoedNameLength = 64;

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
            // A tool the server did not register for this selection and mode is an unknown tool: describe nothing.
            ToolArguments? tool = FindRegisteredTool(context);
            bool registered = tool is not null;
            if (tool is not null)
            {
                OperationInvalidInput? unknown = FindUnknownArgument(
                    tool,
                    context.Params!.Name,
                    context.Params.Arguments);
                if (unknown is not null)
                {
                    return CreateFailureResult(unknown);
                }
            }

            CallToolResult result;
            try
            {
                result = await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException && registered)
            {
                // The SDK binds arguments inside the pipeline and lets a binding exception reach this filter.
                OperationInvalidInput? failure = DiagnoseArguments(tool!.Method, context.Params!.Arguments);
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
    /// Re-reads each argument into its parameter type to find the first value the tool could not bind. The tool
    /// must be in the catalog; a name the catalog does not hold is a caller error, not a clean result.
    /// </summary>
    internal static OperationInvalidInput? DiagnoseArguments(
        ToolArgumentCatalog catalog,
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        return DiagnoseArguments(Require(catalog, toolName).Method, arguments);
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
    /// Finds the first argument the tool does not declare, so a misspelled or invented field fails the call
    /// instead of being ignored. Arguments are checked in ordinal order so the message is deterministic.
    /// </summary>
    internal static OperationInvalidInput? FindUnknownArgument(
        ToolArgumentCatalog catalog,
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        return FindUnknownArgument(Require(catalog, toolName), toolName, arguments);
    }

    /// <summary>
    /// Finds the first argument that the given tool does not declare.
    /// </summary>
    private static OperationInvalidInput? FindUnknownArgument(
        ToolArguments tool,
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        string? unknown = arguments.Keys
            .Where(name => !tool.Names.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (unknown is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(unknown))
        {
            return new OperationInvalidInput(
                InvalidArgumentsReason,
                $"An argument with a blank name is not accepted by {toolName}. " +
                $"Accepted arguments: {tool.AcceptedText}.");
        }

        string shown = Shorten(unknown);
        return new OperationInvalidInput(
            InvalidArgumentsReason,
            $"Argument '{shown}' is not accepted by {toolName}. Accepted arguments: {tool.AcceptedText}.",
            shown);
    }

    /// <summary>
    /// Finds the catalog entry for the tool a request matched, or null when the server did not register the tool.
    /// </summary>
    private static ToolArguments? FindRegisteredTool(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params is null || context.MatchedPrimitive is null)
        {
            return null;
        }

        ToolArgumentCatalog? catalog = context.Services?.GetService<ToolArgumentCatalog>();
        if (catalog is not null && catalog.TryGet(context.Params.Name, out ToolArguments tool))
        {
            return tool;
        }

        // The SDK matched a tool that this server's registration never recorded. Argument checks are skipped for
        // it, so say so: it means a tool was added to the server without going through WithRegisteredTools.
        context.Services?.GetService<ILoggerFactory>()?.CreateLogger(nameof(ToolCallPolicy)).LogWarning(
            "Tool {ToolName} was matched by the SDK but is not in the argument catalog; its arguments are not checked.",
            context.Params.Name);
        return null;
    }

    /// <summary>
    /// Finds a tool in the catalog and fails loudly when it is missing.
    /// </summary>
    private static ToolArguments Require(ToolArgumentCatalog catalog, string toolName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.TryGet(toolName, out ToolArguments tool)
            ? tool
            : throw new ArgumentException($"Tool '{toolName}' is not in the catalog.", nameof(toolName));
    }

    /// <summary>
    /// Replaces the final path segment, written as <c>.name</c> or <c>['name']</c>, with the shortened name.
    /// A name that contains dots or brackets is matched as a whole, never split.
    /// </summary>
    private static string ShortenLastSegment(string path, string raw, string shortened)
    {
        string dotted = "." + raw;
        if (path.EndsWith(dotted, StringComparison.Ordinal))
        {
            return path[..^dotted.Length] + "." + shortened;
        }

        string bracketed = "['" + raw + "']";
        return path.EndsWith(bracketed, StringComparison.Ordinal)
            ? path[..^bracketed.Length] + "['" + shortened + "']"
            : path;
    }

    /// <summary>
    /// Cuts an echoed field name to the length limit without splitting a surrogate pair.
    /// </summary>
    private static string Shorten(string name)
    {
        if (name.Length <= MaximumEchoedNameLength)
        {
            return name;
        }

        int length = char.IsHighSurrogate(name[MaximumEchoedNameLength - 1])
            ? MaximumEchoedNameLength - 1
            : MaximumEchoedNameLength;
        return name[..length] + "...";
    }

    /// <summary>
    /// Matches the serializer message for a JSON property that no input type declares.
    /// </summary>
    [GeneratedRegex("The JSON property '(.*)' could not be mapped to any [.]NET member")]
    private static partial Regex UnmappedMember();

    /// <summary>
    /// Matches the serializer message that lists the required properties an object left out.
    /// </summary>
    [GeneratedRegex("missing required properties including: ((?:'[^']*'(?:, )?)+)")]
    private static partial Regex MissingRequiredProperties();

    /// <summary>
    /// Reports whether a parameter is supplied from tool arguments rather than injected by the host.
    /// </summary>
    internal static bool IsArgumentParameter(ParameterInfo parameter)
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
            Match unmapped = UnmappedMember().Match(exception.Message);
            string path = CombinePath(parameter.Name!, exception.Path);
            string message = $"The value at '{path}' could not be read: {TrimPathSuffix(exception.Message)}";
            if (unmapped.Success)
            {
                string raw = unmapped.Groups[1].Value;
                string field = Shorten(raw);
                path = ShortenLastSegment(path, raw, field);
                message = $"Property '{field}' is not accepted here.";
            }
            Match required = MissingRequiredProperties().Match(exception.Message);
            if (required.Success)
            {
                string[] names = [.. Regex.Matches(required.Groups[1].Value, "'([^']*)'")
                    .Select(match => Shorten(match.Groups[1].Value))];
                message = names.Length == 1
                    ? $"Required field '{names[0]}' is missing at '{path}'."
                    : $"Required fields are missing at '{path}': {string.Join(", ", names)}.";
                path = $"{path}.{names[0]}";
            }

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
    /// Copies the SDK default options, allows union discriminators after other properties, and rejects JSON properties
    /// that no input type declares.
    /// </summary>
    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions)
        {
            AllowOutOfOrderMetadataProperties = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.MakeReadOnly();
        return options;
    }
}
