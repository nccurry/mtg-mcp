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
    /// Gets the total number of specific colored or colorless symbols, including hybrid and Phyrexian symbols.
    /// </summary>
    internal int SpecificSymbolCount => White + Blue + Black + Red + Green + Colorless + EitherSymbols.Count;

    /// <summary>
    /// Gets the full number of mana units required by the printed cost.
    /// </summary>
    internal long TotalSymbolCount => (long)Generic + SpecificSymbolCount;

    /// <summary>
    /// Returns a copy that needs more generic mana, such as mana other rules already spent this turn.
    /// </summary>
    internal OnCurveManaCost WithExtraGeneric(int extra)
    {
        return this with { Generic = Generic + extra };
    }

    /// <summary>
    /// Lists the specific symbols in the fixed matching order: single colors in W, U, B, R, G, C order, then the
    /// hybrid and Phyrexian symbols as written. Each entry lists the colors that can pay that symbol.
    /// </summary>
    internal IReadOnlyList<OnCurveManaSymbol[]> ExpandSpecificSymbols()
    {
        List<OnCurveManaSymbol[]> symbols = [];
        AddSymbols(symbols, OnCurveManaSymbol.White, White);
        AddSymbols(symbols, OnCurveManaSymbol.Blue, Blue);
        AddSymbols(symbols, OnCurveManaSymbol.Black, Black);
        AddSymbols(symbols, OnCurveManaSymbol.Red, Red);
        AddSymbols(symbols, OnCurveManaSymbol.Green, Green);
        AddSymbols(symbols, OnCurveManaSymbol.Colorless, Colorless);
        symbols.AddRange(EitherSymbols);
        return Array.AsReadOnly(symbols.ToArray());
    }

    /// <summary>
    /// Adds the requested repeated symbol to one ordered list.
    /// </summary>
    private static void AddSymbols(List<OnCurveManaSymbol[]> symbols, OnCurveManaSymbol symbol, int count)
    {
        for (int index = 0; index < count; index++)
        {
            symbols.Add([symbol]);
        }
    }
}

/// <summary>
/// Converts direct source values and printed mana costs into supported typed symbols.
/// </summary>
internal static class OnCurveManaSymbols
{
    /// <summary>
    /// Reads one printed mana cost or returns an unsupported outcome. Generic numbers, W, U, B, R, G, C, hybrid
    /// symbols of two colors, and Phyrexian symbols are supported.
    /// </summary>
    internal static OperationResult<OnCurveManaCost> ReadCost(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
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
                if (genericValue > int.MaxValue - generic)
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
    /// Formats one supported symbol for a result.
    /// </summary>
    internal static string Format(OnCurveManaSymbol symbol)
    {
        return symbol switch
        {
            OnCurveManaSymbol.White => "W",
            OnCurveManaSymbol.Blue => "U",
            OnCurveManaSymbol.Black => "B",
            OnCurveManaSymbol.Red => "R",
            OnCurveManaSymbol.Green => "G",
            OnCurveManaSymbol.Colorless => "C",
            _ => throw new ArgumentOutOfRangeException(nameof(symbol), symbol, "The mana symbol is unsupported."),
        };
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
            "A mana cost must use only generic numbers, W, U, B, R, G, C, hybrid color pairs such as {W/U}, and " +
            "Phyrexian symbols such as {G/P}.");
    }
}

/// <summary>
/// Represents one unit of mana a source can add this turn and the colors it may take. A unit with no colors pays
/// generic mana only.
/// </summary>
/// <param name="Symbols">The colors this unit may take.</param>
internal sealed record OnCurveActiveSource(IReadOnlyList<OnCurveManaSymbol> Symbols);

/// <summary>
/// Captures the maximum specific and total cost progress made by the available units of mana.
/// </summary>
/// <param name="SpecificSymbolsCovered">How many specific symbols one exact assignment pays.</param>
/// <param name="TotalSymbolsCovered">How many symbols, including generic mana, the same assignment pays.</param>
internal sealed record OnCurvePaymentAnalysis(int SpecificSymbolsCovered, int TotalSymbolsCovered)
{
    /// <summary>
    /// Reports whether all required colored and colorless symbols are covered.
    /// </summary>
    internal bool CoversSpecificCost(OnCurveManaCost cost)
    {
        return SpecificSymbolsCovered == cost.SpecificSymbolCount;
    }

    /// <summary>
    /// Reports whether all required symbols, including generic mana, are covered.
    /// </summary>
    internal bool CoversFullCost(OnCurveManaCost cost)
    {
        return TotalSymbolsCovered == cost.TotalSymbolCount;
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
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(sources);
        IReadOnlyList<OnCurveManaSymbol[]> required = cost.ExpandSpecificSymbols();
        int[] sourceAssignments = new int[sources.Count];
        Array.Fill(sourceAssignments, -1);
        int matched = 0;
        for (int requirementIndex = 0; requirementIndex < required.Count; requirementIndex++)
        {
            bool[] visitedSources = new bool[sources.Count];
            if (TryAssign(requirementIndex, required, sources, sourceAssignments, visitedSources))
            {
                matched++;
            }
        }

        long genericCovered = Math.Min(cost.Generic, sources.Count - matched);
        return new OnCurvePaymentAnalysis(matched, matched + (int)genericCovered);
    }

    /// <summary>
    /// Finds an augmenting path for one required symbol using stable source order.
    /// </summary>
    private static bool TryAssign(
        int requirementIndex,
        IReadOnlyList<OnCurveManaSymbol[]> required,
        IReadOnlyList<OnCurveActiveSource> sources,
        int[] sourceAssignments,
        bool[] visitedSources)
    {
        OnCurveManaSymbol[] acceptable = required[requirementIndex];
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            if (visitedSources[sourceIndex] || !CanPay(sources[sourceIndex], acceptable))
            {
                continue;
            }

            visitedSources[sourceIndex] = true;
            if (sourceAssignments[sourceIndex] < 0 ||
                TryAssign(sourceAssignments[sourceIndex], required, sources, sourceAssignments, visitedSources))
            {
                sourceAssignments[sourceIndex] = requirementIndex;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether a unit of mana has any of the colors that can pay a symbol.
    /// </summary>
    private static bool CanPay(OnCurveActiveSource source, OnCurveManaSymbol[] acceptable)
    {
        foreach (OnCurveManaSymbol symbol in acceptable)
        {
            if (source.Symbols.Contains(symbol))
            {
                return true;
            }
        }

        return false;
    }
}
