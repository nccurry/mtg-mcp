using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Returns one page of an identity reconciliation preview. The rows are paged; the fingerprint and apply token cover
/// the whole preview, whichever page was read, so <c>local_deck_identity_reconcile_apply</c> needs no page.
/// </summary>
internal sealed record DeckIdentityReconciliationPreviewView(
    Guid DeckId,
    long DeckRevision,
    int RowCount,
    int Offset,
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
    /// Gets the <c>offset</c> that returns the next page of rows, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }
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
    string? Fingerprint,
    int EntryCount,
    int Offset,
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
    /// Gets the <c>offset</c> that returns the next page of entries, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }
}

/// <summary>
/// Returns one page of a category rules preview. The decisions are paged; the fingerprint and apply token cover the
/// whole preview, whichever page was read, so <c>local_deck_category_rules_apply</c> needs no page.
/// </summary>
internal sealed record DeckCategoryRulesPreviewView(
    Guid DeckId,
    long DeckRevision,
    CategoryRuleSource Source,
    CategoryRuleSet ExpandedRules,
    int DecisionCount,
    int Offset,
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
    /// Gets the <c>offset</c> that returns the next page of decisions, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }
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
    /// Builds the page of an identity reconciliation preview that starts at <paramref name="offset"/>; every page has
    /// the fingerprint and apply token of the whole preview.
    /// </summary>
    internal static OperationResult<DeckIdentityReconciliationPreviewView> PageIdentity(
        DeckIdentityReconciliationPreview preview,
        int offset,
        string? expectedPreviewFingerprint)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (Check<DeckIdentityReconciliationPreviewView>(
            offset,
            preview.Rows.Count,
            "rows",
            expectedPreviewFingerprint,
            preview.PreviewFingerprint) is { } refused)
        {
            return refused;
        }

        DeckIdentityReconciliationPreviewView header = new(
            preview.DeckId,
            preview.DeckRevision,
            preview.Rows.Count,
            offset,
            [],
            preview.IsComplete,
            preview.ProposedChangeCount,
            preview.Evidence,
            preview.PreviewFingerprint,
            preview.ApplyToken);
        ResultPageSlice slice = ResultPage.Take(preview.Rows, offset, ResultSize.Measure(header));
        return new OperationSuccess<DeckIdentityReconciliationPreviewView>(header with
        {
            Rows = slice.Rows(preview.Rows),
            Truncated = slice.More ? true : null,
            NextOffset = slice.NextOffset,
            Narrow = slice.More
                ? Narrow("Rows", offset, slice, "local_deck_identity_reconcile_preview", "previewFingerprint")
                : null,
        });
    }

    /// <summary>
    /// Builds the page of a manual import preview that starts at <paramref name="offset"/>; the first page also has
    /// the diagnostics, identities, categories, and bindings, and every page has the fingerprint of the whole import.
    /// </summary>
    internal static OperationResult<DeckImportPreviewView> PageImport(
        DeckImportPreview preview,
        int offset,
        string? expectedPreviewFingerprint)
    {
        ArgumentNullException.ThrowIfNull(preview);
        IReadOnlyList<DeckEntry> entries = preview.Proposal?.Entries ?? [];
        if (Check<DeckImportPreviewView>(
            offset,
            entries.Count,
            "entries",
            expectedPreviewFingerprint,
            preview.Fingerprint) is { } refused)
        {
            return refused;
        }

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
            preview.Fingerprint,
            entries.Count,
            offset,
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
        ResultPageSlice slice = ResultPage.Take(rows, offset, ResultSize.Measure(header));
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
            NextOffset = slice.NextOffset,
            Narrow = slice.More
                ? Narrow("Entries", offset, slice, "local_deck_import_preview", "fingerprint")
                : null,
        });
    }

    /// <summary>
    /// Builds the page of a category rules preview that starts at <paramref name="offset"/>; every page has the
    /// fingerprint and apply token of the whole preview.
    /// </summary>
    internal static OperationResult<DeckCategoryRulesPreviewView> PageCategoryRules(
        DeckCategoryRulesPreview preview,
        int offset,
        string? expectedPreviewFingerprint)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (Check<DeckCategoryRulesPreviewView>(
            offset,
            preview.Decisions.Count,
            "decisions",
            expectedPreviewFingerprint,
            preview.PreviewFingerprint) is { } refused)
        {
            return refused;
        }

        DeckCategoryRulesPreviewView header = new(
            preview.DeckId,
            preview.DeckRevision,
            preview.Source,
            preview.ExpandedRules,
            preview.Decisions.Count,
            offset,
            [],
            preview.IsComplete,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            preview.CorpusGenerationId);
        ResultPageSlice slice = ResultPage.Take(preview.Decisions, offset, ResultSize.Measure(header));
        return new OperationSuccess<DeckCategoryRulesPreviewView>(header with
        {
            Decisions = slice.Rows(preview.Decisions),
            Truncated = slice.More ? true : null,
            NextOffset = slice.NextOffset,
            Narrow = slice.More
                ? Narrow("Decisions", offset, slice, "local_deck_category_rules_preview", "previewFingerprint")
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
    /// Refuses an offset outside the rows, or a later page of a preview that is not the one the earlier pages came
    /// from.
    /// </summary>
    private static OperationResult<T>? Check<T>(
        int offset,
        int count,
        string list,
        string? expectedPreviewFingerprint,
        string? previewFingerprint)
    {
        return ResultPage.CheckPage<T>(
            expectedPreviewFingerprint,
            previewFingerprint,
            new PageList("offset", offset, count, list));
    }

    /// <summary>
    /// Names the rows shown, how to read the rest, and that every page must come from the same preview.
    /// </summary>
    private static string Narrow(string what, int offset, ResultPageSlice slice, string tool, string fingerprint)
    {
        return $"{what} {offset} to {slice.Next - 1} of {slice.Total} are shown to stay under the size limit; the " +
            $"fingerprint covers the whole preview. Call {tool} again with offset {slice.Next} and " +
            $"expectedPreviewFingerprint set to the {fingerprint} of this page, so every page comes from the same " +
            "preview.";
    }
}
