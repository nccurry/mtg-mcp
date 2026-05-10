namespace MtgMcp.Core;

/// <summary>
/// Parses intent block boundaries.
/// </summary>
public static partial class DeckIntentText
{
    /// <summary>
    /// Finds the intent block range.
    /// </summary>
    private static bool TryFindBlock(string text, out int start, out int end)
    {
        start = -1;
        end = -1;

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        IReadOnlyList<TextLineRange> lines = ReadLineRanges(normalized);
        for (int index = 0; index < lines.Count; index++)
        {
            if (!IsMarkerLine(lines[index].Text, Title))
            {
                continue;
            }

            for (int endIndex = index + 1; endIndex < lines.Count; endIndex++)
            {
                if (IsMarkerLine(lines[endIndex].Text, Title))
                {
                    break;
                }

                if (!IsMarkerLine(lines[endIndex].Text, EndMarker))
                {
                    continue;
                }

                start = ToOriginalIndex(text, lines[index].Start);
                end = ToOriginalIndex(text, lines[endIndex].End);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads line ranges in normalized text.
    /// </summary>
    private static IReadOnlyList<TextLineRange> ReadLineRanges(string text)
    {
        List<TextLineRange> lines = [];
        int start = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] != '\n')
            {
                continue;
            }

            lines.Add(new TextLineRange(text[start..index], start, index));
            start = index + 1;
        }

        return lines;
    }

    /// <summary>
    /// Checks whether a line is exactly the marker.
    /// </summary>
    private static bool IsMarkerLine(string line, string marker)
    {
        return line.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a normalized line-ending index to the original string.
    /// </summary>
    private static int ToOriginalIndex(string original, int normalizedIndex)
    {
        int originalIndex = 0;
        int currentNormalized = 0;
        while (originalIndex < original.Length && currentNormalized < normalizedIndex)
        {
            if (original[originalIndex] == '\r'
                && originalIndex + 1 < original.Length
                && original[originalIndex + 1] == '\n')
            {
                originalIndex += 2;
                currentNormalized++;
                continue;
            }

            originalIndex++;
            currentNormalized++;
        }

        return originalIndex;
    }

    /// <summary>
    /// Stores a normalized line range.
    /// </summary>
    private readonly record struct TextLineRange(string Text, int Start, int End);
}
