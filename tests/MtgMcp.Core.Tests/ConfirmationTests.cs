using MtgMcp.Core.Results;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the shared confirmation phrase and its one refusal wording.
/// </summary>
public sealed class ConfirmationTests
{
    /// <summary>
    /// Verifies the phrase joins the action, the target kind, and the id.
    /// </summary>
    [Fact]
    public void Phrase_JoinsActionKindAndId()
    {
        Guid id = Guid.Parse("0197a1b2-c3d4-7e5f-8091-a2b3c4d5e6f7");

        Assert.Equal("delete deck 42", Confirmation.Phrase("delete", "deck", "42"));
        Assert.Equal($"restore backup {id:D}", Confirmation.Phrase("restore", "backup", id));
    }

    /// <summary>
    /// Verifies a long caller-supplied id is echoed at 64 characters and marked as cut.
    /// </summary>
    [Fact]
    public void Phrase_CutsALongIdToTheEchoLimit()
    {
        string phrase = Confirmation.Phrase("delete", "deck", new string('9', 500));

        Assert.Equal("delete deck " + new string('9', Confirmation.MaximumEchoedIdLength) + "...", phrase);
    }

    /// <summary>
    /// Verifies a cut never splits a surrogate pair.
    /// </summary>
    [Fact]
    public void Phrase_DoesNotSplitASurrogatePair()
    {
        string id = new string('a', Confirmation.MaximumEchoedIdLength - 1) + "\U0001F600tail";

        string phrase = Confirmation.Phrase("delete", "deck", id);

        Assert.Equal("delete deck " + new string('a', Confirmation.MaximumEchoedIdLength - 1) + "...", phrase);
    }

    /// <summary>
    /// Verifies the exact phrase passes, and anything else is refused with one wording at the confirmation path.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DELETE DECK 42")]
    [InlineData("delete deck 42 ")]
    public void Check_AnythingButTheExactPhrase_IsRefusedWithTheShownPhrase(string? confirmation)
    {
        OperationInvalidInput? refused = Confirmation.Check(confirmation, "delete deck 42");

        Assert.NotNull(refused);
        Assert.Equal(SharedReasonCodes.ConfirmationRequired, refused.ReasonCode);
        Assert.Equal("confirmation", refused.Path);
        Assert.Equal("confirmation must be exactly \"delete deck 42\".", refused.Message);
        Assert.Null(Confirmation.Check("delete deck 42", "delete deck 42"));
    }
}
