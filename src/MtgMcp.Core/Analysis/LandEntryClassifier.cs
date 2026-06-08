namespace MtgMcp.Core;

/// <summary>
/// Classifies whether a land is usually available the turn it enters.
/// </summary>
internal static class LandEntryClassifier
{
    /// <summary>
    /// Classifies a land's entry timing from cached oracle text and face structure.
    /// </summary>
    public static LandEntryTiming Classify(CardSnapshot snapshot)
    {
        string oracleText = snapshot.OracleText ?? "";
        if (LooksConditionallyTapped(oracleText))
        {
            return LandEntryTiming.ConditionalTapped;
        }

        if (LooksAlwaysTapped(oracleText) || HasNonPrimaryLandFace(snapshot.TypeLine ?? ""))
        {
            return LandEntryTiming.AlwaysTapped;
        }

        return LandEntryTiming.Untapped;
    }

    /// <summary>
    /// Checks whether the land is not reliably untapped for replacement heuristics.
    /// </summary>
    public static bool IsTappedPressure(CardSnapshot snapshot)
    {
        return Classify(snapshot) is LandEntryTiming.AlwaysTapped or LandEntryTiming.ConditionalTapped;
    }

    /// <summary>
    /// Checks whether early-turn simulation should treat this land as tapped.
    /// </summary>
    public static bool IsAlwaysTapped(CardSnapshot snapshot)
    {
        return Classify(snapshot) == LandEntryTiming.AlwaysTapped;
    }

    /// <summary>
    /// Checks for costs or board checks that can allow a land to enter untapped.
    /// </summary>
    private static bool LooksConditionallyTapped(string oracleText)
    {
        return ContainsAny(
                oracleText,
                "enters tapped unless",
                "enters the battlefield tapped unless",
                "enters the battlefield tapped, unless")
            || (oracleText.Contains("you may pay", StringComparison.OrdinalIgnoreCase)
                && oracleText.Contains("if you don't, it enters tapped", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks for unconditional tapped-entry text.
    /// </summary>
    private static bool LooksAlwaysTapped(string oracleText)
    {
        return oracleText.Contains("enters tapped", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("enters the battlefield tapped", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a type line has a land face behind a nonland front face.
    /// </summary>
    private static bool HasNonPrimaryLandFace(string typeLine)
    {
        string[] faces = typeLine.Split(["//"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return faces.Length > 1
            && !faces[0].Contains("Land", StringComparison.OrdinalIgnoreCase)
            && faces.Skip(1).Any(face => face.Contains("Land", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether a value contains any provided token.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Describes whether a land is reliably usable on the turn it enters.
/// </summary>
internal enum LandEntryTiming
{
    /// <summary>
    /// The land does not show tapped-entry pressure.
    /// </summary>
    Untapped,

    /// <summary>
    /// The land appears to always enter tapped.
    /// </summary>
    AlwaysTapped,

    /// <summary>
    /// The land appears to enter tapped unless a condition is met or a cost is paid.
    /// </summary>
    ConditionalTapped,
}
