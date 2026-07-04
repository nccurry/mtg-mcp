using System.Globalization;
using System.Text;
using System.Text.Json;
using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Produces ordered manual artifacts and explicit preservation evidence from one immutable snapshot.
/// </summary>
internal static class DeckArtifactWriter
{
    /// <summary>
    /// Generates one deterministic bounded bundle for a supported format.
    /// </summary>
    internal static DeckExportBundle Write(
        string formatId,
        DeckInterchangeSnapshot snapshot,
        DeckExportOptions options)
    {
        DeckInterchangeFormat format = DeckInterchangeCatalog.Find(formatId) ??
            throw new ArgumentException("The interchange format is not registered.", nameof(formatId));
        IReadOnlyList<DeckFieldPreservation> preservation = BuildPreservation(formatId, options);
        List<DeckExportArtifact> artifacts = [];
        string native = DeckInterchangeCodec.SerializeNative(snapshot);
        switch (formatId)
        {
            case DeckInterchangeCatalog.Native:
                Add(artifacts, "deck.mtg-mcp.json", "application/json", native, "Lossless local deck document.");
                break;
            case DeckInterchangeCatalog.Generic:
                Add(artifacts, "deck.txt", "text/plain", WriteGeneric(snapshot.Deck), "Manual generic deck list.");
                Add(artifacts, "deck.mtg-mcp.json", "application/json", native, "Lossless local deck companion.");
                break;
            case DeckInterchangeCatalog.Archidekt:
                Add(
                    artifacts,
                    "deck.archidekt.txt",
                    "text/plain",
                    WriteArchidekt(snapshot.Deck),
                    "Archidekt manual import text.");
                Add(
                    artifacts,
                    "category-assignments.csv",
                    "text/csv",
                    WriteCategories(snapshot.Deck),
                    "Complete category assignment companion.");
                Add(artifacts, "deck.mtg-mcp.json", "application/json", native, "Lossless local deck companion.");
                break;
            case DeckInterchangeCatalog.Moxfield:
                Add(
                    artifacts,
                    "deck.moxfield.txt",
                    "text/plain",
                    WriteMoxfield(snapshot.Deck, options),
                    "Moxfield Bulk Edit text.");
                Add(
                    artifacts,
                    "category-assignments.csv",
                    "text/csv",
                    WriteCategories(snapshot.Deck),
                    "Complete category assignment companion.");
                Add(artifacts, "deck.mtg-mcp.json", "application/json", native, "Lossless local deck companion.");
                break;
        }

        string preservationJson = JsonSerializer.Serialize(preservation, DeckInterchangeCodec.Options) + "\n";
        Add(
            artifacts,
            "preservation.json",
            "application/json",
            preservationJson,
            "Machine-readable field preservation report.");
        if (formatId is DeckInterchangeCatalog.Archidekt or DeckInterchangeCatalog.Moxfield)
        {
            string provider = formatId == DeckInterchangeCatalog.Archidekt ? "Archidekt" : "Moxfield";
            string readme = $"{provider} manual interchange\n\n" +
                "Paste the provider text artifact manually, inspect the provider result, " +
                "and retain the native JSON and category-assignment companions for fields " +
                "the provider text cannot represent.\n";
            Add(artifacts, "README.txt", "text/plain", readme, "Manual import instructions and preservation limits.");
        }

        return new DeckExportBundle(
            1,
            formatId,
            snapshot.Deck.DeckId,
            snapshot.Deck.Revision,
            snapshot.Deck.UpdatedAtUtc,
            format.Status,
            artifacts,
            preservation);
    }

