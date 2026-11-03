using System.Security.Cryptography;
using System.Text;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks how an export artifact is cut into chunks: characters outside the basic plane are never split, the chunk
/// limit counts the bytes the server writes (escapes included), and the chunks joined in order are the whole file.
/// </summary>
public sealed class DeckExportPagingTests
{
    /// <summary>
    /// Holds the character a test uses for a pair of UTF-16 code units: it takes two characters of the content.
    /// </summary>
    private const string Emoji = "\U0001F600";

    /// <summary>
    /// Verifies a character outside the basic plane that starts at index 29,999 moves whole to the next chunk, and
    /// the chunks joined in order match the length and SHA-256 of the whole content.
    /// </summary>
    [Fact]
    public void Page_WithAnEmojiAtTheChunkEdge_KeepsThePairWhole()
    {
        string content = new string('a', 29_999) + Emoji + new string('b', 100);

        List<DeckExportChunk> chunks = ReadAllChunks(Bundle(content));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(29_999, chunks[0].Content.Length);
        Assert.Equal(29_999, chunks[1].Offset);
        Assert.StartsWith(Emoji, chunks[1].Content, StringComparison.Ordinal);
        AssertJoinMatches(content, chunks);
    }

    /// <summary>
    /// Verifies a page size of one code unit on a pair returns the whole pair, and the next cursor starts after it.
    /// </summary>
    [Fact]
    public void Page_WithPageSizeOneOnAPair_ReturnsTheWholePair()
    {
        DeckExportBundle bundle = Bundle(Emoji + "ab");

        DeckExportBundleView first = Success(DeckExportPaging.Page(bundle, null, null, 1));
        DeckExportBundleView second = Success(DeckExportPaging.Page(bundle, null, first.NextCursor, 1));

        Assert.Equal(Emoji, first.Chunk.Content);
        Assert.Equal("a", second.Chunk.Content);
        Assert.Equal(2, second.Chunk.Offset);
    }

    /// <summary>
    /// Verifies a page size of N returns N UTF-16 code units of the content and a cursor at offset N.
    /// </summary>
    [Fact]
    public void Page_PageSizeCountsCodeUnits()
    {
        DeckExportBundle bundle = Bundle(new string('a', 100));

        DeckExportBundleView first = Success(DeckExportPaging.Page(bundle, null, null, 37));
        DeckExportBundleView second = Success(DeckExportPaging.Page(bundle, null, first.NextCursor, 37));

        Assert.Equal(37, first.Chunk.Content.Length);
        Assert.Equal(37, second.Chunk.Offset);
        Assert.Equal(37, second.Chunk.Content.Length);
    }
    /// <summary>
    /// Verifies a cursor is refused after the artifact content or the deck revision changed, as stale with the current
    /// revision, and for another artifact or a changed cursor as an invalid cursor at the cursor path.
    /// </summary>
    [Fact]
    public void Page_WithACursorOfOtherState_IsRefused()
    {
        DeckExportBundle bundle = Bundle(new string('a', 70_000));
        string cursor = Success(DeckExportPaging.Page(bundle, null, null, 30_000)).NextCursor!;

        OperationConflict stale = Assert.IsType<OperationConflict>(
            DeckExportPaging.Page(Bundle(new string('b', 70_000), bundle.DeckId, 1), null, cursor, 30_000).Value);
        OperationConflict newRevision = Assert.IsType<OperationConflict>(
            DeckExportPaging.Page(bundle with { DeckRevision = 2 }, null, cursor, 30_000).Value);
        OperationInvalidInput edited = Assert.IsType<OperationInvalidInput>(
            DeckExportPaging.Page(bundle, null, cursor + "x", 30_000).Value);
        OperationInvalidInput badSize = Assert.IsType<OperationInvalidInput>(
            DeckExportPaging.Page(bundle, null, null, 30_001).Value);

        Assert.Equal("cursor-stale", stale.ReasonCode);
        Assert.Equal("cursor-stale", newRevision.ReasonCode);
        Assert.Equal(2, newRevision.Current?.Revision);
        Assert.Equal(("invalid-cursor", "cursor"), (edited.ReasonCode, edited.Path));
        Assert.Equal("pageSize", badSize.Path);
    }

