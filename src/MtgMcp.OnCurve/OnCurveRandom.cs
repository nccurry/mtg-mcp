using System.Globalization;

namespace MtgMcp.OnCurve;

/// <summary>
/// Parses and formats the fixed-width seed text used by repeatable on-curve runs.
/// </summary>
internal static class OnCurveSeed
{
    /// <summary>
    /// Defines the exact hexadecimal text width used on the MCP boundary.
    /// </summary>
    internal const int TextLength = 16;

    /// <summary>
    /// Parses one fixed-width hexadecimal seed without accepting signs, spaces, or shortened text.
    /// </summary>
    internal static bool TryParse(string? value, out ulong seed)
    {
        seed = 0;
        if (value is null || value.Length != TextLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsHexadecimal(character))
            {
                return false;
            }
        }

        return ulong.TryParse(
            value,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out seed);
    }

    /// <summary>
    /// Formats one seed with lower-case hexadecimal digits and leading zeroes.
    /// </summary>
    internal static string Format(ulong seed)
    {
        return seed.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reports whether one character is an ASCII hexadecimal digit.
    /// </summary>
    private static bool IsHexadecimal(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}

/// <summary>
/// Produces the versioned SplitMix64 random sequence used by every modeled run.
/// </summary>
internal sealed class SplitMix64Random
{
    /// <summary>
    /// Holds the next state in the sequence.
    /// </summary>
    private ulong state;

    /// <summary>
    /// Starts one isolated random sequence from the supplied seed.
    /// </summary>
    internal SplitMix64Random(ulong seed)
    {
        state = seed;
    }

    /// <summary>
    /// Produces the next 64-bit value in the SplitMix64 version-one sequence.
    /// </summary>
    internal ulong NextUInt64()
    {
        ulong value = unchecked(state += 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }

    /// <summary>
    /// Produces one unbiased zero-based index below the requested exclusive bound.
    /// </summary>
    internal int NextIndex(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);
        ulong bound = (ulong)exclusiveUpperBound;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < threshold);

        return (int)(value % bound);
    }

    /// <summary>
    /// Shuffles a caller-owned list in place with the versioned Fisher-Yates order.
    /// </summary>
    internal void Shuffle<T>(IList<T> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (int index = values.Count - 1; index > 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int swapIndex = NextIndex(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
