using MtgMcp.App.Decks;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Holds what a caller asked for when reading the entries of one deck: which zones, and how many fields per entry.
/// </summary>
/// <param name="Zones">The zones to show, or empty for every zone.</param>
/// <param name="Full">True for every entry field, false for the concise fields.</param>
internal sealed record ArchidektDeckReadOptions(IReadOnlyList<string> Zones, bool Full)
{
    /// <summary>
    /// Gets the default read: every zone, concise entries.
    /// </summary>
    internal static ArchidektDeckReadOptions Default { get; } = new([], Full: false);

    /// <summary>
    /// Gets the zone names a read may ask for, in the order entries are shown.
    /// </summary>
    internal static IReadOnlyList<string> ZoneOrder => DeckZoneCounts.ZoneOrder;

    /// <summary>
    /// Checks the zone names and the response format a caller passed and builds the options, or refuses with the name
    /// of the argument at fault.
    /// </summary>
    internal static OperationResult<ArchidektDeckReadOptions> Parse(
        IReadOnlyList<string>? zones,
        string? responseFormat)
    {
        bool full;
        switch (responseFormat?.Trim().ToLowerInvariant())
        {
            case null or "concise":
                full = false;
                break;
            case "standard" or "full":
                full = true;
                break;
            default:
                return new OperationInvalidInput(
                    "invalid-response-format",
                    "responseFormat must be concise, standard, or full.",
                    "responseFormat");
        }

        List<string> chosen = [];
        for (int index = 0; index < (zones?.Count ?? 0); index++)
        {
            string? zone = ZoneOrder.FirstOrDefault(name =>
                string.Equals(name, zones![index]?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (zone is null)
            {
                return new OperationInvalidInput(
                    "invalid-zone",
                    $"Unknown zone '{zones![index]}'. Allowed values: {string.Join(", ", ZoneOrder)}.",
                    $"zones[{index}]");
            }

            if (!chosen.Contains(zone, StringComparer.Ordinal))
            {
                chosen.Add(zone);
            }
        }

        return new OperationSuccess<ArchidektDeckReadOptions>(new ArchidektDeckReadOptions(chosen, full));
    }

    /// <summary>
    /// Gives the place of a zone in the order entries are shown; a zone the list does not know comes last.
    /// </summary>
    internal static int ZoneRank(string zone)
    {
        for (int index = 0; index < ZoneOrder.Count; index++)
        {
            if (string.Equals(ZoneOrder[index], zone, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return ZoneOrder.Count;
    }

    /// <summary>
    /// Reports whether an entry's zone is one the read asked for.
    /// </summary>
    internal bool Shows(string zone)
    {
        return Zones.Count == 0 || Zones.Contains(zone, StringComparer.Ordinal);
    }
}

/// <summary>
/// Works out the facts the deck summary states about the whole deck. It does no input or output and gives no verdict.
/// </summary>
internal static class ArchidektDeckFacts
{
    /// <summary>
    /// Sets the most duplicate groups a summary lists before it counts the rest as omitted.
    /// </summary>
    internal const int MaximumDuplicateGroups = 25;

    /// <summary>
    /// Sets the number of cards a Commander deck holds.
    /// </summary>
    private const int CommanderDeckSize = 100;

    /// <summary>
    /// Builds the summary of a deck: counts, duplicates of the commander and main zones, repeats in the other zones,
    /// and the size fact.
    /// </summary>
    internal static ArchidektDeckSummary Summarize(RemoteDeckSnapshot deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        IReadOnlyDictionary<string, int> cardsByZone = DeckZoneCounts.Of(
            deck.Entries.Select(entry => (entry.Zone, entry.Quantity)));
        List<ArchidektDuplicateGroup> duplicates = FindDuplicates(deck);
        List<ArchidektDuplicateGroup> repeats = FindRepeatsOutsideMain(deck);
        int mainAndCommander = cardsByZone.GetValueOrDefault("main") + cardsByZone.GetValueOrDefault("commander");
        return new ArchidektDeckSummary(
            deck.Entries.Count,
            deck.Entries.Sum(value => value.Quantity),
            deck.Categories.Count,
            cardsByZone,
            [.. duplicates.Take(MaximumDuplicateGroups)],
            [.. repeats.Take(MaximumDuplicateGroups)])
        {
            DuplicatesOmitted = duplicates.Count > MaximumDuplicateGroups
                ? duplicates.Count - MaximumDuplicateGroups
                : null,
            RepeatsOutsideMainOmitted = repeats.Count > MaximumDuplicateGroups
                ? repeats.Count - MaximumDuplicateGroups
                : null,
            Size = deck.Format == "commander"
                ? new ArchidektDeckSizeFact("commander", CommanderDeckSize, mainAndCommander)
                : null,
        };
    }

    /// <summary>
    /// Finds the cards held more than once across the commander and main zones. A card is the same card when its
    /// Oracle ID matches, or its name when an entry has no Oracle ID. Basic lands are skipped, and so is any entry that
    /// is not in the deck. Groups come in name order.
    /// </summary>
    private static List<ArchidektDuplicateGroup> FindDuplicates(RemoteDeckSnapshot deck)
    {
        return [.. GroupSameCard(
                deck,
                entry => entry.Zone is "commander" or "main" && deck.IsInDeck(entry),
                _ => string.Empty)
            .Select(members => new ArchidektDuplicateGroup(
                members.OrderBy(value => value.CardName, StringComparer.Ordinal).First().CardName,
                members[0].OracleId,
                members.Sum(value => value.Quantity),
                [.. members.Select(value => value.Zone)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(ArchidektDeckReadOptions.ZoneRank)]))
            .OrderBy(group => group.CardName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.OracleId)];
    }

    /// <summary>
    /// Finds the cards held more than once inside the sideboard or the maybeboard. Each zone is read on its own, so a
    /// card that sits once in the sideboard and once in the maybeboard is not a repeat. A card is the same card when
    /// its Oracle ID matches, or its name when an entry has no Oracle ID. Basic lands are skipped. Groups come in zone
    /// order, then name order.
    /// </summary>
    private static List<ArchidektDuplicateGroup> FindRepeatsOutsideMain(RemoteDeckSnapshot deck)
    {
        return [.. GroupSameCard(
                deck,
                entry => entry.Zone is not ("commander" or "main"),
                entry => entry.Zone)
            .Select(members => new ArchidektDuplicateGroup(
                members.OrderBy(value => value.CardName, StringComparer.Ordinal).First().CardName,
                members[0].OracleId,
                members.Sum(value => value.Quantity),
                [members[0].Zone]))
            .OrderBy(group => ArchidektDeckReadOptions.ZoneRank(group.Zones[0]))
            .ThenBy(group => group.CardName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.OracleId)];
    }

    /// <summary>
    /// Groups the entries that are the same card, skipping basic lands and every entry the predicate refuses, and keeps
    /// the groups that hold more than one card. The key prefix lets a caller keep the groups of one zone apart.
    /// </summary>
    private static List<List<RemoteDeckEntry>> GroupSameCard(
        RemoteDeckSnapshot deck,
        Func<RemoteDeckEntry, bool> include,
        Func<RemoteDeckEntry, string> keyPrefix)
    {
        Dictionary<string, List<RemoteDeckEntry>> groups = new(StringComparer.Ordinal);
        foreach (RemoteDeckEntry entry in deck.Entries)
        {
            if (!include(entry) || BasicLandNames.Contains(entry.CardName))
            {
                continue;
            }

            string key = keyPrefix(entry) + "|" + (entry.OracleId is { } oracleId
                ? "oracle:" + oracleId.ToString("D")
                : "name:" + entry.CardName.Trim().ToLowerInvariant());
            if (!groups.TryGetValue(key, out List<RemoteDeckEntry>? members))
            {
                members = [];
                groups[key] = members;
            }

            members.Add(entry);
        }

        return [.. groups.Values.Where(members => members.Sum(value => value.Quantity) > 1)];
    }
}
