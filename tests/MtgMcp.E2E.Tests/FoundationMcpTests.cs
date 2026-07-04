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
    /// Lists the read-only deck surface available in every operation mode.
    /// </summary>
    private static readonly string[] ReadToolNames =
    [
        "deck_backup_list",
        "deck_export_bundle",
        "deck_get",
        "deck_import_preview",
        "deck_interchange_formats",
        "deck_list",
        "deck_validate",
    ];

    /// <summary>
    /// Lists the complete local deck surface available when local writes are permitted.
    /// </summary>
    private static readonly string[] AllToolNames =
    [
        "deck_apply_changes",
        "deck_backup_create",
        "deck_backup_delete",
        "deck_backup_list",
        "deck_backup_restore",
        "deck_category_assign",
        "deck_category_create",
        "deck_category_delete",
        "deck_category_unassign",
        "deck_category_update",
        "deck_create",
        "deck_delete",
        "deck_entry_add",
        "deck_entry_remove",
        "deck_entry_update",
        "deck_export_bundle",
        "deck_get",
        "deck_import_create",
        "deck_import_preview",
        "deck_interchange_formats",
        "deck_list",
        "deck_update",
        "deck_validate",
    ];

    /// <summary>
    /// Verifies initialization, discovery, and capability content in every supported mode.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData(null, "local", 23)]
    [InlineData("read-only", "read-only", 7)]
    [InlineData("local", "local", 23)]
    [InlineData("remote", "remote", 23)]
    public async Task CapabilityResource_EachMode_ReportsExactFoundationSurface(
        string? configuredMode,
        string expectedMode,
        int expectedToolCount)
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
        Assert.NotNull(session.Client.ServerCapabilities.Tools);
        Assert.Null(session.Client.ServerCapabilities.Prompts);
        Assert.Null(session.Client.ServerCapabilities.Logging);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.Equal(
            expectedToolCount == 7 ? ReadToolNames : AllToolNames,
            tools.Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());

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
            "toolsets",
            "dataSchemas",
            "configuration");
        AssertPropertyOrder(root.GetProperty("server"), "name", "packageVersion", "protocolVersion");
        AssertPropertyOrder(root.GetProperty("surface"), "toolCount", "resourceCount", "promptCount");
        AssertPropertyOrder(root.GetProperty("toolsets"), "selection", "authorityBoundary", "items");
        AssertPropertyOrder(root.GetProperty("dataSchemas"), "applicationData", "decks", "deckInterchange");
        AssertPropertyOrder(
            root.GetProperty("configuration"),
            "dataRootConfigured",
            "dataRootState",
            "legacyDataState",
            "migrationBoundary");
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("io.github.nccurry/mtg-mcp", root.GetProperty("server").GetProperty("name").GetString());
        Assert.Equal(expectedVersion, root.GetProperty("server").GetProperty("packageVersion").GetString());
        Assert.Equal(
            session.Client.NegotiatedProtocolVersion,
            root.GetProperty("server").GetProperty("protocolVersion").GetString());
        Assert.Equal(expectedMode, root.GetProperty("operationMode").GetString());
        AssertSurface(root.GetProperty("surface"), expectedToolCount);
        AssertToolsets(root.GetProperty("toolsets"), "default", true, expectedToolCount);
        Assert.Equal(
            "v0.9",
            root.GetProperty("dataSchemas").GetProperty("applicationData").GetString());
        Assert.Equal("v1", root.GetProperty("dataSchemas").GetProperty("decks").GetString());
        Assert.Equal(
            "mtg-mcp.deck/v1",
            root.GetProperty("dataSchemas").GetProperty("deckInterchange").GetString());
        AssertConfiguration(root.GetProperty("configuration"));
        Assert.DoesNotContain(session.DataRoot, content.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(session.DataRoot));
    }

    /// <summary>
    /// Verifies default, all, none, and explicit profiles remain static in every operation mode.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("read-only", "default", "default", 7)]
    [InlineData("local", "default", "default", 23)]
    [InlineData("remote", "default", "default", 23)]
    [InlineData("read-only", "all", "all", 7)]
    [InlineData("local", "all", "all", 23)]
    [InlineData("remote", "all", "all", 23)]
    [InlineData("read-only", "decks", "explicit", 7)]
    [InlineData("local", "decks", "explicit", 23)]
    [InlineData("remote", "decks", "explicit", 23)]
    [InlineData("read-only", "none", "none", 0)]
    [InlineData("local", "none", "none", 0)]
    [InlineData("remote", "none", "none", 0)]
    public async Task ToolsetProfiles_EachMode_ExposeExactStaticSurface(
        string mode,
        string configuredToolsets,
        string expectedSelection,
        int expectedToolCount)
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            mode,
            configuredToolsets,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        if (expectedToolCount == 0)
        {
            Assert.Null(session.Client.ServerCapabilities.Tools);
        }
        else
        {
            Assert.NotNull(session.Client.ServerCapabilities.Tools);
            Assert.False(session.Client.ServerCapabilities.Tools.ListChanged ?? false);
            IList<McpClientTool> first = await session.Client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            IList<McpClientTool> second = await session.Client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            string[] expectedNames = expectedToolCount == 7 ? ReadToolNames : AllToolNames;
            Assert.Equal(expectedNames, first.Select(value => value.Name));
            Assert.Equal(
                first.Select(value => value.Name),
                second.Select(value => value.Name));
        }

        string firstCapability = await ReadCapabilityTextAsync(session).ConfigureAwait(false);
        string secondCapability = await ReadCapabilityTextAsync(session).ConfigureAwait(false);
        Assert.Equal(firstCapability, secondCapability);
        using JsonDocument document = JsonDocument.Parse(firstCapability);
        JsonElement root = document.RootElement;
        AssertSurface(root.GetProperty("surface"), expectedToolCount);
        AssertToolsets(
            root.GetProperty("toolsets"),
            expectedSelection,
            expectedToolCount > 0,
            expectedToolCount);
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
    /// Verifies every deck tool publishes its exact top-level schema and safety annotations.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task DeckTools_LocalMode_PublishExactSchemasAndAnnotations()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        Dictionary<string, string[]> expectedProperties = new(StringComparer.Ordinal)
        {
            ["deck_apply_changes"] = ["changes", "deckId", "expectedRevision"],
            ["deck_backup_create"] = [],
            ["deck_backup_delete"] = ["backupId"],
            ["deck_backup_list"] = [],
            ["deck_backup_restore"] = ["backupId", "expectedDatabaseFingerprint"],
            ["deck_category_assign"] = ["categoryId", "deckId", "entryId", "expectedRevision", "isPrimary"],
            ["deck_category_create"] = ["category", "deckId", "expectedRevision"],
            ["deck_category_delete"] = ["categoryId", "deckId", "expectedRevision"],
            ["deck_category_unassign"] = ["categoryId", "deckId", "entryId", "expectedRevision"],
            ["deck_category_update"] = ["category", "deckId", "expectedRevision"],
            ["deck_create"] = ["request"],
            ["deck_delete"] = ["deckId", "expectedRevision"],
            ["deck_entry_add"] = ["deckId", "entry", "expectedRevision"],
            ["deck_entry_remove"] = ["deckId", "entryId", "expectedRevision"],
            ["deck_entry_update"] = ["deckId", "entry", "expectedRevision"],
            ["deck_export_bundle"] = ["deckId", "formatId", "options"],
            ["deck_get"] = ["deckId"],
            ["deck_import_create"] = ["content", "expectedFingerprint", "formatId", "options"],
            ["deck_import_preview"] = ["content", "formatId", "options"],
            ["deck_interchange_formats"] = [],
            ["deck_list"] = ["cursor", "pageSize"],
            ["deck_update"] = ["deckId", "description", "expectedRevision", "format", "name"],
            ["deck_validate"] = ["deckId"],
        };
        HashSet<string> readOnly = [.. ReadToolNames];
        HashSet<string> destructive =
        [
            "deck_apply_changes",
            "deck_backup_delete",
            "deck_backup_restore",
            "deck_category_delete",
            "deck_category_unassign",
            "deck_delete",
            "deck_entry_remove",
        ];
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        foreach (McpClientTool tool in tools)
        {
            Assert.True(expectedProperties.TryGetValue(tool.Name, out string[]? properties));
            Assert.Equal(
                properties,
                tool.ProtocolTool.InputSchema.GetProperty("properties")
                    .EnumerateObject()
                    .Select(value => value.Name)
                    .Order(StringComparer.Ordinal));
            Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
            Assert.Equal(
                "object",
                tool.ProtocolTool.OutputSchema.Value.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tool.Title));
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.NotNull(tool.ProtocolTool.Annotations);
            Assert.Equal(readOnly.Contains(tool.Name), tool.ProtocolTool.Annotations.ReadOnlyHint);
            Assert.Equal(destructive.Contains(tool.Name), tool.ProtocolTool.Annotations.DestructiveHint);
            Assert.Equal(readOnly.Contains(tool.Name), tool.ProtocolTool.Annotations.IdempotentHint);
            Assert.False(tool.ProtocolTool.Annotations.OpenWorldHint);
        }
    }

    /// <summary>
    /// Verifies the exact mode-specific tool count, one resource, and zero prompts.
    /// </summary>
    private static void AssertSurface(JsonElement surface, int expectedToolCount)
    {
        Assert.Equal(expectedToolCount, surface.GetProperty("toolCount").GetInt32());
        Assert.Equal(1, surface.GetProperty("resourceCount").GetInt32());
        Assert.Equal(0, surface.GetProperty("promptCount").GetInt32());
    }

    /// <summary>
    /// Verifies only the implemented decks toolset is advertised with exact relevance metadata.
    /// </summary>
    private static void AssertToolsets(
        JsonElement toolsets,
        string expectedSelection,
        bool expectedEnabled,
        int expectedVisibleToolCount)
    {
        Assert.Equal(expectedSelection, toolsets.GetProperty("selection").GetString());
        Assert.Equal(
            "Toolsets control relevance; operation mode controls authority.",
            toolsets.GetProperty("authorityBoundary").GetString());
        JsonElement descriptor = Assert.Single(toolsets.GetProperty("items").EnumerateArray());
        AssertPropertyOrder(
            descriptor,
            "name",
            "status",
            "stability",
            "enabled",
            "defaultEnabled",
            "visibleToolCount",
            "description");
        Assert.Equal("decks", descriptor.GetProperty("name").GetString());
        Assert.Equal("available", descriptor.GetProperty("status").GetString());
        Assert.Equal("stable", descriptor.GetProperty("stability").GetString());
        Assert.Equal(expectedEnabled, descriptor.GetProperty("enabled").GetBoolean());
        Assert.True(descriptor.GetProperty("defaultEnabled").GetBoolean());
        Assert.Equal(expectedVisibleToolCount, descriptor.GetProperty("visibleToolCount").GetInt32());
        Assert.Contains(
            "operation mode separately controls local writes",
            descriptor.GetProperty("description").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the exact capability JSON from one initialized session.
    /// </summary>
    private static async Task<string> ReadCapabilityTextAsync(McpProcessSession session)
    {
        ReadResourceResult result = await session.Client.ReadResourceAsync(
            CapabilityUri,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        TextResourceContents content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        return content.Text;
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
