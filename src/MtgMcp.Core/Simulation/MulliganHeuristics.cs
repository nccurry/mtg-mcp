namespace MtgMcp.Core;

/// <summary>
/// Centralizes London mulligan policy shared by performance and goldfish simulation.
/// </summary>
internal static class MulliganHeuristics
{
    /// <summary>
    /// Gets the maximum mulligan attempts modeled for a format.
    /// </summary>
    public static int MaximumMulligans(string format)
    {
        return MaximumMulligans(UsesFreeFirstMulligan(format));
    }

    /// <summary>
    /// Gets the maximum mulligan attempts modeled from the resolved free-mulligan rule.
    /// </summary>
    public static int MaximumMulligans(bool freeFirstMulligan)
    {
        return freeFirstMulligan ? 3 : 2;
    }

    /// <summary>
    /// Computes kept hand size after actual mulligans, including free first mulligans.
    /// </summary>
    public static int TargetHandSize(int mulligans, string format)
    {
        return TargetHandSize(mulligans, UsesFreeFirstMulligan(format));
    }

    /// <summary>
    /// Computes kept hand size from the resolved free-mulligan rule.
    /// </summary>
    public static int TargetHandSize(int mulligans, bool freeFirstMulligan)
    {
        int paidMulligans = freeFirstMulligan && mulligans > 0
            ? mulligans - 1
            : mulligans;
        return Math.Max(0, 7 - paidMulligans);
    }

    /// <summary>
    /// Checks whether the format normally grants a free first mulligan.
    /// </summary>
    public static bool UsesFreeFirstMulligan(string format)
    {
        return format.Contains("commander", StringComparison.OrdinalIgnoreCase)
            || format.Contains("brawl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the format uses Commander deck construction limits.
    /// </summary>
    public static bool UsesCommanderDeckConstruction(string format)
    {
        string normalized = format.Trim();
        return normalized.Equals("commander", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("edh", StringComparison.OrdinalIgnoreCase);
    }
}