    /// <summary>
    /// Writes generic text grouped by canonical zone and local entry order.
    /// </summary>
    private static string WriteGeneric(DeckDocument deck)
    {
        StringBuilder builder = new();
        foreach (IGrouping<string, DeckEntry> zone in deck.Entries.GroupBy(value => value.Zone))
        {
            builder.Append('[').Append(zone.Key).Append("]\n");
            foreach (DeckEntry entry in zone)
            {
                builder.Append(WriteBaseLine(entry)).Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes Archidekt lines with at most one primary backtick category.
    /// </summary>
    private static string WriteArchidekt(DeckDocument deck)
    {
        Dictionary<Guid, string> categories = deck.Categories.ToDictionary(
            value => value.CategoryId,
            value => value.Name);
        Dictionary<Guid, DeckCategoryAssignment> primary = deck.CategoryAssignments
            .Where(value => value.IsPrimary)
            .ToDictionary(value => value.EntryId);
        StringBuilder builder = new();
        foreach (DeckEntry entry in deck.Entries)
        {
            if (entry.Zone == "excluded")
            {
                continue;
            }

            builder.Append(WriteBaseLine(entry));
            if (primary.TryGetValue(entry.EntryId, out DeckCategoryAssignment? assignment) &&
                categories.TryGetValue(assignment.CategoryId, out string? category))
            {
                builder.Append(" `").Append(category.Replace("`", "'", StringComparison.Ordinal)).Append('`');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes accepted Moxfield Bulk Edit lines with explicit local or global tag scope.
    /// </summary>
    private static string WriteMoxfield(DeckDocument deck, DeckExportOptions options)
    {
        Dictionary<Guid, string> categoryNames = deck.Categories.ToDictionary(
            value => value.CategoryId,
            value => value.Name);
        Dictionary<Guid, List<DeckCategoryAssignment>> assignments = deck.CategoryAssignments
            .GroupBy(value => value.EntryId)
            .ToDictionary(value => value.Key, value => value.ToList());
        StringBuilder builder = new();
        foreach (DeckEntry entry in deck.Entries)
        {
            if (entry.Zone == "excluded")
            {
                continue;
            }

            builder.Append(WriteBaseLine(entry));
            if (entry.Finish == "foil")
            {
                builder.Append(" *F*");
            }
            else if (entry.Finish == "etched")
            {
                builder.Append(" *E*");
            }

            if (assignments.TryGetValue(entry.EntryId, out List<DeckCategoryAssignment>? entryAssignments))
            {
                AppendMoxfieldTags(builder, entryAssignments, categoryNames, options.UseGlobalMoxfieldTags);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends ordered Moxfield tags without changing category scope implicitly.
    /// </summary>
    private static void AppendMoxfieldTags(
        StringBuilder builder,
        IReadOnlyList<DeckCategoryAssignment> assignments,
        IReadOnlyDictionary<Guid, string> categoryNames,
        bool useGlobalTags)
    {
        foreach (DeckCategoryAssignment assignment in assignments)
        {
            if (categoryNames.TryGetValue(assignment.CategoryId, out string? category))
            {
                builder.Append(useGlobalTags ? " #!" : " #").Append(category);
            }
        }
    }

    /// <summary>
    /// Writes exact quantity, name, and optional printing hints shared by manual formats.
    /// </summary>
    private static string WriteBaseLine(DeckEntry entry)
    {
        StringBuilder builder = new();
        builder.Append(entry.Quantity.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(entry.CardName);
        if (entry.SetCode is not null)
        {
            builder.Append(" (").Append(entry.SetCode.ToUpperInvariant()).Append(')');
            if (entry.CollectorNumber is not null)
            {
                builder.Append(' ').Append(entry.CollectorNumber);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes every category assignment to a deterministic RFC 4180-style companion table.
    /// </summary>
    private static string WriteCategories(DeckDocument deck)
    {
        Dictionary<Guid, string> categoryNames = deck.Categories.ToDictionary(
            value => value.CategoryId,
            value => value.Name);
        StringBuilder builder = new("entry_id,card_name,category,is_primary\n");
        Dictionary<Guid, DeckEntry> entries = deck.Entries.ToDictionary(value => value.EntryId);
        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (entries.TryGetValue(assignment.EntryId, out DeckEntry? entry) &&
                categoryNames.TryGetValue(assignment.CategoryId, out string? category))
            {
                builder.Append(Csv(entry.EntryId.ToString("D"))).Append(',')
                    .Append(Csv(entry.CardName)).Append(',')
                    .Append(Csv(category)).Append(',')
                    .Append(assignment.IsPrimary ? "true" : "false")
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes one CSV field without relying on locale-sensitive serializers.
    /// </summary>
    private static string Csv(string value)
    {
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    /// <summary>
    /// Describes field representation for the selected target format.
    /// </summary>
    private static IReadOnlyList<DeckFieldPreservation> BuildPreservation(
        string formatId,
        DeckExportOptions options)
    {
        if (formatId == DeckInterchangeCatalog.Native)
        {
            return [new DeckFieldPreservation("all-local-fields", "preserved", "Native JSON is lossless.")];
        }

        bool generic = formatId == DeckInterchangeCatalog.Generic;
        bool archidekt = formatId == DeckInterchangeCatalog.Archidekt;
        bool moxfield = formatId == DeckInterchangeCatalog.Moxfield;
        bool verifiedMoxfieldTags = moxfield && !options.UseGlobalMoxfieldTags;
        string finishStatus = moxfield ? "preserved" : "companion-only";
        string finishDetail = "Preserved in deck.mtg-mcp.json.";
        if (moxfield)
        {
            finishDetail = "Foil and etched markers were verified by current manual provider acceptance.";
        }
        else if (archidekt)
        {
            finishDetail = "Distinct same-print finishes can merge; preserved in deck.mtg-mcp.json.";
        }

        string primaryCategoryStatus = "companion-only";
        string primaryCategoryDetail = "Preserved only in native JSON.";
        string secondaryCategoryStatus = "companion-only";
        string secondaryCategoryDetail = "Preserved in category-assignments.csv and deck.mtg-mcp.json.";
        if (archidekt)
        {
            primaryCategoryStatus = "preserved";
            primaryCategoryDetail = "One primary category per line was verified by current manual acceptance.";
        }
        else if (verifiedMoxfieldTags)
        {
            primaryCategoryStatus = "preserved";
            primaryCategoryDetail = "Local tag emission was verified by current manual acceptance.";
            secondaryCategoryStatus = "preserved";
            secondaryCategoryDetail = "Multiple local tags were verified by current manual acceptance.";
        }
        else if (moxfield)
        {
            const string unverifiedGlobalTags =
                "Global tag application is unverified; assignments remain in the companions.";
            primaryCategoryDetail = unverifiedGlobalTags;
            secondaryCategoryDetail = unverifiedGlobalTags;
        }

        return
        [
            new DeckFieldPreservation(
                "quantity-name",
                "preserved",
                generic ? "Represented in deck.txt." : "Verified by current manual provider acceptance."),
            new DeckFieldPreservation(
                "zone",
                generic ? "preserved" : "companion-only",
                generic
                    ? "Represented by section headings."
                    : "Provider text has no board sections; preserved in deck.mtg-mcp.json."),
            new DeckFieldPreservation(
                "printing-hints",
                "preserved",
                generic
                    ? "Set and collector are represented when present."
                    : "Set and collector were verified by current manual provider acceptance."),
            new DeckFieldPreservation(
                "finishes",
                finishStatus,
                finishDetail),
            new DeckFieldPreservation("stable-identities", "companion-only", "Preserved in deck.mtg-mcp.json."),
            new DeckFieldPreservation("lifecycle-and-bindings", "companion-only", "Preserved in deck.mtg-mcp.json."),
            new DeckFieldPreservation(
                "primary-category",
                primaryCategoryStatus,
                primaryCategoryDetail),
            new DeckFieldPreservation(
                "secondary-categories",
                secondaryCategoryStatus,
                secondaryCategoryDetail),
            new DeckFieldPreservation(
                "excluded-entries",
                generic ? "preserved" : "companion-only",
                generic
                    ? "Represented by the excluded section."
                    : "Excluded entries are omitted from provider text and preserved in deck.mtg-mcp.json."),
        ];
    }

    /// <summary>
    /// Adds an artifact together with its exact content checksum.
    /// </summary>
    private static void Add(
        List<DeckExportArtifact> artifacts,
        string name,
        string mediaType,
        string content,
        string purpose)
    {
        artifacts.Add(new DeckExportArtifact(
            name,
            mediaType,
            content,
            DeckInterchangeCodec.Sha256(content),
            purpose));
    }
}
