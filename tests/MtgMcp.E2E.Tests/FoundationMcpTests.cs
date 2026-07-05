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
    private static readonly string[] DeckReadToolNames =
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
    private static readonly string[] DeckAllToolNames =
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
    /// Lists the Scryfall reads visible in every operation mode.
    /// </summary>
    private static readonly string[] ScryfallReadToolNames =
    [
        "scryfall_autocomplete",
        "scryfall_bulk_metadata",
        "scryfall_card_collection",
        "scryfall_card_get",
        "scryfall_card_prints",
        "scryfall_card_rulings",
        "scryfall_cards_by_tag",
        "scryfall_catalog",
        "scryfall_corpus_status",
        "scryfall_search",
        "scryfall_sets",
        "scryfall_snapshot_get",
        "scryfall_snapshot_list",
        "scryfall_tag_search",
    ];

    /// <summary>
    /// Lists the complete Scryfall surface when local writes are permitted.
    /// </summary>
    private static readonly string[] ScryfallAllToolNames =
    [
        .. ScryfallReadToolNames,
        "scryfall_corpus_delete",
        "scryfall_corpus_rollback",
        "scryfall_corpus_sync",
        "scryfall_snapshot_delete",
    ];

    /// <summary>
    /// Lists the Archidekt reads and previews visible in every operation mode.
    /// </summary>
    private static readonly string[] ArchidektReadToolNames =
    [
        "archidekt_auth_status",
        "archidekt_deck_get",
        "archidekt_deck_list",
        "archidekt_folder_get",
        "archidekt_folder_list",
        "archidekt_pull_preview",
        "archidekt_push_preview",
        "archidekt_snapshot_get",
        "archidekt_snapshot_list",
        "archidekt_snapshot_restore_preview",
        "archidekt_sync_diff",
    ];

    /// <summary>
    /// Lists the complete Archidekt surface when remote writes are permitted.
    /// </summary>
    private static readonly string[] ArchidektAllToolNames =
    [
        .. ArchidektReadToolNames,
        "archidekt_pull_apply",
        "archidekt_deck_create",
        "archidekt_deck_delete",
        "archidekt_folder_create",
        "archidekt_folder_delete",
        "archidekt_folder_move_items",
        "archidekt_folder_update",
        "archidekt_push_apply",
        "archidekt_snapshot_create",
        "archidekt_snapshot_delete",
        "archidekt_snapshot_restore_apply",
        "archidekt_snapshot_update",
    ];

    /// <summary>
    /// Verifies initialization, discovery, and capability content in every supported mode.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData(null, "local", 41)]
    [InlineData("read-only", "read-only", 21)]
    [InlineData("local", "local", 41)]
    [InlineData("remote", "remote", 41)]
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
            ExpectedToolNames("default", expectedMode),
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
        AssertPropertyOrder(
            root.GetProperty("dataSchemas"),
            "applicationData",
            "decks",
            "deckInterchange",
            "scryfall",
            "archidekt");
        AssertPropertyOrder(
            root.GetProperty("configuration"),
            "dataRootConfigured",
            "dataRootState",
            "legacyDataState",
            "migrationBoundary",
            "scryfallFreshnessHours");
        Assert.Equal(4, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("io.github.nccurry/mtg-mcp", root.GetProperty("server").GetProperty("name").GetString());
        Assert.Equal(expectedVersion, root.GetProperty("server").GetProperty("packageVersion").GetString());
        Assert.Equal(
            session.Client.NegotiatedProtocolVersion,
            root.GetProperty("server").GetProperty("protocolVersion").GetString());
        Assert.Equal(expectedMode, root.GetProperty("operationMode").GetString());
        AssertSurface(root.GetProperty("surface"), expectedToolCount);
        AssertToolsets(root.GetProperty("toolsets"), "default", "default", expectedMode);
        Assert.Equal(
            "v0.9",
            root.GetProperty("dataSchemas").GetProperty("applicationData").GetString());
        Assert.Equal("v1", root.GetProperty("dataSchemas").GetProperty("decks").GetString());
        Assert.Equal(
            "mtg-mcp.deck/v1",
            root.GetProperty("dataSchemas").GetProperty("deckInterchange").GetString());
        Assert.Equal("v1", root.GetProperty("dataSchemas").GetProperty("scryfall").GetString());
        Assert.Equal(
            "observed-2026-07-04",
            root.GetProperty("dataSchemas").GetProperty("archidekt").GetString());
        AssertConfiguration(root.GetProperty("configuration"));
        Assert.DoesNotContain(session.DataRoot, content.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(session.DataRoot));
    }

    /// <summary>
    /// Verifies default, all, none, and explicit profiles remain static in every operation mode.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("read-only", "default", "default", 21)]
    [InlineData("local", "default", "default", 41)]
    [InlineData("remote", "default", "default", 41)]
    [InlineData("read-only", "all", "all", 32)]
    [InlineData("local", "all", "all", 53)]
    [InlineData("remote", "all", "all", 64)]
    [InlineData("read-only", "decks", "explicit", 7)]
    [InlineData("local", "decks", "explicit", 23)]
    [InlineData("remote", "decks", "explicit", 23)]
    [InlineData("read-only", "scryfall", "explicit", 14)]
    [InlineData("local", "scryfall", "explicit", 18)]
    [InlineData("remote", "scryfall", "explicit", 18)]
    [InlineData("read-only", "archidekt", "explicit", 11)]
    [InlineData("local", "archidekt", "explicit", 12)]
    [InlineData("remote", "archidekt", "explicit", 23)]
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
            string[] expectedNames = ExpectedToolNames(configuredToolsets, mode);
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
            configuredToolsets,
            mode);
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
            "decks",
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
            "decks",
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
        HashSet<string> readOnly = [.. DeckReadToolNames];
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
    /// Verifies every Archidekt tool publishes its exact top-level schema and safety annotations.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ArchidektTools_RemoteMode_PublishExactSchemasAndAnnotations()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "remote",
            "archidekt",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        Dictionary<string, string[]> expectedProperties = new(StringComparer.Ordinal)
        {
            ["archidekt_auth_status"] = [],
            ["archidekt_deck_create"] = ["expectedLocalRevision", "localDeckId", "request"],
            ["archidekt_deck_delete"] = ["request"],
            ["archidekt_deck_get"] = ["deckId"],
            ["archidekt_deck_list"] = ["cursor", "pageSize"],
            ["archidekt_folder_create"] = ["request"],
            ["archidekt_folder_delete"] = ["request"],
            ["archidekt_folder_get"] = ["folderId"],
            ["archidekt_folder_list"] = [],
            ["archidekt_folder_move_items"] = ["request"],
            ["archidekt_folder_update"] = ["request"],
            ["archidekt_pull_apply"] = ["request"],
            ["archidekt_pull_preview"] = ["localDeckId", "remoteDeckId"],
            ["archidekt_push_apply"] = ["request"],
            ["archidekt_push_preview"] = ["localDeckId"],
            ["archidekt_snapshot_create"] = ["request"],
            ["archidekt_snapshot_delete"] = ["request"],
            ["archidekt_snapshot_get"] = ["deckId", "snapshotId"],
            ["archidekt_snapshot_list"] = ["deckId"],
            ["archidekt_snapshot_restore_apply"] = ["request"],
            ["archidekt_snapshot_restore_preview"] = ["deckId", "snapshotId"],
            ["archidekt_snapshot_update"] = ["request"],
            ["archidekt_sync_diff"] = ["localDeckId"],
        };
        HashSet<string> reads = [.. ArchidektReadToolNames];
        HashSet<string> destructive =
        [
            "archidekt_deck_delete",
            "archidekt_folder_delete",
            "archidekt_folder_move_items",
            "archidekt_pull_apply",
            "archidekt_push_apply",
            "archidekt_snapshot_delete",
            "archidekt_snapshot_restore_apply",
        ];
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(23, tools.Count);
        foreach (McpClientTool tool in tools)
        {
            Assert.Equal(
                expectedProperties[tool.Name],
                tool.ProtocolTool.InputSchema.GetProperty("properties")
                    .EnumerateObject()
                    .Select(value => value.Name)
                    .Order(StringComparer.Ordinal));
            Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
            Assert.NotNull(tool.ProtocolTool.Annotations);
            Assert.Equal(reads.Contains(tool.Name), tool.ProtocolTool.Annotations.ReadOnlyHint);
            Assert.Equal(destructive.Contains(tool.Name), tool.ProtocolTool.Annotations.DestructiveHint);
            Assert.Equal(reads.Contains(tool.Name), tool.ProtocolTool.Annotations.IdempotentHint);
            Assert.Equal(tool.Name != "archidekt_auth_status", tool.ProtocolTool.Annotations.OpenWorldHint);
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
    /// Verifies all implemented toolsets are advertised with exact relevance metadata.
    /// </summary>
    private static void AssertToolsets(
        JsonElement toolsets,
        string expectedSelection,
        string configuredToolsets,
        string mode)
    {
        Assert.Equal(expectedSelection, toolsets.GetProperty("selection").GetString());
        Assert.Equal(
            "Toolsets control relevance; operation mode controls authority.",
            toolsets.GetProperty("authorityBoundary").GetString());
        JsonElement[] descriptors = toolsets.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, descriptors.Length);
        bool decksEnabled = configuredToolsets is "default" or "all" or "decks";
        bool scryfallEnabled = configuredToolsets is "default" or "all" or "scryfall";
        bool archidektEnabled = configuredToolsets is "all" or "archidekt";
        AssertDescriptor(
            descriptors[0],
            "decks",
            decksEnabled,
            defaultEnabled: true,
            decksEnabled ? (mode == "read-only" ? 7 : 23) : 0);
        AssertDescriptor(
            descriptors[1],
            "scryfall",
            scryfallEnabled,
            defaultEnabled: true,
            scryfallEnabled ? (mode == "read-only" ? 14 : 18) : 0);
        AssertDescriptor(
            descriptors[2],
            "archidekt",
            archidektEnabled,
            defaultEnabled: false,
            archidektEnabled ? mode switch
            {
                "read-only" => 11,
                "local" => 12,
                _ => 23,
            } : 0);
        Assert.Contains(
            "operation mode separately controls local writes",
            descriptors[0].GetProperty("description").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies one implemented capability descriptor.
    /// </summary>
    private static void AssertDescriptor(
        JsonElement descriptor,
        string name,
        bool enabled,
        bool defaultEnabled,
        int visibleToolCount)
    {
        AssertPropertyOrder(
            descriptor,
            "name",
            "status",
            "stability",
            "enabled",
            "defaultEnabled",
            "visibleToolCount",
            "description");
        Assert.Equal(name, descriptor.GetProperty("name").GetString());
        Assert.Equal("available", descriptor.GetProperty("status").GetString());
        Assert.Equal("stable", descriptor.GetProperty("stability").GetString());
        Assert.Equal(enabled, descriptor.GetProperty("enabled").GetBoolean());
        Assert.Equal(defaultEnabled, descriptor.GetProperty("defaultEnabled").GetBoolean());
        Assert.Equal(visibleToolCount, descriptor.GetProperty("visibleToolCount").GetInt32());
    }

    /// <summary>
    /// Calculates the exact stable tool names for one profile and mode.
    /// </summary>
    private static string[] ExpectedToolNames(string toolsets, string mode)
    {
        bool readsOnly = mode == "read-only";
        IEnumerable<string> names = [];
        if (toolsets is "default" or "all" or "decks")
        {
            names = names.Concat(readsOnly ? DeckReadToolNames : DeckAllToolNames);
        }

        if (toolsets is "default" or "all" or "scryfall")
        {
            names = names.Concat(readsOnly ? ScryfallReadToolNames : ScryfallAllToolNames);
        }

        if (toolsets is "all" or "archidekt")
        {
            names = names.Concat(mode switch
            {
                "read-only" => ArchidektReadToolNames,
                "local" => [.. ArchidektReadToolNames, "archidekt_pull_apply"],
                _ => ArchidektAllToolNames,
            });
        }

        return names.Order(StringComparer.Ordinal).ToArray();
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
        Assert.Equal(24, configuration.GetProperty("scryfallFreshnessHours").GetDouble());
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
