using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
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
        Name = "deck_import_create",
        Title = "Create Local Deck From Manual Import",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates one local deck from an accepted import preview without provider calls.")]
    internal Task<OperationResult<DeckImportCreateResult>> CreateAsync(
        [Description("Exact format ID used for the accepted preview.")] string formatId,
        [Description("Exact content used for the accepted preview.")] string content,
        [Description("Fingerprint returned by deck_import_preview.")] string expectedFingerprint,
        [Description("The same parsing defaults and explicit opt-ins used for preview.")]
        DeckImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<DeckImportCreateResult>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit local writes."));
        }

        return service.CreateAsync(formatId, content, expectedFingerprint, options, cancellationToken);
    }
}