    /// <summary>
    /// Verifies content made only of characters outside the basic plane is cut on pair edges and joins back exactly.
    /// </summary>
    [Fact]
    public void Page_WithOnlyPairs_NeverSplitsOne()
    {
        string content = string.Concat(Enumerable.Repeat(Emoji, 20_000));

        List<DeckExportChunk> chunks = ReadAllChunks(Bundle(content));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => AssertNoLoneSurrogate(chunk.Content));
        AssertJoinMatches(content, chunks);
    }

    /// <summary>
    /// Verifies content that JSON escapes heavily is cut by the bytes the server writes, with the real serializer: a
    /// chunk of quotes, angle brackets, or control characters never takes more than the chunk limit, and the chunks
    /// join back to the content.
    /// </summary>
    [Theory]
    [InlineData('"')]
    [InlineData('<')]
    [InlineData('\u0001')]
    [InlineData('\\')]
    public void Page_WithHeavilyEscapedContent_StaysUnderTheChunkLimit(char repeated)
    {
        string content = new(repeated, 70_000);

        List<DeckExportChunk> chunks = ReadAllChunks(Bundle(content));

        Assert.True(chunks.Count > 2);
        foreach (DeckExportChunk chunk in chunks)
        {
            // The measure of a string includes its two quotation marks.
            Assert.True(
                ResultSize.Measure(chunk.Content) - 2 <= DeckExportPaging.MaximumChunkBytes,
                $"The chunk at {chunk.Offset} takes {ResultSize.Measure(chunk.Content) - 2} bytes.");
        }

        AssertJoinMatches(content, chunks);
    }

    /// <summary>
    /// Verifies content made of three-byte characters is cut by bytes, not by characters, so a chunk stays under the
    /// byte limit.
    /// </summary>
    [Fact]
    public void Page_WithWideCharacters_CountsBytesNotCharacters()
    {
        string content = new('中', 40_000);

        List<DeckExportChunk> chunks = ReadAllChunks(Bundle(content));

        Assert.All(
            chunks,
            chunk => Assert.True(ResultSize.Measure(chunk.Content) - 2 <= DeckExportPaging.MaximumChunkBytes));
        AssertJoinMatches(content, chunks);
    }

    /// <summary>
    /// Verifies the first chunk carries the preservation report, a later chunk does not, the chunk names how to read
    /// the rest, and the last chunk has no next cursor.
    /// </summary>
    [Fact]
    public void Page_PreservationReportComesOnlyWithTheFirstChunk()
    {
        DeckExportBundle bundle = Bundle(new string('a', 70_000));

        DeckExportBundleView first = Assert.IsType<OperationSuccess<DeckExportBundleView>>(
            DeckExportPaging.Page(bundle, null, null, 30_000).Value).Data;
        DeckExportBundleView second = Assert.IsType<OperationSuccess<DeckExportBundleView>>(
            DeckExportPaging.Page(bundle, null, first.NextCursor, 30_000).Value).Data;
        DeckExportBundleView last = Success(DeckExportPaging.Page(bundle, null, second.NextCursor, 30_000));

        Assert.NotEmpty(first.Preservation);
        Assert.Empty(second.Preservation);
        Assert.True(first.Chunk.Truncated);
        Assert.Contains("nextCursor", first.Chunk.Narrow, StringComparison.Ordinal);
        Assert.NotNull(first.NextCursor);
        Assert.Null(last.NextCursor);
        Assert.Null(last.Chunk.Truncated);
        Assert.Equal(70_000, first.Artifacts[0].TotalLength);
    }

    /// <summary>
    /// Holds the deck every test bundle belongs to.
    /// </summary>
    private static readonly Guid DeckId = Guid.NewGuid();

    /// <summary>
    /// Unwraps a successful chunk page.
    /// </summary>
    private static DeckExportBundleView Success(OperationResult<DeckExportBundleView> result)
    {
        return Assert.IsType<OperationSuccess<DeckExportBundleView>>(result.Value).Data;
    }

    /// <summary>
    /// Builds a bundle with one artifact holding the content and a one-line preservation report.
    /// </summary>
    private static DeckExportBundle Bundle(string content, Guid? deckId = null, long revision = 1)
    {
        return new DeckExportBundle(
            1,
            "generic-text-v1",
            deckId ?? DeckId,
            revision,
            DateTimeOffset.UnixEpoch,
            "available",
            [new DeckExportArtifact("deck.txt", "text/plain", content, Sha256Of(content), "The deck.")],
            [new DeckFieldPreservation("zone", "preserved", "Kept.")]);
    }

    /// <summary>
    /// Reads every chunk of the first artifact by following the next cursor.
    /// </summary>
    private static List<DeckExportChunk> ReadAllChunks(DeckExportBundle bundle)
    {
        List<DeckExportChunk> chunks = [];
        string? cursor = null;
        while (true)
        {
            DeckExportBundleView view = Success(DeckExportPaging.Page(bundle, null, cursor, 30_000));
            chunks.Add(view.Chunk);
            if (view.NextCursor is null)
            {
                return chunks;
            }

            cursor = view.NextCursor;
        }
    }

    /// <summary>
    /// Checks the chunks start where the last one ended, and that joined they have the content's length and SHA-256.
    /// </summary>
    private static void AssertJoinMatches(string content, List<DeckExportChunk> chunks)
    {
        StringBuilder joined = new();
        foreach (DeckExportChunk chunk in chunks)
        {
            Assert.Equal(joined.Length, chunk.Offset);
            joined.Append(chunk.Content);
        }

        Assert.Equal(content.Length, joined.Length);
        Assert.Equal(Sha256Of(content), Sha256Of(joined.ToString()));
    }

    /// <summary>
    /// Checks no high surrogate lacks its low surrogate and no low surrogate lacks its high one.
    /// </summary>
    private static void AssertNoLoneSurrogate(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                Assert.True(index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]));
                index++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(text[index]));
            }
        }
    }

    /// <summary>
    /// Computes the lowercase hexadecimal SHA-256 of the text as UTF-8.
    /// </summary>
    private static string Sha256Of(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
