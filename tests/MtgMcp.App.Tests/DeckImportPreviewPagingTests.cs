using System.Text.Json;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks every page of an import preview stays under the size target even when the import carries many categories,
/// diagnostics, unresolved identities, provider bindings, and a large saved synchronization snapshot.
/// </summary>
public sealed class DeckImportPreviewPagingTests
{
    /// <summary>
    /// Holds the number of bytes every page must stay under.
    /// </summary>
    private const int ResultSizeTarget = 40_000;

    /// <summary>
    /// Verifies a heavy native-style import pages under 40,000 bytes on every page, the first page cuts its header
    /// lists with exact counts beside them, the snapshots are counted and never returned, and every entry and every
    /// category assignment is reached exactly once.
    /// </summary>
    [Fact]
    public void Page_WithAHeavyImport_StaysUnderTheTargetOnEveryPage()
    {
        DeckImportPreview preview = HeavyPreview();
        List<DeckImportPreviewView> pages = [];
        int offset = 0;

        while (true)
        {
            OperationResult<DeckImportPreviewView> result = DeckPreviewPaging.PageImport(preview, offset, null);
            DeckImportPreviewView page = Assert.IsType<OperationSuccess<DeckImportPreviewView>>(result.Value).Data;
            Assert.True(
                ResultSize.Measure(result) < ResultSizeTarget,
                $"page at {offset} takes {ResultSize.Measure(result)} bytes");
            pages.Add(page);
            if (page.NextOffset is not int next)
            {
                break;
            }

            offset = next;
        }

        DeckImportPreviewView first = pages[0];
        List<DeckEntry> entries = [.. pages.SelectMany(page => page.Proposal!.Entries)];
        List<DeckCategoryAssignment> assignments = [.. pages.SelectMany(page => page.Proposal!.CategoryAssignments)];
        Assert.True(pages.Count > 1);
        Assert.Equal(preview.Proposal!.Entries.Select(value => value.EntryId), entries.Select(value => value.EntryId));
        Assert.Equal(preview.Proposal.CategoryAssignments.Count, assignments.Count);
        Assert.Equal(assignments.Count, assignments.Distinct().Count());
        Assert.All(pages, page => Assert.Equal(300, page.EntryCount));
        Assert.True(first.HeaderListsCut);
        Assert.Equal(400, first.CategoryCount);
        Assert.True(first.Proposal!.Categories.Count < 400);
        Assert.Equal(200, first.DiagnosticCount);
        Assert.Equal(300, first.UnresolvedIdentityCount);
        Assert.Equal(5, first.ProviderBindingCount);
        Assert.Equal(3, first.Proposal.SyncBaselineCount);
        Assert.All(pages.Skip(1), page => Assert.Empty(page.Proposal!.Categories));
        Assert.All(pages.Skip(1), page => Assert.Empty(page.Diagnostics));
        Assert.DoesNotContain(
            "canonical-snapshot-body",
            JsonSerializer.Serialize(pages, ToolCallPolicy.SerializerOptions),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a small import is returned whole in one page, with no cut flag and every list in full.
    /// </summary>
    [Fact]
    public void Page_WithASmallImport_ReturnsEverythingInOnePage()
    {
        DeckEntry entry = Entry(0);
        DeckCategory category = new(Guid.NewGuid(), "Ramp", null, 0);
        DeckImportPreview preview = new(
            "generic-text-v1",
            "complete",
            "fingerprint",
            Proposal(
                [entry],
                [category],
                [new DeckCategoryAssignment(entry.EntryId, category.CategoryId, true)],
                [],
                []),
            [],
            0,
            []);

        DeckImportPreviewView page = Assert.IsType<OperationSuccess<DeckImportPreviewView>>(
            DeckPreviewPaging.PageImport(preview, 0, null).Value).Data;

        Assert.Null(page.Truncated);
        Assert.Null(page.NextOffset);
        Assert.Null(page.HeaderListsCut);
        Assert.Single(page.Proposal!.Entries);
        Assert.Single(page.Proposal.Categories);
        Assert.Single(page.Proposal.CategoryAssignments);
    }

    /// <summary>
    /// Builds an import of 300 entries with 400 categories, a heavy synchronization snapshot, and full-length lists of
    /// diagnostics and unresolved identities.
    /// </summary>
    private static DeckImportPreview HeavyPreview()
    {
        List<DeckEntry> entries = [.. Enumerable.Range(0, 300).Select(Entry)];
        List<DeckCategory> categories =
            [.. Enumerable.Range(0, 400).Select(
                index => new DeckCategory(Guid.NewGuid(), $"Category number {index:000}", "#aabbcc", index))];
        List<DeckCategoryAssignment> assignments = [];
        for (int index = 0; index < entries.Count; index++)
        {
            assignments.Add(new DeckCategoryAssignment(entries[index].EntryId, categories[index].CategoryId, true));
            assignments.Add(
                new DeckCategoryAssignment(entries[index].EntryId, categories[index + 100].CategoryId, false));
        }

        List<DeckProviderBinding> bindings =
            [.. Enumerable.Range(0, 5).Select(index => new DeckProviderBinding(
                Guid.NewGuid(),
                "archidekt",
                $"remote-{index}",
                $"https://archidekt.example/decks/{index}",
                "version",
                "fingerprint",
                null,
                null))];
        List<DeckSyncBaseline> baselines =
            [.. bindings.Take(3).Select(value => new DeckSyncBaseline(
                value.BindingId,
                "canonical-snapshot-body" + new string('x', 200_000)))];
        List<DeckInterchangeDiagnostic> diagnostics =
            [.. Enumerable.Range(0, 200).Select(index => new DeckInterchangeDiagnostic(
                "warning",
                "unknown-field",
                $"The field number {index} is not part of the native format and was kept as an extension only.",
                index + 1,
                "$.entries[" + index + "]"))];
        List<string> unresolved = [.. Enumerable.Range(
            0,
            300).Select(index => $"Budget Card {index:000} (tst) {index}")];
        return new DeckImportPreview(
            "mtg-mcp-json-v1",
            "complete",
            "fingerprint",
            Proposal(entries, categories, assignments, bindings, baselines),
            diagnostics,
            0,
            unresolved);
    }

    /// <summary>
    /// Builds a proposal around the given lists.
    /// </summary>
    private static DeckImportProposal Proposal(
        List<DeckEntry> entries,
        List<DeckCategory> categories,
        List<DeckCategoryAssignment> assignments,
        List<DeckProviderBinding> bindings,
        List<DeckSyncBaseline> baselines)
    {
        return new DeckImportProposal(
            Guid.NewGuid(),
            3,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "Imported",
            string.Empty,
            "commander",
            entries,
            categories,
            assignments,
            bindings,
            baselines);
    }

    /// <summary>
    /// Builds one fully identified entry.
    /// </summary>
    private static DeckEntry Entry(int index)
    {
        return new DeckEntry(
            Guid.NewGuid(),
            1,
            $"Budget Card {index:000}",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tst",
            index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "en",
            "nonfoil",
            "main",
            index);
    }
}
