using System.Text.Json;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the parts of an import preview page that come before its entries: the name and description, the categories,
/// and the diagnostics.
/// </summary>
public sealed class DeckImportHeaderTests
{
    /// <summary>
    /// Verifies an import of 300 entries with a 60,000-character description and a long name pages under the size
    /// limit: the name is cut on every page, and the description is cut on the first page and empty after it.
    /// </summary>
    [Fact]
    public void PageImport_LongDescriptionAndName_AreCutSoEveryPageStaysUnderTheLimit()
    {
        DeckImportPreview preview = Preview(300, 0, 0, new string('n', 5_000), new string('d', 60_000));
        List<DeckImportPreviewView> pages = ReadEveryPage(preview);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Equal(ResultText.MaximumNameBytes, page.Proposal!.Name.Length));
        Assert.All(pages, page => Assert.True(page.Proposal!.NameTruncated));
        Assert.Equal(ResultText.MaximumDescriptionBytes, pages[0].Proposal!.Description.Length);
        Assert.Equal(60_000, pages[0].Proposal!.DescriptionLength);
        Assert.All(pages.Skip(1), page => Assert.Equal(string.Empty, page.Proposal!.Description));
        Assert.All(pages, page => Assert.True(page.Proposal!.DescriptionTruncated));
    }

    /// <summary>
    /// Verifies the first page fills the shared allowance with the categories before the diagnostics, so a long list
    /// of diagnostics never hides the categories the paged assignments refer to.
    /// </summary>
    [Fact]
    public void PageImport_ManyDiagnosticsAndSomeCategories_ShowsEveryCategory()
    {
        DeckImportPreview preview = Preview(20, 300, 12, "Deck", "Text");

        DeckImportPreviewView first = ReadEveryPage(preview)[0];

        Assert.Equal(12, first.Proposal!.Categories.Count);
        Assert.Equal(12, first.CategoryCount);
        Assert.True(first.Diagnostics.Count < 300);
        Assert.Equal(300, first.DiagnosticCount);
        Assert.True(first.HeaderListsCut);
    }

    /// <summary>
    /// Pages through a whole import preview by its next offsets and checks every page against the size limit.
    /// </summary>
    private static List<DeckImportPreviewView> ReadEveryPage(DeckImportPreview preview)
    {
        List<DeckImportPreviewView> pages = [];
        int offset = 0;
        while (true)
        {
            OperationResult<DeckImportPreviewView> result = DeckPreviewPaging.PageImport(
                preview,
                offset,
                pages.Count == 0 ? null : preview.Fingerprint);
            DeckImportPreviewView page = Assert.IsType<OperationSuccess<DeckImportPreviewView>>(result.Value).Data;
            int bytes = JsonSerializer.SerializeToUtf8Bytes(page, ToolCallPolicy.SerializerOptions).Length;
            Assert.True(bytes <= ResultSizeLimit.MaximumBytes, $"page {pages.Count} has {bytes} bytes");
            pages.Add(page);
            if (page.NextOffset is not int next)
            {
                return pages;
            }

            offset = next;
        }
    }

    /// <summary>
    /// Builds an import preview with the given number of entries, diagnostics, and categories.
    /// </summary>
    private static DeckImportPreview Preview(
        int entries,
        int diagnostics,
        int categories,
        string name,
        string description)
    {
        DeckImportProposal proposal = new(
            null,
            null,
            null,
            null,
            name,
            description,
            "custom",
            [.. Enumerable.Range(0, entries).Select(index => new DeckEntry(
                Guid.CreateVersion7(),
                1,
                $"Card {index:000}",
                null,
                null,
                null,
                null,
                "en",
                "nonfoil",
                "main",
                index))],
            [.. Enumerable.Range(0, categories).Select(index => new DeckCategory(
                Guid.CreateVersion7(), $"Category {index}", null, index))],
            [],
            [],
            []);
        return new DeckImportPreview(
            "mtg-mcp-json-v1",
            "complete",
            "import-fingerprint",
            proposal,
            [.. Enumerable.Range(0, diagnostics).Select(index => new DeckInterchangeDiagnostic(
                "warning",
                "parse-warning",
                $"Line {index} could not be read exactly, so the nearest card was used and the rest was ignored.",
                index))],
            0,
            []);
    }
}
