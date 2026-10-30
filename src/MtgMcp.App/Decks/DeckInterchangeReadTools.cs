using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
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
        Name = "local_deck_interchange_formats",
        Title = "List Manual Deck Interchange Formats",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: lists format IDs, supported import/export directions, losslessness, instructions, and " +
        "preservation boundaries.")]
    internal OperationResult<IReadOnlyList<DeckInterchangeFormat>> ListFormats()
    {
        return service.ListFormats();
    }

    /// <summary>
    /// Parses caller-provided content without storage mutation or provider access.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_import_preview",
        Title = "Preview Manual Deck Import",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: returns a normalized proposal, diagnostics, unresolved identities, and guarded " +
        "fingerprint. Entries come in pages that fit the size limit; the fingerprint covers the whole import, and " +
        "categories, diagnostics, and unresolved identities come with the first page. When truncated is true, call " +
        "again with the same content and offset set to nextOffset.")]
    internal async Task<OperationResult<DeckImportPreviewView>> PreviewAsync(
        [Description(
            "Exact import-capable format ID returned by local_deck_interchange_formats."), InterchangeFormatValues]
        string formatId,
        [Description("UTF-8 manual deck document, limited to 5 MiB.")] string content,
        [Description("Caller-controlled parsing defaults and explicit opt-ins.")] DeckImportOptions? options = null,
        [Description("Number of proposed entries to skip; use nextOffset from the previous page. Default 0.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationResult<DeckImportPreview> preview = await service
            .PreviewAsync(formatId, content, options, cancellationToken).ConfigureAwait(false);
        return preview is OperationSuccess<DeckImportPreview> success
            ? DeckPreviewPaging.PageImport(success.Data, offset)
            : OperationFailure.Forward<DeckImportPreview, DeckImportPreviewView>(preview);
    }

    /// <summary>
    /// Generates checksummed manual artifacts and a field-preservation report.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_export_bundle",
        Title = "Export Manual Deck Artifact Bundle",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: exports deterministic artifacts without writing files or contacting providers. The result " +
        "lists every artifact (file name, media type, total length, sha256 of the whole content) and returns one " +
        "chunk of one artifact's content, at most about 30,000 characters. When chunk.truncated is true, call again " +
        "with the same arguments, artifact set to the file name, and offset set to chunk.nextOffset; the chunks " +
        "joined in order are the exact file.")]
    internal async Task<OperationResult<DeckExportBundleView>> ExportAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description(
            "Exact export-capable format ID returned by local_deck_interchange_formats."), InterchangeFormatValues]
        string formatId,
        [Description("Explicit provider-format opt-ins; global Moxfield tags are off by default.")]
        DeckExportOptions? options = null,
        [Description("File name of the artifact whose content to read; default the first artifact.")]
        string? artifact = null,
        [Description("Number of characters of the artifact to skip; use chunk.nextOffset from the previous call. Default 0.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationResult<DeckExportBundle> bundle = await service
            .ExportAsync(deckId, formatId, options, cancellationToken).ConfigureAwait(false);
        return bundle is OperationSuccess<DeckExportBundle> success
            ? DeckExportPaging.Page(success.Data, artifact, offset)
            : OperationFailure.Forward<DeckExportBundle, DeckExportBundleView>(bundle);
    }
}
