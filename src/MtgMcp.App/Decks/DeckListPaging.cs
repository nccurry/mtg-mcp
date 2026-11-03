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
    /// Reads the page of decks that the cursor names and returns it with the cursor of the next page.
    /// </summary>
    internal static async Task<OperationResult<DeckPage>> PageAsync(
        SqliteDeckStore store,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        OperationResult<string> fingerprint = await store.GetListFingerprintAsync(cancellationToken)
            .ConfigureAwait(false);
        if (fingerprint is not OperationSuccess<string> list)
        {
            return OperationFailure.Forward<string, DeckPage>(fingerprint);
        }

        PageCursorBinding binding = PageCursorBinding.ForSource(list.Data);
        OperationResult<PageCursorPosition> start = PageCursor.Decode(cursor, binding);
        if (start is not OperationSuccess<PageCursorPosition> position)
        {
            return OperationFailure.Forward<PageCursorPosition, DeckPage>(start);
        }

        OperationResult<DeckListSlice> read = await store.ListAsync(position.Data.Offset, pageSize, cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<DeckListSlice> slice)
        {
            return OperationFailure.Forward<DeckListSlice, DeckPage>(read);
        }

        List<DeckSummary> summaries = [.. slice.Data.Items.Select(ShortDescription)];
        ResultPageSlice kept = ResultPage.Take(summaries, 0, ResultSize.Measure(new DeckPage([], null)));
        string? next = kept.More || slice.Data.HasMore
            ? PageCursor.Encode(PageCursorPosition.AtOffset(position.Data.Offset + kept.Count), binding)
            : null;
        return new OperationSuccess<DeckPage>(new DeckPage(kept.Rows(summaries), next));
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
