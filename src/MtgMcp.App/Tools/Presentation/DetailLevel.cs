namespace MtgMcp.App;

/// <summary>
/// Public detail levels shared by MCP tool presenters.
/// </summary>
public enum DetailLevel
{
    /// <summary>
    /// Returns the smallest bounded response for agent loops.
    /// </summary>
    Summary,

    /// <summary>
    /// Returns bounded supporting evidence without raw model payloads.
    /// </summary>
    Normal,

    /// <summary>
    /// Returns the full underlying model when callers need raw fidelity.
    /// </summary>
    Full,
}

/// <summary>
/// Parses and formats the shared MCP detail-level vocabulary.
/// </summary>
public static class DetailLevelParser
{
    /// <summary>
    /// Summary detail-level wire value.
    /// </summary>
    public const string Summary = "summary";

    /// <summary>
    /// Normal detail-level wire value.
    /// </summary>
    public const string Normal = "normal";

    /// <summary>
    /// Full detail-level wire value.
    /// </summary>
    public const string Full = "full";

    /// <summary>
    /// Parses a caller-supplied detail level, using the supplied default when it is absent.
    /// </summary>
    public static DetailLevel Parse(
        string? detailLevel,
        DetailLevel defaultLevel = DetailLevel.Summary,
        bool allowCompactAlias = false)
    {
        if (string.IsNullOrWhiteSpace(detailLevel))
        {
            return defaultLevel;
        }

        string normalized = detailLevel.Trim().ToLowerInvariant();
        if (allowCompactAlias && normalized == "compact")
        {
            return DetailLevel.Summary;
        }

        return normalized switch
        {
            Summary => DetailLevel.Summary,
            Normal => DetailLevel.Normal,
            Full => DetailLevel.Full,
            _ => throw new ArgumentException("detailLevel must be summary, normal, or full.", nameof(detailLevel)),
        };
    }

    /// <summary>
    /// Normalizes a caller-supplied detail level to the public wire value.
    /// </summary>
    public static string Normalize(
        string? detailLevel,
        DetailLevel defaultLevel = DetailLevel.Summary,
        bool allowCompactAlias = false)
    {
        return Parse(detailLevel, defaultLevel, allowCompactAlias).ToWireName();
    }

    /// <summary>
    /// Converts a detail-level value to its public wire name.
    /// </summary>
    public static string ToWireName(this DetailLevel detailLevel)
    {
        return detailLevel switch
        {
            DetailLevel.Summary => Summary,
            DetailLevel.Normal => Normal,
            DetailLevel.Full => Full,
            _ => throw new ArgumentOutOfRangeException(nameof(detailLevel), detailLevel, null),
        };
    }
}
