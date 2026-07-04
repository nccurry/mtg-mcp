using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MtgMcp.Core;

/// <summary>
/// Enforces the shared invariants carried by public evidence and result contracts.
/// </summary>
internal static class ContractValidation
{
    /// <summary>
    /// Matches stable lowercase kebab-case reason codes.
    /// </summary>
    private static readonly Regex ReasonCodePattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>
    /// Returns a trimmed required value or rejects missing text.
    /// </summary>
    internal static string RequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    /// <summary>
    /// Returns a validated stable reason code.
    /// </summary>
    internal static string ReasonCode(string value, string parameterName)
    {
        string normalized = RequiredText(value, parameterName);
        if (!ReasonCodePattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Reason codes must use lowercase kebab-case.",
                parameterName);
        }

        return normalized;
    }

    /// <summary>
    /// Returns either no optional value or a trimmed nonblank value.
    /// </summary>
    internal static string? OptionalText(string? value, string parameterName)
    {
        return value is null ? null : RequiredText(value, parameterName);
    }

    /// <summary>
    /// Copies assumptions into an immutable view after validating every entry.
    /// </summary>
    internal static IReadOnlyList<string> Assumptions(
        IReadOnlyList<string> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);

        string[] copy = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = RequiredText(values[index], parameterName);
        }

        return new ReadOnlyCollection<string>(copy);
    }
}
