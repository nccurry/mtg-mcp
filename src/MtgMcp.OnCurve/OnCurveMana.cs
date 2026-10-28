using System.Globalization;
using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Names a mana cost symbol color and a supported direct source color.
/// </summary>
internal enum OnCurveManaSymbol
{
    /// <summary>
    /// White mana.
    /// </summary>
    White,

    /// <summary>
    /// Blue mana.
    /// </summary>
    Blue,

    /// <summary>
    /// Black mana.
    /// </summary>
    Black,

    /// <summary>
    /// Red mana.
    /// </summary>
    Red,

    /// <summary>
    /// Green mana.
    /// </summary>
    Green,

    /// <summary>
    /// Colorless mana.
    /// </summary>
    Colorless,
}

/// <summary>
/// Holds the generic and specific symbols from one printed mana cost.
/// </summary>
/// <param name="Generic">Generic mana that any source can pay.</param>
/// <param name="White">Count of {W} symbols.</param>
/// <param name="Blue">Count of {U} symbols.</param>
/// <param name="Black">Count of {B} symbols.</param>
/// <param name="Red">Count of {R} symbols.</param>
/// <param name="Green">Count of {G} symbols.</param>
/// <param name="Colorless">Count of {C} symbols.</param>
/// <param name="Either">
/// Symbols that either of two colors can pay: hybrid symbols such as {W/U}, and Phyrexian symbols, which need their
/// color because life payment is not modeled. Each entry lists the colors that pay it.
/// </param>
/// <param name="HasPhyrexian">True when the cost has a Phyrexian symbol, so life payment stays unmodeled.</param>
internal sealed record OnCurveManaCost(
    int Generic,
    int White,
    int Blue,
    int Black,
    int Red,
    int Green,
    int Colorless,
    IReadOnlyList<OnCurveManaSymbol[]>? Either = null,
    bool HasPhyrexian = false)
{
    /// <summary>
    /// Gets the hybrid and Phyrexian symbols, empty when the cost has none.
    /// </summary>
    internal IReadOnlyList<OnCurveManaSymbol[]> EitherSymbols => Either ?? [];

    /// <summary>
    /// Gets, for each specific symbol, the colors that can pay it as a bit set, in the fixed matching order: single
    /// colors in W, U, B, R, G, C order, then the hybrid and Phyrexian symbols as written.
    /// </summary>
    internal int[] RequiredMasks { get; } = BuildMasks(White, Blue, Black, Red, Green, Colorless, Either);

    /// <summary>
    /// Gets the total number of specific colored or colorless symbols, including hybrid and Phyrexian symbols.
    /// </summary>
    internal int SpecificSymbolCount => RequiredMasks.Length;

    /// <summary>
    /// Gets the full number of mana units required by the printed cost.
    /// </summary>
    internal long TotalSymbolCount => (long)Generic + SpecificSymbolCount;

    /// <summary>
    /// Returns a copy that needs more generic mana, such as mana other rules already spent this turn.
    /// </summary>
    internal OnCurveManaCost WithExtraGeneric(int extra)
    {
        return this with { Generic = (int)Math.Min(int.MaxValue, (long)Generic + extra) };
    }

    /// <summary>
    /// Builds the bit set of each specific symbol.
    /// </summary>
    private static int[] BuildMasks(
        int white,
        int blue,
        int black,
        int red,
        int green,
        int colorless,
        IReadOnlyList<OnCurveManaSymbol[]>? either)
    {
        List<int> masks = [];
        int[] counts = [white, blue, black, red, green, colorless];
        for (int symbol = 0; symbol < counts.Length; symbol++)
        {
            for (int copy = 0; copy < counts[symbol]; copy++)
            {
                masks.Add(1 << symbol);
            }
        }

        foreach (OnCurveManaSymbol[] colors in either ?? [])
        {
            masks.Add(OnCurveManaSymbols.ToMask(colors));
        }

        return [.. masks];
    }
}

/// <summary>
/// Converts direct source values and printed mana costs into supported typed symbols.
/// </summary>
internal static class OnCurveManaSymbols
{
    /// <summary>
    /// Gives the longest mana cost text that is read.
    /// </summary>
    internal const int MaximumCostLength = 128;

    /// <summary>
    /// Gives the most mana a cost may ask for in generic mana, so the arithmetic never overflows.
    /// </summary>
    internal const int MaximumGenericCost = 99;

    /// <summary>
    /// Gives the most symbols a cost may have, which bounds what one payment test costs.
    /// </summary>
    internal const int MaximumCostSymbols = 16;

    /// <summary>
    /// Turns colors into a bit set, one bit for each <see cref="OnCurveManaSymbol"/>.
    /// </summary>
    internal static int ToMask(IEnumerable<OnCurveManaSymbol> symbols)
    {
        int mask = 0;
        foreach (OnCurveManaSymbol symbol in symbols)
        {
            mask |= 1 << (int)symbol;
        }

        return mask;
    }

