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
    /// Holds a category ID every case uses.
    /// </summary>
    private static readonly Guid Category = Guid.CreateVersion7();

    /// <summary>
    /// Verifies acceptable patches have no problem.
    /// </summary>
    [Fact]
    public void FirstProblem_AcceptablePatches_ReturnNull()
    {
        Guid category = Guid.CreateVersion7();

        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange([Entry], Quantity: 2)));
        Assert.Null(PatchDeckEntriesRules.FirstProblem(
            new PatchDeckEntriesChange([Entry], RemoveCategoryIds: [category])));
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
    /// Gives each unacceptable patch with the message the rules must return for it.
    /// </summary>
    public static TheoryData<string, PatchDeckEntriesChange, string> Refusals { get; } = BuildRefusals();

    /// <summary>
    /// Verifies exactly 100 distinct entry IDs are accepted and 101 are refused.
    /// </summary>
    [Fact]
    public void FirstProblem_HundredEntryIds_AreAcceptedAndHundredAndOneAreNot()
    {
        List<Guid> ids = [.. Enumerable.Range(0, 100).Select(_ => Guid.CreateVersion7())];

        Assert.Null(PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange(ids, Quantity: 1)));
        Assert.Null(PatchDeckEntriesRules.EntryIdsProblem(ids));
        ids.Add(Guid.CreateVersion7());
        Assert.Equal(
            "A patch-entries change needs 1 to 100 entry IDs.",
            PatchDeckEntriesRules.FirstProblem(new PatchDeckEntriesChange(ids, Quantity: 1)));
        Assert.NotNull(PatchDeckEntriesRules.EntryIdsProblem(null));
    }

    /// <summary>
    /// Verifies each unacceptable patch returns its exact problem message.
    /// </summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public void FirstProblem_UnacceptablePatches_ReturnTheMessage(
        string name,
        PatchDeckEntriesChange patch,
        string expected)
    {
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Equal(expected, PatchDeckEntriesRules.FirstProblem(patch));
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

    /// <summary>
    /// Verifies the entry field check leaves the categories aside while the full check includes them.
    /// </summary>
    [Fact]
    public void DraftFirstEntryProblem_IgnoresCategoryFields()
    {
        DeckEntryDraft badCategories = new(1, "Card", ZoneCategories: "x", PrimaryCategoryId: Guid.Empty);

        Assert.Null(DeckEntryDraftRules.FirstEntryProblem(badCategories));
        Assert.NotNull(DeckEntryDraftRules.CategoryProblem(badCategories));
        Assert.Equal("Card name is required.", DeckEntryDraftRules.FirstProblem(badCategories with { CardName = " " }));
    }

    /// <summary>
    /// Builds the unacceptable patches with their exact messages.
    /// </summary>
    private static TheoryData<string, PatchDeckEntriesChange, string> BuildRefusals()
    {
        const string zoneMode = "A change that sets a zone must also set zoneCategories to \"match\" or \"leave\".";
        const string printing = "The printing must be a printingId alone, or a setCode with a collectorNumber.";
        string idCount = "A patch-entries change needs 1 to 100 entry IDs.";
        return new TheoryData<string, PatchDeckEntriesChange, string>
        {
            { "empty-list", new PatchDeckEntriesChange([], Quantity: 1), idCount },
            {
                "101-ids",
                new PatchDeckEntriesChange(
                    [.. Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7())],
                    Quantity: 1),
                idCount
            },
            {
                "duplicate-ids",
                new PatchDeckEntriesChange([Entry, Entry], Quantity: 1),
                $"Entry ID {Entry} is listed more than once."
            },
            { "empty-id", new PatchDeckEntriesChange([Guid.Empty], Quantity: 1), "An entry ID is invalid." },
            {
                "no-fields",
                new PatchDeckEntriesChange([Entry], AddCategoryIds: []),
                "A patch-entries change must set at least one field besides entryIds and zoneCategories."
            },
            { "zero-quantity", new PatchDeckEntriesChange([Entry], Quantity: 0), "Entry quantity must be positive." },
            {
                "negative-quantity",
                new PatchDeckEntriesChange([Entry], Quantity: -2),
                "Entry quantity must be positive."
            },
            {
                "blank-zone",
                new PatchDeckEntriesChange([Entry], Zone: " ", ZoneCategories: "leave"),
                "Zone must not be blank."
            },
            { "blank-finish", new PatchDeckEntriesChange([Entry], Finish: " "), "Finish must not be blank." },
            { "blank-language", new PatchDeckEntriesChange([Entry], Language: ""), "Language must not be blank." },
            {
                "empty-printing-id",
                new PatchDeckEntriesChange([Entry], Printing: new DeckPrintingPatch(Guid.Empty)),
                printing
            },
            {
                "printing-both",
                new PatchDeckEntriesChange([Entry], Printing: new DeckPrintingPatch(Guid.CreateVersion7(), "lea", "1")),
                printing
            },
            {
                "empty-category",
                new PatchDeckEntriesChange([Entry], AddCategoryIds: [Guid.Empty]),
                "A category ID is invalid."
            },
            {
                "category-in-two-lists",
                new PatchDeckEntriesChange([Entry], AddCategoryIds: [Category], RemoveCategoryIds: [Category]),
                $"Category ID {Category} is named more than once in primaryCategoryId, addCategoryIds, and " +
                "removeCategoryIds; name it in one field."
            },
            { "zone-without-mode", new PatchDeckEntriesChange([Entry], Zone: "sideboard"), zoneMode },
            {
                "mode-without-zone",
                new PatchDeckEntriesChange([Entry], Quantity: 1, ZoneCategories: "match"),
                "zoneCategories applies only when the change sets a zone."
            },
            {
                "mode-invalid",
                new PatchDeckEntriesChange([Entry], Zone: "main", ZoneCategories: "sometimes"),
                zoneMode
            },
            {
                "match-unknown-zone",
                new PatchDeckEntriesChange([Entry], Zone: "tokens", ZoneCategories: "match"),
                "zoneCategories \"match\" has no category for zone 'tokens'; use \"leave\"."
            },
            {
                "match-with-primary",
                new PatchDeckEntriesChange(
                    [Entry],
                    Zone: "sideboard",
                    ZoneCategories: "match",
                    PrimaryCategoryId: Category),
                "primaryCategoryId cannot be set with zoneCategories \"match\" for a zone that has a zone category; " +
                "the zone category becomes the primary category."
            },
        };
    }
}
