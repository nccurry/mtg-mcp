using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Cuts Archidekt deck and preview results to the shared size limit, so no result reaches the size at which Claude
/// Code saves a tool result to a file and shows the agent only its path.
/// </summary>
internal static class ArchidektResultPaging
{
    /// <summary>
    /// Sets the most bytes the unknown provider fields of a deck may take before they are left out.
    /// </summary>
    private const int MaximumExtensionBytes = 8_000;

    /// <summary>
    /// Builds one page of a deck: a summary, then the entries from <paramref name="offset"/> that fit. Entries are
    /// ordered by card name, then relation ID, not by Archidekt's order numbers, so the pages of separate reads line up
    /// even though Archidekt renumbers its order numbers without an edit.
    /// </summary>
    internal static OperationResult<ArchidektDeckView> PageDeck(RemoteDeckSnapshot deck, int offset)
    {
        ArgumentNullException.ThrowIfNull(deck);
        if (offset < 0 || (offset > 0 && offset >= deck.Entries.Count))
        {
            return new OperationInvalidInput(
                "invalid-offset",
                $"offset must be from 0 to {Math.Max(0, deck.Entries.Count - 1)} for this deck, which has " +
                $"{deck.Entries.Count} entries.",
                "offset");
        }

        Dictionary<string, int> cardsByZone = new(StringComparer.Ordinal);
        foreach (RemoteDeckEntry entry in deck.Entries)
        {
            cardsByZone[entry.Zone] = cardsByZone.GetValueOrDefault(entry.Zone) + entry.Quantity;
        }

        bool first = offset == 0;
        bool omitExtensions = first && deck.Extensions.Count > 0 && ResultSize.Measure(deck.Extensions) > MaximumExtensionBytes;
        ArchidektDeckView header = new(
            new ArchidektDeckSummary(
                deck.Entries.Count,
                deck.Entries.Sum(value => value.Quantity),
                deck.Categories.Count,
                cardsByZone),
            deck.RemoteId,
            deck.RemoteUri,
            deck.Name,
            deck.Description,
            deck.Format,
            deck.Visibility,
            deck.ParentFolderId,
            deck.ContentFingerprint,
            deck.RemoteFingerprint,
            deck.Evidence,
            offset,
            first ? deck.Categories : [],
            [],
            first && !omitExtensions ? deck.Extensions : new Dictionary<string, System.Text.Json.JsonElement>())
        {
            ExtensionsOmitted = omitExtensions ? true : null,
        };
        List<RemoteDeckEntry> ordered = [.. deck.Entries];
        ordered.Sort(static (left, right) =>
        {
            int byName = StringComparer.OrdinalIgnoreCase.Compare(left.CardName, right.CardName);
            return byName != 0
                ? byName
                : StringComparer.Ordinal.Compare(left.ProviderRelationId, right.ProviderRelationId);
        });
        List<int> sizes = [.. ordered.Skip(offset).Select(value => ResultSize.Measure(value) + 1)];
        int keep = ResultSizeLimit.CountFitting(sizes, ResultSize.Measure(header));
        int next = offset + keep;
        bool more = next < deck.Entries.Count;
        return new OperationSuccess<ArchidektDeckView>(header with
        {
            Entries = [.. ordered.Skip(offset).Take(keep)],
            Truncated = more ? true : null,
            NextOffset = more ? next : null,
            Narrow = more
                ? $"Entries {offset} to {next - 1} of {deck.Entries.Count} are shown to stay under the size limit. " +
                    $"Call archidekt_deck_get again with offset {next} for the rest."
                : null,
        });
    }

