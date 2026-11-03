using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the local deck list pages by a cursor bound to the deck list: it reaches every deck once, ends with a null
/// cursor, and goes stale when a deck is created, edited, or deleted between pages.
/// </summary>
public sealed class DeckListPagingTests
{
    /// <summary>
    /// Verifies a list of five decks read two at a time comes back in order with a null cursor on the last page.
    /// </summary>
    [Fact]
    public async Task PageAsync_WalksEveryDeck_AndTheLastPageHasNoCursor()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        await CreateDecksAsync(store, 5);

        DeckPage first = Success(await DeckListPaging.PageAsync(store, null, 2, token));
        DeckPage second = Success(await DeckListPaging.PageAsync(store, first.NextCursor, 2, token));
        DeckPage last = Success(await DeckListPaging.PageAsync(store, second.NextCursor, 2, token));

        Assert.Equal(["Deck 0", "Deck 1"], first.Items.Select(value => value.Name));
        Assert.Equal(["Deck 2", "Deck 3"], second.Items.Select(value => value.Name));
        Assert.Equal(["Deck 4"], last.Items.Select(value => value.Name));
        Assert.NotNull(second.NextCursor);
        Assert.Null(last.NextCursor);
    }

    /// <summary>
    /// Verifies a deck created, renamed, or deleted between pages makes the cursor stale, and an edited cursor or a
    /// page size outside 1 through 100 is refused with the argument path.
    /// </summary>
    [Fact]
    public async Task PageAsync_AfterTheListChanged_RefusesTheCursor()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        List<DeckDocument> decks = await CreateDecksAsync(store, 3);
        string cursor = Success(await DeckListPaging.PageAsync(store, null, 1, token)).NextCursor!;

        OperationInvalidInput edited = Assert.IsType<OperationInvalidInput>(
            (await DeckListPaging.PageAsync(store, cursor + "x", 1, token)).Value);
        OperationInvalidInput size = Assert.IsType<OperationInvalidInput>(
            (await DeckListPaging.PageAsync(store, null, 101, token)).Value);
        _ = Success(await DeckListPaging.PageAsync(store, cursor, 1, token));
        _ = RequireSuccess(await store.ApplyChangesAsync(
            decks[2].DeckId,
            decks[2].Revision,
            [new UpdateDeckMetadataChange("Renamed", null, "custom")],
            token));
        OperationConflict renamed = Assert.IsType<OperationConflict>(
            (await DeckListPaging.PageAsync(store, cursor, 1, token)).Value);
        _ = RequireSuccess(await store.CreateAsync(new DeckCreateRequest("Added"), token));
        OperationConflict created = Assert.IsType<OperationConflict>(
            (await DeckListPaging.PageAsync(store, cursor, 1, token)).Value);

        Assert.Equal(("invalid-cursor", "cursor"), (edited.ReasonCode, edited.Path));
        Assert.Equal("pageSize", size.Path);
        Assert.Equal("cursor-stale", renamed.ReasonCode);
        Assert.Equal("cursor-stale", created.ReasonCode);
    }

    /// <summary>
    /// Verifies a long description is cut and flagged, so one deck cannot push the page over the size limit.
    /// </summary>
    [Fact]
    public async Task PageAsync_WithALongDescription_CutsAndFlagsIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        _ = RequireSuccess(await store.CreateAsync(new DeckCreateRequest("Long", new string('x', 100_000)), token));

        DeckSummary summary = Assert.Single(Success(await DeckListPaging.PageAsync(store, null, 50, token)).Items);

        Assert.True(summary.Description.Length < 100_000);
        Assert.True(summary.DescriptionTruncated);
        Assert.Equal(100_000, summary.DescriptionLength);
    }

    /// <summary>
    /// Creates decks named Deck 0 up to Deck count minus one.
    /// </summary>
    private static async Task<List<DeckDocument>> CreateDecksAsync(SqliteDeckStore store, int count)
    {
        List<DeckDocument> decks = [];
        for (int index = 0; index < count; index++)
        {
            decks.Add(RequireSuccess(await store.CreateAsync(
                new DeckCreateRequest($"Deck {index}"),
                TestContext.Current.CancellationToken)));
        }

        return decks;
    }

    /// <summary>
    /// Extracts a page or fails with the actual result case.
    /// </summary>
    private static DeckPage Success(OperationResult<DeckPage> result)
    {
        return RequireSuccess(result);
    }

    /// <summary>
    /// Extracts successful data or fails with the actual result case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
