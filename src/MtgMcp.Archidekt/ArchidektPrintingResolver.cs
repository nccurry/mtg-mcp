using System.Globalization;
using System.Text.Json;
using MtgMcp.Core.Decks;

namespace MtgMcp.Archidekt;

/// <summary>
/// Carries one page of Archidekt's card search: the results, how many printings match in all, and the query of the
/// next page when there is one.
/// </summary>
internal sealed record ArchidektCardSearchPage(
    IReadOnlyList<JsonElement> Results,
    int? Count,
    string? NextQuery);

/// <summary>
/// Names the Archidekt printing chosen for one card to add, and why.
/// </summary>
internal sealed record ArchidektPrintingChoice(string ProviderCardId, string Description);

/// <summary>
/// Holds the Archidekt printings chosen for the cards a push adds, so a later step reuses them without another
/// search, plus the cards that could not be placed.
/// </summary>
public sealed class ArchidektPrintingChoices
{
    /// <summary>
    /// Holds the choices by entry key.
    /// </summary>
    private readonly Dictionary<string, ArchidektPrintingChoice> chosen = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds the names of the cards no printing was found for, in first-seen order.
    /// </summary>
    private readonly List<string> unresolved = [];

    /// <summary>
    /// Gets the names of the cards no printing was found for, or that were not searched because the request limit
    /// was reached.
    /// </summary>
    public IReadOnlyList<string> UnresolvedCardNames => unresolved;

    /// <summary>
    /// Gets true when the search stopped because the operation reached its provider request limit.
    /// </summary>
    public bool StoppedAtRequestLimit { get; internal set; }

    /// <summary>
    /// Gets how many provider requests the searches used.
    /// </summary>
    public int SearchRequests { get; internal set; }

    /// <summary>
    /// Finds the choice already made for an entry, or null.
    /// </summary>
    internal ArchidektPrintingChoice? Find(RemoteDeckEntry entry)
    {
        return chosen.GetValueOrDefault(Key(entry));
    }

    /// <summary>
    /// Records the printing chosen for an entry.
    /// </summary>
    internal void Add(RemoteDeckEntry entry, ArchidektPrintingChoice choice)
    {
        chosen[Key(entry)] = choice;
    }

    /// <summary>
    /// Records a card no printing was found for.
    /// </summary>
    internal void AddUnresolved(string cardName)
    {
        if (!unresolved.Contains(cardName, StringComparer.OrdinalIgnoreCase))
        {
            unresolved.Add(cardName);
        }
    }

    /// <summary>
    /// Returns the target with the chosen Archidekt card ID set on every entry that has none.
    /// </summary>
    internal RemoteDeckSnapshot ApplyTo(RemoteDeckSnapshot target)
    {
        List<RemoteDeckEntry> entries = [];
        foreach (RemoteDeckEntry entry in target.Entries)
        {
            ArchidektPrintingChoice? choice = string.IsNullOrWhiteSpace(entry.ProviderCardId)
                ? Find(entry)
                : null;
            entries.Add(choice is null ? entry : entry with { ProviderCardId = choice.ProviderCardId });
        }

        return target with { Entries = entries };
    }

    /// <summary>
    /// Builds the key that says two entries ask for the same printing of the same card.
    /// </summary>
    internal static string Key(RemoteDeckEntry entry)
    {
        return string.Join(
            '|',
            entry.CardName.ToLowerInvariant(),
            entry.OracleId?.ToString("D", CultureInfo.InvariantCulture),
            entry.PrintingId?.ToString("D", CultureInfo.InvariantCulture),
            entry.SetCode?.ToLowerInvariant(),
            entry.CollectorNumber?.ToLowerInvariant());
    }
}

/// <summary>
/// Picks the Archidekt printing to add for each new card of a push, with one search per distinct card name, so the
/// preview and the apply agree and a push never sends a card it could not place.
/// </summary>
internal sealed class ArchidektPrintingResolver
{
    /// <summary>
    /// Gives the most search pages read for one card name.
    /// </summary>
    private const int MaximumPagesPerName = 12;

    /// <summary>
    /// Sends the card search requests.
    /// </summary>
    private readonly ArchidektDeckTransport transport;

