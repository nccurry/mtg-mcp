using MtgMcp.Core.Results;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Checks the one loop that decides how many rows of a list fit a result.
/// </summary>
public sealed class ResultSizeLimitTests
{
    /// <summary>
    /// Verifies the loop counts the leading items that fit, reports the bytes they take, and does not read the items
    /// after the first that does not fit.
    /// </summary>
    [Fact]
    public void CountWithin_StopsAtTheFirstItemThatDoesNotFit()
    {
        int read = 0;
        IEnumerable<int> Sizes()
        {
            foreach (int size in new[] { 40, 40, 40, 40 })
            {
                read++;
                yield return size;
            }
        }

        int kept = ResultSizeLimit.CountWithin(Sizes(), 100, out int used);

        Assert.Equal(2, kept);
        Assert.Equal(80, used);
        Assert.Equal(3, read);
    }

    /// <summary>
    /// Verifies a list whose first item is over the limit still keeps that one item, and an empty list keeps none.
    /// </summary>
    [Fact]
    public void CountFitting_KeepsAtLeastOneItem_WhenTheListIsNotEmpty()
    {
        Assert.Equal(1, ResultSizeLimit.CountFitting([ResultSizeLimit.MaximumBytes * 2, 10]));
        Assert.Equal(0, ResultSizeLimit.CountFitting([]));
        Assert.Equal(3, ResultSizeLimit.CountFitting([100, 100, 100]));
    }
}
