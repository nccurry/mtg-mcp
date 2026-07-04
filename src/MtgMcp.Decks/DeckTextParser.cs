using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Parses bounded manual deck text without card lookup, network access, or identity invention.
/// </summary>
internal static partial class DeckTextParser
{
    /// <summary>
    /// Parses one supported text dialect into a deterministic local proposal.
    /// </summary>
    internal static DeckImportProposal Parse(
        string formatId,
        string content,
        DeckImportOptions options,
        List<DeckInterchangeDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> categoryIds = new(StringComparer.OrdinalIgnoreCase);
        List<DeckCategory> categories = [];
        List<DeckEntry> entries = [];
        List<DeckCategoryAssignment> assignments = [];
        string zone = NormalizeZone(options.DefaultZone);
        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryReadSection(line, out string section))
            {
                zone = section;
                continue;
            }

            Match quantity = QuantityLine().Match(line);
            if (!quantity.Success || !int.TryParse(
                    quantity.Groups["quantity"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int count) || count <= 0)
            {
                diagnostics.Add(new DeckInterchangeDiagnostic(
                    "error",
                    "unrecognized-line",
                    "The line is not a positive quantity followed by a card name.",
                    index + 1));
                continue;
            }

            string remainder = quantity.Groups["card"].Value.Trim();
            bool foil = RemoveMarker(ref remainder, "*F*");
            bool etched = RemoveMarker(ref remainder, "*E*");
            List<string> tagNames = formatId switch
            {
                DeckInterchangeCatalog.Archidekt => ReadArchidektCategory(ref remainder),
                DeckInterchangeCatalog.Moxfield => ReadMoxfieldTags(ref remainder),
                _ => [],
            };
            (string? setCode, string? collectorNumber) = ReadPrinting(ref remainder);
            if (remainder.Length == 0)
            {
                diagnostics.Add(new DeckInterchangeDiagnostic(
                    "error",
                    "missing-card-name",
                    "The line does not contain a card name.",
                    index + 1));
                continue;
            }

            Guid entryId = StableId($"entry\n{formatId}\n{index + 1}\n{line}");
            entries.Add(new DeckEntry(
                entryId,
                count,
                remainder,
                null,
                null,
                setCode,
                collectorNumber,
                "en",
                etched ? "etched" : foil ? "foil" : "nonfoil",
                zone,
                entries.Count));
            for (int tagIndex = 0; tagIndex < tagNames.Count; tagIndex++)
            {
                string tagName = tagNames[tagIndex];
                if (!categoryIds.TryGetValue(tagName, out Guid categoryId))
                {
                    categoryId = StableId($"category\n{formatId}\n{tagName.ToUpperInvariant()}");
                    categoryIds.Add(tagName, categoryId);
                    categories.Add(new DeckCategory(categoryId, tagName, null, categories.Count));
                }

                assignments.Add(new DeckCategoryAssignment(entryId, categoryId, tagIndex == 0));
            }
        }

        return new DeckImportProposal(
            null,
            null,
            null,
            null,
            string.IsNullOrWhiteSpace(options.DeckName) ? "Imported Deck" : options.DeckName.Trim(),
            options.Description?.Trim() ?? string.Empty,
            NormalizeRequired(options.Format, "commander"),
            entries,
            categories,
            assignments,
            [],
            []);
    }

    /// <summary>
    /// Reads a bracketed or colon-terminated zone heading.
    /// </summary>
    private static bool TryReadSection(string line, out string zone)
    {
        string candidate = line;
        if (candidate.StartsWith('[', StringComparison.Ordinal) &&
            candidate.EndsWith(']', StringComparison.Ordinal))
        {
            candidate = candidate[1..^1];
        }
        else if (candidate.EndsWith(':', StringComparison.Ordinal))
        {
            candidate = candidate[..^1];
        }
        else
        {
            zone = string.Empty;
            return false;
        }

        zone = NormalizeZone(candidate);
        return zone.Length > 0;
    }

    /// <summary>
    /// Maps common manual section labels into format-neutral local zones.
    /// </summary>
    private static string NormalizeZone(string? value)
    {
        string normalized = NormalizeRequired(value, "main");
        return normalized switch
        {
            "mainboard" or "deck" => "main",
            "commanders" => "commander",
            "sideboard" => "sideboard",
            "maybeboard" or "considering" => "maybeboard",
            _ => normalized,
        };
    }

    /// <summary>
    /// Returns normalized required vocabulary or the supplied default.
    /// </summary>
    private static string NormalizeRequired(string? value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Removes one case-insensitive finish marker from the card text.
    /// </summary>
    private static bool RemoveMarker(ref string value, string marker)
    {
        int index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        value = string.Concat(value.AsSpan(0, index), value.AsSpan(index + marker.Length)).Trim();
        return true;
    }

    /// <summary>
    /// Removes the one trailing Archidekt backtick category when present.
    /// </summary>
    private static List<string> ReadArchidektCategory(ref string value)
    {
        Match match = ArchidektCategory().Match(value);
        if (!match.Success)
        {
            return [];
        }

        value = value[..match.Index].TrimEnd();
        string category = match.Groups["category"].Value.Trim();
        return category.Length == 0 ? [] : [category];
    }

    /// <summary>
    /// Removes trailing Moxfield local or global tags while preserving their names as local categories.
    /// </summary>
    private static List<string> ReadMoxfieldTags(ref string value)
    {
        MatchCollection matches = MoxfieldTag().Matches(value);
        if (matches.Count == 0)
        {
            return [];
        }

        List<string> names = [];
        foreach (Match match in matches)
        {
            string name = match.Groups["tag"].Value.Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        value = value[..matches[0].Index].TrimEnd();
        return names;
    }

    /// <summary>
    /// Removes an optional trailing set code and collector number.
    /// </summary>
    private static (string? SetCode, string? CollectorNumber) ReadPrinting(ref string value)
    {
        Match match = PrintingHint().Match(value);
        if (!match.Success)
        {
            return (null, null);
        }

        value = value[..match.Index].TrimEnd();
        string setCode = match.Groups["set"].Value.ToLowerInvariant();
        string collector = match.Groups["collector"].Success
            ? match.Groups["collector"].Value
            : string.Empty;
        return (setCode, collector.Length == 0 ? null : collector);
    }

    /// <summary>
    /// Derives a stable RFC 4122 identifier from normalized proposal evidence.
    /// </summary>
    private static Guid StableId(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }

    /// <summary>
    /// Matches positive quantity prefixes with optional conventional x suffixes.
    /// </summary>
    [GeneratedRegex(@"^(?<quantity>\d+)x?\s+(?<card>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityLine();

    /// <summary>
    /// Matches one trailing Archidekt category surrounded by backticks.
    /// </summary>
    [GeneratedRegex(@"\s+`(?<category>[^`]*)`\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchidektCategory();

    /// <summary>
    /// Matches trailing Moxfield tag segments.
    /// </summary>
    [GeneratedRegex(@"\s+#!?(?<tag>[^#]+?)(?=\s+#!?|$)", RegexOptions.CultureInvariant)]
    private static partial Regex MoxfieldTag();

    /// <summary>
    /// Matches an optional exact-printing suffix.
    /// </summary>
    [GeneratedRegex(@"\s+\((?<set>[A-Za-z0-9]+)\)(?:\s+(?<collector>\S+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PrintingHint();
}
