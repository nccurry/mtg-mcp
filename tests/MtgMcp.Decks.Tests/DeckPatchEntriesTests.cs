using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies the patch-entries change and drafted entry categories against the store: fields change only when listed,
/// category edits and zone categories end in the documented state, and every refusal writes nothing.
/// </summary>
public sealed class DeckPatchEntriesTests
{
    /// <summary>
    /// Verifies a patch that sets only the quantity changes that field of every listed entry and nothing else.
    /// </summary>
    [Fact]
    public async Task Patch_QuantityOnly_ChangesOnlyQuantityOfListedEntries()
    {
        using Fixture fixture = await Fixture.CreateAsync(3);

        DeckDocument after = await fixture.ApplyAsync(
            new PatchDeckEntriesChange([fixture.EntryIds[0], fixture.EntryIds[1]], Quantity: 4));

        Assert.Equal(fixture.Deck.Revision + 1, after.Revision);
        Assert.Equal(
            [4, 4, 1],
            fixture.EntryIds.Select(id => after.Entries.Single(entry => entry.EntryId == id).Quantity));
        foreach (Guid id in fixture.EntryIds)
        {
            DeckEntry before = fixture.Deck.Entries.Single(entry => entry.EntryId == id);
            Assert.Equal(before with { Quantity = after.Entries.Single(entry => entry.EntryId == id).Quantity },
                after.Entries.Single(entry => entry.EntryId == id));
        }
    }

