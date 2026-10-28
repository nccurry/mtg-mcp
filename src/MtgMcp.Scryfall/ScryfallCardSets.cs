using System.Globalization;
using System.Text;

namespace MtgMcp.Scryfall;

/// <summary>
/// Names the four exact set operations that can combine two card lists.
/// </summary>
public enum ScryfallSetOperation
{
    /// <summary>
    /// Every card that is in either list.
    /// </summary>
    Union,

    /// <summary>
    /// Every card that is in both lists.
    /// </summary>
    Intersection,

    /// <summary>
    /// Every card in the left list that is not in the right list.
    /// </summary>
    Difference,

    /// <summary>
    /// Every card that is in exactly one of the two lists.
    /// </summary>
    SymmetricDifference,
}

/// <summary>
/// Names what makes two cards the same card when two lists are combined.
/// </summary>
public enum ScryfallMatchBy
{
    /// <summary>
    /// Same Oracle card, whatever the printing.
    /// </summary>
    Oracle,

    /// <summary>
    /// Same exact printing.
    /// </summary>
    Printing,

    /// <summary>
    /// Same normalized card name.
    /// </summary>
    Name,
}

/// <summary>
/// Converts set operations and match modes to and from the names callers send and receive.
/// </summary>
public static class ScryfallSetNames
{
    /// <summary>
    /// Lists the operation names in the order they are published.
    /// </summary>
    public static IReadOnlyList<string> Operations { get; } =
        ["union", "intersection", "difference", "symmetric-difference"];

    /// <summary>
    /// Lists the match mode names in the order they are published.
    /// </summary>
    public static IReadOnlyList<string> MatchModes { get; } = ["oracle", "printing", "name"];

    /// <summary>
    /// Reads an operation name.
    /// </summary>
    public static bool TryParseOperation(string? name, out ScryfallSetOperation operation)
    {
        operation = ScryfallSetOperation.Union;
        switch (name)
        {
            case "union":
                return true;
            case "intersection":
                operation = ScryfallSetOperation.Intersection;
                return true;
            case "difference":
                operation = ScryfallSetOperation.Difference;
                return true;
            case "symmetric-difference":
                operation = ScryfallSetOperation.SymmetricDifference;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads a match mode name; a missing name means <c>oracle</c>.
    /// </summary>
    public static bool TryParseMatchBy(string? name, out ScryfallMatchBy matchBy)
    {
        matchBy = ScryfallMatchBy.Oracle;
        switch (name)
        {
            case null:
            case "oracle":
                return true;
            case "printing":
                matchBy = ScryfallMatchBy.Printing;
                return true;
            case "name":
                matchBy = ScryfallMatchBy.Name;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Combines two lists of cards with an exact set operation. It does no input or output, so it can be tested alone.
/// </summary>
public static class ScryfallCardSets
{
    /// <summary>
    /// Combines two card lists. Each result card appears once. With <see cref="ScryfallMatchBy.Printing"/> the
    /// printing is kept; with the other modes the result card comes from the left list when it is there, otherwise
    /// from the right list. The result is ordered by card name, then Oracle ID, then printing ID.
    /// </summary>
    public static IReadOnlyList<ScryfallCard> Combine(
        IReadOnlyList<ScryfallCard> left,
        IReadOnlyList<ScryfallCard> right,
        ScryfallSetOperation operation,
        ScryfallMatchBy matchBy)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        Dictionary<string, ScryfallCard> leftByKey = Index(left, matchBy);
        Dictionary<string, ScryfallCard> rightByKey = Index(right, matchBy);
        List<ScryfallCard> result = [];
        foreach ((string key, ScryfallCard card) in leftByKey)
        {
            if (KeepsLeft(operation, rightByKey.ContainsKey(key)))
            {
                result.Add(card);
            }
        }

        if (operation is ScryfallSetOperation.Union or ScryfallSetOperation.SymmetricDifference)
        {
            foreach ((string key, ScryfallCard card) in rightByKey)
            {
                if (!leftByKey.ContainsKey(key))
                {
                    result.Add(card);
                }
            }
        }

        result.Sort(Compare);
        return result;
    }

    /// <summary>
    /// Orders cards by name (ignoring letter case first), then Oracle ID, then printing ID.
    /// </summary>
    public static int Compare(ScryfallCard first, ScryfallCard second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        int byName = StringComparer.OrdinalIgnoreCase.Compare(first.Name, second.Name);
        if (byName != 0)
        {
            return byName;
        }

        byName = StringComparer.Ordinal.Compare(first.Name, second.Name);
        if (byName != 0)
        {
            return byName;
        }

        int byOracle = string.CompareOrdinal(OracleKey(first), OracleKey(second));
        return byOracle != 0 ? byOracle : first.Id.CompareTo(second.Id);
    }

    /// <summary>
    /// Gives the text that says which card a printing is, for every match mode.
    /// </summary>
    public static string KeyOf(ScryfallCard card, ScryfallMatchBy matchBy)
    {
        ArgumentNullException.ThrowIfNull(card);
        return matchBy switch
        {
            ScryfallMatchBy.Printing => card.Id.ToString("D", CultureInfo.InvariantCulture),
            ScryfallMatchBy.Name => NormalizeName(card.Name),
            _ => OracleKey(card) is { Length: > 0 } oracleKey ? oracleKey : "name:" + NormalizeName(card.Name),
        };
    }

    /// <summary>
    /// Normalizes a card name for exact comparison: composed Unicode form, single spaces, and no letter case.
    /// </summary>
    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string composed = name.Normalize(NormalizationForm.FormC);
        StringBuilder builder = new(composed.Length);
        bool pendingSpace = false;
        foreach (char character in composed)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reports whether a card that is in the left list stays in the result, given whether it is also in the right.
    /// </summary>
    private static bool KeepsLeft(ScryfallSetOperation operation, bool inRight)
    {
        return operation switch
        {
            ScryfallSetOperation.Intersection => inRight,
            ScryfallSetOperation.Difference or ScryfallSetOperation.SymmetricDifference => !inRight,
            _ => true,
        };
    }

    /// <summary>
    /// Keys one list by match mode. When several cards share a key, the first one in card order stands for them.
    /// </summary>
    private static Dictionary<string, ScryfallCard> Index(IReadOnlyList<ScryfallCard> cards, ScryfallMatchBy matchBy)
    {
        List<ScryfallCard> ordered = [.. cards];
        ordered.Sort(Compare);
        Dictionary<string, ScryfallCard> byKey = new(StringComparer.Ordinal);
        foreach (ScryfallCard card in ordered)
        {
            byKey.TryAdd(KeyOf(card, matchBy), card);
        }

        return byKey;
    }

    /// <summary>
    /// Gives the Oracle ID text of a card, using its front face when the card has no Oracle ID of its own.
    /// </summary>
    private static string OracleKey(ScryfallCard card)
    {
        Guid? oracleId = ScryfallConciseFieldTable.EffectiveOracleId(card);
        return oracleId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