    /// <summary>
    /// Creates a resolver over the deck provider routes.
    /// </summary>
    internal ArchidektPrintingResolver(ArchidektDeckTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Chooses a printing for every entry that has no choice yet, and records the choices and the failures in
    /// <paramref name="choices"/>. A provider failure other than the request limit is thrown.
    /// </summary>
    internal async Task ResolveAsync(
        IEnumerable<RemoteDeckEntry> entries,
        ArchidektPrintingChoices choices,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        Dictionary<string, NameSearch> searches = new(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteDeckEntry entry in entries)
        {
            if (choices.Find(entry) is not null)
            {
                continue;
            }

            if (choices.StoppedAtRequestLimit)
            {
                choices.AddUnresolved(entry.CardName);
                continue;
            }

            if (!searches.TryGetValue(entry.CardName, out NameSearch? search))
            {
                search = new NameSearch();
                searches[entry.CardName] = search;
            }

            try
            {
                ArchidektPrintingChoice? choice = await ChooseAsync(entry, search, choices, budget, cancellationToken)
                    .ConfigureAwait(false);
                if (choice is null)
                {
                    choices.AddUnresolved(entry.CardName);
                }
                else
                {
                    choices.Add(entry, choice);
                }
            }
            catch (ArchidektProviderException exception) when (exception.ReasonCode == "request-limit-exceeded")
            {
                choices.StoppedAtRequestLimit = true;
                choices.AddUnresolved(entry.CardName);
            }
        }
    }

    /// <summary>
    /// Finds the printing for one entry, reading more search pages only while the printing it asks for is not found.
    /// </summary>
    private async Task<ArchidektPrintingChoice?> ChooseAsync(
        RemoteDeckEntry entry,
        NameSearch search,
        ArchidektPrintingChoices choices,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        if (search.Pages == 0)
        {
            await ReadPageAsync(entry, search, choices, budget, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            ArchidektPrintingChoice? found = Find(entry, search, exhausted: false);
            if (found is not null)
            {
                return found;
            }

            if (search.NextQuery is null || search.Pages >= MaximumPagesPerName)
            {
                return Find(entry, search, exhausted: true);
            }

            await ReadPageAsync(entry, search, choices, budget, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the next search page for a name and keeps its exact-name results.
    /// </summary>
    private async Task ReadPageAsync(
        RemoteDeckEntry entry,
        NameSearch search,
        ArchidektPrintingChoices choices,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArchidektCardSearchPage page = await transport.SearchCardsAsync(
            entry.CardName,
            search.NextQuery,
            budget,
            cancellationToken).ConfigureAwait(false);
        choices.SearchRequests++;
        search.Pages++;
        search.Count ??= page.Count;
        search.NextQuery = page.NextQuery;
        foreach (JsonElement candidate in page.Results)
        {
            if (CardNames.Match(OracleName(candidate), entry.CardName))
            {
                search.Matches.Add(candidate);
            }
        }
    }

    /// <summary>
    /// Applies the match rules in order to the results read so far. A printing that was asked for is looked for by its
    /// Scryfall ID, then by set and collector number; an entry with no printing takes the first listed match. When the
    /// entry has a printing ID, the set rule waits until every page was read, so the ID wins over a set match.
    /// </summary>
    private static ArchidektPrintingChoice? Find(RemoteDeckEntry entry, NameSearch search, bool exhausted)
    {
        List<JsonElement> candidates = entry.OracleId is { } oracleId
            ? [.. search.Matches.Where(value => OracleId(value) == oracleId)]
            : search.Matches;
        if (entry.PrintingId is { } printingId)
        {
            foreach (JsonElement candidate in candidates)
            {
                if (PrintingId(candidate) == printingId)
                {
                    return Describe(candidate, "the printing set on the entry");
                }
            }
        }

        if (entry.SetCode is not null)
        {
            return entry.PrintingId is null || exhausted
                ? FindBySet(entry, candidates)
                : null;
        }

        if (entry.PrintingId is null && candidates.Count > 0)
        {
            int listed = search.Count ?? search.Matches.Count;
            return Describe(
                candidates[0],
                $"default of {listed.ToString(CultureInfo.InvariantCulture)}; no printing was set");
        }

        return null;
    }

    /// <summary>
    /// Finds the first result in the entry's set, with its collector number when the entry has one.
    /// </summary>
    private static ArchidektPrintingChoice? FindBySet(RemoteDeckEntry entry, List<JsonElement> candidates)
    {
        foreach (JsonElement candidate in candidates)
        {
            if (string.Equals(SetCode(candidate), entry.SetCode, StringComparison.OrdinalIgnoreCase) &&
                (entry.CollectorNumber is null ||
                 string.Equals(CollectorNumber(candidate), entry.CollectorNumber, StringComparison.OrdinalIgnoreCase)))
            {
                return Describe(candidate, "the set and collector number on the entry");
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the choice for one search result.
    /// </summary>
    private static ArchidektPrintingChoice? Describe(JsonElement candidate, string reason)
    {
        if (!ArchidektProviderId.TryRead(candidate, "id", out string? cardId))
        {
            return null;
        }

        string set = SetCode(candidate) ?? "?";
        string collector = CollectorNumber(candidate) ?? "?";
        return new ArchidektPrintingChoice(cardId!, $"Archidekt printing {set} {collector} ({reason}).");
    }

    /// <summary>
    /// Reads the card name from the result's oracle card.
    /// </summary>
    private static string? OracleName(JsonElement candidate)
    {
        return candidate.TryGetProperty("oracleCard", out JsonElement oracle) &&
            oracle.ValueKind == JsonValueKind.Object &&
            oracle.TryGetProperty("name", out JsonElement name) &&
            name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
    }

    /// <summary>
    /// Reads the Oracle ID from the result's oracle card.
    /// </summary>
    private static Guid? OracleId(JsonElement candidate)
    {
        return candidate.TryGetProperty("oracleCard", out JsonElement oracle) &&
            oracle.ValueKind == JsonValueKind.Object &&
            oracle.TryGetProperty("uid", out JsonElement uid) &&
            uid.ValueKind == JsonValueKind.String &&
            Guid.TryParse(uid.GetString(), out Guid value)
            ? value
            : null;
    }

    /// <summary>
    /// Reads the Scryfall printing ID, which Archidekt calls <c>uid</c>.
    /// </summary>
    private static Guid? PrintingId(JsonElement candidate)
    {
        return candidate.TryGetProperty("uid", out JsonElement uid) &&
            uid.ValueKind == JsonValueKind.String &&
            Guid.TryParse(uid.GetString(), out Guid value)
            ? value
            : null;
    }

    /// <summary>
    /// Reads the set code from <c>edition.editioncode</c>, falling back to a top-level <c>setCode</c>.
    /// </summary>
    private static string? SetCode(JsonElement candidate)
    {
        if (candidate.TryGetProperty("edition", out JsonElement edition) &&
            edition.ValueKind == JsonValueKind.Object &&
            edition.TryGetProperty("editioncode", out JsonElement code) &&
            code.ValueKind == JsonValueKind.String)
        {
            return code.GetString();
        }

        return candidate.TryGetProperty("setCode", out JsonElement set) && set.ValueKind == JsonValueKind.String
            ? set.GetString()
            : null;
    }

    /// <summary>
    /// Reads the collector number.
    /// </summary>
    private static string? CollectorNumber(JsonElement candidate)
    {
        return candidate.TryGetProperty("collectorNumber", out JsonElement number) &&
            number.ValueKind == JsonValueKind.String
            ? number.GetString()
            : null;
    }

    /// <summary>
    /// Holds what the search for one card name has read so far.
    /// </summary>
    private sealed class NameSearch
    {
        /// <summary>
        /// Gets the exact-name results read so far, in the order Archidekt listed them.
        /// </summary>
        internal List<JsonElement> Matches { get; } = [];

        /// <summary>
        /// Gets or sets the number of pages read.
        /// </summary>
        internal int Pages { get; set; }

        /// <summary>
        /// Gets or sets how many printings Archidekt says match the search.
        /// </summary>
        internal int? Count { get; set; }

        /// <summary>
        /// Gets or sets the query of the next page, or null when the last page was read.
        /// </summary>
        internal string? NextQuery { get; set; }
    }
}
