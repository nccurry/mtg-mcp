using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Exposes bounded Commander Spellbook evidence without making deckbuilding decisions.
/// </summary>
internal sealed class SpellbookReadTools
{
    /// <summary>
    /// Describes the cursor argument of both paged tools.
    /// </summary>
    private const string CursorHelp =
        "The nextCursor of the previous page; omitted starts at the first page. nextCursor is null on the last page, " +
        "and no cursor goes past source offset 1000.";

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
    [Description(
        "Commander Spellbook: returns one bounded variant page for an exact source query. It does " +
        "not rank results or recommend cards. Pass nextCursor from the result as cursor for the next page; nextCursor " +
        "ends at source offset 1000 and is null on the last page. A page cut to the result size limit says " +
        "truncated true; the cursor then continues after the last row shown." +
        UntrustedTextNotes.Spellbook)]
    internal async Task<OperationResult<SpellbookVariantPage>> SearchVariantsAsync(
        [Description("Exact Commander Spellbook query sent unchanged except for URL escaping.")] string sourceQuery,
        [Description("Source page size from 1 through 25; omitted uses 20.")] int? pageSize = null,
        [Description(CursorHelp)] string? cursor = null,
        [Description("Whether the source groups variants by combo; omitted uses true.")] bool? groupByCombo = null,
        CancellationToken cancellationToken = default)
    {
        PageCursorBinding binding = PageCursorBinding.ForRequest(RequestHash.Of(
            (sourceQuery, typeof(string)),
            (groupByCombo, typeof(bool?))));
        OperationResult<int> offset = SpellbookPaging.Offset(cursor, binding);
        if (offset is not OperationSuccess<int> start)
        {
            return OperationFailure.Forward<int, SpellbookVariantPage>(offset);
        }

        OperationResult<SpellbookEvidence> source = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(
                sourceQuery,
                new SpellbookPageOptions(pageSize, start.Data, groupByCombo)),
            cancellationToken).ConfigureAwait(false);
        if (source is not OperationSuccess<SpellbookEvidence> success)
        {
            return OperationFailure.Forward<SpellbookEvidence, SpellbookVariantPage>(source);
        }

        SpellbookShapedPage page = SpellbookPaging.Shape(success.Data, 0, binding);
        return new OperationSuccess<SpellbookVariantPage>(
            new SpellbookVariantPage(page.Source, page.NextCursor, page.Truncated));
    }

    /// <summary>
    /// Returns one exact source variant without local interpretation.
    /// </summary>
    [McpServerTool(Name = "spellbook_variant_get", Title = "Get Commander Spellbook Variant", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Commander Spellbook: returns the exact JSON object for one variant ID without " +
        "interpreting it." +
        UntrustedTextNotes.Spellbook)]
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
    [Description(
        "Commander Spellbook: sends only commander and main entries from one exact local deck revision to Commander " +
        "Spellbook and reports skipped zones. It does not decide whether a result fits the deck. Pass nextCursor from the result " +
        "as cursor for the next page; nextCursor ends at source offset 1000 and is null on the last page. A page cut " +
        "to the result size limit says truncated true and has no nextCursor; repeat the call with a smaller pageSize." +
        UntrustedTextNotes.Spellbook)]
    internal async Task<OperationResult<SpellbookDeckComboEvidence>> FindDeckCombosAsync(
        [Description("Local deck identifier.")] Guid deckId,
        [Description("Exact local deck revision to read before the source request.")] long expectedRevision,
        [Description("Optional exact Commander Spellbook query sent unchanged except for URL escaping.")] string? sourceQuery = null,
        [Description("Source page size from 1 through 25; omitted uses 20.")] int? pageSize = null,
        [Description(CursorHelp)] string? cursor = null,
        [Description("Whether the source groups variants by combo; omitted uses true.")] bool? groupByCombo = null,
        CancellationToken cancellationToken = default)
    {
        PageCursorBinding binding = PageCursorBinding.ForRequest(RequestHash.Of(
                (sourceQuery, typeof(string)),
                (groupByCombo, typeof(bool?))))
            .And(PageCursorBinding.ForDeck(deckId, expectedRevision));
        OperationResult<int> offset = SpellbookPaging.Offset(cursor, binding);
        if (offset is not OperationSuccess<int> start)
        {
            return OperationFailure.Forward<int, SpellbookDeckComboEvidence>(offset);
        }

        OperationResult<SpellbookDeckComboSelection> selection = await resolver.ResolveAsync(
            deckId,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
        if (selection is not OperationSuccess<SpellbookDeckComboSelection> selected)
        {
            return OperationFailure.Forward<SpellbookDeckComboSelection, SpellbookDeckComboEvidence>(selection);
        }

        OperationResult<SpellbookEvidence> source = await service.FindDeckCombosAsync(
            new SpellbookDeckComboRequest(
                selected.Data.Request,
                sourceQuery,
                new SpellbookPageOptions(pageSize, start.Data, groupByCombo)),
            cancellationToken).ConfigureAwait(false);
        if (source is not OperationSuccess<SpellbookEvidence> success)
        {
            return OperationFailure.Forward<SpellbookEvidence, SpellbookDeckComboEvidence>(source);
        }

        SpellbookShapedPage page = SpellbookPaging.Shape(
            success.Data,
            ResultSize.Measure(selected.Data.Evidence),
            binding);
        return new OperationSuccess<SpellbookDeckComboEvidence>(
            new SpellbookDeckComboEvidence(selected.Data.Evidence, page.Source, page.NextCursor, page.Truncated));
    }
}
