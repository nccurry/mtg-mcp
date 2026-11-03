using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies how a page of two lists fills from the first list before the second, and how page sizes are checked.
/// </summary>
public sealed class ResultPageTests
{
    /// <summary>
    /// Builds rows of a given text length, so the byte budget cuts a list at a predictable row.
    /// </summary>
    private static string[] Rows(int count, int length)
    {
        return [.. Enumerable.Range(0, count).Select(_ => new string('x', length))];
    }

    /// <summary>
    /// Verifies the second list stays empty while the first list still has rows that the size limit cut.
    /// </summary>
    [Fact]
    public void TakeTwo_FirstListCutBySize_SecondListTakesNothing()
    {
        string[] first = Rows(100, 1_000);
        string[] second = Rows(5, 10);

        (ResultPageSlice firstSlice, ResultPageSlice secondSlice) = ResultPage.TakeTwo(first, 0, second, 0, 0);

        Assert.True(firstSlice.Count is > 0 and < 100);
        Assert.True(firstSlice.More);
        Assert.Equal(0, secondSlice.Count);
        Assert.Equal(5, secondSlice.Total);
    }

    /// <summary>
    /// Verifies a page whose boundary falls between the lists fills the rest of the page from the second list.
    /// </summary>
    [Fact]
    public void TakeTwo_BoundaryBetweenTheLists_SecondListFillsTheRest()
    {
        string[] first = Rows(3, 1_000);
        string[] second = Rows(100, 1_000);

        (ResultPageSlice firstSlice, ResultPageSlice secondSlice) = ResultPage.TakeTwo(first, 0, second, 0, 0);

        Assert.Equal(3, firstSlice.Count);
        Assert.False(firstSlice.More);
        Assert.True(secondSlice.Count is > 0 and < 100);
        Assert.True(secondSlice.More);
    }

    /// <summary>
    /// Verifies following the offsets of each page reads every row of both lists once, first list first, when the
    /// first list spans several pages and the second list starts on the page where the first ends.
    /// </summary>
    [Fact]
    public void TakeTwo_FollowingTheOffsets_ReadsEachRowOnceInListOrder()
    {
        string[] first = Rows(70, 1_000);
        string[] second = Rows(70, 1_000);
        int firstOffset = 0;
        int secondOffset = 0;
        List<(int First, int Second)> pages = [];

        while (firstOffset < first.Length || secondOffset < second.Length)
        {
            (ResultPageSlice firstSlice, ResultPageSlice secondSlice) = ResultPage.TakeTwo(
                first,
                firstOffset,
                second,
                secondOffset,
                0);
            Assert.True(firstSlice.Count + secondSlice.Count > 0, "a page showed no row");
            Assert.True(secondSlice.Count == 0 || !firstSlice.More, "the second list started before the first ended");
            pages.Add((firstSlice.Count, secondSlice.Count));
            firstOffset = firstSlice.Next;
            secondOffset = secondSlice.Next;
        }

        Assert.True(pages.Count > 2);
        Assert.Equal(70, pages.Sum(page => page.First));
        Assert.Equal(70, pages.Sum(page => page.Second));
        Assert.Contains(pages, page => page.First > 0 && page.Second > 0);
    }

    /// <summary>
    /// Verifies a page with no room still shows one row, from the second list once the first list has ended.
    /// </summary>
    [Fact]
    public void TakeTwo_FirstListEnded_AFullHeaderStillShowsOneSecondRow()
    {
        (ResultPageSlice firstSlice, ResultPageSlice secondSlice) = ResultPage.TakeTwo(
            Rows(2, 10),
            2,
            Rows(4, 10),
            1,
            ResultSizeLimitBytes());

        Assert.Equal(0, firstSlice.Count);
        Assert.Equal(1, secondSlice.Count);
    }

    /// <summary>
    /// Verifies a page size passes inside its range or when missing, and is refused outside it with the unit named.
    /// </summary>
    [Fact]
    public void CheckPageSize_RefusesOutsideTheRangeAndNamesTheUnit()
    {
        Assert.Null(ResultPage.CheckPageSize(1, 10));
        Assert.Null(ResultPage.CheckPageSize(10, 10));
        Assert.Null(ResultPage.CheckPageSize(null, 10));
        Assert.Equal("pageSize must be from 1 through 10.", ResultPage.CheckPageSize(0, 10)?.Message);
        Assert.Equal("invalid-page-size", ResultPage.CheckPageSize(11, 10)?.ReasonCode);
        Assert.Equal("pageSize", ResultPage.CheckPageSize(-1, 10)?.Path);
        Assert.Equal(
            "pageSize must be from 1 through 10 UTF-16 code units.",
            ResultPage.CheckPageSize(11, 10, "UTF-16 code units")?.Message);
    }

    /// <summary>
    /// Verifies a slice keeps at most the page size and leaves a smaller slice as it is.
    /// </summary>
    [Fact]
    public void Limit_KeepsAtMostThePageSize()
    {
        ResultPageSlice slice = new(4, 10, 30);

        Assert.Equal(new ResultPageSlice(4, 3, 30), slice.Limit(3));
        Assert.Equal(slice, slice.Limit(10));
        Assert.Equal(slice, slice.Limit(50));
    }

    /// <summary>
    /// Gives a header size that leaves no room for any row.
    /// </summary>
    private static int ResultSizeLimitBytes()
    {
        return ResultSizeLimit.MaximumBytes;
    }
}
