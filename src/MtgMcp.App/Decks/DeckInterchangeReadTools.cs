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
        "fingerprint. Entries come in pages that fit the size limit; the fingerprint covers the whole import. " +
        "The first page also carries the categories, provider bindings, diagnostics, and unresolved identities, " +
        "each cut to fit with its exact count beside it, and counts the saved synchronization snapshots without " +
        "returning them. When truncated is true, call again with the same content, offset set to nextOffset, and " +
        "expectedPreviewFingerprint set to the fingerprint of the first page.")]
    internal async Task<OperationResult<DeckImportPreviewView>> PreviewAsync(
        [Description(
            "Exact import-capable format ID returned by local_deck_interchange_formats."), InterchangeFormatValues]
        string formatId,
        [Description("UTF-8 manual deck document, limited to 5 MiB.")] string content,
        [Description("Caller-controlled parsing defaults and explicit opt-ins.")] DeckImportOptions? options = null,
        [Description("Number of proposed entries to skip; use nextOffset from the previous page. Default 0.")]
        int offset = 0,
        [Description(ResultPage.FingerprintHelp)] string? expectedPreviewFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult<DeckImportPreview> preview = await service
            .PreviewAsync(formatId, content, options, cancellationToken).ConfigureAwait(false);
        return preview is OperationSuccess<DeckImportPreview> success
            ? DeckPreviewPaging.PageImport(success.Data, offset, expectedPreviewFingerprint)
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
        "lists every artifact (file name, media type, total length in UTF-16 code units, sha256 of the whole content) " +
        "and returns one chunk of one artifact's content, at most 30,000 UTF-8 bytes of JSON text. When " +
        "chunk.truncated is true, call again with the same arguments, artifact set to the file name, and offset " +
        "set to chunk.nextOffset; the chunks joined in order are the exact file. The field-preservation report " +
        "comes only with the chunk at offset 0.")]
    internal async Task<OperationResult<DeckExportBundleView>> ExportAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description(
            "Exact export-capable format ID returned by local_deck_interchange_formats."), InterchangeFormatValues]
        string formatId,
        [Description("Explicit provider-format opt-ins; global Moxfield tags are off by default.")]
        DeckExportOptions? options = null,
        [Description("File name of the artifact whose content to read; default the first artifact.")]
        string? artifact = null,
        [Description(
            "Number of UTF-16 code units of the artifact to skip; pass chunk.nextOffset from the previous call. " +
            "Default 0.")]
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
