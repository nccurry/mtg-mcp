using System.Globalization;
using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Names a target-cost symbol and supported direct source color.
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
/// Holds the supported generic and specific symbols from one printed mana cost.
/// </summary>
internal sealed record OnCurveManaCost(
    int Generic,
    int White,
    int Blue,
    int Black,
    int Red,
    int Green,
    int Colorless)
{
    /// <summary>
    /// Gets the total number of specific colored or colorless symbols.
    /// </summary>
    internal int SpecificSymbolCount => White + Blue + Black + Red + Green + Colorless;

    /// <summary>
    /// Gets the full number of mana units required by the printed cost.
    /// </summary>
    internal long TotalSymbolCount => (long)Generic + SpecificSymbolCount;

    /// <summary>
    /// Expands specific symbols in the fixed target-first-v1 matching order.
    /// </summary>
    internal IReadOnlyList<OnCurveManaSymbol> ExpandSpecificSymbols()
    {
        List<OnCurveManaSymbol> symbols = [];
        AddSymbols(symbols, OnCurveManaSymbol.White, White);
        AddSymbols(symbols, OnCurveManaSymbol.Blue, Blue);
        AddSymbols(symbols, OnCurveManaSymbol.Black, Black);
        AddSymbols(symbols, OnCurveManaSymbol.Red, Red);
        AddSymbols(symbols, OnCurveManaSymbol.Green, Green);
        AddSymbols(symbols, OnCurveManaSymbol.Colorless, Colorless);
        return Array.AsReadOnly(symbols.ToArray());
    }

    /// <summary>
    /// Adds the requested repeated symbol to one ordered list.
    /// </summary>
    private static void AddSymbols(List<OnCurveManaSymbol> symbols, OnCurveManaSymbol symbol, int count)
    {
        for (int index = 0; index < count; index++)
        {
            symbols.Add(symbol);
        }
    }
}

/// <summary>
/// Converts direct source values and simple printed mana costs into supported typed symbols.
/// </summary>
internal static class OnCurveManaSymbols
{
    /// <summary>
    /// Reads one simple printed mana cost or returns an unsupported outcome.
    /// </summary>
    internal static OperationResult<OnCurveManaCost> ReadCost(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return Unsupported();
        }

        int generic = 0;
        int white = 0;
        int blue = 0;
        int black = 0;
        int red = 0;
        int green = 0;
        int colorless = 0;
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
            else
            {
                switch (symbol)
                {
                    case "W":
                        white++;
                        break;
                    case "U":
                        blue++;
                        break;
                    case "B":
                        black++;
                        break;
                    case "R":
                        red++;
                        break;
                    case "G":
                        green++;
                        break;
                    case "C":
                        colorless++;
                        break;
                    default:
                        return Unsupported();
                }
            }

            index = closeIndex + 1;
        }

        return new OperationSuccess<OnCurveManaCost>(
            new OnCurveManaCost(generic, white, blue, black, red, green, colorless));
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
    /// Formats one supported symbol for a trace payment entry.
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
            "unsupported-on-curve-mana-cost",
            "The target mana cost must use only generic, W, U, B, R, G, and C symbols.");
    }
}

/// <summary>
/// Represents one active modeled land and its source colors.
/// </summary>
internal sealed record OnCurveActiveSource(
    Guid EntryId,
    IReadOnlyList<OnCurveManaSymbol> Symbols);

/// <summary>
/// Captures the maximum specific and total target-cost progress made by active sources.
/// </summary>
internal sealed record OnCurvePaymentAnalysis(
    int SpecificSymbolsCovered,
    int TotalSymbolsCovered,
    IReadOnlyList<OnCurvePaymentSource> PaymentSources)
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
/// Finds one stable maximum source-to-symbol matching for a simple target cost.
/// </summary>
internal static class OnCurveManaPayment
{
    /// <summary>
    /// Measures payment progress and records the deterministic source assignment when one is needed for a trace.
    /// </summary>
    internal static OnCurvePaymentAnalysis Analyze(
        OnCurveManaCost cost,
        IReadOnlyList<OnCurveActiveSource> sources)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(sources);
        IReadOnlyList<OnCurveManaSymbol> required = cost.ExpandSpecificSymbols();
        int[] sourceAssignments = Enumerable.Repeat(-1, sources.Count).ToArray();
        for (int requirementIndex = 0; requirementIndex < required.Count; requirementIndex++)
        {
            bool[] visitedSources = new bool[sources.Count];
            _ = TryAssign(requirementIndex, required, sources, sourceAssignments, visitedSources);
        }

        OnCurvePaymentSource?[] paymentsBySource = new OnCurvePaymentSource[sources.Count];
        int matched = 0;
        for (int sourceIndex = 0; sourceIndex < sourceAssignments.Length; sourceIndex++)
        {
            int requirementIndex = sourceAssignments[sourceIndex];
            if (requirementIndex < 0)
            {
                continue;
            }

            matched++;
            paymentsBySource[sourceIndex] = new OnCurvePaymentSource(
                sources[sourceIndex].EntryId,
                OnCurveManaSymbols.Format(required[requirementIndex]));
        }

        int genericCovered = Math.Min(cost.Generic, sources.Count - matched);
        int genericRemaining = genericCovered;
        for (int sourceIndex = 0; sourceIndex < sourceAssignments.Length && genericRemaining > 0; sourceIndex++)
        {
            if (sourceAssignments[sourceIndex] >= 0)
            {
                continue;
            }

            paymentsBySource[sourceIndex] = new OnCurvePaymentSource(sources[sourceIndex].EntryId, "generic");
            genericRemaining--;
        }

        List<OnCurvePaymentSource> paymentSources = [];
        foreach (OnCurvePaymentSource? payment in paymentsBySource)
        {
            if (payment is not null)
            {
                paymentSources.Add(payment);
            }
        }

        return new OnCurvePaymentAnalysis(
            matched,
            matched + genericCovered,
            Array.AsReadOnly(paymentSources.ToArray()));
    }

    /// <summary>
    /// Finds an augmenting path for one required symbol using stable source order.
    /// </summary>
    private static bool TryAssign(
        int requirementIndex,
        IReadOnlyList<OnCurveManaSymbol> required,
        IReadOnlyList<OnCurveActiveSource> sources,
        int[] sourceAssignments,
        bool[] visitedSources)
    {
        OnCurveManaSymbol requirement = required[requirementIndex];
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            if (visitedSources[sourceIndex] || !sources[sourceIndex].Symbols.Contains(requirement))
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
}
