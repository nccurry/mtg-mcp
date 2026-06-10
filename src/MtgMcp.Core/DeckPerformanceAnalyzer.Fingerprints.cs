using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Contains deterministic Stats Lab replay metadata and fingerprint helpers.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Identifies the current public shape of Stats Lab performance results.
    /// </summary>
    private const int StatsLabSchemaVersion = 2;

    /// <summary>
    /// Identifies the current deterministic Stats Lab behavior contract.
    /// </summary>
    private const string StatsLabModelVersion = "stats-lab-1";

    /// <summary>
    /// Keeps profile fingerprint serialization aligned with external profile JSON.
    /// </summary>
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Builds a stable fingerprint for the deck construction inputs sampled by Stats Lab.
    /// </summary>
    private static string BuildDeckFingerprint(DeckWorkspace workspace, IReadOnlyList<DeckCard> included)
    {
        StringBuilder builder = new();
        builder.Append("format|").Append(NormalizeFingerprintValue(workspace.Format)).AppendLine();
        foreach (DeckCard card in OrderFingerprintCards(included))
        {
            builder
                .Append("card|")
                .Append(NormalizeFingerprintValue(card.Name)).Append('|')
                .Append(Math.Max(0, card.Quantity).ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(NormalizeFingerprintValue(DeckCategoryOrdering.PrimaryCategory(card))).Append('|')
                .Append(NormalizeFingerprintValues(card.Categories))
                .AppendLine();
        }

        return HashFingerprint(builder.ToString());
    }

    /// <summary>
    /// Builds a stable fingerprint for cached card facts used by Stats Lab.
    /// </summary>
    private static string BuildCardDataFingerprint(IReadOnlyList<DeckCard> included)
    {
        StringBuilder builder = new();
        foreach (DeckCard card in OrderFingerprintCards(included))
        {
            CardSnapshot snapshot = PerformanceMana.GetSnapshot(card);
            builder
                .Append("snapshot|")
                .Append(NormalizeFingerprintValue(card.Name)).Append('|')
                .Append(NormalizeFingerprintValue(snapshot.ManaCost)).Append('|')
                .Append((snapshot.ManaValue ?? 0).ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
                .Append(NormalizeFingerprintValue(snapshot.TypeLine)).Append('|')
                .Append(NormalizeFingerprintValue(snapshot.OracleText)).Append('|')
                .Append(NormalizeFingerprintValues(snapshot.ColorIdentity)).Append('|')
                .Append(NormalizeFingerprintValues(snapshot.ProducedMana))
                .AppendLine();
        }

        return HashFingerprint(builder.ToString());
    }

    /// <summary>
    /// Builds a stable fingerprint for the resolved simulation profile.
    /// </summary>
    private static string BuildProfileFingerprint(SimulationProfile profile)
    {
        return HashFingerprint(JsonSerializer.Serialize(profile, FingerprintJsonOptions));
    }

    /// <summary>
    /// Orders cards so equivalent deck inputs hash the same way.
    /// </summary>
    private static List<DeckCard> OrderFingerprintCards(IEnumerable<DeckCard> cards)
    {
        List<DeckCard> ordered = cards.ToList();
        ordered.Sort((left, right) =>
        {
            int byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
            {
                return byName;
            }

            int byPrimaryCategory = string.Compare(
                DeckCategoryOrdering.PrimaryCategory(left),
                DeckCategoryOrdering.PrimaryCategory(right),
                StringComparison.OrdinalIgnoreCase);
            if (byPrimaryCategory != 0)
            {
                return byPrimaryCategory;
            }

            return string.Compare(
                NormalizeFingerprintValues(left.Categories),
                NormalizeFingerprintValues(right.Categories),
                StringComparison.OrdinalIgnoreCase);
        });

        return ordered;
    }

    /// <summary>
    /// Normalizes one fingerprint field.
    /// </summary>
    private static string NormalizeFingerprintValue(string? value)
    {
        return (value ?? "").Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a list-like fingerprint field.
    /// </summary>
    private static string NormalizeFingerprintValues(IEnumerable<string> values)
    {
        List<string> normalized = [];
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                normalized.Add(NormalizeFingerprintValue(value));
            }
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(',', normalized);
    }

    /// <summary>
    /// Hashes a canonical fingerprint payload.
    /// </summary>
    private static string HashFingerprint(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
