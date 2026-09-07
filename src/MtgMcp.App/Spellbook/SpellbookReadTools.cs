using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Exposes bounded Commander Spellbook evidence without making deckbuilding decisions.
/// </summary>
internal sealed class SpellbookReadTools
{
    /// <summary>
    /// Acquires unchanged source evidence through the adapter's cache and pacing boundary.
    /// </summary>
    private readonly SpellbookService service;

    /// <summary>
    /// Resolves revision-guarded local decks before a source deck request.
    /// </summary>
    private readonly SpellbookDeckInputResolver resolver;

    /// <summary>
    /// Creates the read-only surface around one source service and local deck resolver.
    /// </summary>
    internal SpellbookReadTools(SpellbookService service, SpellbookDeckInputResolver resolver)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// Returns one bounded source page for an exact Commander Spellbook search query.
    /// </summary>
    [McpServerTool(Name = "spellbook_variant_search", Title = "Search Commander Spellbook Variants", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns a bounded Commander Spellbook variant page for an exact source query. It does not rank results or recommend cards.")]
    internal Task<OperationResult<SpellbookEvidence>> SearchVariantsAsync(
        [Description("Exact Commander Spellbook query sent unchanged except for URL escaping.")] string sourceQuery,
        [Description("Source page size from 1 through 25; omitted uses 20.")] int? limit = null,
        [Description("Source page offset from 0 through 1000; omitted uses 0.")] int? offset = null,
        [Description("Whether the source groups variants by combo; omitted uses true.")] bool? groupByCombo = null,
        CancellationToken cancellationToken = default)
    {
        return service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(
                sourceQuery,
                new SpellbookPageOptions(limit, offset, groupByCombo)),
            cancellationToken);
    }

    /// <summary>
    /// Returns one exact source variant without local interpretation.
    /// </summary>
    [McpServerTool(Name = "spellbook_variant_get", Title = "Get Commander Spellbook Variant", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns the exact Commander Spellbook JSON object for one variant ID without interpreting it.")]
    internal Task<OperationResult<SpellbookEvidence>> GetVariantAsync(
        [Description("Exact Commander Spellbook variant identifier.")] string variantId,
        CancellationToken cancellationToken = default)
    {
        return service.GetVariantAsync(variantId, cancellationToken);
    }

    /// <summary>
    /// Sends only supported entries from one exact local deck revision to Commander Spellbook.
    /// </summary>
    [McpServerTool(Name = "spellbook_deck_combos_find", Title = "Find Commander Spellbook Deck Combos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Sends only commander and main entries from one exact local deck revision to Commander Spellbook and reports skipped zones. It does not decide whether a result fits the deck.")]
    internal async Task<OperationResult<SpellbookDeckComboEvidence>> FindDeckCombosAsync(
        [Description("Local deck identifier.")] Guid deckId,
        [Description("Exact local deck revision to read before the source request.")] long expectedRevision,
        [Description("Optional exact Commander Spellbook query sent unchanged except for URL escaping.")] string? sourceQuery = null,
        [Description("Source page size from 1 through 25; omitted uses 20.")] int? limit = null,
        [Description("Source page offset from 0 through 1000; omitted uses 0.")] int? offset = null,
        [Description("Whether the source groups variants by combo; omitted uses true.")] bool? groupByCombo = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult<SpellbookDeckComboSelection> selection = await resolver.ResolveAsync(
            deckId,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
        return selection switch
        {
            OperationSuccess<SpellbookDeckComboSelection> success => await FindDeckCombosAsync(
                success.Data,
                sourceQuery,
                limit,
                offset,
                groupByCombo,
                cancellationToken).ConfigureAwait(false),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Acquires source evidence and keeps source data separate from local deck-selection evidence.
    /// </summary>
    private async Task<OperationResult<SpellbookDeckComboEvidence>> FindDeckCombosAsync(
        SpellbookDeckComboSelection selection,
        string? sourceQuery,
        int? limit,
        int? offset,
        bool? groupByCombo,
        CancellationToken cancellationToken)
    {
        OperationResult<SpellbookEvidence> source = await service.FindDeckCombosAsync(
            new SpellbookDeckComboRequest(
                selection.Request,
                sourceQuery,
                new SpellbookPageOptions(limit, offset, groupByCombo)),
            cancellationToken).ConfigureAwait(false);
        return source switch
        {
            OperationSuccess<SpellbookEvidence> success => new OperationSuccess<SpellbookDeckComboEvidence>(
                new SpellbookDeckComboEvidence(selection.Evidence, success.Data)),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}
