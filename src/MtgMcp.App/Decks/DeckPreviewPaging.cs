using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Returns one page of an identity reconciliation preview. The rows are paged; the fingerprint and apply token cover
/// the whole preview, whichever page was read, so <c>local_deck_identity_reconcile_apply</c> needs no page.
/// </summary>
internal sealed record DeckIdentityReconciliationPreviewView(
    Guid DeckId,
    long Revision,
    int RowCount,
    IReadOnlyList<DeckIdentityReconciliationRow> Rows,
    bool IsComplete,
    int ProposedChangeCount,
    ScryfallCollectionEvidenceBinding? Evidence,
    string PreviewFingerprint,
    string ApplyToken)
{
    /// <summary>
    /// Gets true when rows were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to do to read the rows that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>cursor</c> that returns the next page of rows, or null on the last page.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// Returns the proposed deck of an import preview as one page shows it: the proposed entries and the category
/// assignments of those entries are paged. The categories and provider bindings come with the first page only, and
/// the saved synchronization snapshots are not returned, only counted, because creating the deck reads them again from
/// the content.
/// </summary>
internal sealed record DeckImportProposalView(
    Guid? SourceDeckId,
    long? SourceRevision,
    DateTimeOffset? SourceCreatedAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    string Name,
    string Description,
    string Format,
    IReadOnlyList<DeckEntry> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<DeckCategoryAssignment> CategoryAssignments,
    IReadOnlyList<DeckProviderBinding> ProviderBindings,
    int SyncBaselineCount)
{
    /// <summary>
    /// Gets true when the name was cut to the length every page may repeat.
    /// </summary>
    public bool? NameTruncated { get; init; }

    /// <summary>
    /// Gets true when <c>description</c> is not the whole description: it is cut on the first page and empty on the
    /// pages after it.
    /// </summary>
    public bool? DescriptionTruncated { get; init; }

    /// <summary>
    /// Gets the length of the whole description in UTF-16 code units when <see cref="DescriptionTruncated"/> is true.
    /// </summary>
    public int? DescriptionLength { get; init; }
}

/// <summary>
/// Returns one page of a manual import preview. The first page carries the diagnostics, unresolved identities,
/// categories, and provider bindings, each cut to a shared allowance with its exact count beside it; later pages carry
/// only entries. The fingerprint covers the whole import, whichever page was read.
/// </summary>
internal sealed record DeckImportPreviewView(
    string FormatId,
    string Completeness,
    string? PreviewFingerprint,
    int EntryCount,
    DeckImportProposalView? Proposal,
    IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics,
    int OmittedDiagnosticCount,
    IReadOnlyList<string> UnresolvedIdentities)
{
    /// <summary>
    /// Gets the exact number of diagnostics before the first page cut them, not counting the ones the parser omitted.
    /// </summary>
    public int? DiagnosticCount { get; init; }

    /// <summary>
    /// Gets the exact number of unresolved identities before the first page cut them.
    /// </summary>
    public int? UnresolvedIdentityCount { get; init; }

    /// <summary>
    /// Gets the exact number of proposed categories before the first page cut them.
    /// </summary>
    public int? CategoryCount { get; init; }

    /// <summary>
    /// Gets the exact number of proposed provider bindings before the first page cut them.
    /// </summary>
    public int? ProviderBindingCount { get; init; }

    /// <summary>
    /// Gets true when the first page cut a list of diagnostics, identities, categories, or bindings.
    /// </summary>
    public bool? HeaderListsCut { get; init; }

    /// <summary>
    /// Gets true when entries were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to do to read the entries that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>cursor</c> that returns the next page of entries, or null on the last page.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// Returns one page of a category rules preview. The decisions are paged; the fingerprint and apply token cover the
/// whole preview, whichever page was read, so <c>local_deck_category_rules_apply</c> needs no page.
/// </summary>
internal sealed record DeckCategoryRulesPreviewView(
    Guid DeckId,
    long Revision,
    CategoryRuleSource Source,
    CategoryRuleSet ExpandedRules,
    int DecisionCount,
    IReadOnlyList<CategoryDecision> Decisions,
    bool IsComplete,
    string PreviewFingerprint,
    string ApplyToken,
    Guid? CorpusGenerationId)
{
    /// <summary>
    /// Gets true when decisions were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to do to read the decisions that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>cursor</c> that returns the next page of decisions, or null on the last page.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// Cuts the local deck preview results to the shared size limit. Paging changes what is shown, never the preview that
/// the fingerprint and apply token are bound to.
/// </summary>
internal static class DeckPreviewPaging
{
    /// <summary>
    /// Gives the most bytes the first page of an import preview spends on its diagnostics, unresolved identities,
    /// categories, and provider bindings together, so the entries always have room.
    /// </summary>
    private const int ImportHeaderListBytes = 12_000;

    /// <summary>
    /// Pairs one proposed entry with its category assignments, so the page measures both together.
    /// </summary>
    private sealed record ImportRow(DeckEntry Entry, IReadOnlyList<DeckCategoryAssignment> Assignments);

    /// <summary>
    /// Builds the page of an identity reconciliation preview that <paramref name="cursor"/> names; every page has
    /// the fingerprint and apply token of the whole preview.
    /// </summary>
    internal static OperationResult<DeckIdentityReconciliationPreviewView> PageIdentity(
        DeckIdentityReconciliationPreview preview,
        string? cursor,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ResultPage.CheckPageSize(pageSize, MaximumPageSize) is { } badSize)
        {
            return badSize;
        }

        OperationResult<PageCursorPosition> start = ReadStart(
            cursor,
            preview.PreviewFingerprint,
            preview.DeckId,
            preview.DeckRevision);
        if (start is not OperationSuccess<PageCursorPosition> position)
        {
            return OperationFailure.Forward<PageCursorPosition, DeckIdentityReconciliationPreviewView>(start);
        }

        int offset = position.Data.Offset;

        DeckIdentityReconciliationPreviewView header = new(
            preview.DeckId,
            preview.DeckRevision,
            preview.Rows.Count,
            [],
            preview.IsComplete,
            preview.ProposedChangeCount,
            preview.Evidence,
            preview.PreviewFingerprint,
            preview.ApplyToken);
        ResultPageSlice slice = ResultPage.Take(preview.Rows, offset, ResultSize.Measure(header)).Limit(pageSize);
        return new OperationSuccess<DeckIdentityReconciliationPreviewView>(header with
        {
            Rows = slice.Rows(preview.Rows),
            Truncated = slice.More ? true : null,
            NextCursor = NextCursor(slice, preview.PreviewFingerprint, preview.DeckId, preview.DeckRevision),
            Narrow = slice.More
                ? Narrow("Rows", offset, slice, "local_deck_identity_reconcile_preview")
                : null,
        });
    }

    /// <summary>
    /// Builds the page of a manual import preview that <paramref name="cursor"/> names; the first page also has
    /// the diagnostics, identities, categories, and bindings, and every page has the fingerprint of the whole import.
    /// </summary>
    internal static OperationResult<DeckImportPreviewView> PageImport(
        DeckImportPreview preview,
        string? cursor,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ResultPage.CheckPageSize(pageSize, MaximumPageSize) is { } badSize)
        {
            return badSize;
        }

        IReadOnlyList<DeckEntry> entries = preview.Proposal?.Entries ?? [];
        OperationResult<PageCursorPosition> start = ReadStart(cursor, preview.PreviewFingerprint ?? "none", null, null);
        if (start is not OperationSuccess<PageCursorPosition> position)
        {
            return OperationFailure.Forward<PageCursorPosition, DeckImportPreviewView>(start);
        }

        int offset = position.Data.Offset;

        bool first = offset == 0;
        DeckImportProposal? proposal = preview.Proposal;
        int allowance = ImportHeaderListBytes;

        // Categories come first: a caller needs them to read the category assignments of the later pages.
        IReadOnlyList<DeckCategory> categories = first && proposal is not null
            ? ResultPage.CutList(proposal.Categories, ref allowance)
            : [];
        IReadOnlyList<DeckProviderBinding> bindings = first && proposal is not null
            ? ResultPage.CutList(proposal.ProviderBindings, ref allowance)
            : [];
        IReadOnlyList<string> unresolved = first
            ? ResultPage.CutList(preview.UnresolvedIdentities, ref allowance)
            : [];
        IReadOnlyList<DeckInterchangeDiagnostic> diagnostics = first
            ? ResultPage.CutList(preview.Diagnostics, ref allowance)
            : [];
        bool cut = first &&
            (diagnostics.Count < preview.Diagnostics.Count ||
                unresolved.Count < preview.UnresolvedIdentities.Count ||
                (proposal is not null &&
                    (categories.Count < proposal.Categories.Count ||
                        bindings.Count < proposal.ProviderBindings.Count)));
        ShownText name = ResultText.Name(proposal?.Name ?? string.Empty);
        ShownText description = ResultText.Description(proposal?.Description ?? string.Empty, first);
        DeckImportProposalView? shell = proposal is null
            ? null
            : new DeckImportProposalView(
                proposal.SourceDeckId,
                proposal.SourceRevision,
                proposal.SourceCreatedAtUtc,
                proposal.SourceUpdatedAtUtc,
                name.Text,
                description.Text,
                proposal.Format,
                [],
                categories,
                [],
                bindings,
                proposal.SyncBaselines.Count)
            {
                NameTruncated = name.TruncatedOrNull,
                DescriptionTruncated = description.TruncatedOrNull,
                DescriptionLength = description.LengthIfTruncated,
            };
        DeckImportPreviewView header = new(
            preview.FormatId,
            preview.Completeness,
            preview.PreviewFingerprint,
            entries.Count,
            shell,
            diagnostics,
            first ? preview.OmittedDiagnosticCount : 0,
            unresolved)
        {
            DiagnosticCount = first ? preview.Diagnostics.Count : null,
            UnresolvedIdentityCount = first ? preview.UnresolvedIdentities.Count : null,
            CategoryCount = first && proposal is not null ? proposal.Categories.Count : null,
            ProviderBindingCount = first && proposal is not null ? proposal.ProviderBindings.Count : null,
            HeaderListsCut = cut ? true : null,
        };
        List<ImportRow> rows = ImportRows(proposal, entries);
        ResultPageSlice slice = ResultPage.Take(rows, offset, ResultSize.Measure(header)).Limit(pageSize);
        List<ImportRow> shown = slice.Rows(rows);
        return new OperationSuccess<DeckImportPreviewView>(header with
        {
            Proposal = shell is null
                ? null
                : shell with
                {
                    Entries = [.. shown.Select(row => row.Entry)],
                    CategoryAssignments = [.. shown.SelectMany(row => row.Assignments)],
                },
            Truncated = slice.More ? true : null,
            NextCursor = NextCursor(slice, preview.PreviewFingerprint ?? "none", null, null),
            Narrow = slice.More
                ? Narrow("Entries", offset, slice, "local_deck_import_preview")
                : null,
        });
    }

    /// <summary>
    /// Builds the page of a category rules preview that <paramref name="cursor"/> names; every page has the
    /// fingerprint and apply token of the whole preview.
    /// </summary>
    internal static OperationResult<DeckCategoryRulesPreviewView> PageCategoryRules(
        DeckCategoryRulesPreview preview,
        string? cursor,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ResultPage.CheckPageSize(pageSize, MaximumPageSize) is { } badSize)
        {
            return badSize;
        }

        OperationResult<PageCursorPosition> start = ReadStart(
            cursor,
            preview.PreviewFingerprint,
            preview.DeckId,
            preview.DeckRevision);
        if (start is not OperationSuccess<PageCursorPosition> position)
        {
            return OperationFailure.Forward<PageCursorPosition, DeckCategoryRulesPreviewView>(start);
        }

        int offset = position.Data.Offset;

        DeckCategoryRulesPreviewView header = new(
            preview.DeckId,
            preview.DeckRevision,
            preview.Source,
            preview.ExpandedRules,
            preview.Decisions.Count,
            [],
            preview.IsComplete,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            preview.CorpusGenerationId);
        ResultPageSlice slice = ResultPage.Take(preview.Decisions, offset, ResultSize.Measure(header)).Limit(pageSize);
        return new OperationSuccess<DeckCategoryRulesPreviewView>(header with
        {
            Decisions = slice.Rows(preview.Decisions),
            Truncated = slice.More ? true : null,
            NextCursor = NextCursor(slice, preview.PreviewFingerprint, preview.DeckId, preview.DeckRevision),
            Narrow = slice.More
                ? Narrow("Decisions", offset, slice, "local_deck_category_rules_preview")
                : null,
        });
    }

    /// <summary>
    /// Pairs every proposed entry with the category assignments that name it.
    /// </summary>
    private static List<ImportRow> ImportRows(DeckImportProposal? proposal, IReadOnlyList<DeckEntry> entries)
    {
        Dictionary<Guid, List<DeckCategoryAssignment>> assignments = [];
        foreach (DeckCategoryAssignment assignment in proposal?.CategoryAssignments ?? [])
        {
            if (!assignments.TryGetValue(assignment.EntryId, out List<DeckCategoryAssignment>? list))
            {
                list = [];
                assignments.Add(assignment.EntryId, list);
            }

            list.Add(assignment);
        }

        List<ImportRow> rows = new(entries.Count);
        foreach (DeckEntry entry in entries)
        {
            rows.Add(new ImportRow(entry, assignments.GetValueOrDefault(entry.EntryId) ?? []));
        }

        return rows;
    }


    /// <summary>
    /// Gives the most rows a preview page may hold, and the page size when the caller names none.
    /// </summary>
    internal const int MaximumPageSize = 100;

    /// <summary>
    /// Builds the binding of a preview cursor: the preview fingerprint, and the deck when the preview belongs to one.
    /// </summary>
    private static PageCursorBinding Bind(string previewFingerprint, Guid? deckId, long? revision)
    {
        PageCursorBinding binding = PageCursorBinding.ForPreview(previewFingerprint);
        return deckId is Guid id && revision is long value
            ? binding.And(PageCursorBinding.ForDeck(id, value))
            : binding;
    }

    /// <summary>
    /// Reads the position of a page from its cursor. A cursor of a preview that no longer matches is stale and carries
    /// the preview fingerprint as it is now.
    /// </summary>
    private static OperationResult<PageCursorPosition> ReadStart(
        string? cursor,
        string previewFingerprint,
        Guid? deckId,
        long? revision)
    {
        return PageCursor.Decode(
            cursor,
            Bind(previewFingerprint, deckId, revision),
            current: new ConflictState(revision, PreviewFingerprint: previewFingerprint));
    }

    /// <summary>
    /// Writes the cursor of the page after a slice, or null when the slice reaches the end of the list.
    /// </summary>
    private static string? NextCursor(
        ResultPageSlice slice,
        string previewFingerprint,
        Guid? deckId,
        long? revision)
    {
        return slice.More
            ? PageCursor.Encode(PageCursorPosition.AtOffset(slice.Next), Bind(previewFingerprint, deckId, revision))
            : null;
    }

    /// <summary>
    /// Names the rows shown and how to read the rest.
    /// </summary>
    private static string Narrow(string what, int offset, ResultPageSlice slice, string tool)
    {
        return $"{what} {offset} to {slice.Next - 1} of {slice.Total} are shown to stay under the size limit. Call " +
            $"{tool} again with cursor set to nextCursor and the same arguments; the previewFingerprint covers the " +
            "whole preview.";
    }
}
