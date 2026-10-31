using System.Text;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Holds a piece of free text as one page of a result shows it: the text that fits, whether it is the whole text, and
/// the length of the whole text in UTF-16 code units (<see cref="string.Length"/>).
/// </summary>
internal readonly record struct ShownText(string Text, bool Truncated, int Length)
{
    /// <summary>
    /// Gets the length of the whole text when this is not all of it, or null when it is.
    /// </summary>
    internal int? LengthIfTruncated => Truncated ? Length : null;

    /// <summary>
    /// Gets true when this is not all of the text, or null when it is, for a result field that appears only then.
    /// </summary>
    internal bool? TruncatedOrNull => Truncated ? true : null;
}

/// <summary>
/// Cuts the free text of a deck header, a name or a description, so a provider or a caller cannot make every page of a
/// paged result larger than the size limit by writing a long one.
/// </summary>
internal static class ResultText
{
    /// <summary>
    /// Sets the most UTF-8 bytes of a deck name a page shows.
    /// </summary>
    internal const int MaximumNameBytes = 1_000;

    /// <summary>
    /// Sets the most UTF-8 bytes of a deck description the first page shows.
    /// </summary>
    internal const int MaximumDescriptionBytes = 8_000;

    /// <summary>
    /// Cuts a name to <see cref="MaximumNameBytes"/> bytes. Every page repeats the name.
    /// </summary>
    internal static ShownText Name(string name)
    {
        return Cut(name, MaximumNameBytes);
    }

    /// <summary>
    /// Cuts a description to <see cref="MaximumDescriptionBytes"/> bytes on the first page and shows none on a later
    /// page, so the pages after the first stay small.
    /// </summary>
    /// <param name="description">The whole description.</param>
    /// <param name="firstPage">True for the first page of the result.</param>
    internal static ShownText Description(string description, bool firstPage)
    {
        return firstPage
            ? Cut(description, MaximumDescriptionBytes)
            : new ShownText(string.Empty, description.Length > 0, description.Length);
    }

    /// <summary>
    /// Keeps the longest start of a text that takes at most the given UTF-8 bytes, never cutting a surrogate pair.
    /// </summary>
    internal static ShownText Cut(string text, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= maximumBytes / 3 || Encoding.UTF8.GetByteCount(text) <= maximumBytes)
        {
            return new ShownText(text, Truncated: false, text.Length);
        }

        int bytes = 0;
        int end = 0;
        while (end < text.Length)
        {
            int units = char.IsHighSurrogate(text[end]) && end + 1 < text.Length && char.IsLowSurrogate(text[end + 1])
                ? 2
                : 1;
            int size = Encoding.UTF8.GetByteCount(text.AsSpan(end, units));
            if (bytes + size > maximumBytes)
            {
                break;
            }

            bytes += size;
            end += units;
        }

        return new ShownText(text[..end], Truncated: true, text.Length);
    }
}
