using System.Reflection;
using System.Text.Json;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies how a failed argument conversion names the type it expected, resolved from the bound parameter type.
/// </summary>
public sealed class ToolCallPolicyExpectedTypeTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built.
    /// </summary>
    public ToolCallPolicyExpectedTypeTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Verifies an enum is described by its names the tool serializer writes.
    /// </summary>
    [Fact]
    public void DescribeExpectedType_Enum_ListsTheSerializedNames()
    {
        Assert.Equal("one of: FirstChoice, SecondChoice", ToolCallPolicy.DescribeExpectedType(typeof(Choice)));
    }

    /// <summary>
    /// Verifies each scalar and collection type gets its plain-word description, and an unlisted type gets none.
    /// </summary>
    [Theory]
    [InlineData(typeof(Guid), "a UUID")]
    [InlineData(typeof(DateTimeOffset), "a date and time")]
    [InlineData(typeof(DateTime), "a date and time")]
    [InlineData(typeof(long), "an integer")]
    [InlineData(typeof(decimal), "a number")]
    [InlineData(typeof(bool), "true or false")]
    [InlineData(typeof(string), "text")]
    [InlineData(typeof(Guid[]), "a list")]
    [InlineData(typeof(Holder), "an object")]
    [InlineData(typeof(char), null)]
    public void DescribeExpectedType_Type_GivesPlainWords(Type type, string? expected)
    {
        Assert.Equal(expected, ToolCallPolicy.DescribeExpectedType(type));
    }

    /// <summary>
    /// Verifies a path is followed through members, list items, and nullable wrappers.
    /// </summary>
    [Theory]
    [InlineData("$.choice", typeof(Choice))]
    [InlineData("$.count", typeof(int))]
    [InlineData("$.when", typeof(DateTimeOffset))]
    [InlineData("$.ids[2]", typeof(Guid))]
    [InlineData("$.inner.ratio", typeof(double))]
    [InlineData("$['ratio']", null)]
    [InlineData("$.nothing", null)]
    public void ResolveTarget_Path_FindsTheDeclaredType(string path, Type? expected)
    {
        Assert.Equal(expected, ToolCallPolicy.ResolveTarget(typeof(Holder), path));
    }

    /// <summary>
    /// Verifies each kind of wrong value in an argument object reports the expected type in plain words.
    /// </summary>
    [Theory]
    [InlineData("{\"choice\":\"x\"}", "choice", "must be one of: FirstChoice, SecondChoice.")]
    [InlineData("{\"count\":\"x\"}", "count", "must be an integer.")]
    [InlineData("{\"when\":5}", "when", "must be a date and time.")]
    [InlineData("{\"inner\":{\"ratio\":\"x\"}}", "inner.ratio", "must be a number.")]
    [InlineData("{\"ids\":5}", "ids", "must be a list.")]
    [InlineData("{\"ids\":[\"x\"]}", "ids[0]", "must be a UUID.")]
    [InlineData("{\"letter\":5}", "letter", "could not be read:")]
    public void DiagnoseArguments_WrongValue_NamesPathAndExpectedType(string json, string path, string expected)
    {
        MethodInfo method = typeof(ToolCallPolicyExpectedTypeTests).GetMethod(
            nameof(Probe),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Dictionary<string, JsonElement> arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{\"holder\":" + json + "}")!;

        OperationInvalidInput failure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.DiagnoseArguments(method, arguments));

        Assert.Equal("holder." + path, failure.Path);
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a wrong value inside a patch change resolves through the union to the declared field type.
    /// </summary>
    [Theory]
    [InlineData("{\"kind\":\"patch-entries\",\"entryIds\":[7]}", "changes[3].entryIds[0]", "must be a UUID.")]
    [InlineData("{\"kind\":\"patch-entries\",\"entryIds\":7}", "changes[3].entryIds", "must be a list.")]
    [InlineData(
        "{\"kind\":\"patch-entries\",\"entryIds\":[],\"printing\":{\"printingId\":\"x\"}}",
        "changes[3].printing.printingId",
        "must be a UUID.")]
    [InlineData("{\"kind\":\"remove-entry\",\"entryId\":5}", "changes[3].entryId", "must be a UUID.")]
    [InlineData("{\"kind\":\"remove-entry\",\"entryIds\":[7]}", "changes[3].entryIds[0]", "must be a UUID.")]
    public void DiagnoseArguments_PatchChange_ResolvesThroughTheUnion(string change, string path, string expected)
    {
        string valid = "{\"kind\":\"remove-entry\",\"entryId\":\"0193e97a-ef95-7700-8000-000000000003\"}";
        Dictionary<string, JsonElement> arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{\"deckId\":\"0193e97a-ef95-7700-8000-000000000001\",\"expectedRevision\":1,\"changes\":[" +
            valid + "," + valid + "," + valid + "," + change + "]}")!;

        OperationInvalidInput failure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));

        Assert.Equal(path, failure.Path);
        Assert.Equal("The value at '" + path + "' " + expected, failure.Message);
    }

    /// <summary>
    /// Stands in for a tool method whose one parameter is bound from arguments.
    /// </summary>
    private static void Probe(Holder holder)
    {
        _ = holder;
    }

    /// <summary>
    /// Gives an enum the description lists by name.
    /// </summary>
    private enum Choice
    {
        /// <summary>
        /// The first value.
        /// </summary>
        FirstChoice,

        /// <summary>
        /// The second value.
        /// </summary>
        SecondChoice,
    }

    /// <summary>
    /// Carries one nested object for path resolution.
    /// </summary>
    private sealed record Inner(double Ratio);

    /// <summary>
    /// Carries one field of each type the description covers.
    /// </summary>
    private sealed record Holder(
        Choice? Choice,
        int? Count,
        DateTimeOffset? When,
        IReadOnlyList<Guid>? Ids,
        Inner? Inner,
        char? Letter);
}