    /// <summary>
    /// Verifies the plain fields each change only when listed, and that zone with leave touches no category.
    /// </summary>
    [Fact]
    public async Task Patch_PlainFieldsAndLeave_ChangeOnlyListedFields()
    {
        using Fixture fixture = await Fixture.CreateAsync(1, assignRamp: true);

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "Sideboard",
            SortOrder: 7,
            Finish: "foil",
            Language: "ja",
            ZoneCategories: DeckZoneCategories.Leave));

        DeckEntry entry = Assert.Single(after.Entries);
        Assert.Equal(("sideboard", 7, "foil", "ja", 1, "Card 0"), (entry.Zone, entry.SortOrder, entry.Finish, entry.Language, entry.Quantity, entry.CardName));
        Assert.Equal(fixture.Deck.CategoryAssignments, after.CategoryAssignments);
        Assert.Equal(fixture.Deck.Categories, after.Categories);
    }

    /// <summary>
    /// Verifies a patch applies to an entry added earlier in the same batch, against its state at that point.
    /// </summary>
    [Fact]
    public async Task Patch_EntryAddedEarlierInTheBatch_IsPatched()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);
        Guid added = Guid.CreateVersion7();

        DeckDocument after = await fixture.ApplyAsync(
            new AddDeckEntryChange(new DeckEntryDraft(1, "New Card", EntryId: added)),
            new PatchDeckEntriesChange([added], Quantity: 3, PrimaryCategoryId: fixture.RampId));

        Assert.Equal(3, after.Entries.Single(entry => entry.EntryId == added).Quantity);
        Assert.Contains(new DeckCategoryAssignment(added, fixture.RampId, true), after.CategoryAssignments);
    }

    /// <summary>
    /// Verifies a category the entry has as non-primary becomes primary when named, and the old primary is dropped.
    /// </summary>
    [Fact]
    public async Task Patch_PrimaryCategory_PromotesNonPrimaryAndRemovesPreviousPrimary()
    {
        using Fixture fixture = await Fixture.CreateAsync(1, assignRamp: true);
        Guid draw = fixture.CategoryId("Draw");
        await fixture.ApplyAsync(new PatchDeckEntriesChange(fixture.EntryIds, AddCategoryIds: [draw]));

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(fixture.EntryIds, PrimaryCategoryId: draw));

        Assert.Equal(
            new DeckCategoryAssignment(fixture.EntryIds[0], draw, true),
            Assert.Single(after.CategoryAssignments));
    }

    /// <summary>
    /// Verifies naming the previous primary in addCategoryIds keeps it assigned as non-primary.
    /// </summary>
    [Fact]
    public async Task Patch_PrimaryCategoryWithAddCategoryIds_KeepsPreviousPrimaryAsNonPrimary()
    {
        using Fixture fixture = await Fixture.CreateAsync(1, assignRamp: true);
        Guid draw = fixture.CategoryId("Draw");

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            PrimaryCategoryId: draw,
            AddCategoryIds: [fixture.RampId]));

        Assert.Equal(
            [(draw, true), (fixture.RampId, false)],
            after.CategoryAssignments.OrderByDescending(value => value.IsPrimary)
                .Select(value => (value.CategoryId, value.IsPrimary)));
    }

    /// <summary>
    /// Verifies removeCategoryIds unassigns and addCategoryIds leaves an existing assignment as it is.
    /// </summary>
    [Fact]
    public async Task Patch_AddAndRemoveCategoryIds_EditNonPrimaryAssignments()
    {
        using Fixture fixture = await Fixture.CreateAsync(1, assignRamp: true);
        Guid draw = fixture.CategoryId("Draw");

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            AddCategoryIds: [fixture.RampId, draw]));
        Assert.Equal(
            [(fixture.RampId, true), (draw, false)],
            after.CategoryAssignments.OrderByDescending(value => value.IsPrimary)
                .Select(value => (value.CategoryId, value.IsPrimary)));

        after = await fixture.ApplyAsync(new PatchDeckEntriesChange(fixture.EntryIds, RemoveCategoryIds: [draw]));
        Assert.Equal(
            new DeckCategoryAssignment(fixture.EntryIds[0], fixture.RampId, true),
            Assert.Single(after.CategoryAssignments));
    }

    /// <summary>
    /// Verifies each refused patch returns invalid-deck-change and rolls the whole batch back, including an earlier
    /// valid change of the same batch.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("same-category")]
    [InlineData("duplicate-entry")]
    [InlineData("unknown-entry")]
    [InlineData("unknown-category")]
    [InlineData("zone-without-mode")]
    [InlineData("mode-without-zone")]
    [InlineData("match-with-primary")]
    [InlineData("match-unknown-zone")]
    [InlineData("too-many")]
    [InlineData("bad-printing")]
    [InlineData("zero-quantity")]
    public async Task Patch_Refused_ReturnsInvalidDeckChangeAndWritesNothing(string kind)
    {
        using Fixture fixture = await Fixture.CreateAsync(2);
        Guid entry = fixture.EntryIds[0];
        PatchDeckEntriesChange patch = kind switch
        {
            "none" => new PatchDeckEntriesChange([entry]),
            "same-category" => new PatchDeckEntriesChange(
                [entry],
                AddCategoryIds: [fixture.RampId],
                RemoveCategoryIds: [fixture.RampId]),
            "duplicate-entry" => new PatchDeckEntriesChange([entry, entry], Quantity: 2),
            "unknown-entry" => new PatchDeckEntriesChange([entry, Guid.CreateVersion7()], Quantity: 2),
            "unknown-category" => new PatchDeckEntriesChange([entry], AddCategoryIds: [Guid.CreateVersion7()]),
            "zone-without-mode" => new PatchDeckEntriesChange([entry], Zone: "sideboard"),
            "mode-without-zone" => new PatchDeckEntriesChange([entry], Quantity: 2, ZoneCategories: "match"),
            "match-with-primary" => new PatchDeckEntriesChange(
                [entry],
                Zone: "sideboard",
                ZoneCategories: "match",
                PrimaryCategoryId: fixture.RampId),
            "match-unknown-zone" => new PatchDeckEntriesChange([entry], Zone: "tokens", ZoneCategories: "match"),
            "too-many" => new PatchDeckEntriesChange(
                [.. Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7())],
                Quantity: 2),
            "bad-printing" => new PatchDeckEntriesChange(
                [entry],
                Printing: new DeckPrintingPatch(SetCode: "tst")),
            _ => new PatchDeckEntriesChange([entry], Quantity: 0),
        };

        OperationResult<DeckDocument> result = await fixture.TryApplyAsync(
            new UpdateDeckMetadataChange("Renamed", null, "custom"),
            patch);

        OperationInvalidInput refused = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-deck-change", refused.ReasonCode);
        DeckDocument stored = await fixture.ReadAsync();
        Assert.Equal(fixture.Deck.Revision, stored.Revision);
        Assert.Equal(fixture.Deck.Name, stored.Name);
        Assert.Equal(fixture.Deck.Entries, stored.Entries);
        Assert.Equal(fixture.Deck.Categories, stored.Categories);
    }

    /// <summary>
    /// Verifies one patch moves 13 entries from the maybeboard to the sideboard with match, where some also carry a
    /// non-primary Sideboard category, and each ends with exactly one zone category.
    /// </summary>
    [Fact]
    public async Task Patch_MaybeboardToSideboardWithMatch_LeavesExactlyOneZoneCategoryEach()
    {
        using Fixture fixture = await Fixture.CreateAsync(13, zone: "maybeboard", maybeboardCategories: true);
        Guid sideboard = fixture.CategoryId(DeckZoneCategories.Sideboard);
        Guid maybeboard = fixture.CategoryId(DeckZoneCategories.Maybeboard);
        await fixture.ApplyAsync(new PatchDeckEntriesChange(
            [.. fixture.EntryIds.Where((_, index) => index % 2 == 0)],
            AddCategoryIds: [sideboard]));

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "sideboard",
            ZoneCategories: DeckZoneCategories.Match));

        Assert.All(after.Entries, entry => Assert.Equal("sideboard", entry.Zone));
        foreach (Guid id in fixture.EntryIds)
        {
            DeckCategoryAssignment[] zoneAssignments = [.. after.CategoryAssignments
                .Where(value => value.EntryId == id && (value.CategoryId == sideboard || value.CategoryId == maybeboard))];
            Assert.Equal(new DeckCategoryAssignment(id, sideboard, true), Assert.Single(zoneAssignments));
            Assert.Equal(1, after.CategoryAssignments.Count(value => value.EntryId == id && value.IsPrimary));
        }
    }

    /// <summary>
    /// Verifies match keeps a non-zone category that was primary, as a non-primary one, under the zone category.
    /// </summary>
    [Fact]
    public async Task Patch_MatchKeepsFunctionalCategories_AsNonPrimary()
    {
        using Fixture fixture = await Fixture.CreateAsync(1, assignRamp: true);

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "maybeboard",
            ZoneCategories: DeckZoneCategories.Match));

        Guid maybeboard = after.Categories.Single(value => value.Name == DeckZoneCategories.Maybeboard).CategoryId;
        Assert.Equal(
            [(maybeboard, true), (fixture.RampId, false)],
            after.CategoryAssignments.OrderByDescending(value => value.IsPrimary)
                .Select(value => (value.CategoryId, value.IsPrimary)));
    }

    /// <summary>
    /// Verifies match creates the zone category once, in the same batch, when the deck lacks it, and that an existing
    /// category with another letter case is reused.
    /// </summary>
    [Fact]
    public async Task Patch_MatchWithMissingZoneCategory_CreatesItOnce()
    {
        using Fixture fixture = await Fixture.CreateAsync(2);

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "sideboard",
            ZoneCategories: DeckZoneCategories.Match));

        DeckCategory created = Assert.Single(after.Categories, value => value.Name == DeckZoneCategories.Sideboard);
        Assert.Equal(
            [(fixture.EntryIds[0], created.CategoryId, true), (fixture.EntryIds[1], created.CategoryId, true)],
            after.CategoryAssignments.OrderBy(value => value.EntryId == fixture.EntryIds[0] ? 0 : 1)
                .Select(value => (value.EntryId, value.CategoryId, value.IsPrimary)));

        DeckDocument again = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            [fixture.EntryIds[0]],
            Zone: "maybeboard",
            ZoneCategories: DeckZoneCategories.Match));
        Assert.Single(again.Categories, value => value.Name == DeckZoneCategories.Maybeboard);

        using Fixture lower = await Fixture.CreateAsync(1, extraCategories: ["sideboard"]);
        DeckDocument reused = await lower.ApplyAsync(new PatchDeckEntriesChange(
            lower.EntryIds,
            Zone: "sideboard",
            ZoneCategories: DeckZoneCategories.Match));
        Assert.Equal(lower.CategoryId("sideboard"), Assert.Single(reused.CategoryAssignments).CategoryId);
        Assert.DoesNotContain(reused.Categories, value => value.Name == DeckZoneCategories.Sideboard);
    }

    /// <summary>
    /// Verifies moving to main with match removes the zone categories, and a lost primary category needs a new
    /// primary when the entry has other categories.
    /// </summary>
    [Fact]
    public async Task Patch_SideboardToMain_RequiresPrimaryWhenOtherCategoriesRemain()
    {
        using Fixture fixture = await Fixture.CreateAsync(1, zone: "sideboard", maybeboardCategories: true);
        Guid sideboard = fixture.CategoryId(DeckZoneCategories.Sideboard);
        await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            PrimaryCategoryId: sideboard,
            AddCategoryIds: [fixture.RampId]));

        OperationResult<DeckDocument> refused = await fixture.TryApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "main",
            ZoneCategories: DeckZoneCategories.Match));
        Assert.Equal("invalid-deck-change", Assert.IsType<OperationInvalidInput>(refused.Value).ReasonCode);
        Assert.Equal("sideboard", Assert.Single((await fixture.ReadAsync()).Entries).Zone);

        DeckDocument moved = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "main",
            ZoneCategories: DeckZoneCategories.Match,
            PrimaryCategoryId: fixture.RampId));
        Assert.Equal("main", Assert.Single(moved.Entries).Zone);
        Assert.Equal(
            new DeckCategoryAssignment(fixture.EntryIds[0], fixture.RampId, true),
            Assert.Single(moved.CategoryAssignments));
    }

    /// <summary>
    /// Verifies moving to main with match is accepted when the entry's zone category was its only category.
    /// </summary>
    [Fact]
    public async Task Patch_SideboardToMain_WithNoOtherCategory_Succeeds()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);
        await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "sideboard",
            ZoneCategories: DeckZoneCategories.Match));

        DeckDocument moved = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Zone: "main",
            ZoneCategories: DeckZoneCategories.Match));

        Assert.Equal("main", Assert.Single(moved.Entries).Zone);
        Assert.Empty(moved.CategoryAssignments);
    }

    /// <summary>
    /// Verifies a printing group with no card data lookup is written as given to the printing fields.
    /// </summary>
    [Fact]
    public async Task Patch_PrintingAsGiven_ReplacesThePrintingFields()
    {
        using Fixture fixture = await Fixture.CreateAsync(2);
        Guid printing = Guid.CreateVersion7();

        DeckDocument byId = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Printing: new DeckPrintingPatch(PrintingId: printing)));
        Assert.All(byId.Entries, entry => Assert.Equal(printing, entry.PrintingId));
        Assert.All(byId.Entries, entry => Assert.Null(entry.SetCode));
        Assert.All(byId.Entries, entry => Assert.Null(entry.CollectorNumber));

        DeckDocument bySet = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Printing: new DeckPrintingPatch(SetCode: "LEA", CollectorNumber: "232")));
        Assert.All(bySet.Entries, entry => Assert.Null(entry.PrintingId));
        Assert.All(bySet.Entries, entry => Assert.Equal("lea", entry.SetCode));
        Assert.All(bySet.Entries, entry => Assert.Equal("232", entry.CollectorNumber));
    }

    /// <summary>
    /// Verifies a looked-up printing replaces the given group for its entry, and language follows the printing unless
    /// the patch sets it.
    /// </summary>
    [Fact]
    public async Task Patch_ResolvedPrinting_IsWrittenForItsEntry()
    {
        using Fixture fixture = await Fixture.CreateAsync(2);
        Guid printing = Guid.CreateVersion7();
        Guid resolved = Guid.CreateVersion7();

        DeckDocument after = await fixture.ApplyAsync(new PatchDeckEntriesChange(
            fixture.EntryIds,
            Printing: new DeckPrintingPatch(PrintingId: printing),
            Printings: [new DeckEntryPrinting(fixture.EntryIds[0], resolved, "abc", "9", "de")]));

        DeckEntry first = after.Entries.Single(entry => entry.EntryId == fixture.EntryIds[0]);
        DeckEntry second = after.Entries.Single(entry => entry.EntryId == fixture.EntryIds[1]);
        Assert.Equal(resolved, first.PrintingId);
        Assert.Equal(["abc", "9", "de"], [first.SetCode, first.CollectorNumber, first.Language]);
        Assert.Equal(printing, second.PrintingId);
        Assert.Null(second.SetCode);
        Assert.Equal("en", second.Language);
    }

    /// <summary>
    /// Verifies a draft's categories are assigned with its entry, the primary one as primary.
    /// </summary>
    [Fact]
    public async Task AddEntry_WithCategoryIds_AssignsThemInOneChange()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);
        Guid draw = fixture.CategoryId("Draw");
        Guid added = Guid.CreateVersion7();

        DeckDocument after = await fixture.ApplyAsync(new AddDeckEntryChange(new DeckEntryDraft(
            1,
            "Added",
            EntryId: added,
            CategoryIds: [draw],
            PrimaryCategoryId: fixture.RampId)));

        Assert.Equal(
            [(fixture.RampId, true), (draw, false)],
            after.CategoryAssignments.Where(value => value.EntryId == added)
                .OrderByDescending(value => value.IsPrimary)
                .Select(value => (value.CategoryId, value.IsPrimary)));
    }

    /// <summary>
    /// Verifies a draft can name a category an earlier change of the same batch adds by its chosen ID.
    /// </summary>
    [Fact]
    public async Task AddEntry_WithCategoryAddedEarlierInTheBatch_AssignsIt()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);
        Guid category = Guid.CreateVersion7();
        Guid added = Guid.CreateVersion7();

        DeckDocument after = await fixture.ApplyAsync(
            new AddDeckCategoryChange(new DeckCategoryDraft("Fresh", CategoryId: category)),
            new AddDeckEntryChange(new DeckEntryDraft(1, "Added", EntryId: added, CategoryIds: [category])));

        Assert.Equal(
            new DeckCategoryAssignment(added, category, false),
            Assert.Single(after.CategoryAssignments, value => value.EntryId == added));
    }

    /// <summary>
    /// Verifies a draft naming an unknown category refuses the whole batch.
    /// </summary>
    [Fact]
    public async Task AddEntry_WithUnknownCategory_RefusesTheBatch()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);

        OperationResult<DeckDocument> result = await fixture.TryApplyAsync(
            new AddDeckEntryChange(new DeckEntryDraft(1, "Before")),
            new AddDeckEntryChange(new DeckEntryDraft(1, "Added", CategoryIds: [Guid.CreateVersion7()])));

        Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal(fixture.Deck.Entries, (await fixture.ReadAsync()).Entries);
    }

    /// <summary>
    /// Verifies a draft with match gets the zone category as its primary one, created once for several drafts.
    /// </summary>
    [Fact]
    public async Task AddEntry_WithMatch_AssignsZoneCategoryCreatedOnce()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        DeckDocument after = await fixture.ApplyAsync(
            new AddDeckEntryChange(new DeckEntryDraft(
                1,
                "One",
                Zone: "Sideboard",
                EntryId: first,
                CategoryIds: [fixture.RampId],
                ZoneCategories: "match")),
            new AddDeckEntryChange(new DeckEntryDraft(1, "Two", Zone: "sideboard", EntryId: second, ZoneCategories: "match")));

        Guid sideboard = Assert.Single(after.Categories, value => value.Name == DeckZoneCategories.Sideboard).CategoryId;
        Assert.Equal(
            [(first, fixture.RampId, false), (first, sideboard, true), (second, sideboard, true)],
            after.CategoryAssignments.Where(value => value.EntryId == first || value.EntryId == second)
                .OrderBy(value => value.EntryId == first ? 0 : 1).ThenBy(value => value.IsPrimary)
                .Select(value => (value.EntryId, value.CategoryId, value.IsPrimary)));
    }

    /// <summary>
    /// Verifies a draft that sets a primary category with match for a zone that has a zone category is refused.
    /// </summary>
    [Fact]
    public async Task AddEntry_WithMatchAndPrimary_IsRefused()
    {
        using Fixture fixture = await Fixture.CreateAsync(1);

        OperationResult<DeckDocument> result = await fixture.TryApplyAsync(new AddDeckEntryChange(new DeckEntryDraft(
            1,
            "Added",
            Zone: "sideboard",
            PrimaryCategoryId: fixture.RampId,
            ZoneCategories: "match")));

        Assert.IsType<OperationInvalidInput>(result.Value);
    }

    /// <summary>
    /// Verifies the normalizer keeps a draft's categories, primary category, and zone categories mode.
    /// </summary>
    [Fact]
    public void Normalize_Draft_KeepsCategoryFields()
    {
        Guid first = Guid.CreateVersion7();
        Guid primary = Guid.CreateVersion7();

        DeckNewEntry normalized = DeckContractValidator.Normalize(
            new DeckEntryDraft(1, "Card", CategoryIds: [first, first], PrimaryCategoryId: primary, ZoneCategories: " Match "),
            Guid.CreateVersion7);

        Assert.Equal([first], normalized.CategoryIds);
        Assert.Equal(primary, normalized.PrimaryCategoryId);
        Assert.Equal("match", normalized.ZoneCategories);
        Assert.True(normalized.HasCategories);
        Assert.False(DeckContractValidator.Normalize(new DeckEntryDraft(1, "Card"), Guid.CreateVersion7).HasCategories);
        Assert.Throws<DeckInputException>(() => DeckContractValidator.Normalize(
            new DeckEntryDraft(1, "Card", ZoneCategories: "sometimes"),
            Guid.CreateVersion7));
        Assert.Throws<DeckInputException>(() => DeckContractValidator.Normalize(
            new DeckEntryDraft(1, "Card", CategoryIds: [Guid.Empty]),
            Guid.CreateVersion7));
    }

    /// <summary>
    /// Verifies a created deck honors drafted categories, creating a zone category when asked, and refuses unknown
    /// ones instead of dropping them.
    /// </summary>
    [Fact]
    public async Task Create_WithDraftedCategories_AssignsThemOrRefuses()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        Guid ramp = Guid.CreateVersion7();
        Guid entry = Guid.CreateVersion7();
        Guid sideboardEntry = Guid.CreateVersion7();

        OperationResult<DeckDocument> created = await store.CreateAsync(
            new DeckCreateRequest(
                "Drafted",
                Entries:
                [
                    new DeckEntryDraft(1, "A", EntryId: entry, CategoryIds: [ramp], PrimaryCategoryId: ramp),
                    new DeckEntryDraft(1, "B", Zone: "sideboard", EntryId: sideboardEntry, ZoneCategories: "match"),
                ],
                Categories: [new DeckCategoryDraft("Ramp", CategoryId: ramp)]),
            TestContext.Current.CancellationToken);

        DeckDocument deck = Assert.IsType<OperationSuccess<DeckDocument>>(created.Value).Data;
        Guid sideboard = Assert.Single(deck.Categories, value => value.Name == DeckZoneCategories.Sideboard).CategoryId;
        Assert.Contains(new DeckCategoryAssignment(entry, ramp, true), deck.CategoryAssignments);
        Assert.Contains(new DeckCategoryAssignment(sideboardEntry, sideboard, true), deck.CategoryAssignments);

        OperationResult<DeckDocument> refused = await store.CreateAsync(
            new DeckCreateRequest(
                "Unknown",
                Entries: [new DeckEntryDraft(1, "A", CategoryIds: [Guid.CreateVersion7()])]),
            TestContext.Current.CancellationToken);
        Assert.IsType<OperationInvalidInput>(refused.Value);
    }

    /// <summary>
    /// Verifies the zone category definition maps zones and names in both directions, ignoring case.
    /// </summary>
    [Fact]
    public void ZoneCategories_MapZonesAndNamesBothWays()
    {
        Assert.Equal(DeckZoneCategories.Sideboard, DeckZoneCategories.NameForZone(" SIDEBOARD "));
        Assert.Equal(DeckZoneCategories.Commander, DeckZoneCategories.NameForZone("commander"));
        Assert.Null(DeckZoneCategories.NameForZone("main"));
        Assert.Equal("maybeboard", DeckZoneCategories.ZoneForName(" maybeBoard "));
        Assert.Null(DeckZoneCategories.ZoneForName("Ramp"));
        Assert.True(DeckZoneCategories.IsMode("Leave"));
        Assert.False(DeckZoneCategories.IsMode("auto"));
    }

    /// <summary>
    /// Holds a temporary store with a deck of plain entries and the categories a patch needs.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>
        /// Owns the folder behind the store.
        /// </summary>
        private readonly TemporaryDeckDirectory directory;

        /// <summary>
        /// Holds the store under test.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Creates a fixture around a store and the deck made in it.
        /// </summary>
        private Fixture(SqliteDeckStore store, DeckDocument deck, TemporaryDeckDirectory directory)
        {
            this.store = store;
            this.directory = directory;
            Deck = deck;
            EntryIds = [.. deck.Entries.OrderBy(value => value.SortOrder).Select(value => value.EntryId)];
            RampId = CategoryId("Ramp");
        }

        /// <summary>
        /// Gets the deck as created.
        /// </summary>
        internal DeckDocument Deck { get; }

        /// <summary>
        /// Gets the entry IDs in the order the entries were made.
        /// </summary>
        internal IReadOnlyList<Guid> EntryIds { get; }

        /// <summary>
        /// Gets the ID of the Ramp category.
        /// </summary>
        internal Guid RampId { get; }

        /// <summary>
        /// Creates a deck of entries in a zone, with Ramp and Draw categories, optionally with Maybeboard and
        /// Sideboard categories, extra categories, and the first entry assigned to Ramp as its primary category.
        /// </summary>
        internal static async Task<Fixture> CreateAsync(
            int entries,
            string zone = "main",
            bool assignRamp = false,
            bool maybeboardCategories = false,
            string[]? extraCategories = null)
        {
            TemporaryDeckDirectory directory = new();
            SqliteDeckStore store = new(directory.Path, "test");
            List<DeckCategoryDraft> categories = [new("Ramp"), new("Draw")];
            if (maybeboardCategories)
            {
                categories.AddRange([new DeckCategoryDraft(DeckZoneCategories.Maybeboard), new DeckCategoryDraft(DeckZoneCategories.Sideboard)]);
            }

            categories.AddRange((extraCategories ?? []).Select(name => new DeckCategoryDraft(name)));
            List<DeckCategoryDraft> withIds = [.. categories.Select(value => value with { CategoryId = Guid.CreateVersion7() })];
            List<DeckEntryDraft> drafts = [.. Enumerable.Range(0, entries).Select(index => new DeckEntryDraft(
                1,
                $"Card {index}",
                Zone: zone,
                SortOrder: index,
                EntryId: Guid.CreateVersion7()))];
            List<DeckCategoryAssignment> assignments = [];
            if (assignRamp)
            {
                assignments.Add(new DeckCategoryAssignment(drafts[0].EntryId!.Value, withIds[0].CategoryId!.Value, true));
            }

            if (maybeboardCategories)
            {
                Guid maybeboard = withIds.Single(value => value.Name == DeckZoneCategories.Maybeboard).CategoryId!.Value;
                assignments.AddRange(drafts.Select(value => new DeckCategoryAssignment(value.EntryId!.Value, maybeboard, true)));
            }

            DeckDocument created = Assert.IsType<OperationSuccess<DeckDocument>>((await store.CreateAsync(
                new DeckCreateRequest("Patch", Format: "custom", Entries: drafts, Categories: withIds, CategoryAssignments: assignments),
                TestContext.Current.CancellationToken)).Value).Data;
            DeckDocument deck = Assert.IsType<OperationSuccess<DeckDocument>>(
                (await store.GetAsync(created.DeckId, TestContext.Current.CancellationToken)).Value).Data;
            return new Fixture(store, deck, directory);
        }

        /// <summary>
        /// Gives the ID of the category with a name in the created deck.
        /// </summary>
        internal Guid CategoryId(string name)
        {
            return Deck.Categories.Single(value => value.Name == name).CategoryId;
        }

        /// <summary>
        /// Applies a batch to the deck at its current revision and requires success.
        /// </summary>
        internal async Task<DeckDocument> ApplyAsync(params DeckChange[] changes)
        {
            return Assert.IsType<OperationSuccess<DeckDocument>>((await TryApplyAsync(changes)).Value).Data;
        }

        /// <summary>
        /// Applies a batch to the deck at its current revision and returns the outcome.
        /// </summary>
        internal async Task<OperationResult<DeckDocument>> TryApplyAsync(params DeckChange[] changes)
        {
            DeckDocument current = await ReadAsync();
            return await store.ApplyChangesAsync(
                Deck.DeckId,
                current.Revision,
                changes,
                TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// Reads the deck as it is stored now.
        /// </summary>
        internal async Task<DeckDocument> ReadAsync()
        {
            return Assert.IsType<OperationSuccess<DeckDocument>>(
                (await store.GetAsync(Deck.DeckId, TestContext.Current.CancellationToken)).Value).Data;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            store.Dispose();
            directory.Dispose();
        }
    }
}