    /// <summary>
    /// Builds one page of a preview: a summary, then the operations and the differences that fit, starting at the
    /// given offsets. Operations come first, because they are what the apply tool would run.
    /// </summary>
    internal static OperationResult<ArchidektSyncPreviewView> PagePreview(
        ArchidektSyncPreview preview,
        int operationOffset,
        int differenceOffset,
        string? applyNotice)
    {
        ArgumentNullException.ThrowIfNull(preview);
        OperationInvalidInput? invalid = CheckOffset("operationOffset", operationOffset, preview.Operations.Count) ??
            CheckOffset("differenceOffset", differenceOffset, preview.Differences.Count);
        if (invalid is not null)
        {
            return invalid;
        }

        ArchidektSyncPreviewView header = new(
            preview.Direction,
            Summarize(preview),
            applyNotice is null,
            applyNotice,
            preview.LocalDeckId,
            preview.LocalRevision,
            preview.RemoteDeckId,
            preview.RemoteFingerprint,
            preview.ContentFingerprint,
            preview.PreviewFingerprint,
            preview.HasConflicts,
            preview.PredictedProviderRequests,
            operationOffset,
            differenceOffset,
            [],
            []);
        int budget = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - ResultSize.Measure(header);
        List<int> operationSizes = [.. preview.Operations.Skip(operationOffset).Select(value => ResultSize.Measure(value) + 1)];
        int keptOperations = ResultSizeLimit.CountFitting(operationSizes, ResultSize.Measure(header));
        int used = operationSizes.Take(keptOperations).Sum();
        List<int> differenceSizes = [.. preview.Differences.Skip(differenceOffset).Select(value => ResultSize.Measure(value) + 1)];
        int keptDifferences = 0;
        while (keptDifferences < differenceSizes.Count && used + differenceSizes[keptDifferences] <= budget)
        {
            used += differenceSizes[keptDifferences];
            keptDifferences++;
        }

        // A page with no operation to show must still make progress through the differences.
        if (keptOperations == 0 && keptDifferences == 0 && differenceSizes.Count > 0)
        {
            keptDifferences = 1;
        }

        int nextOperation = operationOffset + keptOperations;
        int nextDifference = differenceOffset + keptDifferences;
        bool moreOperations = nextOperation < preview.Operations.Count;
        bool moreDifferences = nextDifference < preview.Differences.Count;
        bool more = moreOperations || moreDifferences;
        return new OperationSuccess<ArchidektSyncPreviewView>(header with
        {
            Operations = [.. preview.Operations.Skip(operationOffset).Take(keptOperations)],
            Differences = [.. preview.Differences.Skip(differenceOffset).Take(keptDifferences)],
            Truncated = more ? true : null,
            NextOperationOffset = moreOperations ? nextOperation : null,
            NextDifferenceOffset = moreDifferences ? nextDifference : null,
            Narrow = more
                ? "Rows were left out to stay under the size limit; the summary and every guard cover the whole " +
                    $"preview. Call again with operationOffset {nextOperation} and differenceOffset {nextDifference} " +
                    "for the rest."
                : null,
        });
    }

    /// <summary>
    /// Explains why the tool that applies a push is not available, or returns null when it is.
    /// </summary>
    internal static string? PushApplyNotice(OperationMode mode, ArchidektAuthStatus auth)
    {
        string? reasons = Combine(
            OperationModeGuard.Allows(mode, OperationRequirement.RemoteWrite)
                ? null
                : $"the server runs in {OperationModeParser.Format(mode)} mode, and a push needs remote mode, which is " +
                    $"the default (--mode=remote); remove the --mode={OperationModeParser.Format(mode)} override or " +
                    "the MTGMCP__MODE setting and restart the server",
            CredentialReason(auth));
        return reasons is null ? null : $"archidekt_push_apply is not available: {reasons}.";
    }

    /// <summary>
    /// Explains why the tool that applies a pull is not available, or returns null when it is.
    /// </summary>
    internal static string? PullApplyNotice(OperationMode mode, ArchidektAuthStatus auth)
    {
        string? reasons = Combine(
            OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite)
                ? null
                : "the server runs in read-only mode, and a pull needs local or remote mode; remote is the default " +
                    "(--mode=remote), so remove the --mode=read-only override or the MTGMCP__MODE setting and " +
                    "restart the server",
            CredentialReason(auth));
        return reasons is null ? null : $"archidekt_pull_apply is not available: {reasons}.";
    }

    /// <summary>
    /// Counts the operations and differences of a preview, and the differences that make it conflict.
    /// </summary>
    private static ArchidektPreviewSummary Summarize(ArchidektSyncPreview preview)
    {
        SortedDictionary<string, int> byKind = new(StringComparer.Ordinal);
        foreach (ArchidektRemoteOperation operation in preview.Operations)
        {
            byKind[operation.Kind] = byKind.GetValueOrDefault(operation.Kind) + 1;
        }

        SortedDictionary<string, int> byState = new(StringComparer.Ordinal);
        int conflicts = 0;
        string conflictSide = preview.Direction == "push" ? "remote-" : "local-";
        foreach (ArchidektDifference difference in preview.Differences)
        {
            byState[difference.State] = byState.GetValueOrDefault(difference.State) + 1;
            if (preview.HasConflicts &&
                (difference.State.StartsWith(conflictSide, StringComparison.Ordinal) ||
                    difference.State == "concurrent-changed"))
            {
                conflicts++;
            }
        }

        return new ArchidektPreviewSummary(
            preview.Operations.Count,
            byKind,
            preview.Differences.Count,
            byState,
            conflicts);
    }

    /// <summary>
    /// Refuses an offset outside the list it pages.
    /// </summary>
    private static OperationInvalidInput? CheckOffset(string name, int offset, int count)
    {
        return offset < 0 || offset > count
            ? new OperationInvalidInput(
                "invalid-offset",
                $"{name} must be from 0 to {count} for this preview.",
                name)
            : null;
    }

    /// <summary>
    /// Names the missing credentials as a reason, or returns null when they are configured.
    /// </summary>
    private static string? CredentialReason(ArchidektAuthStatus auth)
    {
        return auth.CredentialsConfigured
            ? null
            : "Archidekt credentials are not configured (see archidekt_auth_status)";
    }

    /// <summary>
    /// Joins the reasons that apply with "and", or returns null when none does.
    /// </summary>
    private static string? Combine(string? first, string? second)
    {
        return (first, second) switch
        {
            (null, null) => null,
            (null, _) => second,
            (_, null) => first,
            _ => $"{first}; and {second}",
        };
    }
}
