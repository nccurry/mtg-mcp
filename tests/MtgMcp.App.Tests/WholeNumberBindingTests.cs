using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies integer tool arguments follow JSON Schema "integer": a whole number written with a fractional part such
/// as <c>328.0</c> is accepted, and any other number is refused with its JSON path.
/// </summary>
public sealed class WholeNumberBindingTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built.
    /// </summary>
    public WholeNumberBindingTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Verifies whole numbers written with a fractional part or an exponent become plain integers, at any depth.
    /// </summary>
    [Fact]
    public void Normalize_WholeNumbers_BecomeIntegersAtAnyDepth()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            "{ \"a\": 328.0, \"b\": 3.28e2, \"c\": { \"d\": [ 1.0, { \"e\": -0.0 } ] }, \"f\": 9007199254740993.0 }");

        WholeNumberArguments.Normalize(arguments);

        Assert.Equal("328", arguments["a"].GetRawText());
        Assert.Equal("328", arguments["b"].GetRawText());
        Assert.Equal("{\"d\":[1,{\"e\":0}]}", arguments["c"].GetRawText());
        Assert.Equal("9007199254740993", arguments["f"].GetRawText());
    }

    /// <summary>
    /// Verifies a number with a fractional part, a number too large for 64 bits, and other values are left as they
    /// were, and the same instance is kept when nothing changes.
    /// </summary>
    [Fact]
    public void Normalize_OtherValues_AreLeftAlone()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            "{ \"a\": 1.5, \"b\": 1e300, \"c\": \"2.0\", \"d\": true, \"e\": null, \"f\": [ 7, \"x\" ], \"g\": 5 }");
        Dictionary<string, string> before = arguments.ToDictionary(pair => pair.Key, pair => pair.Value.GetRawText());

        WholeNumberArguments.Normalize(arguments);
        WholeNumberArguments.Normalize(null);

        Assert.Equal(before, arguments.ToDictionary(pair => pair.Key, pair => pair.Value.GetRawText()));
    }

    /// <summary>
    /// Verifies nested integer fields in a batch edit bind as whole numbers once normalized.
    /// </summary>
    [Fact]
    public void ApplyChanges_WholeNumberFields_Bind()
    {
        Dictionary<string, JsonElement> arguments = Arguments(ApplyChangesJson("3.0", "1.0", "328.0"));
        WholeNumberArguments.Normalize(arguments);

        Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));
    }

    /// <summary>
    /// Verifies a nested integer field with a fractional part, or out of range, names its JSON path in a bounded
    /// invalid-input failure.
    /// </summary>
    [Theory]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    [InlineData("328.0000001")]
    public void ApplyChanges_BadSortOrder_NamesThePath(string sortOrder)
    {
        Dictionary<string, JsonElement> arguments = Arguments(ApplyChangesJson("3", "1", sortOrder));
        WholeNumberArguments.Normalize(arguments);

        OperationInvalidInput failure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));

        Assert.Equal("invalid-arguments", failure.ReasonCode);
        Assert.Equal("changes[0].entryDraft.sortOrder", failure.Path);
    }

    /// <summary>
    /// Verifies every integer a tool input can hold is an <c>int</c> or <c>long</c> (or nullable), and each reads a
    /// normalized whole number and fails with a serializer error, which the filter turns into a path, for a number
    /// with a fractional part or out of range. No tool input can reach the generic invocation error that way.
    /// </summary>
    [Fact]
    public void EveryToolInput_ReadsWholeNumbersAndRefusesTheRest()
    {
        HashSet<Type> seen = [];
        List<(string Tool, Type Type)> integers = [];
        foreach ((string tool, MethodInfo method) in catalog.Methods)
        {
            foreach (ParameterInfo parameter in method.GetParameters().Where(ToolCallPolicy.IsArgumentParameter))
            {
                Walk(tool, parameter.ParameterType, seen, integers);
            }
        }

        Type[] covered = [typeof(int), typeof(long)];
        Assert.NotEmpty(integers);
        Assert.Empty(integers
            .Where(value => !covered.Contains(value.Type))
            .Select(value => $"{value.Tool}: {value.Type}"));
        Dictionary<string, JsonElement> number = Arguments("{ \"n\": 5.0 }");
        WholeNumberArguments.Normalize(number);
        foreach (Type type in integers.Select(value => value.Type).Distinct())
        {
            object? read = JsonSerializer.Deserialize(number["n"], type, ToolCallPolicy.SerializerOptions);

            Assert.Equal(5, Convert.ToInt32(read, CultureInfo.InvariantCulture));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("5.5", type, ToolCallPolicy.SerializerOptions));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize("99999999999999999999", type, ToolCallPolicy.SerializerOptions));
        }
    }

    /// <summary>
    /// Builds a batch edit request with the given numbers written as JSON text.
    /// </summary>
    private static string ApplyChangesJson(string revision, string quantity, string sortOrder)
    {
        return "{ \"deckId\": \"0193e97a-ef95-7700-8000-000000000001\", \"expectedRevision\": " + revision +
            ", \"changes\": [ { \"kind\": \"add-entry\", \"entryDraft\": { \"quantity\": " + quantity +
            ", \"cardName\": \"Sol Ring\", \"sortOrder\": " + sortOrder + " } } ] }";
    }

    /// <summary>
    /// Collects the integer types reachable from one parameter type.
    /// </summary>
    private static void Walk(string tool, Type type, HashSet<Type> seen, List<(string Tool, Type Type)> integers)
    {
        Type effective = Nullable.GetUnderlyingType(type) ?? type;
        if (effective == typeof(string) || effective == typeof(Guid) || effective == typeof(JsonElement) ||
            effective.IsEnum || !seen.Add(effective))
        {
            return;
        }

        if (effective.IsPrimitive)
        {
            if (effective != typeof(bool) && effective != typeof(double) && effective != typeof(float) &&
                effective != typeof(char))
            {
                integers.Add((tool, effective));
            }

            return;
        }

        if (effective.IsArray)
        {
            Walk(tool, effective.GetElementType()!, seen, integers);
            return;
        }

        if (effective.IsGenericType)
        {
            foreach (Type argument in effective.GetGenericArguments())
            {
                Walk(tool, argument, seen, integers);
            }

            if (effective.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                return;
            }
        }

        foreach (JsonDerivedTypeAttribute derived in effective.GetCustomAttributes<JsonDerivedTypeAttribute>())
        {
            Walk(tool, derived.DerivedType, seen, integers);
        }

        foreach (PropertyInfo property in effective.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            {
                Walk(tool, property.PropertyType, seen, integers);
            }
        }
    }

    /// <summary>
    /// Parses a JSON object into the argument dictionary a tool call carries.
    /// </summary>
    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }
}
