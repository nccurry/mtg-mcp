using MtgMcp.Core.Results;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies a failed result keeps its case and details when it is carried to another result type.
/// </summary>
public sealed class OperationFailureTests
{
    /// <summary>
    /// Verifies each failure case keeps its type, reason, message, and details.
    /// </summary>
    [Fact]
    public void Forward_EachFailureCase_KeepsTheCase()
    {
        OperationResult<int>[] failures =
        [
            new OperationNotFound("missing-thing", "Not here."),
            new OperationNotCached("not-installed", "Install it."),
            new OperationUnsupported("not-supported", "Not supported."),
            new OperationUnavailable("down", "Unavailable."),
            new OperationConflict("stale", "Changed.", new ConflictState(Revision: 4)),
            new OperationInvalidInput("bad-input", "Bad.", "field"),
        ];

        foreach (OperationResult<int> failure in failures)
        {
            OperationResult<string> forwarded = OperationFailure.Forward<int, string>(failure);

            Assert.Equal(failure.Value.GetType(), forwarded.Value.GetType());
            Assert.Equal(failure.Value, forwarded.Value);
        }
    }

    /// <summary>
    /// Verifies a success has no failure to carry and becomes an unavailable result.
    /// </summary>
    [Fact]
    public void Forward_Success_BecomesUnavailable()
    {
        OperationResult<string> forwarded = OperationFailure.Forward<int, string>(new OperationSuccess<int>(1));

        Assert.Equal("unexpected-operation-state", Assert.IsType<OperationUnavailable>(forwarded.Value).ReasonCode);
    }
}
