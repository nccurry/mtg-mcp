using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Reads one local deck and plans the edits that keep one copy of each card, without writing anything.
/// </summary>
internal sealed class DeckDuplicatesCoordinator
{
    /// <summary>
    /// Gives the most unmatched entries one result lists.
    /// </summary>
    private const int MaximumUnmatchedListed = 50;

    /// <summary>
    /// Names the exemption that skips basic lands and applies each card's own copy limit.
    /// </summary>
    internal const string SingletonExceptions = "commander-singleton-exceptions";

    /// <summary>
    /// Names the choice of no exemption.
    /// </summary>
    internal const string NoExemption = "none";

    /// <summary>
    /// Says why entry card data is missing when no card data is installed.
    /// </summary>
    private const string NotInstalledMessage =
        "Scryfall card data is not installed. Run scryfall_card_data_sync to install it, then repeat the request.";

    /// <summary>
    /// Reads stored decks.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Reads installed card data.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates the coordinator around the deck store and the Scryfall service.
    /// </summary>
    internal DeckDuplicatesCoordinator(SqliteDeckStore store, ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scryfall);
        this.store = store;
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Builds the duplicate plan of one deck.
    /// </summary>
    internal async Task<OperationResult<DeckDuplicatesPreview>> PreviewAsync(
        Guid deckId,
        long? expectedRevision,
        string matchBy,
        string exempt,
        IReadOnlyList<DuplicateKeepGroupInput>? keepPriority,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = Validate(matchBy, exempt, keepPriority, out ScryfallMatchBy match);
        if (invalid is not null)
        {
            return invalid;
        }

        OperationResult<DeckDocument> loaded = await store.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (loaded is not OperationSuccess<DeckDocument> read)
        {
            return OperationFailure.Forward<DeckDocument, DeckDuplicatesPreview>(loaded);
        }

        DeckDocument deck = read.Data;
        if (expectedRevision is long expected && expected != deck.Revision)
        {
            return new OperationConflict(
                "deck-revision-conflict",
                $"The deck is at revision {deck.Revision}, not {expected}. Repeat the request with the current " +
                "revision, or omit expectedRevision.",
                new ConflictState(Revision: deck.Revision));
        }

        bool exemptExceptions = exempt == SingletonExceptions;
        bool needsCards = match == ScryfallMatchBy.Oracle || exemptExceptions;
        ScryfallCard?[] cards = new ScryfallCard?[deck.Entries.Count];
        bool installed = true;
        if (needsCards)
        {
            OperationResult<ScryfallInstalledListCards> cardRead = await ScryfallToolExecution.RunAsync(() =>
                scryfall.ReadInstalledListCardsAsync(
                    [.. deck.Entries.Select(entry => ScryfallEntryLookup.For(
                        entry.PrintingId, entry.SetCode, entry.CollectorNumber, entry.OracleId))],
                    includeRaw: false,
                    includeFacts: false,
                    cancellationToken)).ConfigureAwait(false);
            if (cardRead is not OperationSuccess<ScryfallInstalledListCards> cardsRead)
            {
                return OperationFailure.Forward<ScryfallInstalledListCards, DeckDuplicatesPreview>(cardRead);
            }

            installed = cardsRead.Data.Installed;
            if (!installed && match == ScryfallMatchBy.Oracle)
            {
                return new OperationNotCached("card-data-not-installed", NotInstalledMessage);
            }

            for (int index = 0; index < cards.Length; index++)
            {
                cards[index] = cardsRead.Data.Cards[index]?.Card;
            }
        }

        Dictionary<Guid, IReadOnlyList<string>> categories = DeckEntryCategories.NamesByEntry(deck);
        List<DuplicateCandidate> candidates = [];
        for (int index = 0; index < deck.Entries.Count; index++)
        {
            candidates.Add(Candidate(deck.Entries[index], cards[index], match, categories));
        }

        DeckDuplicatesPreview preview = DeckDuplicateFinder.Find(deck, candidates, keepPriority!, exemptExceptions);
        List<string> limits = [];
        if (exemptExceptions && !installed)
        {
            limits.Add(
                "Card data is not installed, so only basic land names are exempt and every other card is limited " +
                "to one copy. Run scryfall_card_data_sync for the cards that allow more.");
        }

        return new OperationSuccess<DeckDuplicatesPreview>(CutToSize(preview, limits));
    }

