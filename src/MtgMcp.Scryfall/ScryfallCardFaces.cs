using System.Text.RegularExpressions;

namespace MtgMcp.Scryfall;

/// <summary>
/// Reads the printed faces of a Scryfall card: type lines, text, names, mana costs, and mana symbols. Every tool that
/// needs one of these reads it here, so the same card is read the same way everywhere.
/// </summary>
public static partial class ScryfallCardFaces
{
    /// <summary>
    /// Lists the five colors and colorless that count as mana colors, in display order.
    /// </summary>
    public static IReadOnlyList<string> ManaColors { get; } = ["W", "U", "B", "R", "G", "C"];

    /// <summary>
    /// Gives the type line of the face a commander is designated from: the front face of a card with faces.
    /// </summary>
    public static string FrontTypeLine(ScryfallCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Faces.Count > 0 ? card.Faces[0].TypeLine ?? string.Empty : card.TypeLine ?? string.Empty;
    }

    /// <summary>
    /// Gives the oracle text of the front face of a card.
    /// </summary>
    public static string FrontText(ScryfallCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Faces.Count > 0 ? card.Faces[0].OracleText ?? string.Empty : card.OracleText ?? string.Empty;
    }

    /// <summary>
    /// Gives the oracle text of every face, joined with line breaks.
    /// </summary>
    public static string AllText(ScryfallCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Faces.Count == 0
            ? card.OracleText ?? string.Empty
            : string.Join('\n', card.Faces.Select(face => face.OracleText ?? string.Empty));
    }

    /// <summary>
    /// Gives the name of the front face of a card.
    /// </summary>
    public static string FrontName(ScryfallCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Faces.Count > 0 ? card.Faces[0].Name : card.Name;
    }

    /// <summary>
    /// Lists the card types and supertypes on the front face, before the dash.
    /// </summary>
    public static IReadOnlyList<string> FrontTypes(ScryfallCard card)
    {
        return Words(FrontTypeLine(card), beforeDash: true);
    }

    /// <summary>
    /// Lists the subtypes on the front face, after the dash.
    /// </summary>
    public static IReadOnlyList<string> FrontSubtypes(ScryfallCard card)
    {
        return Words(FrontTypeLine(card), beforeDash: false);
    }

    /// <summary>
    /// Lists the type line of each face, or the card's own type line (split at the two slashes) when it has no faces.
    /// </summary>
    public static IReadOnlyList<string> TypeLines(ScryfallCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        List<string> lines = [];
        foreach (ScryfallCardFace face in card.Faces)
        {
            if (!string.IsNullOrEmpty(face.TypeLine))
            {
                lines.Add(face.TypeLine);
            }
        }

        if (lines.Count == 0 && !string.IsNullOrEmpty(card.TypeLine))
        {
            lines.AddRange(card.TypeLine.Split(" // ", StringSplitOptions.RemoveEmptyEntries));
        }

        return lines;
    }

    /// <summary>
    /// Reports whether a card is a land: on its front face only, or on any face.
    /// </summary>
    public static bool IsLand(ScryfallCard card, bool frontOnly)
    {
        ArgumentNullException.ThrowIfNull(card);
        return frontOnly
            ? FrontTypes(card).Contains("Land", StringComparer.Ordinal)
            : TypeLines(card).Any(line => Words(line, beforeDash: true).Contains("Land", StringComparer.Ordinal));
    }

    /// <summary>
    /// Reports whether a card is a basic land, which a Commander deck may hold in any number.
    /// </summary>
    public static bool IsBasicLand(ScryfallCard card)
    {
        return FrontTypes(card).Contains("Basic", StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads the mana cost the card is cast for. An adventure or flip card is cast for the cost of its front face
    /// (rule 715.4), so the joined cost Scryfall writes at the top is not used. Any other card gives its own cost, or
    /// the front face's when it has none of its own; a split card therefore keeps its two costs joined by two slashes.
    /// </summary>
    public static string? FrontManaCost(ScryfallCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        bool frontFaceLayout = card.Faces.Count > 0 &&
            (string.Equals(card.Layout, "adventure", StringComparison.Ordinal) ||
                string.Equals(card.Layout, "flip", StringComparison.Ordinal));
        if (frontFaceLayout)
        {
            return card.Faces[0].ManaCost;
        }

        return string.IsNullOrEmpty(card.ManaCost) && card.Faces.Count > 0 ? card.Faces[0].ManaCost : card.ManaCost;
    }

    /// <summary>
    /// Lists the text inside each pair of braces of a mana cost, in order, such as <c>2</c>, <c>R</c>, or <c>W/P</c>.
    /// </summary>
    public static IReadOnlyList<string> ManaSymbols(string? manaCost)
    {
        return [.. SymbolPattern().Matches(manaCost ?? string.Empty).Select(match => match.Groups[1].Value)];
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

    /// <summary>
    /// Matches one mana symbol and captures what is inside the braces.
    /// </summary>
    [GeneratedRegex(@"\{([^}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();
}
