using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Pins how the MCP SDK builds tool input schemas from parameter types, which the tool surface relies on.
/// </summary>
public sealed class ToolSchemaSdkTests
{
    /// <summary>
    /// A sample choice type declared the way tool choice parameters are declared.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SampleChoice>))]
    internal enum SampleChoice
    {
        /// <summary>The default choice.</summary>
        [JsonStringEnumMemberName("default")]
        Default,

        /// <summary>A hyphenated choice.</summary>
        [JsonStringEnumMemberName("cache-only")]
        CacheOnly,
    }

    /// <summary>
    /// Verifies a string-enum parameter is published as a JSON Schema enum of its wire names.
    /// </summary>
    [Fact]
    public void EnumParameter_PublishesWireNamesAsEnum()
    {
        McpServerTool tool = McpServerTool.Create(
            (SampleChoice policy) => policy.ToString(),
            new McpServerToolCreateOptions { Name = "sample", SerializerOptions = ToolCallPolicy.SerializerOptions });

        JsonElement policy = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("policy");

        Assert.Equal(
            ["default", "cache-only"],
            policy.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// Verifies allowed-value annotations on string parameters and record properties publish a JSON Schema enum.
    /// </summary>
    [Fact]
    public void AllowedValuesAnnotation_PublishesEnum()
    {
        McpServerTool tool = McpServerTool.Create(
            ([System.ComponentModel.DataAnnotations.AllowedValues("cards", "art", "prints")] string unique, SampleRequest request) =>
                unique + request.Visibility,
            new McpServerToolCreateOptions { Name = "sample", SerializerOptions = ToolCallPolicy.SerializerOptions });

        JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");

        Assert.Equal(
            ["cards", "art", "prints"],
            properties.GetProperty("unique").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["private", "public"],
            properties.GetProperty("request").GetProperty("properties").GetProperty("visibility")
                .GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// A sample request record with an annotated string choice property.
    /// </summary>
    internal sealed record SampleRequest(
        [property: System.ComponentModel.DataAnnotations.AllowedValues("private", "public")] string Visibility);

    /// <summary>
    /// Records that the SDK's default input schema does not forbid unknown properties, so rejection needs our own rule.
    /// </summary>
    [Fact]
    public void DefaultInputSchema_DoesNotForbidUnknownProperties()
    {
        McpServerTool tool = McpServerTool.Create(
            (string name) => name,
            new McpServerToolCreateOptions { Name = "sample", SerializerOptions = ToolCallPolicy.SerializerOptions });

        JsonElement schema = tool.ProtocolTool.InputSchema;

        Assert.False(
            schema.TryGetProperty("additionalProperties", out JsonElement additional) &&
            additional.ValueKind == JsonValueKind.False,
            schema.GetRawText());
    }
}
