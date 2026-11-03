using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Cuts Archidekt deck and preview results to the page size the caller asked for and to the shared size limit, so no
/// result reaches the size at which Claude Code saves a tool result to a file and shows the agent only its path.
/// </summary>
internal static class ArchidektResultPaging
{
    /// <summary>
    /// Sets the most bytes the unknown provider fields of a deck may take before they are left out.
    /// </summary>
    private const int MaximumExtensionBytes = 8_000;

    /// <summary>
    /// Sets the most UTF-8 bytes of a deck description a page of the deck list shows.
    /// </summary>
    private const int MaximumListedDescriptionBytes = 500;

    /// <summary>
    /// Builds one page of a deck: a summary of the whole deck, then the entries of the chosen zones from the cursor
    /// that fit. Entries are ordered by zone, then card name, then relation ID, not by Archidekt's order numbers, so
    /// the pages of separate reads line up even though Archidekt renumbers its order numbers without an edit. The
    /// position counts the chosen entries, not the whole deck.
    /// </summary>
    /// <param name="deck">The deck as read now.</param>
    /// <param name="cursor">The cursor of the page to read, or null for the first.</param>
    /// <param name="pageSize">The most entries the page shows.</param>
    /// <param name="request">The arguments that must not change between pages.</param>
    /// <param name="sourceBinding">The state of the deck or snapshot the entries come from.</param>
    /// <param name="options">The zones and format of the read.</param>
    /// <param name="callAgain">Says which call reads the next page.</param>
    internal static OperationResult<ArchidektDeckView> PageDeck(
        RemoteDeckSnapshot deck,
        string? cursor,
        int pageSize,
        PageCursorBinding request,
        PageCursorBinding sourceBinding,
        ArchidektDeckReadOptions options,
        string callAgain = "Call archidekt_deck_get again")
    {
        ArgumentNullException.ThrowIfNull(deck);
        List<RemoteDeckEntry> chosen = [.. deck.Entries.Where(value => options.Shows(value.Zone))];
        chosen.Sort(static (left, right) =>
        {
            int byZone = ArchidektDeckReadOptions.ZoneRank(left.Zone)
                .CompareTo(ArchidektDeckReadOptions.ZoneRank(right.Zone));
            if (byZone != 0)
            {
                return byZone;
            }

            int byName = StringComparer.OrdinalIgnoreCase.Compare(left.CardName, right.CardName);
            return byName != 0
                ? byName
                : StringComparer.Ordinal.Compare(left.ProviderRelationId, right.ProviderRelationId);
        });
        PageCursorBinding binding = request.And(sourceBinding);
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            binding,
            current: new ConflictState(RemoteFingerprint: deck.RemoteFingerprint));
        if (decoded is not OperationSuccess<PageCursorPosition> start)
        {
            return OperationFailure.Forward<PageCursorPosition, ArchidektDeckView>(decoded);
        }

        int offset = start.Data.Offset;
        if (offset > chosen.Count)
        {
            return PageCursor.Invalid();
        }

