using System.ComponentModel;
using System.Text.Json.Serialization;
using MtgMcp.Core.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Names one group of the keep order: a zone or a category. Exactly one of the two is given.
/// </summary>
internal sealed record DuplicateKeepGroupInput(
    [property: Description("Zone name to keep first, such as commander or main. Letter case is ignored.")]
    string? Zone = null,
    [property: Description("Category name to keep first, such as Ramp. Letter case is ignored.")]
    string? Category = null);

/// <summary>
/// Identifies the card a group of duplicate entries stands for.
/// </summary>
internal sealed record DuplicateCard(string Name, Guid? OracleId);

/// <summary>
/// Describes one entry of a duplicate card: its quantity, place, and rank, and whether the plan keeps it.
/// </summary>
internal sealed record DuplicateEntry(
    Guid EntryId,
    int Quantity,
    string Zone,
    IReadOnlyList<string> Categories,
    int Rank,
    bool Kept,
    int KeptQuantity);

/// <summary>
/// Describes a card that has more copies than a deck may hold, and the plan that leaves the allowed number.
/// </summary>
internal sealed record DuplicateGroup(DuplicateCard Card, int Limit, IReadOnlyList<DuplicateEntry> Entries)
{
    /// <summary>
    /// Gets how a tie at the cut line was settled, or null when the keep order alone chose the entries.
    /// </summary>
    [JsonPropertyName("tieBrokenBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TieBrokenBy { get; init; }

    /// <summary>
    /// Gets the entry fields in which the tied entries of an ambiguous group differ, or null for a group that is not
    /// ambiguous.
    /// </summary>
    [JsonPropertyName("differsIn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DiffersIn { get; init; }
}

/// <summary>
/// Describes an entry that has no identity for the chosen match mode, so it is left alone.
/// </summary>
internal sealed record DuplicateUnmatched(Guid EntryId, string Name, string Reason);

/// <summary>
/// Carries the deck and revision that a duplicate preview was made from.
/// </summary>
internal sealed record DuplicateDeckRef(Guid DeckId, long Revision);

/// <summary>
/// Returns the plan that keeps one copy of each card: the duplicates found, the edits for
/// <c>local_deck_apply_changes</c>, the cards that need a more specific keep order, and the entries left alone.
/// </summary>
internal sealed record DeckDuplicatesPreview(
    DuplicateDeckRef Deck,
    IReadOnlyList<DuplicateGroup> Duplicates,
    IReadOnlyList<DeckChangeInput> Changes,
    IReadOnlyList<DuplicateGroup> Ambiguous,
    IReadOnlyList<DuplicateUnmatched> Unmatched,
    string Next)
{
    /// <summary>
    /// Gets notes on what the plan does not cover, or null when there are none.
    /// </summary>
    [JsonPropertyName("limits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Limits { get; init; }

    /// <summary>
    /// Gets true when the result lists only the first duplicates to stay under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to do to see the rest when the result was cut.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets how many unmatched entries the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("unmatchedOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnmatchedOmitted { get; init; }
}

/// <summary>
/// Holds the entries of one deck with the identity each has for the chosen match mode, as the pure finder reads them.
/// </summary>
/// <param name="Entry">The stored entry.</param>
/// <param name="Categories">The names of the categories assigned to the entry, primary first.</param>
/// <param name="PrimaryCategory">The name of the entry's primary category, or null when it has none.</param>
/// <param name="Key">The text that says which card the entry is, or null when the entry has no identity.</param>
/// <param name="Card">The Scryfall card, when card data was read for the entry.</param>
/// <param name="OracleId">The Oracle ID for the result, when known.</param>
/// <param name="UnmatchedReason">Why the entry has no identity, when it has none.</param>
internal sealed record DuplicateCandidate(
    DeckEntry Entry,
    IReadOnlyList<string> Categories,
    string? PrimaryCategory,
    string? Key,
    ScryfallCard? Card,
    Guid? OracleId,
    string? UnmatchedReason);

/// <summary>
/// Finds the duplicates of a deck and plans the edits that keep one copy of each card, following the keep order. It
/// does no input or output.
/// </summary>
internal static class DeckDuplicateFinder
{
    /// <summary>
    /// Names the way a tie is settled when the tied entries are identical, so the first in deck order is kept.
    /// </summary>
    internal const string IdenticalEntries = "identical-entries";

    /// <summary>
    /// Lists the entry fields that can differ between tied entries, in the order the next step names them.
    /// </summary>
    private static readonly string[] ComparedFields =
        ["printing", "language", "finish", "zone", "categories", "primary category"];

    /// <summary>
    /// Finds duplicates and plans the edits.
    /// </summary>
    /// <param name="deck">The deck that was read.</param>
    /// <param name="candidates">One candidate for each entry, in deck order.</param>
    /// <param name="keepPriority">The keep order, best first.</param>
    /// <param name="exemptSingletonExceptions">Whether basic lands and card-limit exceptions are skipped.</param>
    internal static DeckDuplicatesPreview Find(
        DeckDocument deck,
        IReadOnlyList<DuplicateCandidate> candidates,
        IReadOnlyList<DuplicateKeepGroupInput> keepPriority,
        bool exemptSingletonExceptions)
    {
        List<DuplicateGroup> duplicates = [];
        List<DeckChangeInput> changes = [];
        List<DuplicateGroup> ambiguous = [];
        HashSet<string> differing = [];
        foreach (IGrouping<string, DuplicateCandidate> group in candidates
            .Where(candidate => candidate.Key is not null)
            .GroupBy(candidate => candidate.Key!, StringComparer.Ordinal)
            .OrderBy(group => NameOf(group.First()), StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            int copies = group.Sum(candidate => candidate.Entry.Quantity);
            int? limit = Limit(group.First(), exemptSingletonExceptions);
            if (limit is not int allowed || copies <= allowed)
            {
                continue;
            }

            Plan(group.ToList(), allowed, keepPriority, duplicates, changes, ambiguous, differing);
        }

        List<DuplicateUnmatched> unmatched =
        [
            .. candidates
                .Where(candidate => candidate.Key is null)
                .Select(candidate => new DuplicateUnmatched(
                    candidate.Entry.EntryId,
                    candidate.Entry.CardName,
                    candidate.UnmatchedReason ?? "no-scryfall-identity")),
        ];
        string next = NextStep(deck, duplicates.Count, ambiguous.Count, differing);
        return new DeckDuplicatesPreview(
            new DuplicateDeckRef(deck.DeckId, deck.Revision),
            duplicates,
            changes,
            ambiguous,
            unmatched,
            next);
    }

    /// <summary>
    /// Tells the caller what to do with the plan.
    /// </summary>
    private static string NextStep(DeckDocument deck, int duplicates, int ambiguous, HashSet<string> differing)
    {
        List<string> steps = [];
        if (duplicates > 0)
        {
            steps.Add(
                $"Send changes to local_deck_apply_changes with expectedRevision {deck.Revision}, then " +
                "archidekt_push_preview if the deck is bound to Archidekt.");
        }

        if (ambiguous > 0)
        {
            List<string> fields = [.. ComparedFields.Where(differing.Contains)];
            string differs = $"For the ambiguous cards, the tied entries differ in {string.Join(", ", fields)}; " +
                "differsIn on each card says which.";
            steps.Add(
                fields.Exists(field => field is "zone" or "categories")
                    ? differs + " For a zone or category difference, add a keepPriority group that ranks one " +
                        "entry above the others, then ask again. For other differences, choose the entries to " +
                        "remove and send remove-entry changes to local_deck_apply_changes."
                    : differs + " keepPriority cannot separate them: choose the entries to remove and send " +
                        "remove-entry changes to local_deck_apply_changes.");
        }

        return steps.Count == 0 ? "No duplicates need a change." : string.Join(' ', steps);
    }

    /// <summary>
    /// Gives the number of copies the deck may keep of the card a candidate stands for: one, or the card's own limit
    /// when exceptions are exempt, or null when the card has no limit and is skipped.
    /// </summary>
    private static int? Limit(DuplicateCandidate candidate, bool exemptSingletonExceptions)
    {
        if (!exemptSingletonExceptions)
        {
            return 1;
        }

        if (candidate.Card is { } card)
        {
            return CommanderCardText.CopyLimit(card);
        }

        return BasicLandNames.Contains(candidate.Entry.CardName)
            ? null
            : 1;
    }

    /// <summary>
    /// Gives the display name of the card a group stands for.
    /// </summary>
    private static string NameOf(DuplicateCandidate candidate)
    {
        return candidate.Card?.Name ?? candidate.Entry.CardName;
    }

    /// <summary>
    /// Plans one card: ranks its entries and keeps the allowed copies on the best ranked entries. When the cut line
    /// splits entries of the same rank, identical entries keep the first in deck order; any other split is reported
    /// as ambiguous without a planned change and without marking a tied entry as kept.
    /// </summary>
    private static void Plan(
        List<DuplicateCandidate> group,
        int allowed,
        IReadOnlyList<DuplicateKeepGroupInput> keepPriority,
        List<DuplicateGroup> duplicates,
        List<DeckChangeInput> changes,
        List<DuplicateGroup> ambiguous,
        HashSet<string> differing)
    {
        List<(DuplicateCandidate Candidate, int Rank)> ranked =
        [
            .. group.Select(candidate => (candidate, RankOf(candidate, keepPriority))).OrderBy(pair => pair.Item2),
        ];
        int[] keptQuantities = new int[ranked.Count];
        int remaining = allowed;
        for (int index = 0; index < ranked.Count; index++)
        {
            keptQuantities[index] = Math.Min(ranked[index].Candidate.Entry.Quantity, remaining);
            remaining -= keptQuantities[index];
        }

        List<int[]> ties = TiesAtTheCut(ranked, keptQuantities);
        HashSet<string> differences = [];
        foreach (int[] tie in ties)
        {
            differences.UnionWith(DifferingFields([.. tie.Select(index => ranked[index].Candidate)]));
        }

        bool unresolved = differences.Count > 0;
        HashSet<int> tiedIndexes = unresolved ? [.. ties.SelectMany(tie => tie)] : [];
        DuplicateCandidate first = ranked[0].Candidate;
        DuplicateGroup summary = new(
            new DuplicateCard(NameOf(first), first.OracleId),
            allowed,
            [.. ranked.Select((pair, index) => new DuplicateEntry(
                pair.Candidate.Entry.EntryId,
                pair.Candidate.Entry.Quantity,
                pair.Candidate.Entry.Zone,
                pair.Candidate.Categories,
                pair.Rank,
                !tiedIndexes.Contains(index) && keptQuantities[index] > 0,
                tiedIndexes.Contains(index) ? 0 : keptQuantities[index]))])
        {
            TieBrokenBy = ties.Count > 0 && !unresolved ? IdenticalEntries : null,
            DiffersIn = unresolved ? [.. ComparedFields.Where(differences.Contains)] : null,
        };
        if (unresolved)
        {
            differing.UnionWith(differences);
            ambiguous.Add(summary);
            return;
        }

        duplicates.Add(summary);
        for (int index = 0; index < ranked.Count; index++)
        {
            DeckEntry entry = ranked[index].Candidate.Entry;
            if (keptQuantities[index] == 0)
            {
                changes.Add(new RemoveDeckEntryInput(entry.EntryId));
            }
            else if (keptQuantities[index] < entry.Quantity)
            {
                changes.Add(new UpdateDeckEntryInput(entry with { Quantity = keptQuantities[index] }));
            }
        }
    }

    /// <summary>
    /// Finds the sets of same-rank entries that the cut line splits, so the choice between them would be a guess.
    /// Entries that all keep or all lose their copies are not a tie.
    /// </summary>
    private static List<int[]> TiesAtTheCut(
        List<(DuplicateCandidate Candidate, int Rank)> ranked,
        int[] keptQuantities)
    {
        List<int[]> ties = [];
        foreach (IGrouping<int, int> sameRank in Enumerable.Range(0, ranked.Count).GroupBy(index => ranked[index].Rank))
        {
            int[] indexes = [.. sameRank];
            if (indexes.Length < 2)
            {
                continue;
            }

            bool anyKept = indexes.Any(index => keptQuantities[index] > 0);
            bool anyCut = indexes.Any(index => keptQuantities[index] < ranked[index].Candidate.Entry.Quantity);
            if (anyKept && anyCut)
            {
                ties.Add(indexes);
            }
        }

        return ties;
    }

    /// <summary>
    /// Names the fields in which tied entries differ: printing, language, finish, zone (letter case ignored), and
    /// the set of categories and the primary category (letter case and order ignored). Quantity and sort order are
    /// not compared.
    /// </summary>
    private static HashSet<string> DifferingFields(List<DuplicateCandidate> tied)
    {
        HashSet<string> fields = [];
        DuplicateCandidate head = tied[0];
        foreach (DuplicateCandidate other in tied.Skip(1))
        {
            if (PrintingKeyOf(head.Entry) != PrintingKeyOf(other.Entry))
            {
                fields.Add("printing");
            }

            if (head.Entry.Language != other.Entry.Language)
            {
                fields.Add("language");
            }

            if (head.Entry.Finish != other.Entry.Finish)
            {
                fields.Add("finish");
            }

            if (!head.Entry.Zone.Equals(other.Entry.Zone, StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("zone");
            }

            if (!CategorySet(head).SetEquals(CategorySet(other)))
            {
                fields.Add("categories");
            }

            if (!string.Equals(head.PrimaryCategory, other.PrimaryCategory, StringComparison.OrdinalIgnoreCase))
            {
                fields.Add("primary category");
            }
        }

        return fields;
    }

    /// <summary>
    /// Reads the categories of a candidate as a set that ignores letter case.
    /// </summary>
    private static HashSet<string> CategorySet(DuplicateCandidate candidate)
    {
        return new HashSet<string>(candidate.Categories, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the printing of an entry: its printing ID, else its set and collector number, else its normalized name.
    /// </summary>
    private static string PrintingKeyOf(DeckEntry entry)
    {
        if (entry.PrintingId is Guid printing)
        {
            return "id:" + printing.ToString("D");
        }

        if (!string.IsNullOrWhiteSpace(entry.SetCode) && !string.IsNullOrWhiteSpace(entry.CollectorNumber))
        {
            string set = entry.SetCode.Trim().ToLowerInvariant();
            return $"set:{set}/{entry.CollectorNumber.Trim().ToLowerInvariant()}";
        }

        return "name:" + ScryfallCardSets.NormalizeName(entry.CardName);
    }

    /// <summary>
    /// Ranks an entry by the first keep group its zone or any of its categories belongs to; entries in no listed
    /// group rank after every listed group.
    /// </summary>
    private static int RankOf(DuplicateCandidate candidate, IReadOnlyList<DuplicateKeepGroupInput> keepPriority)
    {
        for (int index = 0; index < keepPriority.Count; index++)
        {
            DuplicateKeepGroupInput group = keepPriority[index];
            if (group.Zone is { Length: > 0 } zone &&
                candidate.Entry.Zone.Equals(zone, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            if (group.Category is { Length: > 0 } category &&
                candidate.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return keepPriority.Count;
    }
}
