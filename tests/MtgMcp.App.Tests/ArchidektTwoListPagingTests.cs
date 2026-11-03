using MtgMcp.App.Archidekt;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the paging of previews that carry two lists, operations and differences, when one list ends before the
/// other.
/// </summary>
public sealed class ArchidektTwoListPagingTests
{
    /// <summary>
    /// Builds a push preview with three operations and many large differences.
    /// </summary>
    private static ArchidektSyncPreview BuildPreview(string fingerprint, int differenceCount)
    {
        List<ArchidektDifference> differences =
        [
            .. Enumerable.Range(0, differenceCount).Select(index => new ArchidektDifference(
                $"/entries/{index:000}",
                "local-changed",
                null,
                new string('a', 400),
                new string('b', 400))),
        ];
        List<ArchidektRemoteOperation> operations =
        [
            .. Enumerable.Range(1, 3).Select(index => new ArchidektRemoteOperation(index, "entry-update", "x", "y")),
        ];
        return new ArchidektSyncPreview(
            "push", Guid.NewGuid(), 1, "42", "remote", "content", fingerprint, false, differences, operations, 3);
    }

    /// <summary>
    /// Verifies a caller that passes only nextCursor reaches every difference and every operation exactly once when
    /// the operations end on the first page and the differences go on for many pages, and that the last page has no
    /// next cursor.
    /// </summary>
    [Fact]
    public void PushPreview_OperationsEndBeforeDifferences_FollowingNextCursorReadsEveryRowOnce()
    {
        ArchidektSyncPreview preview = BuildPreview("preview", 200);
        List<string> paths = [];
        List<int> sequences = [];
        string? cursor = null;
        int pages = 0;

        while (true)
        {
            ArchidektSyncPreviewView page = Assert.IsType<OperationSuccess<ArchidektSyncPreviewView>>(
                ArchidektResultPaging.PagePreview(
                    preview,
                    cursor,
                    100,
                    PageCursorBinding.None,
                    null,
                    ArchidektResponseFormat.Standard).Value).Data;
            paths.AddRange(page.Differences.Select(value => value.Path));
            sequences.AddRange(page.Operations.Select(value => value.Sequence));
            pages++;
            Assert.True(pages < 100, "the pages never ended");
            if (page.NextCursor is null)
            {
                Assert.Null(page.Truncated);
                break;
            }

            Assert.True(page.Truncated);
            cursor = page.NextCursor;
        }

        Assert.True(pages > 2);
        Assert.Equal([1, 2, 3], sequences);
        Assert.Equal(preview.Differences.Select(value => value.Path), paths);
    }

    /// <summary>
    /// Verifies the rows of both lists together never pass the page size.
    /// </summary>
    [Fact]
    public void PushPreview_PageSize_CapsTheRowsOfBothLists()
    {
        ArchidektSyncPreview preview = BuildPreview("preview", 10);

        ArchidektSyncPreviewView page = Assert.IsType<OperationSuccess<ArchidektSyncPreviewView>>(
            ArchidektResultPaging.PagePreview(
                preview,
                null,
                5,
                PageCursorBinding.None,
                null,
                ArchidektResponseFormat.Standard).Value).Data;

        Assert.Equal(3, page.Operations.Count);
        Assert.Equal(2, page.Differences.Count);
        Assert.NotNull(page.NextCursor);
    }

    /// <summary>
    /// Verifies a cursor from a preview that has since changed is refused as stale with the current preview, and a
    /// cursor that is not one of this tool is invalid at the cursor argument.
    /// </summary>
    [Fact]
    public void PushPreview_CursorOfAnotherPreview_IsStale_AndAMalformedCursorIsInvalid()
    {
        ArchidektSyncPreview before = BuildPreview("before", 200);
        string cursor = Assert.IsType<OperationSuccess<ArchidektSyncPreviewView>>(
            ArchidektResultPaging.PagePreview(
                before,
                null,
                100,
                PageCursorBinding.None,
                null,
                ArchidektResponseFormat.Standard).Value).Data.NextCursor!;
        ArchidektSyncPreview now = BuildPreview("now", 1);

        OperationConflict stale = Assert.IsType<OperationConflict>(
            ArchidektResultPaging.PagePreview(
                now,
                cursor,
                100,
                PageCursorBinding.None,
                null,
                ArchidektResponseFormat.Standard).Value);
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(
            ArchidektResultPaging.PagePreview(
                now,
                "not-a-cursor",
                100,
                PageCursorBinding.None,
                null,
                ArchidektResponseFormat.Standard).Value);

        Assert.Equal("cursor-stale", stale.ReasonCode);
        Assert.Equal("now", stale.Current?.PreviewFingerprint);
        Assert.Equal("invalid-cursor", invalid.ReasonCode);
        Assert.Equal("cursor", invalid.Path);
    }
}
