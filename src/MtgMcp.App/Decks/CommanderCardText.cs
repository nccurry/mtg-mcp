using System.Globalization;
using System.Text.RegularExpressions;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Reads the printed text of a Scryfall card for the Commander rule facts: the front face, deck-limit statements,
/// partner abilities, and companion restrictions. Each reader returns what the text says and nothing more.
/// </summary>
internal static partial class CommanderCardText
{
    /// <summary>
    /// Lists the number words that a "can have up to N cards named" statement uses.
    /// </summary>
    private static readonly string[] NumberWords =
    [
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    /// <summary>
    /// Lists the partner variants the Comprehensive Rules name (rule 702.124i).
    /// </summary>
    internal static IReadOnlyList<string> KnownPartnerVariants { get; } =
        ["Character select", "Father & son", "Friends forever", "Survivors"];

    /// <summary>
    /// Gives the type line of the face a commander is designated from: the front face of a card with faces.
    /// </summary>
    internal static string FrontTypeLine(ScryfallCard card)
    {
        return card.Faces.Count > 0 ? card.Faces[0].TypeLine ?? string.Empty : card.TypeLine ?? string.Empty;
    }

    /// <summary>
    /// Gives the oracle text of the front face of a card.
    /// </summary>
    internal static string FrontText(ScryfallCard card)
    {
        return card.Faces.Count > 0 ? card.Faces[0].OracleText ?? string.Empty : card.OracleText ?? string.Empty;
    }

    /// <summary>
    /// Gives the oracle text of every face, joined with line breaks.
    /// </summary>
    internal static string AllText(ScryfallCard card)
    {
        if (card.Faces.Count == 0)
        {
            return card.OracleText ?? string.Empty;
        }

        return string.Join('\n', card.Faces.Select(face => face.OracleText ?? string.Empty));
    }

    /// <summary>
    /// Gives the name of the front face of a card.
    /// </summary>
    internal static string FrontName(ScryfallCard card)
    {
        return card.Faces.Count > 0 ? card.Faces[0].Name : card.Name;
    }

    /// <summary>
    /// Reports whether the front face is legendary.
    /// </summary>
    internal static bool IsLegendary(ScryfallCard card)
    {
        return Words(FrontTypeLine(card), beforeDash: true).Contains("Legendary", StringComparer.Ordinal);
    }

    /// <summary>
    /// Lists the card types and supertypes on the front face, before the dash.
    /// </summary>
    internal static IReadOnlyList<string> FrontTypes(ScryfallCard card)
    {
        return Words(FrontTypeLine(card), beforeDash: true);
    }

    /// <summary>
    /// Lists the subtypes on the front face, after the dash.
    /// </summary>
    internal static IReadOnlyList<string> FrontSubtypes(ScryfallCard card)
    {
        return Words(FrontTypeLine(card), beforeDash: false);
    }

    /// <summary>
    /// Reports whether a card is a basic land, which a Commander deck may hold in any number.
    /// </summary>
    internal static bool IsBasicLand(ScryfallCard card)
    {
        return FrontTypes(card).Contains("Basic", StringComparer.Ordinal);
    }

    /// <summary>
    /// Reports whether a card is a legendary Background enchantment.
    /// </summary>
    internal static bool IsBackground(ScryfallCard card)
    {
        IReadOnlyList<string> types = FrontTypes(card);
        return types.Contains("Legendary", StringComparer.Ordinal) &&
            types.Contains("Enchantment", StringComparer.Ordinal) &&
            FrontSubtypes(card).Contains("Background", StringComparer.Ordinal);
    }

    /// <summary>
    /// Reports whether the text of any face says the card can be a commander.
    /// </summary>
    internal static bool SaysCanBeCommander(ScryfallCard card)
    {
        return AllText(card).Contains("can be your commander", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads how many copies a Commander deck may hold: null for any number, otherwise the limit. Basic lands and
    /// cards whose text says "A deck can have any number of cards named" have no limit; a card whose text says "A
    /// deck can have up to N cards named" is limited to N; every other card to one. Search texts such as "search your
    /// library for up to three cards named" are not deck limits and do not match.
    /// </summary>
    internal static int? CopyLimit(ScryfallCard card)
    {
        if (IsBasicLand(card))
        {
            return null;
        }

        string text = AllText(card);
        if (AnyNumberPattern().IsMatch(text))
        {
            return null;
        }

        Match upTo = UpToPattern().Match(text);
        if (upTo.Success && NumberOf(upTo.Groups[1].Value) is int limit)
        {
            return limit;
        }

        return 1;
    }

    /// <summary>
    /// Reads the card named after "Partner with" in the text, or null when the card has no such ability.
    /// </summary>
    internal static string? PartnerWithName(ScryfallCard card)
    {
        Match match = PartnerWithPattern().Match(AllText(card));
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Lists the variants named by "Partner—variant" lines in the text of every face.
    /// </summary>
    internal static IReadOnlyList<string> PartnerVariants(ScryfallCard card)
    {
        return [.. PartnerVariantPattern().Matches(AllText(card)).Select(match => match.Groups[1].Value.Trim())];
    }

    /// <summary>
    /// Reports whether the keyword list holds a keyword, ignoring letter case.
    /// </summary>
    internal static bool HasKeyword(ScryfallCard card, string keyword)
    {
        return card.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the restriction line of a companion card, or null when the text has none.
    /// </summary>
    internal static string? CompanionRestriction(ScryfallCard card)
    {
        Match match = CompanionPattern().Match(AllText(card));
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Counts each mana symbol of a mana cost, so a cost with two of the same symbol can be found.
    /// </summary>
    internal static Dictionary<string, int> SymbolCounts(string? manaCost)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (Match match in SymbolPattern().Matches(manaCost ?? string.Empty))
        {
            counts[match.Value] = counts.GetValueOrDefault(match.Value) + 1;
        }

        return counts;
    }

    /// <summary>
    /// Matches a statement that a deck can hold any number of a card.
    /// </summary>
    [GeneratedRegex(
        @"A deck can have any number of cards named",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnyNumberPattern();

    /// <summary>
    /// Matches a statement that a deck can hold up to a number of a card.
    /// </summary>
    [GeneratedRegex(
        @"A deck can have up to (\w+) cards named",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpToPattern();

    /// <summary>
    /// Matches the card named by a Partner with ability.
    /// </summary>
    [GeneratedRegex(@"^Partner with ([^\n(]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PartnerWithPattern();

    /// <summary>
    /// Matches a partner variant line, such as Partner, an em dash, and the variant name.
    /// </summary>
    [GeneratedRegex("^Partner—([^\\n(]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PartnerVariantPattern();

    /// <summary>
    /// Matches the restriction of a companion card.
    /// </summary>
    [GeneratedRegex("^Companion\\s*—\\s*([^\\n(]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex CompanionPattern();

    /// <summary>
    /// Matches one mana symbol.
    /// </summary>
    [GeneratedRegex(@"\{[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();

    /// <summary>
    /// Reads a number written as digits or as an English word up to twenty.
    /// </summary>
    private static int? NumberOf(string text)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int digits))
        {
            return digits;
        }

        int word = Array.FindIndex(NumberWords, value => value.Equals(text, StringComparison.OrdinalIgnoreCase));
        return word < 0 ? null : word + 1;
    }

    /// <summary>
    /// Splits a type line into words before or after its dash.
    /// </summary>
    private static List<string> Words(string typeLine, bool beforeDash)
    {
        string[] halves = typeLine.Split('—');
        string part = beforeDash ? halves[0] : halves.Length > 1 ? halves[1] : string.Empty;
        return [.. part.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }
}
