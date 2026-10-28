using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Prompts;

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
    /// Every toolset is on by default; the field stays in the resource for clients that read it. A toolset that is off
    /// because of missing credentials reports that in <c>disabledReason</c>.
    /// </summary>
    private const bool DefaultEnabled = true;

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
            (string credentialState, string? authenticationStatusTool) = GetCredentialProjection(
                configuration,
                descriptor.Toolset);
            toolsets.Add(new FoundationToolsetStatus(
                descriptor.Name,
                "implemented",
                credentialState,
                authenticationStatusTool,
                CapabilityToolsetPolicy.Format(descriptor.Stability),
                enabled,
                DefaultEnabled,
                configuration.Toolsets.FindDisabledReason(descriptor.Toolset),
                enabled ? descriptor.GetVisibleToolCount(configuration.Mode) : 0,
                descriptor.Description,
                descriptor.Toolset == CapabilityToolset.Playgroup
                    ? ["deck-update"]
                    : []));
        }

        FoundationCapabilityDocument document = new(
            8,
            new FoundationServerStatus(
                FoundationServerIdentity.Name,
                FoundationServerIdentity.PackageVersion,
                server.NegotiatedProtocolVersion ?? "unavailable"),
            OperationModeParser.Format(configuration.Mode),
            new FoundationSurfaceStatus(toolCount, 1, WorkflowPrompts.PromptCount),
            new FoundationToolsetsStatus(
                configuration.Toolsets.Label,
                "Toolsets control relevance; operation mode controls authority.",
                toolsets),
            new FoundationDataSchemas(
                "v0.9",
                "v1",
                "mtg-mcp.deck/v1",
                "v1",
                "observed-2026-07-04",
                "public-api-1.0.0",
                "api-6.3.3"),
            configuration.ToPublicStatus());
        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    /// <summary>
    /// Projects configuration presence without loading credentials or contacting a provider.
    /// </summary>
    internal static (string CredentialState, string? AuthenticationStatusTool) GetCredentialProjection(
        FoundationConfiguration configuration,
        CapabilityToolset toolset)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return toolset switch
        {
            CapabilityToolset.Decks or CapabilityToolset.Scryfall or CapabilityToolset.Stats or
                CapabilityToolset.Spellbook =>
                ("not-required", null),
            CapabilityToolset.Archidekt => (
                configuration.Credentials.Archidekt ? "configured-unverified" : "not-configured",
                "archidekt_auth_status"),
            CapabilityToolset.Playgroup => (
                GetPlaygroupCredentialState(configuration),
                "playgroup_auth_status"),
            _ => ("not-required", null),
        };
    }

    /// <summary>
    /// Reports the Playgroup credential state from setting and file presence only.
    /// </summary>
    private static string GetPlaygroupCredentialState(FoundationConfiguration configuration)
    {
        string? gateReason = configuration.Toolsets.FindDisabledReason(CapabilityToolset.Playgroup);
        if (gateReason == DisabledToolset.CredentialsInvalid)
        {
            return "configuration-invalid";
        }

        return configuration.Credentials.Playgroup
            ? "configured-unverified"
            : "not-configured";
    }
}