    /// <summary>
    /// Checks the match mode, the exemption, and the keep order.
    /// </summary>
    private static OperationInvalidInput? Validate(
        string matchBy,
        string exempt,
        IReadOnlyList<DuplicateKeepGroupInput>? keepPriority,
        out ScryfallMatchBy match)
    {
        if (!ScryfallSetNames.TryParseMatchBy(matchBy, out match))
        {
            return new OperationInvalidInput(
                "invalid-match-by",
                $"matchBy must be one of: {string.Join(", ", ScryfallSetNames.MatchModes)}.",
                "matchBy");
        }

        if (exempt is not (NoExemption or SingletonExceptions))
        {
            return new OperationInvalidInput(
                "invalid-exempt",
                $"exempt must be {NoExemption} or {SingletonExceptions}.",
                "exempt");
        }

        if (keepPriority is null || keepPriority.Count == 0)
        {
            return new OperationInvalidInput(
                "invalid-keep-priority",
                "keepPriority needs at least one group, each with a zone or a category, best first.",
                "keepPriority");
        }

        for (int index = 0; index < keepPriority.Count; index++)
        {
            bool hasZone = !string.IsNullOrWhiteSpace(keepPriority[index].Zone);
            bool hasCategory = !string.IsNullOrWhiteSpace(keepPriority[index].Category);
            if (hasZone == hasCategory)
            {
                return new OperationInvalidInput(
                    "invalid-keep-priority",
                    "Each keepPriority group needs exactly one of zone or category.",
                    $"keepPriority[{index}]");
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the candidate for one entry: the identity it has for the match mode, or why it has none.
    /// </summary>
    private static DuplicateCandidate Candidate(
        DeckEntry entry,
        ScryfallCard? card,
        ScryfallMatchBy match,
        Dictionary<Guid, IReadOnlyList<string>> categories)
    {
        IReadOnlyList<string> names = categories.GetValueOrDefault(entry.EntryId) ?? [];
        Guid? oracleId = card is null ? entry.OracleId : ScryfallConciseFieldTable.EffectiveOracleId(card);
        (string? key, string? reason) = match switch
        {
            ScryfallMatchBy.Oracle => card is not null
                ? (ScryfallCardSets.KeyOf(card, ScryfallMatchBy.Oracle), null)
                : (null, ScryfallEntryLookup.For(entry.PrintingId, entry.SetCode, entry.CollectorNumber,
                    entry.OracleId) is null
                    ? "no-scryfall-identity"
                    : "not-in-card-data"),
            ScryfallMatchBy.Name => (ScryfallCardSets.NormalizeName(entry.CardName), null),
            _ => PrintingKey(entry),
        };
        return new DuplicateCandidate(entry, names, key, card, oracleId, reason);
    }

    /// <summary>
    /// Reads the exact printing of an entry: its printing ID, or its set and collector number.
    /// </summary>
    private static (string? Key, string? Reason) PrintingKey(DeckEntry entry)
    {
        if (entry.PrintingId is Guid printing)
        {
            return ("id:" + printing.ToString("D"), null);
        }

        if (!string.IsNullOrWhiteSpace(entry.SetCode) && !string.IsNullOrWhiteSpace(entry.CollectorNumber))
        {
            string set = entry.SetCode.Trim().ToLowerInvariant();
            return ($"set:{set}/{entry.CollectorNumber.Trim().ToLowerInvariant()}", null);
        }

        return (null, "no-printing-identity");
    }

    /// <summary>
    /// Keeps the leading duplicate groups that fit under the result size limit. The plan is safe to apply in parts,
    /// because each group's edits stand alone and a second preview finds the rest.
    /// </summary>
    private static DeckDuplicatesPreview CutToSize(DeckDuplicatesPreview preview, List<string> limits)
    {
        List<object> groups = [.. preview.Duplicates, .. preview.Ambiguous];
        List<int> sizes = [];
        foreach (DuplicateGroup group in preview.Duplicates)
        {
            int size = ResultSize.Measure(group) + 1;
            size += preview.Changes
                .Where(change => EditsEntry(change, group))
                .Sum(change => ResultSize.Measure(change) + 1);
            sizes.Add(size);
        }

        sizes.AddRange(preview.Ambiguous.Select(group => ResultSize.Measure(group) + 1));
        List<DuplicateUnmatched> unmatched = [.. preview.Unmatched.Take(MaximumUnmatchedListed)];
        int unmatchedBytes = unmatched.Sum(entry => ResultSize.Measure(entry) + 1);
        int keep = ResultSizeLimit.CountFitting(sizes, unmatchedBytes);
        DeckDuplicatesPreview result = preview with
        {
            Unmatched = unmatched,
            UnmatchedOmitted = preview.Unmatched.Count > unmatched.Count
                ? preview.Unmatched.Count - unmatched.Count
                : null,
            Limits = limits.Count == 0 ? null : limits,
        };
        if (keep >= groups.Count)
        {
            return result;
        }

        int keptDuplicates = Math.Min(keep, preview.Duplicates.Count);
        List<DuplicateGroup> keptGroups = [.. preview.Duplicates.Take(keptDuplicates)];
        HashSet<Guid> keptEntries = [.. keptGroups.SelectMany(group => group.Entries.Select(entry => entry.EntryId))];
        return result with
        {
            Duplicates = keptGroups,
            Changes = [.. preview.Changes.Where(change => keptEntries.Contains(EntryOf(change)))],
            Ambiguous = [.. preview.Ambiguous.Take(Math.Max(0, keep - preview.Duplicates.Count))],
            Truncated = true,
            Narrow = "The result lists only the first duplicates to stay under the size limit. Apply these changes " +
                "with local_deck_apply_changes, then run the preview again for the rest.",
        };
    }

    /// <summary>
    /// Reports whether a change edits an entry of a duplicate group.
    /// </summary>
    private static bool EditsEntry(DeckChangeInput change, DuplicateGroup group)
    {
        Guid id = EntryOf(change);
        return group.Entries.Any(entry => entry.EntryId == id);
    }

    /// <summary>
    /// Reads the entry ID a change edits.
    /// </summary>
    private static Guid EntryOf(DeckChangeInput change)
    {
        return change switch
        {
            RemoveDeckEntryInput remove => remove.EntryId,
            UpdateDeckEntryInput update => update.Entry.EntryId,
            _ => Guid.Empty,
        };
    }
}

/// <summary>
/// Exposes the duplicate preview tool: a read that returns the edits and writes nothing.
/// </summary>
internal sealed class DeckDuplicatesReadTools
{
    /// <summary>
    /// Holds the example request published in the description.
    /// </summary>
    internal const string Example =
        "{\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\",\"matchBy\":\"oracle\"," +
        "\"exempt\":\"commander-singleton-exceptions\",\"keepPriority\":[{\"zone\":\"commander\"}," +
        "{\"category\":\"Ramp\"},{\"zone\":\"main\"},{\"category\":\"Sideboard\"},{\"category\":\"Maybeboard\"}]}";

    /// <summary>
    /// Builds the plans.
    /// </summary>
    private readonly DeckDuplicatesCoordinator coordinator;

    /// <summary>
    /// Creates the tool around one coordinator.
    /// </summary>
    internal DeckDuplicatesReadTools(DeckDuplicatesCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Plans the edits that keep one copy of each card across every zone and category of a deck.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_duplicates_preview",
        Title = "Preview Duplicate Removal",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: finds cards that have more than one copy across all zones and categories of a deck and " +
        "plans the edits that keep one copy of each, writing nothing. You choose which copy to keep with " +
        "keepPriority, a list of {zone} or {category} groups, best first; the best-ranked entry is kept (lowered " +
        "with update-entry if it holds more copies than allowed) and the others get remove-entry. " +
        "When entries tie for the best rank the card is listed in ambiguous with no change; add a more specific " +
        "group and ask again. matchBy oracle (default) needs installed card data, name and printing do not. " +
        "exempt commander-singleton-exceptions skips basic lands and keeps as many copies as a card's text allows " +
        "for the rest. Send the changes to local_deck_apply_changes with the returned revision, then archidekt_push_preview " +
        "for a bound deck." + UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "dedupe): " + Example)]
    internal Task<OperationResult<DeckDuplicatesPreview>> PreviewAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Required: none, or commander-singleton-exceptions."),
         AllowedValues("none", "commander-singleton-exceptions")]
        string exempt,
        [Description(
            "Required: the keep order, best first. Each group has exactly one of zone or category; names ignore " +
            "letter case. Entries in no listed group rank last.")]
        IReadOnlyList<DuplicateKeepGroupInput> keepPriority,
        [Description("Optional exact deck revision; a different current revision returns conflict.")]
        long? expectedRevision = null,
        [Description("What makes two entries the same card: oracle (default), printing, or name."),
         AllowedValues("oracle", "printing", "name")]
        string matchBy = "oracle",
        CancellationToken cancellationToken = default)
    {
        return coordinator.PreviewAsync(deckId, expectedRevision, matchBy, exempt, keepPriority, cancellationToken);
    }
}
