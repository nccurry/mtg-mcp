using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Exposes the foundation's only MCP resource through an explicitly registered type.
/// </summary>
internal sealed class FoundationResources
{
    /// <summary>
    /// Identifies the one stable foundation resource.
    /// </summary>
    internal const string CapabilityUri = "mtg://server/capabilities";

    /// <summary>
    /// Serializes the deterministic capability contract using web JSON conventions.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores the validated private runtime configuration.
    /// </summary>
    private readonly FoundationConfiguration configuration;

    /// <summary>
    /// Creates the resource around one validated process configuration.
    /// </summary>
    internal FoundationResources(FoundationConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Returns effective runtime capability metadata without credentials or absolute paths.
    /// </summary>
    [McpServerResource(
        UriTemplate = CapabilityUri,
        Name = "Server Capabilities",
        MimeType = "application/json")]
    [Description("Effective mtg-mcp mode, surface, toolset selection, schema, and clean-break status.")]
    internal string GetCapabilities(McpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        int toolCount = CapabilityToolsetRegistry.CountVisibleTools(
            configuration.Toolsets,
            configuration.Mode);
        List<FoundationToolsetStatus> toolsets = [];
        foreach (CapabilityToolsetDescriptor descriptor in CapabilityToolsetRegistry.Implemented)
        {
            bool enabled = configuration.Toolsets.Includes(descriptor.Toolset);
            toolsets.Add(new FoundationToolsetStatus(
                descriptor.Name,
                CapabilityToolsetPolicy.Format(descriptor.Availability),
                CapabilityToolsetPolicy.Format(descriptor.Stability),
                enabled,
                descriptor.DefaultEnabled,
                enabled ? descriptor.GetVisibleToolCount(configuration.Mode) : 0,
                descriptor.Description));
        }

        FoundationCapabilityDocument document = new(
            4,
            new FoundationServerStatus(
                FoundationServerIdentity.Name,
                FoundationServerIdentity.PackageVersion,
                server.NegotiatedProtocolVersion ?? "unavailable"),
            OperationModeParser.Format(configuration.Mode),
            new FoundationSurfaceStatus(toolCount, 1, 0),
            new FoundationToolsetsStatus(
                configuration.Toolsets.Label,
                "Toolsets control relevance; operation mode controls authority.",
                toolsets),
            new FoundationDataSchemas("v0.9", "v1", "mtg-mcp.deck/v1", "v1", "observed-2026-07-04"),
            configuration.ToPublicStatus());
        return JsonSerializer.Serialize(document, SerializerOptions);
    }
}
