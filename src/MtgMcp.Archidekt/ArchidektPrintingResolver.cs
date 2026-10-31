using System.Globalization;
using MtgMcp.Core.Decks;

namespace MtgMcp.Archidekt;

/// <summary>
/// Describes one Archidekt card search: the card name, matched exactly, and the set to look in when the entry has one.
/// </summary>
internal sealed record ArchidektCardSearch(string CardName, string? SetCode, bool ExactName = true)
{
    /// <summary>
    /// Builds the query of the first page. Archidekt matches a name as part of longer names unless <c>exact=true</c> is
    /// sent, and filters by set with <c>edition</c>, which keeps a card with hundreds of printings, such as a basic
    /// land, inside the pages that are read.
    /// </summary>
    internal string FirstQuery()
    {
        string exact = ExactName ? "&exact=true" : string.Empty;
        string edition = SetCode is null ? string.Empty : $"&edition={Uri.EscapeDataString(SetCode)}";
        return $"?name={Uri.EscapeDataString(CardName)}{exact}{edition}&pageSize=25";
    }
}

/// <summary>
/// Carries one page of Archidekt's card search: the printings it names, how many results the page held, how many
/// printings match in all, and the query of the next page when there is one.
/// </summary>
internal sealed record ArchidektCardSearchPage(
    IReadOnlyList<ArchidektCardPrinting> Results,
    int ResultCount,
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
        Dictionary<string, CardSearch> searches = new(StringComparer.OrdinalIgnoreCase);
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

            string? setCode = ArchidektContract.Optional(entry.SetCode)?.ToLowerInvariant();
            string searchKey = $"{entry.CardName}|{setCode}";
            if (!searches.TryGetValue(searchKey, out CardSearch? search))
            {
                search = new CardSearch(new ArchidektCardSearch(entry.CardName, setCode));
                searches[searchKey] = search;
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
    /// An entry with a set code is looked for among the printings in that set, so a card with many printings is found
    /// within the pages that are read.
    /// </summary>
    private async Task<ArchidektPrintingChoice?> ChooseAsync(
        RemoteDeckEntry entry,
        CardSearch search,
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
    /// Reads the next search page for a name and keeps its exact-name results. A first page with no result at all is
    /// asked for again without <c>exact=true</c>, for a card that Archidekt names by both faces, since the name rule
    /// below keeps only the entry's own card.
    /// </summary>
    private async Task ReadPageAsync(
        RemoteDeckEntry entry,
        CardSearch search,
        ArchidektPrintingChoices choices,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArchidektCardSearchPage page = await transport.SearchCardsAsync(
            search.Query,
            search.NextQuery,
            budget,
            cancellationToken).ConfigureAwait(false);
        choices.SearchRequests++;
        if (search.Pages == 0 && page.ResultCount == 0 && search.Query.ExactName)
        {
            search.Query = search.Query with { ExactName = false };
            page = await transport.SearchCardsAsync(search.Query, nextQuery: null, budget, cancellationToken)
                .ConfigureAwait(false);
            choices.SearchRequests++;
        }

        search.Pages++;
        search.Count ??= page.Count;
        search.NextQuery = page.NextQuery;
        search.SkippedResults += page.ResultCount;
        foreach (ArchidektCardPrinting candidate in page.Results)
        {
            if (CardNames.Match(candidate.CardName, entry.CardName))
            {
                search.Matches.Add(candidate);
                search.SkippedResults--;
            }
        }
    }

    /// <summary>
    /// Applies the match rules in order to the results read so far. A printing that was asked for is looked for by its
    /// Scryfall ID, then by set and collector number; an entry with no printing takes the first listed match. When the
    /// entry has a printing ID, the set rule waits until every page was read, so the ID wins over a set match.
    /// </summary>
    private static ArchidektPrintingChoice? Find(RemoteDeckEntry entry, CardSearch search, bool exhausted)
    {
        List<ArchidektCardPrinting> candidates = entry.OracleId is { } oracleId
            ? [.. search.Matches.Where(value => value.OracleId == oracleId)]
            : search.Matches;
        if (entry.PrintingId is { } printingId)
        {
            foreach (ArchidektCardPrinting candidate in candidates)
            {
                if (candidate.PrintingId == printingId)
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
            // Counts only printings of this card: Archidekt's own count may include other cards, and a search that
            // is not read to the end cannot say more than what it was told, less what was seen not to match.
            int listed = search.NextQuery is null
                ? search.Matches.Count
                : Math.Max(search.Matches.Count, (search.Count ?? search.Matches.Count) - search.SkippedResults);
            return Describe(
                candidates[0],
                $"first of {listed.ToString(CultureInfo.InvariantCulture)} Archidekt printings of this name; " +
                "no printing was set");
        }

        return null;
    }

    /// <summary>
    /// Finds the first result in the entry's set, with its collector number when the entry has one.
    /// </summary>
    private static ArchidektPrintingChoice? FindBySet(RemoteDeckEntry entry, List<ArchidektCardPrinting> candidates)
    {
        foreach (ArchidektCardPrinting candidate in candidates)
        {
            if (string.Equals(candidate.SetCode, entry.SetCode, StringComparison.OrdinalIgnoreCase) &&
                (entry.CollectorNumber is null ||
                 string.Equals(candidate.CollectorNumber, entry.CollectorNumber, StringComparison.OrdinalIgnoreCase)))
            {
                return Describe(candidate, "the set and collector number on the entry");
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the choice for one search result.
    /// </summary>
    private static ArchidektPrintingChoice Describe(ArchidektCardPrinting candidate, string reason)
    {
        string set = candidate.SetCode ?? "?";
        string collector = candidate.CollectorNumber ?? "?";
        return new ArchidektPrintingChoice(
            candidate.ProviderCardId,
            $"Archidekt printing {set} {collector} ({reason}).");
    }

    /// <summary>
    /// Holds what the search for one card name, in one set or in all, has read so far.
    /// </summary>
    private sealed class CardSearch(ArchidektCardSearch query)
    {
        /// <summary>
        /// Gets or sets the search; the exact-name flag is dropped when Archidekt answers an exact name with nothing.
        /// </summary>
        internal ArchidektCardSearch Query { get; set; } = query;

        /// <summary>
        /// Gets the exact-name results read so far, in the order Archidekt listed them.
        /// </summary>
        internal List<ArchidektCardPrinting> Matches { get; } = [];

        /// <summary>
        /// Gets or sets how many results read so far were for other cards.
        /// </summary>
        internal int SkippedResults { get; set; }

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
