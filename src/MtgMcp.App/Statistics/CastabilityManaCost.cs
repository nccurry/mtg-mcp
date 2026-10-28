using System.Globalization;
using MtgMcp.Scryfall;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Reads a mana cost into the exact requirement the statistics engine understands, or says why it cannot.
/// </summary>
internal static class CastabilityManaCost
{
    /// <summary>
    /// Reads a mana cost. Costs with no cost, a variable symbol, a hybrid or Phyrexian symbol, a snow symbol, or two
    /// costs (split cards) cannot be expressed exactly and give a reason instead.
    /// </summary>
    internal static (ManaRequirementInput? Requirement, string? SkipReason) Parse(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return (null, "no-mana-cost");
        }

        if (manaCost.Contains("//", StringComparison.Ordinal))
        {
            return (null, "split-card-has-two-costs");
        }

        int white = 0;
        int blue = 0;
        int black = 0;
        int red = 0;
        int green = 0;
        int colorless = 0;
        int generic = 0;
        foreach (string symbol in ScryfallCardFaces.ManaSymbols(manaCost))
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
                case "X":
                    return (null, "x-cost");
                default:
                    if (symbol.Contains('/', StringComparison.Ordinal))
                    {
                        return (null, "hybrid-or-phyrexian-symbol");
                    }

                    if (!int.TryParse(symbol, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    {
                        return (null, "unsupported-symbol");
                    }

                    generic += number;
                    break;
            }
        }

        return (new ManaRequirementInput(white, blue, black, red, green, colorless, generic), null);
    }
}
