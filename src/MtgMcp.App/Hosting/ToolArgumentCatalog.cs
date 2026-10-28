using System.Reflection;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Owned by one server: records, for each tool that registration created, the method behind it and the
/// arguments it accepts. The call filter reads it from the request services, so it describes only tools that
/// registration built for this server, and it works out each tool's accepted-argument text once.
/// </summary>
internal sealed class ToolArgumentCatalog
{
    /// <summary>
    /// Holds the tools by name.
    /// </summary>
    private readonly Dictionary<string, ToolArguments> tools = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards the dictionary, because tests and hosts may register tools from several threads.
    /// </summary>
    private readonly object gate = new();

    /// <summary>
    /// Gets a copy of the method behind each registered tool.
    /// </summary>
    internal IReadOnlyDictionary<string, MethodInfo> Methods
    {
        get
        {
            lock (gate)
            {
                return tools.ToDictionary(pair => pair.Key, pair => pair.Value.Method, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// Records one tool method. Recording the same method again is allowed; a different method with the same tool
    /// name is a build error.
    /// </summary>
    internal void Register(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        string name = method.GetCustomAttribute<McpServerToolAttribute>()?.Name
            ?? throw new InvalidOperationException($"{method.Name} is not declared as an MCP tool.");
        lock (gate)
        {
            if (tools.TryGetValue(name, out ToolArguments? existing))
            {
                if (existing.Method != method)
                {
                    throw new InvalidOperationException(
                        $"Tool name '{name}' is declared by both {existing.Method.DeclaringType?.Name} " +
                        $"and {method.DeclaringType?.Name}.");
                }

                return;
            }

            tools[name] = ToolArguments.Create(method);
        }
    }

    /// <summary>
    /// Finds a registered tool by name.
    /// </summary>
    internal bool TryGet(string name, out ToolArguments arguments)
    {
        lock (gate)
        {
            bool found = tools.TryGetValue(name, out ToolArguments? value);
            arguments = value!;
            return found;
        }
    }
}

/// <summary>
/// Describes the arguments of one tool method: the parameters supplied by the caller and the text that lists them.
/// </summary>
internal sealed record ToolArguments(
    MethodInfo Method,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<string> Names,
    string AcceptedText)
{
    /// <summary>
    /// Reads the caller-supplied parameters of a tool method.
    /// </summary>
    internal static ToolArguments Create(MethodInfo method)
    {
        List<ParameterInfo> parameters = [];
        List<string> names = [];
        foreach (ParameterInfo parameter in method.GetParameters())
        {
            if (ToolCallPolicy.IsArgumentParameter(parameter) && parameter.Name is not null)
            {
                parameters.Add(parameter);
                names.Add(parameter.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return new ToolArguments(method, parameters, names, names.Count == 0 ? "none" : string.Join(", ", names));
    }
}
