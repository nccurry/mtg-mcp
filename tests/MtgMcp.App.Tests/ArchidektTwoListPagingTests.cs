using MtgMcp.App.Archidekt;
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
    /// Verifies a caller that passes only the next offsets a page names reaches every difference and every operation
    /// exactly once when the operations end on the first page and the differences go on for many pages.
    /// </summary>
    [Fact]
    public void PushPreview_OperationsEndBeforeDifferences_FollowingTheFieldsAloneReadsEveryRowOnce()
    {
        List<ArchidektDifference> differences =
        [
            .. Enumerable.Range(0, 200).Select(index => new ArchidektDifference(
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
        ArchidektSyncPreview preview = new(
            "push", Guid.NewGuid(), 1, "42", "remote", "content", "preview", false, differences, operations, 3);
        List<string> paths = [];
        List<int> sequences = [];
        int operationOffset = 0;
        int differenceOffset = 0;
        int pages = 0;

        while (true)
        {
            OperationResult<ArchidektSyncPreviewView> result =
                ArchidektResultPaging.PagePreview(preview, operationOffset, differenceOffset, null, "preview");
            ArchidektSyncPreviewView page = Assert.IsType<OperationSuccess<ArchidektSyncPreviewView>>(result.Value).Data;
            paths.AddRange(page.Differences.Select(value => value.Path));
            sequences.AddRange(page.Operations.Select(value => value.Sequence));
            pages++;
            Assert.True(pages < 100, "the pages never ended");
            if (page.Truncated is null)
            {
                Assert.Null(page.NextOperationOffset);
                Assert.Null(page.NextDifferenceOffset);
                break;
            }

            operationOffset = Assert.NotNull(page.NextOperationOffset);
            differenceOffset = Assert.NotNull(page.NextDifferenceOffset);
        }

        Assert.True(pages > 2);
        Assert.Equal([1, 2, 3], sequences);
        Assert.Equal(differences.Select(value => value.Path), paths);
    }
}
