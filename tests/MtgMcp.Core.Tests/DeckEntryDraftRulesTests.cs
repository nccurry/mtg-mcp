using MtgMcp.Core.Decks;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Checks the rules that decide whether a new deck entry can be written.
/// </summary>
public sealed class DeckEntryDraftRulesTests
{
    /// <summary>
    /// Verifies a draft with the usual values has no problem.
    /// </summary>
    [Fact]
    public void FirstProblem_ValidDraft_IsNull()
    {
        Assert.Null(DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(1, "Sol Ring", EntryId: Guid.NewGuid())));
    }

    /// <summary>
    /// Verifies each refused value gives its own reason.
    /// </summary>
    [Theory]
    [InlineData(0, "Sol Ring", "en", "nonfoil", "main", "Entry quantity must be positive.")]
    [InlineData(1, " ", "en", "nonfoil", "main", "Card name is required.")]
    [InlineData(1, "Sol Ring", "", "nonfoil", "main", "Language is required.")]
    [InlineData(1, "Sol Ring", "en", " ", "main", "Finish is required.")]
    [InlineData(1, "Sol Ring", "en", "nonfoil", "", "Zone is required.")]
    public void FirstProblem_BlankOrNonPositiveValue_NamesTheProblem(
        int quantity,
        string name,
        string language,
        string finish,
        string zone,
        string expected)
    {
        DeckEntryDraft draft = new(quantity, name, Language: language, Finish: finish, Zone: zone);

        Assert.Equal(expected, DeckEntryDraftRules.FirstProblem(draft));
    }

    /// <summary>
    /// Verifies an explicitly empty ID is refused for the entry, the Oracle card, and the printing.
    /// </summary>
    [Fact]
    public void FirstProblem_EmptyIds_AreRefused()
    {
        Assert.Equal(
            "The entry ID is invalid.",
            DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(1, "Sol Ring", EntryId: Guid.Empty)));
        Assert.Equal(
            "The Oracle ID is invalid.",
            DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(1, "Sol Ring", OracleId: Guid.Empty)));
        Assert.Equal(
            "The printing ID is invalid.",
            DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(1, "Sol Ring", PrintingId: Guid.Empty)));
    }
}
