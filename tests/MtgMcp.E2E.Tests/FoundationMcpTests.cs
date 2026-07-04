using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the complete foundation surface through an official stdio MCP client.
/// </summary>
public sealed class FoundationMcpTests
{
    /// <summary>
    /// Identifies the single public resource expected from every operation mode.
    /// </summary>
    private const string CapabilityUri = "mtg://server/capabilities";

    /// <summary>
    /// Lists the path-free legacy inspection states permitted by the public contract.
    /// </summary>
    private static readonly string[] LegacyDataStates =
        ["not-detected", "detected", "inspection-unavailable"];

    /// <summary>
    /// Verifies initialization, discovery, and capability content in every supported mode.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData(null, "local")]
    [InlineData("read-only", "read-only")]
    [InlineData("local", "local")]
    [InlineData("remote", "remote")]
    public async Task CapabilityResource_EachMode_ReportsExactFoundationSurface(
        string? configuredMode,
        string expectedMode)
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            configuredMode,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        string expectedVersion = Environment.GetEnvironmentVariable("MTGMCP_E2E_VERSION")
            ?? "0.9.0-preview.1";

        Assert.Equal("io.github.nccurry/mtg-mcp", session.Client.ServerInfo.Name);
        Assert.Equal("mtg-mcp", session.Client.ServerInfo.Title);
        Assert.Equal(expectedVersion, session.Client.ServerInfo.Version);
        Assert.Null(session.Client.ServerInstructions);
        Assert.NotNull(session.Client.ServerCapabilities.Resources);
        Assert.Null(session.Client.ServerCapabilities.Tools);
        Assert.Null(session.Client.ServerCapabilities.Prompts);
        Assert.Null(session.Client.ServerCapabilities.Logging);

        IList<McpClientResource> resources = await session.Client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        McpClientResource resource = Assert.Single(resources);
        Assert.Equal(CapabilityUri, resource.Uri);
        Assert.Equal("Server Capabilities", resource.Name);
        Assert.Equal("application/json", resource.MimeType);

        ReadResourceResult result = await session.Client.ReadResourceAsync(
            CapabilityUri,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        TextResourceContents content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        using JsonDocument payload = JsonDocument.Parse(content.Text);
        JsonElement root = payload.RootElement;

        AssertPropertyOrder(
            root,
            "schemaVersion",
            "server",
            "operationMode",
            "surface",
            "modules",
            "dataSchemas",
            "configuration");
        AssertPropertyOrder(root.GetProperty("server"), "name", "packageVersion", "protocolVersion");
        AssertPropertyOrder(root.GetProperty("surface"), "toolCount", "resourceCount", "promptCount");
        AssertPropertyOrder(root.GetProperty("dataSchemas"), "applicationData");
        AssertPropertyOrder(
            root.GetProperty("configuration"),
            "dataRootConfigured",
            "dataRootState",
            "legacyDataState",
            "migrationBoundary");
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("io.github.nccurry/mtg-mcp", root.GetProperty("server").GetProperty("name").GetString());
        Assert.Equal(expectedVersion, root.GetProperty("server").GetProperty("packageVersion").GetString());
        Assert.Equal(
            session.Client.NegotiatedProtocolVersion,
            root.GetProperty("server").GetProperty("protocolVersion").GetString());
        Assert.Equal(expectedMode, root.GetProperty("operationMode").GetString());
        AssertSurface(root.GetProperty("surface"));
        AssertFoundationModule(root.GetProperty("modules"));
        Assert.Equal(
            "v0.9",
            root.GetProperty("dataSchemas").GetProperty("applicationData").GetString());
        AssertConfiguration(root.GetProperty("configuration"));
        Assert.DoesNotContain(session.DataRoot, content.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(session.DataRoot));
    }

    /// <summary>
    /// Verifies an unknown resource remains an explicit protocol failure.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ReadResource_UnknownUri_ReturnsProtocolFailure()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await session.Client.ReadResourceAsync(
                    "mtg://server/unknown",
                    cancellationToken: TestContext.Current.CancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the public surface counts remain exactly zero tools, one resource, and zero prompts.
    /// </summary>
    private static void AssertSurface(JsonElement surface)
    {
        Assert.Equal(0, surface.GetProperty("toolCount").GetInt32());
        Assert.Equal(1, surface.GetProperty("resourceCount").GetInt32());
        Assert.Equal(0, surface.GetProperty("promptCount").GetInt32());
    }

    /// <summary>
    /// Verifies only the implemented foundation module is advertised.
    /// </summary>
    private static void AssertFoundationModule(JsonElement modules)
    {
        JsonElement module = Assert.Single(modules.EnumerateArray());
        Assert.Equal("foundation", module.GetProperty("name").GetString());
        Assert.Equal("available", module.GetProperty("status").GetString());
    }

    /// <summary>
    /// Verifies configuration status is path-free and reflects the isolated absent data root.
    /// </summary>
    private static void AssertConfiguration(JsonElement configuration)
    {
        Assert.True(configuration.GetProperty("dataRootConfigured").GetBoolean());
        Assert.Equal("not-created", configuration.GetProperty("dataRootState").GetString());
        Assert.Contains(
            configuration.GetProperty("legacyDataState").GetString(),
            LegacyDataStates);
        Assert.Contains(
            "migrat",
            configuration.GetProperty("migrationBoundary").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies a JSON object retains the deterministic public property order.
    /// </summary>
    private static void AssertPropertyOrder(JsonElement element, params string[] expectedNames)
    {
        Assert.Equal(
            expectedNames,
            element.EnumerateObject().Select(property => property.Name).ToArray());
    }
}