        bool first = offset == 0;
        bool omitExtensions = first &&
            deck.Extensions.Count > 0 &&
            ResultSize.Measure(deck.Extensions) > MaximumExtensionBytes;
        ShownText name = ResultText.Name(deck.Name);
        ShownText description = ResultText.Description(deck.Description, first);
        ArchidektDeckView header = new(
            Summary: ArchidektDeckFacts.Summarize(deck),
            RemoteDeckId: deck.RemoteId,
            RemoteUri: deck.RemoteUri,
            Name: name.Text,
            Description: description.Text,
            Format: deck.Format,
            Visibility: deck.Visibility,
            ParentFolderId: deck.ParentFolderId,
            ContentFingerprint: deck.ContentFingerprint,
            RemoteFingerprint: deck.RemoteFingerprint,
            Evidence: deck.Evidence,
            ShownEntryCount: chosen.Count,
            Categories: first ? deck.Categories : [],
            Entries: [],
            Extensions: first && !omitExtensions
                ? deck.Extensions
                : new Dictionary<string, System.Text.Json.JsonElement>())
        {
            ExtensionsOmitted = omitExtensions ? true : null,
            NameTruncated = name.TruncatedOrNull,
            DescriptionTruncated = description.TruncatedOrNull,
            DescriptionLength = description.LengthIfTruncated,
        };
        List<ArchidektDeckEntryView> ordered =
        [
            .. chosen.Select(value => options.Standard
                ? ArchidektStandardDeckEntryView.StandardFrom(value, deck.IsInDeck(value))
                : ArchidektDeckEntryView.From(value, deck.IsInDeck(value))),
        ];
        ResultPageSlice slice = ResultPage.Take(ordered, offset, ResultSize.Measure(header)).Limit(pageSize);
        return new OperationSuccess<ArchidektDeckView>(header with
        {
            Entries = slice.Rows(ordered),
            Truncated = slice.More ? true : null,
            NextCursor = slice.More ? PageCursor.Encode(PageCursorPosition.AtOffset(slice.Next), binding) : null,
            Narrow = slice.More ? DeckNarrow(options, offset, slice, callAgain) : null,
        });
    }

    /// <summary>
    /// Says how to read the entries a deck page left out, and how to read fewer fields per entry.
    /// </summary>
    private static string DeckNarrow(
        ArchidektDeckReadOptions options,
        int offset,
        ResultPageSlice slice,
        string callAgain)
    {
        string zones = options.Zones.Count == 0
            ? string.Empty
            : $" (zones {string.Join(", ", options.Zones)})";
        string fields = options.Standard
            ? "Pass responseFormat \"concise\" for fewer fields per entry."
            : "These entries hold the fields card name, quantity, zone, categories, printing, Oracle ID, and inDeck; " +
                "responseFormat \"standard\" adds the rest.";
        return $"Entries {offset + 1} to {slice.Next} of {slice.Total}{zones} are shown. {callAgain} with cursor " +
            "set to nextCursor and the same other arguments for the rest. Pass zones (commander, main, sideboard, " +
            $"maybeboard) to read fewer entries. {fields}";
    }

    /// <summary>
    /// Builds one page of the deck list: the summaries from the cursor that fit, each with its description cut.
    /// </summary>
    internal static OperationResult<RemoteDeckPage> PageDeckList(RemoteDeckPage list, string? cursor, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(list);
        PageCursorBinding binding = PageCursorBinding.ForSource(list.Evidence.SourceChecksum);
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(cursor, binding);
        if (decoded is not OperationSuccess<PageCursorPosition> start)
        {
            return OperationFailure.Forward<PageCursorPosition, RemoteDeckPage>(decoded);
        }

        int offset = start.Data.Offset;
        if (offset > list.Items.Count)
        {
            return PageCursor.Invalid();
        }

        List<RemoteDeckSummary> cut = [.. list.Items.Select(CutDescription)];
        RemoteDeckPage header = new([], null, list.Evidence);
        ResultPageSlice slice = ResultPage.Take(cut, offset, ResultSize.Measure(header)).Limit(pageSize);
        return new OperationSuccess<RemoteDeckPage>(header with
        {
            Items = slice.Rows(cut),
            NextCursor = slice.More ? PageCursor.Encode(PageCursorPosition.AtOffset(slice.Next), binding) : null,
        });
    }

    /// <summary>
    /// Cuts the description of a listed deck, and says so when it cuts.
    /// </summary>
    private static RemoteDeckSummary CutDescription(RemoteDeckSummary deck)
    {
        if (deck.Description is null)
        {
            return deck;
        }

        ShownText shown = ResultText.Cut(deck.Description, MaximumListedDescriptionBytes);
        return shown.Truncated ? deck with { Description = shown.Text, DescriptionTruncated = true } : deck;
    }

    /// <summary>
    /// Builds one page of a preview: a summary, then the operations and the differences that fit, from the positions
    /// in the cursor. Operations come first, because they are what the apply tool would run. The differences are shown
    /// whole, or as their differing fields when <paramref name="format"/> is concise; the page is cut by the size of
    /// the rows shown, and nothing else in the result depends on the format.
    /// </summary>
    internal static OperationResult<ArchidektSyncPreviewView> PagePreview(
        ArchidektSyncPreview preview,
        string? cursor,
        int pageSize,
        PageCursorBinding request,
        string? applyNotice,
        ArchidektResponseFormat format)
    {
        ArgumentNullException.ThrowIfNull(preview);
        PageCursorBinding binding = request.And(PageCursorBinding.ForPreview(preview.PreviewFingerprint));
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            binding,
            PageCursorShape.TwoOffsets,
            current: new ConflictState(preview.Revision, preview.RemoteFingerprint, preview.PreviewFingerprint));
        if (decoded is not OperationSuccess<PageCursorPosition> start)
        {
            return OperationFailure.Forward<PageCursorPosition, ArchidektSyncPreviewView>(decoded);
        }

        IReadOnlyList<ArchidektDifferenceRow> rows = DifferenceRows(preview.Differences, format);
        if (start.Data.FirstOffset > preview.Operations.Count || start.Data.SecondOffset > rows.Count)
        {
            return PageCursor.Invalid();
        }

        applyNotice = ArchidektApplyAvailability.WithBlockedNotice(applyNotice, preview.ApplyBlockedNotice);
        ArchidektSyncPreviewView header = new(
            preview.Direction,
            Summarize(preview),
            applyNotice is null,
            applyNotice,
            preview.LocalDeckId,
            preview.Revision,
            preview.RemoteDeckId,
            preview.RemoteFingerprint,
            preview.ContentFingerprint,
            preview.PreviewFingerprint,
            preview.HasConflicts,
            preview.PredictedProviderRequests,
            [],
            [])
        {
            ProviderRequestLimit = preview.ProviderRequestLimit,
            ApplyOptions = preview.ApplyOptionsNotice,
        };
        (ResultPageSlice operations, ResultPageSlice differences) = TakeTwoWithin(
            preview.Operations,
            start.Data.FirstOffset,
            rows,
            start.Data.SecondOffset,
            pageSize,
            ResultSize.Measure(header));
        bool more = operations.More || differences.More;
        return new OperationSuccess<ArchidektSyncPreviewView>(header with
        {
            Operations = operations.Rows(preview.Operations),
            Differences = differences.Rows(rows),
            Truncated = more ? true : null,
            NextCursor = more
                ? PageCursor.Encode(PageCursorPosition.AtOffsets(operations.Next, differences.Next), binding)
                : null,
            Narrow = more ? TwoListNarrow("the summary and every guard", format) : null,
        });
    }

    /// <summary>
    /// Builds one page of a snapshot: its metadata, then its saved deck in the same pages as a deck read.
    /// </summary>
    internal static OperationResult<ArchidektSnapshotView> PageSnapshot(
        RemoteNamedSnapshot snapshot,
        string? cursor,
        int pageSize,
        PageCursorBinding request,
        ArchidektDeckReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        OperationResult<ArchidektDeckView> deck = PageDeck(
            snapshot.Deck,
            cursor,
            pageSize,
            request,
            PageCursorBinding.ForSource(snapshot.Summary.Checksum + "." + snapshot.Deck.ContentFingerprint),
            options,
            "Call archidekt_snapshots_get again with the same kind, remoteDeckId, and snapshotId");
        return deck is OperationSuccess<ArchidektDeckView> page
            ? new OperationSuccess<ArchidektSnapshotView>(
                new ArchidektSnapshotView(snapshot.Summary, snapshot.Evidence, page.Data))
            : OperationFailure.Forward<ArchidektDeckView, ArchidektSnapshotView>(deck);
    }

    /// <summary>
    /// Builds one page of a folder tree: the folder count, then the folder records from the cursor that fit.
    /// </summary>
    internal static OperationResult<ArchidektFolderTreeView> PageFolders(
        RemoteFolderTree tree,
        string? cursor,
        int pageSize,
        PageCursorBinding request)
    {
        ArgumentNullException.ThrowIfNull(tree);
        PageCursorBinding binding = request.And(PageCursorBinding.ForRemote(tree.TreeFingerprint));
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            binding,
            current: new ConflictState(RemoteFingerprint: tree.TreeFingerprint));
        if (decoded is not OperationSuccess<PageCursorPosition> start)
        {
            return OperationFailure.Forward<PageCursorPosition, ArchidektFolderTreeView>(decoded);
        }

        int offset = start.Data.Offset;
        if (offset > tree.Items.Count)
        {
            return PageCursor.Invalid();
        }

        ArchidektFolderTreeView header = new(tree.Items.Count, [], tree.Evidence, tree.TreeFingerprint);
        ResultPageSlice slice = ResultPage.Take(tree.Items, offset, ResultSize.Measure(header)).Limit(pageSize);
        return new OperationSuccess<ArchidektFolderTreeView>(header with
        {
            Items = slice.Rows(tree.Items),
            Truncated = slice.More ? true : null,
            NextCursor = slice.More ? PageCursor.Encode(PageCursorPosition.AtOffset(slice.Next), binding) : null,
            Narrow = slice.More
                ? $"Folders {offset + 1} to {slice.Next} of {tree.Items.Count} are shown. Call archidekt_folders_get " +
                    "again with cursor set to nextCursor and the same other arguments for the rest."
                : null,
        });
    }

    /// <summary>
    /// Builds one page of the differences between a local deck, its baseline, and the remote deck, shown whole or, when
    /// <paramref name="format"/> is concise, as their differing fields.
    /// </summary>
    internal static OperationResult<ArchidektSyncDiffView> PageDiff(
        ArchidektSyncDiff diff,
        string? cursor,
        int pageSize,
        PageCursorBinding request,
        ArchidektResponseFormat format)
    {
        ArgumentNullException.ThrowIfNull(diff);
        PageCursorBinding binding = request
            .And(PageCursorBinding.ForRemote(diff.RemoteFingerprint))
            .And(PageCursorBinding.ForDeck(diff.LocalDeckId, diff.Revision))
            .And(PageCursorBinding.ForSource(diff.BaselineRemoteFingerprint ?? "none"));
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            binding,
            current: new ConflictState(diff.Revision, diff.RemoteFingerprint));
        if (decoded is not OperationSuccess<PageCursorPosition> start)
        {
            return OperationFailure.Forward<PageCursorPosition, ArchidektSyncDiffView>(decoded);
        }

        int offset = start.Data.Offset;
        if (offset > diff.Differences.Count)
        {
            return PageCursor.Invalid();
        }

        ArchidektSyncDiffView header = new(
            diff.LocalDeckId,
            diff.Revision,
            diff.RemoteDeckId,
            diff.RemoteFingerprint,
            diff.BaselineRemoteFingerprint,
            diff.HasConflicts,
            diff.Differences.Count,
            []);
        IReadOnlyList<ArchidektDifferenceRow> rows = DifferenceRows(diff.Differences, format);
        ResultPageSlice slice = ResultPage.Take(rows, offset, ResultSize.Measure(header)).Limit(pageSize);
        return new OperationSuccess<ArchidektSyncDiffView>(header with
        {
            Differences = slice.Rows(rows),
            Truncated = slice.More ? true : null,
            NextCursor = slice.More ? PageCursor.Encode(PageCursorPosition.AtOffset(slice.Next), binding) : null,
            Narrow = slice.More
                ? $"Differences {offset + 1} to {slice.Next} of {diff.Differences.Count} are shown. Call " +
                    "archidekt_sync_diff again with cursor set to nextCursor and the same other arguments" +
                    (format == ArchidektResponseFormat.Concise ? ", including responseFormat \"concise\"." : ".")
                : null,
        });
    }

    /// <summary>
    /// Builds one page of a snapshot restore preview: counts and guards, then the operations and the differences that
    /// fit, from the positions in the cursor.
    /// </summary>
    internal static OperationResult<ArchidektSnapshotRestorePreviewView> PageRestorePreview(
        ArchidektSnapshotRestorePreview preview,
        string? cursor,
        int pageSize,
        PageCursorBinding request)
    {
        ArgumentNullException.ThrowIfNull(preview);
        PageCursorBinding binding = request.And(PageCursorBinding.ForPreview(preview.PreviewFingerprint));
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            binding,
            PageCursorShape.TwoOffsets,
            current: new ConflictState(
                RemoteFingerprint: preview.RemoteFingerprint,
                PreviewFingerprint: preview.PreviewFingerprint));
        if (decoded is not OperationSuccess<PageCursorPosition> start)
        {
            return OperationFailure.Forward<PageCursorPosition, ArchidektSnapshotRestorePreviewView>(decoded);
        }

        if (start.Data.FirstOffset > preview.Operations.Count || start.Data.SecondOffset > preview.Differences.Count)
        {
            return PageCursor.Invalid();
        }

        ArchidektSnapshotRestorePreviewView header = new(
            preview.RemoteDeckId,
            preview.SnapshotId,
            preview.SnapshotChecksum,
            preview.SnapshotContentFingerprint,
            preview.RemoteFingerprint,
            preview.PreviewFingerprint,
            preview.PredictedProviderRequests,
            preview.Operations.Count,
            preview.Differences.Count,
            [],
            []);
        (ResultPageSlice operations, ResultPageSlice differences) = TakeTwoWithin(
            preview.Operations,
            start.Data.FirstOffset,
            preview.Differences,
            start.Data.SecondOffset,
            pageSize,
            ResultSize.Measure(header));
        bool more = operations.More || differences.More;
        return new OperationSuccess<ArchidektSnapshotRestorePreviewView>(header with
        {
            Operations = operations.Rows(preview.Operations),
            Differences = differences.Rows(preview.Differences),
            Truncated = more ? true : null,
            NextCursor = more
                ? PageCursor.Encode(PageCursorPosition.AtOffsets(operations.Next, differences.Next), binding)
                : null,
            Narrow = more ? TwoListNarrow("the counts and every guard", ArchidektResponseFormat.Standard) : null,
        });
    }

    /// <summary>
    /// Pages two lists of one result with one page size for the rows of both: the first list fills the page, and the
    /// second takes what is left, each cut again by the size limit.
    /// </summary>
    private static (ResultPageSlice First, ResultPageSlice Second) TakeTwoWithin<TFirst, TSecond>(
        IReadOnlyList<TFirst> first,
        int firstOffset,
        IReadOnlyList<TSecond> second,
        int secondOffset,
        int pageSize,
        int headerBytes)
    {
        int firstCount = Math.Min(pageSize, first.Count - firstOffset);
        int secondCount = Math.Min(pageSize - firstCount, second.Count - secondOffset);
        (ResultPageSlice firstSlice, ResultPageSlice secondSlice) = ResultPage.TakeTwo(
            [.. first.Take(firstOffset + firstCount)],
            firstOffset,
            [.. second.Take(secondOffset + secondCount)],
            secondOffset,
            headerBytes);
        return (firstSlice with { Total = first.Count }, secondSlice with { Total = second.Count });
    }

    /// <summary>
    /// Says how to read the rows a two-list page left out, and that every page must come from the same preview.
    /// </summary>
    private static string TwoListNarrow(string covers, ArchidektResponseFormat format)
    {
        return $"More rows follow; {covers} cover the whole preview. Call again with cursor set to nextCursor and " +
            "the same other arguments, so every page comes from the same preview" +
            (format == ArchidektResponseFormat.Concise ? ", and with responseFormat \"concise\" again." : ".");
    }

    /// <summary>
    /// Picks the form the difference rows are shown in: whole values, or only the fields that differ.
    /// </summary>
    private static IReadOnlyList<ArchidektDifferenceRow> DifferenceRows(
        IReadOnlyList<ArchidektDifference> differences,
        ArchidektResponseFormat format)
    {
        if (format == ArchidektResponseFormat.Standard)
        {
            return differences;
        }

        List<ArchidektDifferenceRow> rows = new(differences.Count);
        foreach (ArchidektDifference difference in differences)
        {
            rows.Add(ArchidektConciseDifference.From(difference));
        }

        return rows;
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
