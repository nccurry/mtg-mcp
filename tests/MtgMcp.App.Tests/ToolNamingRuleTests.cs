using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Enforces the naming rule of the MCP surface: tool names are snake_case with a registered prefix, property names
/// are camelCase, and fixed values the server defines are kebab-case. Provider pass-through values keep the
/// provider's spelling and appear only in the named allowlists below.
/// </summary>
public sealed partial class ToolNamingRuleTests
{
    /// <summary>
    /// Lists the registered tool name prefixes.
    /// </summary>
    private static readonly string[] Prefixes =
        ["local_deck_", "scryfall_", "card_", "stats_", "archidekt_", "playgroup_", "spellbook_"];

    /// <summary>
    /// Names the property names that keep a provider's spelling, with the reason for each.
    /// </summary>
    private static readonly HashSet<string> ProviderPropertyNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Names the fixed values that keep a provider's spelling, with the reason for each.
    /// </summary>
    private static readonly HashSet<string> ProviderValues = new(StringComparer.Ordinal);

    /// <summary>
    /// Names the input fields whose values are the names of result properties, so they are camelCase like the
    /// properties they name (or a dotted path of them) instead of kebab-case.
    /// </summary>
    private static readonly HashSet<string> PropertyReferenceFields = new(StringComparer.Ordinal)
    {
        "fields", "field", "groupBy",
    };

