using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Returns one page of an identity reconciliation preview. The fingerprint and apply token describe the whole
/// preview, whichever page was read, so <c>local_deck_identity_reconcile_apply</c> needs no page.
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
    /// Gets what to pass to read the rows that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>offset</c> that returns the next page of rows, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }
}

/// <summary>
/// Returns one page of a manual import preview. The proposal's entries are paged; the categories, assignments, bindings,
/// diagnostics, and unresolved identities come with the first page only. The fingerprint describes the whole preview.
/// </summary>
internal sealed record DeckImportPreviewView(
    string FormatId,
    string Completeness,
    string? Fingerprint,
    int EntryCount,
    int Offset,
    DeckImportProposal? Proposal,
    IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics,
    int OmittedDiagnosticCount,
    IReadOnlyList<string> UnresolvedIdentities)
{
    /// <summary>
    /// Gets true when entries were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the entries that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>offset</c> that returns the next page of entries, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }
}

/// <summary>
/// Returns one page of a category rules preview. The fingerprint and apply token describe the whole preview, whichever
/// page was read, so <c>local_deck_category_rules_apply</c> needs no page.
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
    /// Gets what to pass to read the decisions that were left out.
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
    /// Builds one page of an identity reconciliation preview.
    /// </summary>
    internal static OperationResult<DeckIdentityReconciliationPreviewView> PageIdentity(
        DeckIdentityReconciliationPreview preview,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(preview);
        OperationInvalidInput? invalid = CheckOffset(offset, preview.Rows.Count);
        if (invalid is not null)
        {
            return invalid;
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
        (int kept, int next, bool more) = Take(preview.Rows, offset, header);
        return new OperationSuccess<DeckIdentityReconciliationPreviewView>(header with
        {
            Rows = [.. preview.Rows.Skip(offset).Take(kept)],
            Truncated = more ? true : null,
            NextOffset = more ? next : null,
            Narrow = more ? Narrow("Rows", offset, next, preview.Rows.Count, "local_deck_identity_reconcile_preview") : null,
        });
    }

    /// <summary>
    /// Builds one page of a manual import preview.
    /// </summary>
    internal static OperationResult<DeckImportPreviewView> PageImport(DeckImportPreview preview, int offset)
    {
        ArgumentNullException.ThrowIfNull(preview);
        IReadOnlyList<DeckEntry> entries = preview.Proposal?.Entries ?? [];
        OperationInvalidInput? invalid = CheckOffset(offset, entries.Count);
        if (invalid is not null)
        {
            return invalid;
        }

        bool first = offset == 0;
        DeckImportProposal? shell = preview.Proposal is null
            ? null
            : first
                ? preview.Proposal with { Entries = [] }
                : preview.Proposal with
                {
                    Entries = [],
                    Categories = [],
                    CategoryAssignments = [],
                    ProviderBindings = [],
                    SyncBaselines = [],
                };
        DeckImportPreviewView header = new(
            preview.FormatId,
            preview.Completeness,
            preview.Fingerprint,
            entries.Count,
            offset,
            shell,
            first ? preview.Diagnostics : [],
            first ? preview.OmittedDiagnosticCount : 0,
            first ? preview.UnresolvedIdentities : []);
        (int kept, int next, bool more) = Take(entries, offset, header);
        return new OperationSuccess<DeckImportPreviewView>(header with
        {
            Proposal = shell is null ? null : shell with { Entries = [.. entries.Skip(offset).Take(kept)] },
            Truncated = more ? true : null,
            NextOffset = more ? next : null,
            Narrow = more ? Narrow("Entries", offset, next, entries.Count, "local_deck_import_preview") : null,
        });
    }

    /// <summary>
    /// Builds one page of a category rules preview.
    /// </summary>
    internal static OperationResult<DeckCategoryRulesPreviewView> PageCategoryRules(
        DeckCategoryRulesPreview preview,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(preview);
        OperationInvalidInput? invalid = CheckOffset(offset, preview.Decisions.Count);
        if (invalid is not null)
        {
            return invalid;
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
        (int kept, int next, bool more) = Take(preview.Decisions, offset, header);
        return new OperationSuccess<DeckCategoryRulesPreviewView>(header with
        {
            Decisions = [.. preview.Decisions.Skip(offset).Take(kept)],
            Truncated = more ? true : null,
            NextOffset = more ? next : null,
            Narrow = more
                ? Narrow("Decisions", offset, next, preview.Decisions.Count, "local_deck_category_rules_preview")
                : null,
        });
    }

    /// <summary>
    /// Counts the rows from <paramref name="offset"/> that fit beside the header, and where the next page starts.
    /// </summary>
    private static (int Kept, int Next, bool More) Take<T>(IReadOnlyList<T> rows, int offset, object header)
    {
        List<int> sizes = [.. rows.Skip(offset).Select(value => ResultSize.Measure(value) + 1)];
        int kept = ResultSizeLimit.CountFitting(sizes, ResultSize.Measure(header));
        int next = offset + kept;
        return (kept, next, next < rows.Count);
    }

    /// <summary>
    /// Names the rows shown and how to read the rest.
    /// </summary>
    private static string Narrow(string what, int offset, int next, int count, string tool)
    {
        return $"{what} {offset} to {next - 1} of {count} are shown to stay under the size limit; the fingerprint " +
            $"and apply token cover the whole preview. Call {tool} again with offset {next} for the rest.";
    }

    /// <summary>
    /// Refuses an offset outside the rows it pages.
    /// </summary>
    private static OperationInvalidInput? CheckOffset(int offset, int count)
    {
        return offset < 0 || (offset > 0 && offset >= count)
            ? new OperationInvalidInput(
                "invalid-offset",
                $"offset must be from 0 to {Math.Max(0, count - 1)} for this preview, which has {count} rows.",
                "offset")
            : null;
    }
}
