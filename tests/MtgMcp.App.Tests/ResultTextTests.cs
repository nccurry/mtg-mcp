using System.Text;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks how the free text of a deck header is cut: by UTF-8 bytes, never between the two halves of a pair.
/// </summary>
public sealed class ResultTextTests
{
    /// <summary>
    /// Holds a character that takes two UTF-16 code units and four UTF-8 bytes.
    /// </summary>
    private const string Emoji = "\U0001F600";

    /// <summary>
    /// Verifies a description whose byte limit falls inside an emoji is cut before the emoji, within the byte limit,
    /// and reports the whole length.
    /// </summary>
    [Fact]
    public void Description_WithTheLimitInsideAnEmoji_KeepsThePairWhole()
    {
        string description = new string('a', ResultText.MaximumDescriptionBytes - 2) + Emoji + new string('b', 100);

        ShownText shown = ResultText.Description(description, firstPage: true);

        Assert.True(shown.Truncated);
        Assert.Equal(description.Length, shown.Length);
        Assert.Equal(new string('a', ResultText.MaximumDescriptionBytes - 2), shown.Text);
        Assert.True(Encoding.UTF8.GetByteCount(shown.Text) <= ResultText.MaximumDescriptionBytes);
    }

    /// <summary>
    /// Verifies a name of multibyte characters is cut by bytes, not characters, and does not end in half a pair.
    /// </summary>
    [Fact]
    public void Name_OfEmoji_IsCutByBytes()
    {
        string name = string.Concat(Enumerable.Repeat(Emoji, 1_000));

        ShownText shown = ResultText.Name(name);

        Assert.True(shown.Truncated);
        Assert.Equal(ResultText.MaximumNameBytes, Encoding.UTF8.GetByteCount(shown.Text));
        Assert.False(char.IsHighSurrogate(shown.Text[^1]));
        Assert.Equal(ResultText.MaximumNameBytes / 2, shown.Text.Length);
    }

    /// <summary>
    /// Verifies a short text comes back whole and unmarked.
    /// </summary>
    [Fact]
    public void Name_Short_IsWhole()
    {
        ShownText shown = ResultText.Name("Izzet " + Emoji);

        Assert.False(shown.Truncated);
        Assert.Null(shown.TruncatedOrNull);
        Assert.Equal("Izzet " + Emoji, shown.Text);
    }
}
