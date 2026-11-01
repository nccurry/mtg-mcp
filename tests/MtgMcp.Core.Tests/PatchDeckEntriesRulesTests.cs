using MtgMcp.Core.Decks;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the shared rules a patch of deck entries and a drafted entry must meet before the store runs them.
/// </summary>
public sealed class PatchDeckEntriesRulesTests
{
    /// <summary>
    /// Holds an entry ID every case uses.
    /// </summary>
    private static readonly Guid Entry = Guid.CreateVersion7();

    /// <summary>
    /// Verifies acceptable patches have no problem.
    /// </summary>
    [Fact]
    public void FirstProblem_AcceptablePatches_ReturnNull()
    {
        Guid category = Guid.CreateVersion7();

        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange([Entry], Quantity: 2)));
        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange([Entry], RemoveCategoryIds: [category])));
        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange(
            [Entry],
            Zone: "main",
            ZoneCategories: "match",
            PrimaryCategoryId: category)));
        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange(
            [Entry],
            Zone: "tokens",
            ZoneCategories: "leave")));
        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange(
            [Entry],
            Printing: new DeckPrintingPatch(SetCode: "lea", CollectorNumber: "1"))));
    }

    /// <summary>
    /// Verifies each unacceptable patch names its problem.
    /// </summary>
    [Theory]
    [InlineData("empty-list", "needs 1 to 100 entry IDs")]
    [InlineData("empty-id", "entry ID is invalid")]
    [InlineData("no-fields", "at least one field")]
    [InlineData("blank-zone", "Zone must not be blank")]
    [InlineData("blank-finish", "Finish must not be blank")]
    [InlineData("blank-language", "Language must not be blank")]
    [InlineData("empty-category", "category ID is invalid")]
    [InlineData("printing-both", "printingId alone")]
    [InlineData("mode-invalid", "\"match\" or \"leave\"")]
    public void FirstProblem_UnacceptablePatches_NameTheProblem(string kind, string expected)
    {
        PatchDeckEntriesChange patch = kind switch
        {
            "empty-list" => new PatchDeckEntriesChange([], Quantity: 1),
            "empty-id" => new PatchDeckEntriesChange([Guid.Empty], Quantity: 1),
            "no-fields" => new PatchDeckEntriesChange([Entry], AddCategoryIds: []),
            "blank-zone" => new PatchDeckEntriesChange([Entry], Zone: " ", ZoneCategories: "leave"),
            "blank-finish" => new PatchDeckEntriesChange([Entry], Finish: " "),
            "blank-language" => new PatchDeckEntriesChange([Entry], Language: ""),
            "empty-category" => new PatchDeckEntriesChange([Entry], AddCategoryIds: [Guid.Empty]),
            "printing-both" => new PatchDeckEntriesChange(
                [Entry],
                Printing: new DeckPrintingPatch(Guid.CreateVersion7(), "lea", "1")),
            _ => new PatchDeckEntriesChange([Entry], Zone: "main", ZoneCategories: "sometimes"),
        };

        Assert.Contains(expected, PatchDeckEntriesRules.FirstProblem(patch), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a drafted category field problem is refused by the draft rules.
    /// </summary>
    [Fact]
    public void DraftFirstProblem_BadCategoryFields_AreRefused()
    {
        Assert.Null(DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(
            1,
            "Card",
            CategoryIds: [Entry],
            PrimaryCategoryId: Entry,
            ZoneCategories: "Match")));
        Assert.Contains("zoneCategories", DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(1, "Card", ZoneCategories: "x")));
        Assert.Contains("category ID", DeckEntryDraftRules.FirstProblem(new DeckEntryDraft(1, "Card", PrimaryCategoryId: Guid.Empty)));
    }
}
