namespace MtgMcp.Core;

/// <summary>
/// Provides supported deck intent vocabulary values and aliases.
/// </summary>
public static class DeckIntentVocabulary
{
    /// <summary>
    /// Stores the accepted power level values.
    /// </summary>
    public static readonly IReadOnlyList<string> PowerLevels =
    [
        "precon",
        "casual",
        "tuned-casual",
        "high-power",
        "cedh"
    ];

    /// <summary>
    /// Stores the accepted heuristic profile values.
    /// </summary>
    public static readonly IReadOnlyList<string> HeuristicProfiles =
    [
        "auto",
        "commander-baseline",
        "command-zone-template",
        "edhrec-foundation",
        "mana-rich-39-land",
        "fifty-mana-sources",
        "package-8x8",
        "package-7x9",
        "package-9x7",
        "seventy-five-percent",
        "cedh-turbo",
        "cedh-midrange",
        "cedh-stax",
        "cedh-tempo"
    ];

    /// <summary>
    /// Stores the accepted package template values.
    /// </summary>
    public static readonly IReadOnlyList<string> PackageTemplates =
    [
        "none",
        "8x8",
        "7x9",
        "9x7"
    ];

    /// <summary>
    /// Stores supported power level aliases.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PowerLevelAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["upgraded-precon"] = "casual",
            ["mid-power"] = "tuned-casual",
            ["optimized"] = "high-power",
            ["competitive"] = "cedh"
        };

    /// <summary>
    /// Stores supported heuristic profile aliases.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> HeuristicProfileAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["baseline"] = "commander-baseline",
            ["commander"] = "commander-baseline",
            ["command-zone"] = "command-zone-template",
            ["commandzone"] = "command-zone-template",
            ["8x8"] = "package-8x8",
            ["7x9"] = "package-7x9",
            ["9x7"] = "package-9x7",
            ["75"] = "seventy-five-percent",
            ["75-percent"] = "seventy-five-percent"
        };

    /// <summary>
    /// Stores supported package template aliases.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PackageTemplateAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["package-8x8"] = "8x8",
            ["package-7x9"] = "7x9",
            ["package-9x7"] = "9x7"
        };

    /// <summary>
    /// Tries to normalize a power level value.
    /// </summary>
    public static bool TryNormalizePowerLevel(string value, out string normalized)
    {
        return TryNormalizeKnownValue(value, PowerLevels, PowerLevelAliases, out normalized);
    }

    /// <summary>
    /// Tries to normalize a heuristic profile value.
    /// </summary>
    public static bool TryNormalizeHeuristicProfile(string value, out string normalized)
    {
        return TryNormalizeKnownValue(value, HeuristicProfiles, HeuristicProfileAliases, out normalized);
    }

    /// <summary>
    /// Tries to normalize a package template value.
    /// </summary>
    public static bool TryNormalizePackageTemplate(string value, out string normalized)
    {
        return TryNormalizeKnownValue(value, PackageTemplates, PackageTemplateAliases, out normalized);
    }

    /// <summary>
    /// Normalizes an intent vocabulary token for matching.
    /// </summary>
    public static string NormalizeToken(string value)
    {
        List<char> chars = [];
        bool pendingSeparator = false;
        foreach (char current in value.Trim())
        {
            if (char.IsLetterOrDigit(current))
            {
                if (pendingSeparator && chars.Count > 0)
                {
                    chars.Add('-');
                }

                chars.Add(char.ToLowerInvariant(current));
                pendingSeparator = false;
            }
            else if (current == '-' || current == '_' || char.IsWhiteSpace(current))
            {
                pendingSeparator = chars.Count > 0;
            }
        }

        return new string(chars.ToArray());
    }

    /// <summary>
    /// Tries to normalize against a known value set and alias map.
    /// </summary>
    private static bool TryNormalizeKnownValue(
        string value,
        IReadOnlyList<string> supportedValues,
        IReadOnlyDictionary<string, string> aliases,
        out string normalized)
    {
        normalized = NormalizeToken(value);
        if (aliases.TryGetValue(normalized, out string? alias))
        {
            normalized = alias;
        }

        string candidate = normalized;
        return supportedValues.Any(supportedValue => supportedValue.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
