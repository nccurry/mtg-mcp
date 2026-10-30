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
    internal static OperationResult<ArchidektDeckView> PageDeck(
        RemoteDeckSnapshot deck,
        int offset,
        string callAgain = "Call archidekt_deck_get again")
    {
        ArgumentNullException.ThrowIfNull(deck);
        OperationInvalidInput? invalid = ResultPage.CheckOffset("offset", offset, deck.Entries.Count, "entries");
        if (invalid is not null)
        {
            return invalid;
        }

        Dictionary<string, int> cardsByZone = new(StringComparer.Ordinal);
        foreach (RemoteDeckEntry entry in deck.Entries)
        {
            cardsByZone[entry.Zone] = cardsByZone.GetValueOrDefault(entry.Zone) + entry.Quantity;
        }

        bool first = offset == 0;
        bool omitExtensions = first &&
            deck.Extensions.Count > 0 &&
            ResultSize.Measure(deck.Extensions) > MaximumExtensionBytes;
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
            first && !omitExtensions
                ? deck.Extensions
                : new Dictionary<string, System.Text.Json.JsonElement>())
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
        ResultPageSlice slice = ResultPage.Take(ordered, offset, ResultSize.Measure(header));
        return new OperationSuccess<ArchidektDeckView>(header with
        {
            Entries = slice.Rows(ordered),
            Truncated = slice.More ? true : null,
            NextOffset = slice.NextOffset,
            Narrow = slice.More
                ? $"Entries {offset} to {slice.Next - 1} of {deck.Entries.Count} are shown to stay under the size " +
                    $"limit. {callAgain} with offset {slice.Next} for the rest."
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
        string? applyNotice,
        string? expectedPreviewFingerprint)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (CheckPages<ArchidektSyncPreviewView>(
            preview.PreviewFingerprint,
            expectedPreviewFingerprint,
            operationOffset,
            preview.Operations.Count,
            differenceOffset,
            preview.Differences.Count) is { } refused)
        {
            return refused;
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
        (ResultPageSlice operations, ResultPageSlice differences) = ResultPage.TakeTwo(
            preview.Operations,
            operationOffset,
            preview.Differences,
            differenceOffset,
            ResultSize.Measure(header));
        bool more = operations.More || differences.More;
        return new OperationSuccess<ArchidektSyncPreviewView>(header with
        {
            Operations = operations.Rows(preview.Operations),
            Differences = differences.Rows(preview.Differences),
            Truncated = more ? true : null,
            NextOperationOffset = operations.NextOffset,
            NextDifferenceOffset = differences.NextOffset,
            Narrow = more ? TwoListNarrow("the summary and every guard", operations, differences) : null,
        });
    }

    /// <summary>
    /// Builds one page of a snapshot: its metadata, then its saved deck in the same pages as a deck read.
    /// </summary>
    internal static OperationResult<ArchidektSnapshotView> PageSnapshot(RemoteNamedSnapshot snapshot, int offset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        OperationResult<ArchidektDeckView> deck = PageDeck(
            snapshot.Deck,
            offset,
            "Call archidekt_snapshots_get again with the same kind, deckId, and snapshotId");
        return deck is OperationSuccess<ArchidektDeckView> page
            ? new OperationSuccess<ArchidektSnapshotView>(
                new ArchidektSnapshotView(snapshot.Summary, snapshot.Evidence, page.Data))
            : OperationFailure.Forward<ArchidektDeckView, ArchidektSnapshotView>(deck);
    }

    /// <summary>
    /// Builds one page of a folder tree: the folder count, then the folder records from <paramref name="offset"/> that
    /// fit.
    /// </summary>
    internal static OperationResult<ArchidektFolderTreeView> PageFolders(RemoteFolderTree tree, int offset)
    {
        ArgumentNullException.ThrowIfNull(tree);
        OperationInvalidInput? invalid = ResultPage.CheckOffset("offset", offset, tree.Items.Count, "folders");
        if (invalid is not null)
        {
            return invalid;
        }

        ArchidektFolderTreeView header = new(tree.Items.Count, offset, [], tree.Evidence, tree.TreeFingerprint);
        ResultPageSlice slice = ResultPage.Take(tree.Items, offset, ResultSize.Measure(header));
        return new OperationSuccess<ArchidektFolderTreeView>(header with
        {
            Items = slice.Rows(tree.Items),
            Truncated = slice.More ? true : null,
            NextOffset = slice.NextOffset,
            Narrow = slice.More
                ? $"Folders {offset} to {slice.Next - 1} of {tree.Items.Count} are shown to stay under the size " +
                    $"limit. Call archidekt_folders_get again with offset {slice.Next} for the rest."
                : null,
        });
    }

    /// <summary>
    /// Builds one page of the differences between a local deck, its baseline, and the remote deck.
    /// </summary>
    internal static OperationResult<ArchidektSyncDiffView> PageDiff(ArchidektSyncDiff diff, int differenceOffset)
    {
        ArgumentNullException.ThrowIfNull(diff);
        OperationInvalidInput? invalid = ResultPage.CheckOffset(
            "differenceOffset",
            differenceOffset,
            diff.Differences.Count,
            "differences");
        if (invalid is not null)
        {
            return invalid;
        }

        ArchidektSyncDiffView header = new(
            diff.LocalDeckId,
            diff.LocalRevision,
            diff.RemoteDeckId,
            diff.RemoteFingerprint,
            diff.BaselineRemoteFingerprint,
            diff.HasConflicts,
            diff.Differences.Count,
            differenceOffset,
            []);
        ResultPageSlice slice = ResultPage.Take(diff.Differences, differenceOffset, ResultSize.Measure(header));
        return new OperationSuccess<ArchidektSyncDiffView>(header with
        {
            Differences = slice.Rows(diff.Differences),
            Truncated = slice.More ? true : null,
            NextDifferenceOffset = slice.NextOffset,
            Narrow = slice.More
                ? $"Differences {differenceOffset} to {slice.Next - 1} of {diff.Differences.Count} are shown to " +
                    $"stay under the size limit. Call archidekt_sync_diff again with differenceOffset {slice.Next} " +
                    "for the rest."
                : null,
        });
    }

    /// <summary>
    /// Builds one page of a snapshot restore preview: counts and guards, then the operations and the differences that
    /// fit, starting at the given offsets.
    /// </summary>
    internal static OperationResult<ArchidektSnapshotRestorePreviewView> PageRestorePreview(
        ArchidektSnapshotRestorePreview preview,
        int operationOffset,
        int differenceOffset,
        string? expectedPreviewFingerprint)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (CheckPages<ArchidektSnapshotRestorePreviewView>(
                preview.PreviewFingerprint,
                expectedPreviewFingerprint,
                operationOffset,
                preview.Operations.Count,
                differenceOffset,
                preview.Differences.Count) is { } refused)
        {
            return refused;
        }

        ArchidektSnapshotRestorePreviewView header = new(
            preview.DeckId,
            preview.SnapshotId,
            preview.SnapshotChecksum,
            preview.SnapshotContentFingerprint,
            preview.RemoteFingerprint,
            preview.PreviewFingerprint,
            preview.PredictedProviderRequests,
            preview.Operations.Count,
            preview.Differences.Count,
            operationOffset,
            differenceOffset,
            [],
            []);
        (ResultPageSlice operations, ResultPageSlice differences) = ResultPage.TakeTwo(
            preview.Operations,
            operationOffset,
            preview.Differences,
            differenceOffset,
            ResultSize.Measure(header));
        bool more = operations.More || differences.More;
        return new OperationSuccess<ArchidektSnapshotRestorePreviewView>(header with
        {
            Operations = operations.Rows(preview.Operations),
            Differences = differences.Rows(preview.Differences),
            Truncated = more ? true : null,
            NextOperationOffset = operations.NextOffset,
            NextDifferenceOffset = differences.NextOffset,
            Narrow = more ? TwoListNarrow("the counts and every guard", operations, differences) : null,
        });
    }

    /// <summary>
    /// Refuses a request whose offsets are outside the operations or differences, or whose preview is not the one the
    /// earlier pages came from.
    /// </summary>
    private static OperationResult<T>? CheckPages<T>(
        string previewFingerprint,
        string? expectedPreviewFingerprint,
        int operationOffset,
        int operationCount,
        int differenceOffset,
        int differenceCount)
    {
        OperationInvalidInput? invalid =
            ResultPage.CheckOffset("operationOffset", operationOffset, operationCount, "operations") ??
            ResultPage.CheckOffset("differenceOffset", differenceOffset, differenceCount, "differences");
        if (invalid is not null)
        {
            return invalid;
        }

        OperationConflict? changed = ResultPage.CheckSameResult(
            expectedPreviewFingerprint,
            previewFingerprint,
            operationOffset > 0 || differenceOffset > 0);
        if (changed is not null)
        {
            return changed;
        }

        return null;
    }

    /// <summary>
    /// Says how to read the rows a two-list page left out, and that every page must come from the same preview.
    /// </summary>
    private static string TwoListNarrow(string covers, ResultPageSlice operations, ResultPageSlice differences)
    {
        return $"Rows were left out to stay under the size limit; {covers} cover the whole preview. Call again " +
            $"with operationOffset {operations.Next}, differenceOffset {differences.Next}, and " +
            "expectedPreviewFingerprint set to the previewFingerprint of this page, so every page comes from the " +
            "same preview.";
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
        bool push = preview.Direction == "push";
        foreach (ArchidektDifference difference in preview.Differences)
        {
            byState[difference.State] = byState.GetValueOrDefault(difference.State) + 1;
            if (preview.HasConflicts &&
                (push
                    ? ArchidektDifferenceStates.IsRemoteChange(difference)
                    : ArchidektDifferenceStates.IsLocalChange(difference)))
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
}
