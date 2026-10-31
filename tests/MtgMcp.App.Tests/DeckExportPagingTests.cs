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
    /// Verifies an offset between the two halves of a pair is refused as an invalid offset, and an offset on the
    /// whole pair is accepted.
    /// </summary>
    [Fact]
    public void Page_WithAnOffsetInsideAPair_IsRefused()
    {
        DeckExportBundle bundle = Bundle(new string('a', 29_999) + Emoji + new string('b', 100));

        OperationResult<DeckExportBundleView> inside = DeckExportPaging.Page(bundle, null, 30_000);
        OperationResult<DeckExportBundleView> onPair = DeckExportPaging.Page(bundle, null, 29_999);

        OperationInvalidInput refused = Assert.IsType<OperationInvalidInput>(inside.Value);
        Assert.Equal("invalid-offset", refused.ReasonCode);
        Assert.Equal("offset", refused.Path);
        Assert.StartsWith(
            Emoji,
            Assert.IsType<OperationSuccess<DeckExportBundleView>>(onPair.Value).Data.Chunk.Content,
            StringComparison.Ordinal);
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
    /// the rest, and the last chunk has no next offset.
    /// </summary>
    [Fact]
    public void Page_PreservationReportComesOnlyWithTheFirstChunk()
    {
        DeckExportBundle bundle = Bundle(new string('a', 70_000));

        DeckExportBundleView first = Assert.IsType<OperationSuccess<DeckExportBundleView>>(
            DeckExportPaging.Page(bundle, null, 0).Value).Data;
        DeckExportBundleView second = Assert.IsType<OperationSuccess<DeckExportBundleView>>(
            DeckExportPaging.Page(bundle, null, first.Chunk.NextOffset!.Value).Value).Data;
        DeckExportBundleView last = Assert.IsType<OperationSuccess<DeckExportBundleView>>(
            DeckExportPaging.Page(bundle, null, 60_000).Value).Data;

        Assert.NotEmpty(first.Preservation);
        Assert.Empty(second.Preservation);
        Assert.True(first.Chunk.Truncated);
        Assert.Contains("offset", first.Chunk.Narrow, StringComparison.Ordinal);
        Assert.Null(last.Chunk.NextOffset);
        Assert.Null(last.Chunk.Truncated);
        Assert.Equal(70_000, first.Artifacts[0].TotalLength);
    }

    /// <summary>
    /// Builds a bundle with one artifact holding the content and a one-line preservation report.
    /// </summary>
    private static DeckExportBundle Bundle(string content)
    {
        return new DeckExportBundle(
            1,
            "generic-text-v1",
            Guid.NewGuid(),
            1,
            DateTimeOffset.UnixEpoch,
            "available",
            [new DeckExportArtifact("deck.txt", "text/plain", content, Sha256Of(content), "The deck.")],
            [new DeckFieldPreservation("zone", "preserved", "Kept.")]);
    }

    /// <summary>
    /// Reads every chunk of the first artifact by following the next offset.
    /// </summary>
    private static List<DeckExportChunk> ReadAllChunks(DeckExportBundle bundle)
    {
        List<DeckExportChunk> chunks = [];
        int offset = 0;
        while (true)
        {
            DeckExportBundleView view = Assert.IsType<OperationSuccess<DeckExportBundleView>>(
                DeckExportPaging.Page(bundle, null, offset).Value).Data;
            chunks.Add(view.Chunk);
            if (view.Chunk.NextOffset is not int next)
            {
                return chunks;
            }

            Assert.True(next > offset, "The next offset must advance.");
            offset = next;
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
