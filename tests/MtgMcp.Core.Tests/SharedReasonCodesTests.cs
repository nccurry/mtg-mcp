using MtgMcp.Core.Results;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the shared reason codes are the agreed kebab-case spellings and are accepted by the result contracts.
/// </summary>
public sealed class SharedReasonCodesTests
{
    /// <summary>
    /// Verifies each shared code has its agreed spelling and a result accepts it as a reason code.
    /// </summary>
    [Theory]
    [InlineData(SharedReasonCodes.StaleDeckRevision, "stale-deck-revision")]
    [InlineData(SharedReasonCodes.PreviewChanged, "preview-changed")]
    [InlineData(SharedReasonCodes.CursorStale, "cursor-stale")]
    [InlineData(SharedReasonCodes.InvalidCursor, "invalid-cursor")]
    public void Codes_HaveTheirAgreedSpelling_AndPassReasonCodeValidation(string code, string expected)
    {
        Assert.Equal(expected, code);
        Assert.Equal(expected, new OperationConflict(code, "Changed.").ReasonCode);
    }
}
