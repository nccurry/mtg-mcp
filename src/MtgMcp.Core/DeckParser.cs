using System.Text.RegularExpressions;

namespace MtgMcp.Core;

/// <summary>
/// Provides deck parser behavior.
/// </summary>
public sealed partial class DeckParser
{
    /// <summary>
    /// Parses the decklist.
    /// </summary>
    public static ParsedDecklist Parse(string decklist)
    {
        ParsedDecklist parsed = new();
        string currentCategory = DeckDefaults.Mainboard;
        string[] lines = decklist.ReplaceLineEndings("\n").Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            string rawLine = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (rawLine.StartsWith("//", StringComparison.Ordinal))
            {
                string heading = rawLine.TrimStart('/').Trim();
                currentCategory = NormalizeCategory(heading, currentCategory);
                continue;
            }

            if (!rawLine.Any(char.IsDigit) && IsLikelyHeading(rawLine))
            {
                currentCategory = NormalizeCategory(rawLine.TrimEnd(':'), currentCategory);
                continue;
            }

            Match match = DeckLineRegex().Match(rawLine);
            if (!match.Success)
            {
                parsed.Warnings.Add($"Line {index + 1} could not be parsed: {rawLine}");
                continue;
            }

            int quantity = int.Parse(
                match.Groups["quantity"].Value,
                System.Globalization.CultureInfo.InvariantCulture
            );
            string name = CleanCardName(match.Groups["name"].Value);
            if (quantity < 1 || string.IsNullOrWhiteSpace(name))
            {
                parsed.Warnings.Add($"Line {index + 1} has an invalid quantity or card name.");
                continue;
            }

            parsed.Cards.Add(
                new ParsedDecklistLine
                {
                    Quantity = quantity,
                    Name = name,
                    Category = currentCategory,
                    LineNumber = index + 1,
                }
            );
        }

        return parsed;
    }

    /// <summary>
    /// Determines whether likely heading.
    /// </summary>
    private static bool IsLikelyHeading(string value)
    {
        return value.Equals(DeckDefaults.Mainboard, StringComparison.OrdinalIgnoreCase)
            || value.Equals(DeckDefaults.Sideboard, StringComparison.OrdinalIgnoreCase)
            || value.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase)
            || value.Equals("Commander", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(':');
    }

    /// <summary>
    /// Normalizes the category.
    /// </summary>
    private static string NormalizeCategory(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.Equals("Commander", StringComparison.OrdinalIgnoreCase))
        {
            return DeckDefaults.Mainboard;
        }

        if (value.Equals("Sideboard", StringComparison.OrdinalIgnoreCase))
        {
            return DeckDefaults.Sideboard;
        }

        if (
            value.Equals("Maybeboard", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Maybe", StringComparison.OrdinalIgnoreCase)
        )
        {
            return DeckDefaults.Maybeboard;
        }

        if (
            value.Equals("Mainboard", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Main", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Deck", StringComparison.OrdinalIgnoreCase)
        )
        {
            return DeckDefaults.Mainboard;
        }

        return value.Trim();
    }

    /// <summary>
    /// Cleans the card name.
    /// </summary>
    private static string CleanCardName(string value)
    {
        int setMarker = value.IndexOf(" (", StringComparison.Ordinal);
        if (setMarker > 0)
        {
            value = value[..setMarker];
        }

        return value.Trim();
    }

    /// <summary>
    /// Handles deck line regex.
    /// </summary>
    [GeneratedRegex(
        @"^(?<quantity>\d+)\s+x?\s*(?<name>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DeckLineRegex();
}
