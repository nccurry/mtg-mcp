using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes network-free manual deck discovery, preview, and export workflows in every operation mode.
/// </summary>
internal sealed class DeckInterchangeReadTools
{
    /// <summary>
    /// Stores the bounded manual interchange application service.
    /// </summary>
    private readonly DeckInterchangeService service;

    /// <summary>
    /// Creates read-only interchange tools around one shared service.
    /// </summary>
    internal DeckInterchangeReadTools(DeckInterchangeService service)
    {
        this.service = service;
    }

    /// <summary>
    /// Lists deterministic manual interchange formats and their supported directions.
    /// </summary>
    [McpServerTool(
        Name = "deck_interchange_formats",
        Title = "List Manual Deck Interchange Formats",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists format IDs, supported import/export directions, losslessness, instructions, and experimental boundaries.")]
    internal OperationResult<IReadOnlyList<DeckInterchangeFormat>> ListFormats()
    {
        return service.ListFormats();
    }

    /// <summary>
    /// Parses caller-provided content without storage mutation or provider access.
    /// </summary>
    [McpServerTool(
        Name = "deck_import_preview",
        Title = "Preview Manual Deck Import",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a normalized proposal, diagnostics, unresolved identities, and guarded fingerprint.")]
    internal Task<OperationResult<DeckImportPreview>> PreviewAsync(
        [Description("Exact import-capable format ID returned by deck_interchange_formats.")] string formatId,
        [Description("UTF-8 manual deck document, limited to 5 MiB.")] string content,
        [Description("Caller-controlled parsing defaults and explicit opt-ins.")] DeckImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return service.PreviewAsync(formatId, content, options, cancellationToken);
    }

    /// <summary>
    /// Generates checksummed manual artifacts and a field-preservation report.
    /// </summary>
    [McpServerTool(
        Name = "deck_export_bundle",
        Title = "Export Manual Deck Artifact Bundle",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Exports deterministic artifacts without writing files or contacting providers.")]
    internal Task<OperationResult<DeckExportBundle>> ExportAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Exact export-capable format ID returned by deck_interchange_formats.")] string formatId,
        [Description("Explicit provider-format opt-ins; global Moxfield tags are off by default.")]
        DeckExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return service.ExportAsync(deckId, formatId, options, cancellationToken);
    }
}
