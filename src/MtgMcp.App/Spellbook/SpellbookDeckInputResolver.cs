using System.Globalization;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Selects supported local deck zones for one Commander Spellbook request without judging the deck.
/// </summary>
internal sealed class SpellbookDeckInputResolver
{
    /// <summary>
    /// Reads one revisioned local deck without mutating it.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Creates a resolver around the process-local deck store.
    /// </summary>
    internal SpellbookDeckInputResolver(SqliteDeckStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Loads one exact local deck revision and prepares its supported source zones.
    /// </summary>
    internal async Task<OperationResult<SpellbookDeckComboSelection>> ResolveAsync(
        Guid deckId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty || expectedRevision <= 0)
        {
            return Invalid<SpellbookDeckComboSelection>(
                "The deck ID and expected revision must identify one local deck revision.");
        }

        OperationResult<DeckDocument> loaded = await store.GetAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        return loaded switch
        {
            OperationSuccess<DeckDocument> success when success.Data.Revision != expectedRevision =>
                new OperationConflict(
                    "deck-revision-conflict",
                    "The local deck revision changed; load it again before finding Commander Spellbook combos."),
            OperationSuccess<DeckDocument> success => Prepare(success.Data),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Groups exact local card names by supported source zone and records skipped local entries.
    /// </summary>
    private static OperationResult<SpellbookDeckComboSelection> Prepare(DeckDocument deck)
    {
        SortedDictionary<string, int> commanders = new(StringComparer.Ordinal);
        SortedDictionary<string, int> main = new(StringComparer.Ordinal);
        List<SpellbookSkippedDeckEntry> skipped = [];
        foreach (DeckEntry entry in deck.Entries)
        {
            if (string.Equals(entry.Zone, "commander", StringComparison.Ordinal))
            {
                OperationInvalidInput? failure = AddEntry(commanders, entry, "commander");
                if (failure is not null)
                {
                    return failure;
                }

                continue;
            }

            if (string.Equals(entry.Zone, "main", StringComparison.Ordinal))
            {
                OperationInvalidInput? failure = AddEntry(main, entry, "main");
                if (failure is not null)
                {
                    return failure;
                }

                continue;
            }

            skipped.Add(new SpellbookSkippedDeckEntry(
                entry.EntryId,
                entry.Zone,
                entry.CardName,
                entry.Quantity));
        }

        if (commanders.Count > SpellbookDeckRequestLimits.MaximumCommanderEntries)
        {
            return Invalid<SpellbookDeckComboSelection>(
                $"The local deck has more than {SpellbookDeckRequestLimits.MaximumCommanderEntries.ToString(CultureInfo.InvariantCulture)} distinct commander entries for Commander Spellbook.");
        }

        if (main.Count > SpellbookDeckRequestLimits.MaximumMainEntries)
        {
            return Invalid<SpellbookDeckComboSelection>(
                $"The local deck has more than {SpellbookDeckRequestLimits.MaximumMainEntries.ToString(CultureInfo.InvariantCulture)} distinct main-deck entries for Commander Spellbook.");
        }

        if (commanders.Count == 0 && main.Count == 0)
        {
            return Invalid<SpellbookDeckComboSelection>(
                "The local deck has no commander or main entries to send to Commander Spellbook.");
        }

        SpellbookDeckEntry[] commanderEntries = commanders
            .Select(value => new SpellbookDeckEntry(value.Key, value.Value))
            .ToArray();
        SpellbookDeckEntry[] mainEntries = main
            .Select(value => new SpellbookDeckEntry(value.Key, value.Value))
            .ToArray();
        SpellbookSentDeckEntry[] sent =
        [
            .. commanderEntries.Select(value => new SpellbookSentDeckEntry("commander", value.Card, value.Quantity)),
            .. mainEntries.Select(value => new SpellbookSentDeckEntry("main", value.Card, value.Quantity)),
        ];
        SpellbookDeckSelectionEvidence evidence = new(
            deck.DeckId,
            deck.Revision,
            sent,
            skipped);
        return new OperationSuccess<SpellbookDeckComboSelection>(
            new SpellbookDeckComboSelection(
                new SpellbookDeckRequest(commanderEntries, mainEntries),
                evidence));
    }

    /// <summary>
    /// Adds one local entry to a source zone without trimming, renaming, or expanding it.
    /// </summary>
    private static OperationInvalidInput? AddEntry(
        IDictionary<string, int> entries,
        DeckEntry entry,
        string zone)
    {
        if (string.IsNullOrWhiteSpace(entry.CardName) ||
            entry.CardName.Length > SpellbookDeckRequestLimits.MaximumCardNameLength ||
            entry.Quantity <= 0)
        {
            return Invalid<SpellbookDeckComboSelection>(
                $"The local {zone} entry is outside Commander Spellbook's supported request limits.");
        }

        try
        {
            entries.TryGetValue(entry.CardName, out int existingQuantity);
            entries[entry.CardName] = checked(existingQuantity + entry.Quantity);
            return null;
        }
        catch (OverflowException)
        {
            return Invalid<SpellbookDeckComboSelection>(
                "Repeated local deck entry quantities exceed the supported integer range.");
        }
    }

    /// <summary>
    /// Creates one local-selection failure before any source request starts.
    /// </summary>
    private static OperationInvalidInput Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-spellbook-deck-selection", message);
    }
}
