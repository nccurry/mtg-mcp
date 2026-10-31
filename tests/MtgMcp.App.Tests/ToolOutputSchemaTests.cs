using System.Text.Json;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies every registered tool publishes the one shared object output schema, so no tool publishes the open
/// schema <c>true</c> that protocol 2026-07-28 rejects.
/// </summary>
public sealed class ToolOutputSchemaTests
{
    /// <summary>
    /// Verifies each tool publishes exactly the shared schema. A tool that returns something other than an
    /// operation result gets a different schema or none, so it fails here.
    /// </summary>
    [Fact]
    public void EveryRegisteredTool_PublishesTheSharedObjectOutputSchema()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        string shared = OperationResultSchema.Schema.GetRawText();

        Assert.NotEmpty(registered.Tools);
        foreach (McpServerTool tool in registered.Tools)
        {
            JsonElement schema = tool.ProtocolTool.OutputSchema ??
                throw new Xunit.Sdk.XunitException($"{tool.ProtocolTool.Name} publishes no outputSchema.");
            Assert.True(
                schema.ValueKind == JsonValueKind.Object,
                $"{tool.ProtocolTool.Name} outputSchema is {schema.ValueKind}.");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal(shared, schema.GetRawText());
        }
    }

    /// <summary>
    /// Verifies the shared schema lists every kind an operation result can have, read from Core.
    /// </summary>
    [Fact]
    public void SharedSchema_ListsEveryOperationResultKind()
    {
        string[] expected =
        [
            "success", "not-found", "not-cached", "unsupported", "unavailable", "conflict", "invalid-input",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), OperationResultSchema.Kinds.Order(StringComparer.Ordinal));
        JsonElement kind = OperationResultSchema.Schema.GetProperty("properties").GetProperty("kind");
        Assert.Equal(
            OperationResultSchema.Kinds,
            kind.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!));
        Assert.Equal("kind", OperationResultSchema.Schema.GetProperty("required")[0].GetString());
    }
}
