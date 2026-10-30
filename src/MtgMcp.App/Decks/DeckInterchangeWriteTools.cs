using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes fingerprint-guarded local import creation only when local writes are allowed.
/// </summary>
internal sealed class DeckInterchangeWriteTools
{
    /// <summary>
    /// Stores the bounded manual interchange application service.
    /// </summary>
    private readonly DeckInterchangeService service;

    /// <summary>
    /// Stores effective process authority for invocation-time enforcement.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates the mutation tool around one service and validated operation mode.
    /// </summary>
    internal DeckInterchangeWriteTools(DeckInterchangeService service, OperationMode mode)
    {
        this.service = service;
        this.mode = mode;
    }

    /// <summary>
    /// Re-parses and atomically creates one local deck only when the preview fingerprint matches.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_import_create",
        Title = "Create Local Deck From Manual Import",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: creates one local deck from an accepted import preview without provider calls. The " +
        "result is a deck summary (deck ID, revision, counts, entry IDs) with the accepted diagnostics; pass " +
        "returnDeck true to also get the whole deck.")]
    internal async Task<OperationResult<DeckImportCreateView>> CreateAsync(
        [Description("Exact format ID used for the accepted preview."), InterchangeFormatValues]
        string formatId,
        [Description("Exact content used for the accepted preview.")] string content,
        [Description("Fingerprint returned by local_deck_import_preview.")] string expectedFingerprint,
        [Description("The same parsing defaults and explicit opt-ins used for preview.")]
        DeckImportOptions? options = null,
        [Description("Set true to also return the whole deck after the write. The default summary is much smaller.")]
        bool returnDeck = false,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return new OperationUnsupported(
                "operation-mode-denied",
                "The effective operation mode does not permit local writes.");
        }

        OperationResult<DeckImportCreateResult> created = await service
            .CreateAsync(formatId, content, expectedFingerprint, options, cancellationToken).ConfigureAwait(false);
        return created is OperationSuccess<DeckImportCreateResult> success
            ? new OperationSuccess<DeckImportCreateView>(DeckImportCreateView.From(success.Data, returnDeck))
            : OperationFailure.Forward<DeckImportCreateResult, DeckImportCreateView>(created);
    }
}
