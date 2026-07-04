using System.Text.Json.Serialization;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Describes the versioned public capability document returned by the foundation resource.
/// </summary>
internal sealed record FoundationCapabilityDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("server")] FoundationServerStatus Server,
    [property: JsonPropertyName("operationMode")] string OperationMode,
    [property: JsonPropertyName("surface")] FoundationSurfaceStatus Surface,
    [property: JsonPropertyName("toolsets")] FoundationToolsetsStatus Toolsets,
    [property: JsonPropertyName("dataSchemas")] FoundationDataSchemas DataSchemas,
    [property: JsonPropertyName("configuration")] FoundationConfigurationStatus Configuration);

/// <summary>
/// Describes the initialized server and negotiated protocol versions.
/// </summary>
internal sealed record FoundationServerStatus(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("packageVersion")] string PackageVersion,
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion);

/// <summary>
/// Reports the complete foundation MCP surface counts.
/// </summary>
internal sealed record FoundationSurfaceStatus(
    [property: JsonPropertyName("toolCount")] int ToolCount,
    [property: JsonPropertyName("resourceCount")] int ResourceCount,
    [property: JsonPropertyName("promptCount")] int PromptCount);

/// <summary>
/// Reports the configured selection and implemented toolsets without advertising placeholders.
/// </summary>
internal sealed record FoundationToolsetsStatus(
    [property: JsonPropertyName("selection")] string Selection,
    [property: JsonPropertyName("authorityBoundary")] string AuthorityBoundary,
    [property: JsonPropertyName("items")] IReadOnlyList<FoundationToolsetStatus> Items);

/// <summary>
/// Reports one implemented toolset's relevance, availability, and visible surface.
/// </summary>
internal sealed record FoundationToolsetStatus(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stability")] string Stability,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("defaultEnabled")] bool DefaultEnabled,
    [property: JsonPropertyName("visibleToolCount")] int VisibleToolCount,
    [property: JsonPropertyName("description")] string Description);

/// <summary>
/// Reports the application-data schema family without exposing a filesystem path.
/// </summary>
internal sealed record FoundationDataSchemas(
    [property: JsonPropertyName("applicationData")] string ApplicationData,
    [property: JsonPropertyName("decks")] string Decks,
    [property: JsonPropertyName("deckInterchange")] string DeckInterchange);