    /// <summary>
    /// Verifies every registered tool name is snake_case, starts with a registered prefix, and fits 64 characters.
    /// </summary>
    [Fact]
    public void ToolNames_AreSnakeCaseWithARegisteredPrefix()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);

        Assert.NotEmpty(registered.Tools);
        foreach (McpServerTool tool in registered.Tools)
        {
            string name = tool.ProtocolTool.Name;
            Assert.True(SnakeCase().IsMatch(name), $"{name} is not snake_case.");
            Assert.True(
                Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)),
                $"{name} has no registered prefix.");
            Assert.True(name.Length <= 64, $"{name} is longer than 64 characters.");
        }
    }

    /// <summary>
    /// Verifies every property named in a tool's input schema, at any depth, is camelCase.
    /// </summary>
    [Fact]
    public void InputSchemaPropertyNames_AreCamelCase()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach (McpServerTool tool in registered.Tools)
        {
            CollectSchemaNames(tool.ProtocolTool.Name, tool.ProtocolTool.InputSchema, offenders, values: null);
        }

        Assert.True(offenders.Count == 0, "Input schema names that are not camelCase:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies every property named in a tool's published output schema is camelCase.
    /// </summary>
    [Fact]
    public void OutputSchemaPropertyNames_AreCamelCase()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach (McpServerTool tool in registered.Tools)
        {
            JsonElement schema = tool.ProtocolTool.OutputSchema ??
                throw new Xunit.Sdk.XunitException($"{tool.ProtocolTool.Name} publishes no outputSchema.");
            CollectSchemaNames(tool.ProtocolTool.Name, schema, offenders, values: null);
        }

        Assert.True(offenders.Count == 0, "Output schema names that are not camelCase:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies every enum value in an input or output schema is kebab-case, unless it is a named provider value.
    /// </summary>
    [Fact]
    public void SchemaEnumValues_AreKebabCase()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach (McpServerTool tool in registered.Tools)
        {
            CollectSchemaNames(tool.ProtocolTool.Name, tool.ProtocolTool.InputSchema, names: null, offenders);
            CollectSchemaNames(tool.ProtocolTool.Name, tool.ProtocolTool.OutputSchema!.Value, names: null, offenders);
        }

        Assert.True(offenders.Count == 0, "Schema values that are not kebab-case:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies every property of every input and result type, as the server serializes it, is camelCase, and that
    /// every union discriminator property is camelCase with kebab-case values.
    /// </summary>
    [Fact]
    public void SerializedTypes_UseCamelCasePropertiesAndKebabCaseDiscriminators()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        HashSet<Type> visited = [];
        foreach ((string tool, MethodInfo method) in registered.Catalog.Methods
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                CheckType(tool, parameter.ParameterType, visited, offenders);
            }

            CheckType(tool, OperationResultSchema.ResultType(method), visited, offenders);
        }

        Assert.True(visited.Count > 100, "The walk reached too few types to be meaningful.");
        Assert.True(offenders.Count == 0, "Serialized names that break the rule:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies the spelling checks accept the right case and reject the wrong one.
    /// </summary>
    [Theory]
    [InlineData("local_deck_get", true, false, false)]
    [InlineData("localDeckGet", false, true, false)]
    [InlineData("pageSize", false, true, false)]
    [InlineData("page_size", true, false, false)]
    [InlineData("PageSize", false, false, false)]
    [InlineData("add-entry", false, false, true)]
    [InlineData("addEntry", false, true, false)]
    [InlineData("not_legal", true, false, false)]
    public void SpellingChecks_AcceptOnlyTheirOwnCase(string value, bool snake, bool camel, bool kebab)
    {
        Assert.Equal(snake, SnakeCase().IsMatch(value));
        Assert.Equal(camel, CamelCase().IsMatch(value));
        Assert.Equal(kebab, KebabCase().IsMatch(value));
    }

    /// <summary>
    /// Walks a published JSON schema and records property names or fixed values that break the rule.
    /// </summary>
    private static void CollectSchemaNames(
        string tool,
        JsonElement node,
        List<string>? names,
        List<string>? values,
        string path = "$")
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in node.EnumerateArray())
            {
                CollectSchemaNames(tool, item, names, values, $"{path}[{index++}]");
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty member in node.EnumerateObject())
        {
            string child = $"{path}.{member.Name}";
            if (member.Name == "properties" && member.Value.ValueKind == JsonValueKind.Object)
            {
                CollectPropertyNames(tool, member.Value, names, values, child);
            }
            else if (values is not null && (member.Name == "enum" || member.Name == "const"))
            {
                CollectLiterals(tool, member.Value, values, child);
            }
            else
            {
                CollectSchemaNames(tool, member.Value, names, values, child);
            }
        }
    }

    /// <summary>
    /// Records the property names of one schema <c>properties</c> object that are not camelCase, then walks each one.
    /// </summary>
    private static void CollectPropertyNames(
        string tool,
        JsonElement properties,
        List<string>? names,
        List<string>? values,
        string path)
    {
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (names is not null && !CamelCase().IsMatch(property.Name) &&
                !ProviderPropertyNames.Contains(property.Name))
            {
                names.Add($"{tool} {path}.{property.Name}");
            }

            CollectSchemaNames(tool, property.Value, names, values, $"{path}.{property.Name}");
        }
    }

    /// <summary>
    /// Gets the name of the schema property that owns an <c>enum</c> path, skipping array <c>items</c> and the keyword.
    /// </summary>
    private static string OwnerName(string path)
    {
        string[] parts = path.Split('.');
        for (int index = parts.Length - 1; index >= 0; index--)
        {
            if (parts[index] is not ("enum" or "const" or "items"))
            {
                return parts[index];
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Records the string values of a schema <c>enum</c> or <c>const</c> that are not kebab-case.
    /// </summary>
    private static void CollectLiterals(string tool, JsonElement literals, List<string> values, string path)
    {
        IEnumerable<JsonElement> items = literals.ValueKind == JsonValueKind.Array
            ? literals.EnumerateArray()
            : [literals];
        foreach (JsonElement literal in items)
        {
            string? text = literal.ValueKind == JsonValueKind.String ? literal.GetString() : null;
            Regex expected = PropertyReferenceFields.Contains(OwnerName(path)) ? PropertyPath() : KebabCase();
            if (text is not null && !expected.IsMatch(text) && !ProviderValues.Contains(text))
            {
                values.Add($"{tool} {path}: {text}");
            }
        }
    }

    /// <summary>
    /// Walks a CLR type through the serializer's own metadata and records property names and discriminators that
    /// break the rule.
    /// </summary>
    private static void CheckType(string tool, Type type, HashSet<Type> visited, List<string> offenders)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(CancellationToken) ||
            type.Namespace?.StartsWith("ModelContextProtocol", StringComparison.Ordinal) == true ||
            !visited.Add(type))
        {
            return;
        }

        if (type.IsEnum)
        {
            CheckEnum(tool, type, offenders);
            return;
        }

        JsonTypeInfo info;
        try
        {
            info = ToolCallPolicy.SerializerOptions.GetTypeInfo(type);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                foreach (JsonPropertyInfo property in info.Properties)
                {
                    if (!CamelCase().IsMatch(property.Name) && !ProviderPropertyNames.Contains(property.Name))
                    {
                        offenders.Add($"{tool}: {type.Name}.{property.Name} is not camelCase.");
                    }

                    CheckType(tool, property.PropertyType, visited, offenders);
                }

                break;
            case JsonTypeInfoKind.Enumerable:
            case JsonTypeInfoKind.Dictionary:
                if (info.ElementType is not null)
                {
                    CheckType(tool, info.ElementType, visited, offenders);
                }

                break;
        }

        if (info.PolymorphismOptions is not { } polymorphism)
        {
            return;
        }

        if (!CamelCase().IsMatch(polymorphism.TypeDiscriminatorPropertyName))
        {
            offenders.Add(
                $"{tool}: {type.Name} discriminator property {polymorphism.TypeDiscriminatorPropertyName} " +
                "is not camelCase.");
        }

        foreach (JsonDerivedType derived in polymorphism.DerivedTypes)
        {
            string? value = derived.TypeDiscriminator?.ToString();
            if (value is not null && !KebabCase().IsMatch(value) && !ProviderValues.Contains(value))
            {
                offenders.Add($"{tool}: {derived.DerivedType.Name} discriminator {value} is not kebab-case.");
            }

            CheckType(tool, derived.DerivedType, visited, offenders);
        }
    }

    /// <summary>
    /// Records the serialized values of an enum that are not kebab-case.
    /// </summary>
    private static void CheckEnum(string tool, Type type, List<string> offenders)
    {
        foreach (object value in Enum.GetValues(type))
        {
            string text = JsonSerializer.Serialize(value, ToolCallPolicy.SerializerOptions).Trim('"');
            if (!KebabCase().IsMatch(text) && !ProviderValues.Contains(text))
            {
                offenders.Add($"{tool}: enum {type.Name} value {text} is not kebab-case.");
            }
        }
    }

    /// <summary>
    /// Matches a snake_case tool name of at least two lowercase parts.
    /// </summary>
    [GeneratedRegex("^[a-z]+(_[a-z]+)+$")]
    private static partial Regex SnakeCase();

    /// <summary>
    /// Matches a camelCase name that starts with a lowercase letter and has no separator.
    /// </summary>
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex CamelCase();

    /// <summary>
    /// Matches a camelCase property name, or a dotted path of them such as <c>prices.tix</c>.
    /// </summary>
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)*$")]
    private static partial Regex PropertyPath();

    /// <summary>
    /// Matches a kebab-case value of lowercase words and digits joined by single hyphens.
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();
}