    /// <summary>
    /// Reads one printed mana cost or returns an unsupported outcome. Generic numbers, W, U, B, R, G, C, hybrid
    /// symbols of two colors, and Phyrexian symbols are supported.
    /// </summary>
    internal static OperationResult<OnCurveManaCost> ReadCost(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost) || manaCost.Length > MaximumCostLength)
        {
            return Unsupported();
        }

        int generic = 0;
        int[] single = new int[6];
        List<OnCurveManaSymbol[]> either = [];
        bool phyrexian = false;
        int index = 0;
        while (index < manaCost.Length)
        {
            if (manaCost[index] != '{')
            {
                return Unsupported();
            }

            int closeIndex = manaCost.IndexOf('}', index + 1);
            if (closeIndex < 0 || closeIndex == index + 1)
            {
                return Unsupported();
            }

            string symbol = manaCost[(index + 1)..closeIndex];
            if (TryReadGeneric(symbol, out int genericValue))
            {
                if (genericValue > MaximumGenericCost - generic)
                {
                    return Unsupported();
                }

                generic += genericValue;
            }
            else if (TryParseProducedMana(symbol, out OnCurveManaSymbol color))
            {
                single[(int)color]++;
            }
            else if (TryReadEither(symbol, out OnCurveManaSymbol[] colors, out bool isPhyrexian))
            {
                either.Add(colors);
                phyrexian |= isPhyrexian;
            }
            else
            {
                return Unsupported();
            }

            index = closeIndex + 1;
        }

        if (single.Sum() + either.Count > MaximumCostSymbols)
        {
            return Unsupported();
        }

        return new OperationSuccess<OnCurveManaCost>(new OnCurveManaCost(
            generic,
            single[(int)OnCurveManaSymbol.White],
            single[(int)OnCurveManaSymbol.Blue],
            single[(int)OnCurveManaSymbol.Black],
            single[(int)OnCurveManaSymbol.Red],
            single[(int)OnCurveManaSymbol.Green],
            single[(int)OnCurveManaSymbol.Colorless],
            Array.AsReadOnly(either.ToArray()),
            phyrexian));
    }

    /// <summary>
    /// Maps one direct Scryfall produced-mana value to a supported model symbol.
    /// </summary>
    internal static bool TryParseProducedMana(string? value, out OnCurveManaSymbol symbol)
    {
        symbol = value switch
        {
            "W" => OnCurveManaSymbol.White,
            "U" => OnCurveManaSymbol.Blue,
            "B" => OnCurveManaSymbol.Black,
            "R" => OnCurveManaSymbol.Red,
            "G" => OnCurveManaSymbol.Green,
            "C" => OnCurveManaSymbol.Colorless,
            _ => default,
        };
        return value is "W" or "U" or "B" or "R" or "G" or "C";
    }

    /// <summary>
    /// Reads a hybrid symbol of two colors such as <c>W/U</c>, or a Phyrexian symbol such as <c>G/P</c> or
    /// <c>G/U/P</c>. A Phyrexian symbol is paid with its colors only.
    /// </summary>
    private static bool TryReadEither(string symbol, out OnCurveManaSymbol[] colors, out bool phyrexian)
    {
        colors = [];
        phyrexian = false;
        string[] parts = symbol.Split('/');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (parts[^1] == "P")
        {
            phyrexian = true;
            parts = parts[..^1];
        }

        List<OnCurveManaSymbol> list = [];
        foreach (string part in parts)
        {
            if (!TryParseProducedMana(part, out OnCurveManaSymbol color) || color == OnCurveManaSymbol.Colorless ||
                list.Contains(color))
            {
                return false;
            }

            list.Add(color);
        }

        if (!phyrexian && list.Count != 2)
        {
            return false;
        }

        colors = [.. list];
        return true;
    }

    /// <summary>
    /// Reads a nonnegative generic symbol without accepting signs, spaces, or decimal formatting.
    /// </summary>
    private static bool TryReadGeneric(string symbol, out int value)
    {
        value = 0;
        if (symbol.Length == 0 || symbol.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        return int.TryParse(symbol, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Creates one stable unsupported-cost result without repeating parser details in callers.
    /// </summary>
    private static OperationUnsupported Unsupported()
    {
        return new OperationUnsupported(
            "unsupported-mana-cost",
            "A mana cost must use only generic numbers (at most 99 in all), W, U, B, R, G, C, hybrid color pairs such " +
            "as " +
            "{W/U}, and Phyrexian symbols such as {G/P}, with at most 16 symbols and 128 characters.");
    }
}

/// <summary>
/// Represents one unit of mana a source can add this turn and the colors it may take. A unit with no colors pays
/// generic costs only.
/// </summary>
/// <param name="Symbols">The colors this unit may take.</param>
internal sealed record OnCurveActiveSource(IReadOnlyList<OnCurveManaSymbol> Symbols)
{
    /// <summary>
    /// Gets the colors as a bit set, one bit for each <see cref="OnCurveManaSymbol"/>.
    /// </summary>
    internal int Mask { get; } = OnCurveManaSymbols.ToMask(Symbols);
}

/// <summary>
/// Captures the maximum specific and total cost progress made by the available units of mana.
/// </summary>
/// <param name="SpecificSymbolsCovered">How many specific symbols one exact assignment pays.</param>
/// <param name="TotalSymbolsCovered">How many symbols, including generic mana, the same assignment pays.</param>
/// <param name="SymbolsNeeded">How many symbols the cost and any extra generic mana need in all.</param>
internal readonly record struct OnCurvePaymentAnalysis(
    int SpecificSymbolsCovered,
    int TotalSymbolsCovered,
    long SymbolsNeeded)
{
    /// <summary>
    /// Reports whether all required symbols, including generic mana and any extra generic mana, are covered.
    /// </summary>
    internal bool IsComplete => TotalSymbolsCovered == SymbolsNeeded;

    /// <summary>
    /// Reports whether all required symbols, including generic mana, are covered.
    /// </summary>
    internal bool CoversFullCost(OnCurveManaCost cost)
    {
        return TotalSymbolsCovered == cost.TotalSymbolCount;
    }
}

/// <summary>
/// Holds the arrays one matching run needs, so repeated runs allocate nothing once the arrays are large enough.
/// </summary>
internal sealed class OnCurveMatchScratch
{
    /// <summary>
    /// Gets or sets, for each unit of mana, the index of the symbol it is assigned to, or -1.
    /// </summary>
    internal int[] Assignments { get; private set; } = new int[16];

    /// <summary>
    /// Gets or sets, for each unit of mana, whether the current search has visited it.
    /// </summary>
    internal bool[] Visited { get; private set; } = new bool[16];

    /// <summary>
    /// Makes both arrays at least this long.
    /// </summary>
    internal void Reserve(int length)
    {
        if (Assignments.Length >= length)
        {
            return;
        }

        int size = Math.Max(length, Assignments.Length * 2);
        Assignments = new int[size];
        Visited = new bool[size];
    }
}

/// <summary>
/// Finds one maximum source-to-symbol matching for a cost: specific symbols are matched first, then generic mana
/// takes any unit that is left.
/// </summary>
internal static class OnCurveManaPayment
{
    /// <summary>
    /// Measures how much of a cost the units of mana can pay with one exact assignment.
    /// </summary>
    internal static OnCurvePaymentAnalysis Analyze(
        OnCurveManaCost cost,
        IReadOnlyList<OnCurveActiveSource> sources)
    {
        return Analyze(cost, sources, new OnCurveMatchScratch());
    }

    /// <summary>
    /// Measures payment with arrays the caller reuses between runs.
    /// </summary>
    internal static OnCurvePaymentAnalysis Analyze(
        OnCurveManaCost cost,
        IReadOnlyList<OnCurveActiveSource> sources,
        OnCurveMatchScratch scratch,
        int extraGeneric = 0)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(scratch);
        int[] required = cost.RequiredMasks;
        scratch.Reserve(sources.Count);
        int[] assignments = scratch.Assignments;
        Array.Fill(assignments, -1, 0, sources.Count);
        int matched = 0;
        for (int requirementIndex = 0; requirementIndex < required.Length; requirementIndex++)
        {
            Array.Clear(scratch.Visited, 0, sources.Count);
            if (TryAssign(requirementIndex, required, sources, scratch))
            {
                matched++;
            }
        }

        long genericNeeded = (long)cost.Generic + extraGeneric;
        long genericPaid = Math.Min(genericNeeded, (long)sources.Count - matched);
        return new OnCurvePaymentAnalysis(
            matched,
            matched + (int)genericPaid,
            genericNeeded + required.Length);
    }

    /// <summary>
    /// Finds an augmenting path for one required symbol using stable source order.
    /// </summary>
    private static bool TryAssign(
        int requirementIndex,
        int[] required,
        IReadOnlyList<OnCurveActiveSource> sources,
        OnCurveMatchScratch scratch)
    {
        int acceptable = required[requirementIndex];
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            if (scratch.Visited[sourceIndex] || (sources[sourceIndex].Mask & acceptable) == 0)
            {
                continue;
            }

            scratch.Visited[sourceIndex] = true;
            if (scratch.Assignments[sourceIndex] < 0 ||
                TryAssign(scratch.Assignments[sourceIndex], required, sources, scratch))
            {
                scratch.Assignments[sourceIndex] = requirementIndex;
                return true;
            }
        }

        return false;
    }
}
