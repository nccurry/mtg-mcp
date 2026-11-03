using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Pages the local deck list under the shared cursor rule: the cursor binds the list as it is, so a deck created,
/// edited, or deleted between pages makes the cursor stale, and a page stops early at the result size limit.
/// </summary>
internal static class DeckListPaging
{
    /// <summary>
    /// Names the tool whose cursors this paging issues, so a cursor from another tool is refused.
    /// </summary>
    private const string ToolName = "local_deck_list";

    /// <summary>
    /// Reads the page of decks that the cursor names and returns it with the cursor of the next page.
    /// </summary>
    internal static async Task<OperationResult<DeckPage>> PageAsync(
        SqliteDeckStore store,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        OperationResult<DeckListSlice> read = await store.ListAsync(
            fingerprint =>
            {
                OperationResult<PageCursorPosition> start = PageCursor.Decode(cursor, Bind(fingerprint));
                if (start is not OperationSuccess<PageCursorPosition> position)
                {
                    return OperationFailure.Forward<PageCursorPosition, int>(start);
                }

                offset = position.Data.Offset;
                return new OperationSuccess<int>(offset);
            },
            pageSize,
            cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<DeckListSlice> slice)
        {
            return OperationFailure.Forward<DeckListSlice, DeckPage>(read);
        }

        List<DeckSummary> summaries = [.. slice.Data.Items.Select(ShortDescription)];
        ResultPageSlice kept = ResultPage.Take(summaries, 0, ResultSize.Measure(new DeckPage([], null)));
        string? next = kept.More || slice.Data.HasMore
            ? PageCursor.Encode(PageCursorPosition.AtOffset(offset + kept.Count), Bind(slice.Data.ListFingerprint))
            : null;
        return new OperationSuccess<DeckPage>(new DeckPage(kept.Rows(summaries), next));
    }

    /// <summary>
    /// Binds a cursor to this tool and to the fingerprint of the list it was issued for.
    /// </summary>
    private static PageCursorBinding Bind(string listFingerprint)
    {
        return PageCursorBinding.ForSource(listFingerprint)
            .And(PageCursorBinding.ForRequest(RequestHash.Of((ToolName, typeof(string)))));
    }

    /// <summary>
    /// Cuts the description of a summary to the length a list page may show.
    /// </summary>
    private static DeckSummary ShortDescription(DeckSummary summary)
    {
        ShownText description = ResultText.Description(summary.Description, firstPage: true);
        return summary with
        {
            Description = description.Text,
            DescriptionTruncated = description.TruncatedOrNull,
            DescriptionLength = description.LengthIfTruncated,
        };
    }
}
