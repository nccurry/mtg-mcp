using MtgMcp.Core.Decks;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies provider-neutral deck records and the closed local mutation vocabulary.
/// </summary>
public sealed class DeckContractTests
{
    /// <summary>
    /// Verifies collection-bearing contracts detach from mutable caller-owned lists.
    /// </summary>
    [Fact]
    public void CollectionContracts_CopyCallerOwnedLists()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeckSummary summary = new(Guid.CreateVersion7(), "Deck", string.Empty, "custom", 1, now, now);
        DeckEntryDraft draft = new(1, "Card");
        DeckEntry entry = new(
            Guid.CreateVersion7(), 1, "Card", null, null, null, null, "en", "nonfoil", "main", 0);
        DeckCategory category = new(Guid.CreateVersion7(), "Category", null, 0);
        DeckCategoryAssignment assignment = new(entry.EntryId, category.CategoryId, true);
        DeckProviderBinding binding = new(
            Guid.CreateVersion7(), "provider", "remote", null, null, null, null, null);
        DeckValidationIssue issue = new("example", "Example issue.");
        DeckBackup backup = new(Guid.CreateVersion7(), 1, "v1:hash", now, 1, false);
        List<DeckSummary> summaries = [summary];
        List<DeckEntryDraft> drafts = [draft];
        List<DeckEntry> entries = [entry];
        List<DeckCategory> categories = [category];
        List<DeckCategoryAssignment> assignments = [assignment];
        List<DeckProviderBinding> bindings = [binding];
        List<DeckValidationIssue> issues = [issue];
        List<DeckBackup> backups = [backup];

        DeckPage page = new(summaries, null);
        DeckCreateRequest request = new(
            "Deck",
            Entries: drafts,
            Categories: [new DeckCategoryDraft("Category")],
            CategoryAssignments: assignments,
            ProviderBindings: bindings);
        DeckDocument document = new(
            summary.DeckId,
            summary.Name,
            summary.Description,
            summary.Format,
            summary.Revision,
            now,
            now,
            entries,
            categories,
            assignments,
            bindings);
        DeckValidationReport report = new(summary.DeckId, 1, false, issues);
        DeckBackupPage backupPage = new("v1:current", backups);

        summaries.Clear();
        drafts.Clear();
        entries.Clear();
        categories.Clear();
        assignments.Clear();
        bindings.Clear();
        issues.Clear();
        backups.Clear();

        Assert.Single(page.Items);
        Assert.Single(request.Entries!);
        Assert.Single(request.Categories!);
        Assert.Single(request.CategoryAssignments!);
        Assert.Single(request.ProviderBindings!);
        Assert.Single(document.Entries);
        Assert.Single(document.Categories);
        Assert.Single(document.CategoryAssignments);
        Assert.Single(document.ProviderBindings);
        Assert.Single(report.Issues);
        Assert.Single(backupPage.Items);
    }

    /// <summary>
    /// Verifies unresolved and fully identified entries remain independently addressable.
    /// </summary>
    [Fact]
    public void DeckEntries_PreserveExplicitIdentityAndExtensibleVocabulary()
    {
        Guid firstId = Guid.CreateVersion7();
        Guid secondId = Guid.CreateVersion7();
        Guid oracleId = Guid.CreateVersion7();
        Guid printingId = Guid.CreateVersion7();
        DeckEntry unresolved = new(
            firstId, 1, "Unknown Card", null, null, null, null, "en", "nonfoil", "main", 0);
        DeckEntry identified = new(
            secondId,
            1,
            "Known Card",
            oracleId,
            printingId,
            "tst",
            "42",
            "jp",
            "etched",
            "custom-zone",
            1);

        Assert.Null(unresolved.OracleId);
        Assert.Null(unresolved.PrintingId);
        Assert.Equal(oracleId, identified.OracleId);
        Assert.Equal(printingId, identified.PrintingId);
        Assert.Equal("custom-zone", identified.Zone);
        Assert.NotEqual(unresolved.EntryId, identified.EntryId);
    }

    /// <summary>
    /// Exhaustively identifies every mutation case so future additions require a test update.
    /// </summary>
    [Fact]
    public void DeckChanges_ExposeEveryClosedCase()
    {
        Guid id = Guid.CreateVersion7();
        DeckEntry entry = new(id, 1, "Card", null, null, null, null, "en", "nonfoil", "main", 0);
        DeckCategory category = new(id, "Ramp", null, 0);
        DeckProviderBinding binding = new(
            id, "archidekt", "42", null, null, null, null, null);
        DeckChange[] changes =
        [
            new UpdateDeckMetadataChange("Deck", null, "commander"),
            new AddDeckEntryChange(new DeckEntryDraft(1, "Card")),
            new UpdateDeckEntryChange(entry),
            new RemoveDeckEntryChange(id),
            new AddDeckCategoryChange(new DeckCategoryDraft("Ramp")),
            new UpdateDeckCategoryChange(category),
            new RemoveDeckCategoryChange(id),
            new AssignDeckCategoryChange(id, id, true),
            new UnassignDeckCategoryChange(id, id),
            new UpsertDeckProviderBindingChange(binding, "{}"),
            new RemoveDeckProviderBindingChange(id),
        ];

        Assert.Equal(
            [
                "metadata",
                "entry-add",
                "entry-update",
                "entry-remove",
                "category-add",
                "category-update",
                "category-remove",
                "category-assign",
                "category-unassign",
                "binding-upsert",
                "binding-remove",
            ],
            changes.Select(Describe).ToArray());
    }

    /// <summary>
    /// Maps each closed change case to its stable test description.
    /// </summary>
    private static string Describe(DeckChange change)
    {
        return change switch
        {
            UpdateDeckMetadataChange => "metadata",
            AddDeckEntryChange => "entry-add",
            UpdateDeckEntryChange => "entry-update",
            RemoveDeckEntryChange => "entry-remove",
            AddDeckCategoryChange => "category-add",
            UpdateDeckCategoryChange => "category-update",
            RemoveDeckCategoryChange => "category-remove",
            AssignDeckCategoryChange => "category-assign",
            UnassignDeckCategoryChange => "category-unassign",
            UpsertDeckProviderBindingChange => "binding-upsert",
            RemoveDeckProviderBindingChange => "binding-remove",
        };
    }
}
