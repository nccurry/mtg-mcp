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
    [property: JsonPropertyName("modules")] IReadOnlyList<FoundationModuleStatus> Modules,
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
/// Reports one currently available production module without advertising placeholders.
/// </summary>
internal sealed record FoundationModuleStatus(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status);

/// <summary>
/// Reports the application-data schema family without exposing a filesystem path.
/// </summary>
internal sealed record FoundationDataSchemas(
    [property: JsonPropertyName("applicationData")] string ApplicationData,
    [property: JsonPropertyName("decks")] string Decks);
