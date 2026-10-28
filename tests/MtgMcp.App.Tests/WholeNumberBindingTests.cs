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
    /// Verifies whole numbers in every spelling are read for each integer type tool inputs use.
    /// </summary>
    [Theory]
    [InlineData("328.0", 328)]
    [InlineData("3.28e2", 328)]
    [InlineData("328", 328)]
    [InlineData("-0.0", 0)]
    [InlineData("2147483647.0", int.MaxValue)]
    public void Deserialize_WholeNumber_IsAccepted(string json, int expected)
    {
        JsonSerializerOptions options = ToolCallPolicy.SerializerOptions;

        Assert.Equal(expected, JsonSerializer.Deserialize<int>(json, options));
        Assert.Equal(expected, JsonSerializer.Deserialize<int?>(json, options));
        Assert.Equal(expected, JsonSerializer.Deserialize<long>(json, options));
        Assert.Equal(expected, JsonSerializer.Deserialize<long?>(json, options));
        Assert.Null(JsonSerializer.Deserialize<int?>("null", options));
    }

    /// <summary>
    /// Verifies a number the type cannot hold exactly, or a value that is not a number, is refused with the rule.
    /// </summary>
    [Theory]
    [InlineData("1.5", "fractional part")]
    [InlineData("328.0000001", "fractional part")]
    [InlineData("2147483648", "out of range")]
    [InlineData("-2147483649.0", "out of range")]
    [InlineData("1e300", "out of range")]
    [InlineData("\"5\"", "not a number")]
    [InlineData("true", "not a number")]
    public void Deserialize_NotAWholeIntNumber_IsRefusedWithTheRule(string json, string reason)
    {
        JsonException exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<int>(json, ToolCallPolicy.SerializerOptions));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a 64-bit value keeps its exact value and a value past the 64-bit range is refused.
    /// </summary>
    [Fact]
    public void Deserialize_Long_KeepsTheExactValueAndRefusesOverflow()
    {
        JsonSerializerOptions options = ToolCallPolicy.SerializerOptions;

        Assert.Equal(9007199254740993L, JsonSerializer.Deserialize<long>("9007199254740993", options));
        Assert.Equal(long.MaxValue, JsonSerializer.Deserialize<long>("9223372036854775807", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>("9223372036854775808", options));
    }

    /// <summary>
    /// Verifies nested integer fields in a batch edit bind as whole numbers.
    /// </summary>
    [Fact]
    public void ApplyChanges_WholeNumberFields_Bind()
    {
        Dictionary<string, JsonElement> arguments = Arguments(ApplyChangesJson("3.0", "1.0", "328.0"));

        Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));
    }

    /// <summary>
    /// Verifies a nested integer field with a fractional part, or out of range, names its JSON path in a bounded
    /// invalid-input failure.
    /// </summary>
    [Theory]
    [InlineData("1.5", "fractional part")]
    [InlineData("99999999999", "out of range")]
    public void ApplyChanges_BadSortOrder_NamesThePath(string sortOrder, string reason)
    {
        Dictionary<string, JsonElement> arguments = Arguments(ApplyChangesJson("3", "1", sortOrder));

        OperationInvalidInput failure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));

        Assert.Equal("invalid-arguments", failure.ReasonCode);
        Assert.Equal("changes[0].entryDraft.sortOrder", failure.Path);
        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies every integer a tool input can hold is an <c>int</c> or <c>long</c> (or nullable), the types the
    /// whole-number converters cover, so no tool can fall back to the serializer's generic failure.
    /// </summary>
    [Fact]
    public void EveryToolInput_UsesOnlyIntegerTypesTheConvertersCover()
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
        foreach (Type type in integers.Select(value => value.Type).Distinct())
        {
            object? read = JsonSerializer.Deserialize("5.0", type, ToolCallPolicy.SerializerOptions);

            Assert.Equal(5, Convert.ToInt32(read, CultureInfo.InvariantCulture));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("5.5", type, ToolCallPolicy.SerializerOptions));
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
