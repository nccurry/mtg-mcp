using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Holds a batch of deck changes after the entries they add were looked up, and what the lookup could not settle.
/// </summary>
internal sealed record DeckChangeResolution(
    IReadOnlyList<DeckChange> Changes,
    IReadOnlyList<DeckWriteIssue> Issues);

/// <summary>
/// Holds new deck entries after their card identity was looked up, and what the lookup could not settle.
/// </summary>
internal sealed record DeckDraftResolution(
    IReadOnlyList<DeckEntryDraft> Drafts,
    IReadOnlyList<DeckWriteIssue> Issues);

/// <summary>
/// Fills in the card identity of a new deck entry from installed Scryfall card data when the caller gave only a
/// card name, or a set code and collector number, so the entry does not need a separate reconcile step. It uses the
/// same exact lookups as identity reconciliation and never uses the network. An entry that cannot be resolved is
/// kept as given and reported, never refused.
/// </summary>
internal sealed class DeckEntryResolver
{
    /// <summary>
    /// Gives the most entries looked up in one request to the card data.
    /// </summary>
    private const int LookupBatchSize = 100;

    /// <summary>
    /// Says what happens to entries when no card data is installed.
    /// </summary>
    private const string NotInstalledMessage =
        "Scryfall card data is not installed, so new entries were added without a card identity. This does not stop " +
        "a push to Archidekt: a push uses Archidekt's default printing for an entry with no printing set. To fill " +
        "in the card identity, run scryfall_card_data_sync, then local_deck_identity_reconcile_preview.";

    /// <summary>
    /// Says what happens to entries when the card data lookup itself fails.
    /// </summary>
    private const string LookupFailedMessage =
        "The installed card data could not be read, so new entries were added without a card identity. This does " +
        "not stop a push to Archidekt: a push uses Archidekt's default printing for an entry with no printing set. " +
        "To fill in the card identity, run local_deck_identity_reconcile_preview.";

    /// <summary>
    /// Reads installed card data.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates the resolver around the Scryfall service.
    /// </summary>
    internal DeckEntryResolver(ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(scryfall);
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Looks up the identity of every entry that a batch adds, and the printing a patch gives each entry against the
    /// card of that entry, leaving every other change as it was. The deck gives the card of an entry it holds; an entry
    /// the batch adds or replaces earlier gives its drafted card.
    /// </summary>
    internal async Task<DeckChangeResolution> ResolveChangesAsync(
        IReadOnlyList<DeckChange> changes,
        DeckDocument? deck,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        List<DeckEntryDraft> drafts = [];
        foreach (DeckChange change in changes)
        {
            if (change is AddDeckEntryChange add)
            {
                drafts.Add(add.Entry);
            }
        }

        DeckDraftResolution resolution = await ResolveAsync(drafts, cancellationToken).ConfigureAwait(false);
        List<DeckChange> result = new(changes.Count);
        int next = 0;
        foreach (DeckChange change in changes)
        {
            result.Add(change is AddDeckEntryChange ? new AddDeckEntryChange(resolution.Drafts[next++]) : change);
        }

        List<DeckWriteIssue> issues = [.. resolution.Issues];
        await ResolvePatchPrintingsAsync(result, deck, issues, cancellationToken).ConfigureAwait(false);
        return new DeckChangeResolution(result, issues);
    }

    /// <summary>
    /// Replaces each patch that sets a printing with the same patch carrying the printing looked up against each
    /// listed entry's own card. An entry whose lookup finds nothing keeps the printing as given and is reported.
    /// </summary>
    private async Task ResolvePatchPrintingsAsync(
        List<DeckChange> changes,
        DeckDocument? deck,
        List<DeckWriteIssue> issues,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, DeckEntryDraft> cards = [];
        foreach (DeckEntry entry in deck?.Entries ?? [])
        {
            cards[entry.EntryId] = new DeckEntryDraft(
                entry.Quantity,
                entry.CardName,
                Language: entry.Language,
                EntryId: entry.EntryId);
        }

        for (int index = 0; index < changes.Count; index++)
        {
            switch (changes[index])
            {
                case AddDeckEntryChange { Entry.EntryId: Guid added } add:
                    cards[added] = add.Entry;
                    break;
                case UpdateDeckEntryChange update:
                    cards[update.Entry.EntryId] = new DeckEntryDraft(
                        update.Entry.Quantity,
                        update.Entry.CardName,
                        Language: update.Entry.Language,
                        EntryId: update.Entry.EntryId);
                    break;
                case RemoveDeckEntryChange remove:
                    cards.Remove(remove.EntryId);
                    break;
                case PatchDeckEntriesChange { Printing: not null } patch:
                    changes[index] = patch with
                    {
                        Printings = await LookUpPrintingsAsync(patch, cards, issues, cancellationToken)
                            .ConfigureAwait(false),
                    };
                    break;
            }
        }
    }

    /// <summary>
    /// Looks up the patch's printing for each listed entry that is known, using the entry's card name and language.
    /// </summary>
    private async Task<IReadOnlyList<DeckEntryPrinting>> LookUpPrintingsAsync(
        PatchDeckEntriesChange patch,
        Dictionary<Guid, DeckEntryDraft> cards,
        List<DeckWriteIssue> issues,
        CancellationToken cancellationToken)
    {
        DeckPrintingPatch printing = patch.Printing!;
        List<DeckEntryDraft> drafts = [];
        foreach (Guid id in patch.EntryIds)
        {
            if (cards.TryGetValue(id, out DeckEntryDraft? card))
            {
                drafts.Add(new DeckEntryDraft(
                    card.Quantity,
                    card.CardName,
                    PrintingId: printing.PrintingId,
                    SetCode: printing.SetCode,
                    CollectorNumber: printing.CollectorNumber,
                    Language: patch.Language ?? card.Language,
                    EntryId: id));
            }
        }

        DeckDraftResolution resolution = await ResolvePendingAsync(drafts, [.. Enumerable.Range(0, drafts.Count)], cancellationToken)
            .ConfigureAwait(false);
        issues.AddRange(resolution.Issues);
        List<DeckEntryPrinting> resolved = [];
        foreach (DeckEntryDraft draft in resolution.Drafts)
        {
            if (draft.OracleId is not null)
            {
                resolved.Add(new DeckEntryPrinting(
                    draft.EntryId!.Value,
                    draft.PrintingId,
                    draft.SetCode,
                    draft.CollectorNumber,
                    draft.Language));
            }
        }

        return resolved;
    }

    /// <summary>
    /// Looks up every draft that has a name but no printing or Oracle ID, and returns the drafts in their original
    /// order. A draft that already carries an identity is left alone.
    /// </summary>
    internal async Task<DeckDraftResolution> ResolveAsync(
        IReadOnlyList<DeckEntryDraft> drafts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        List<int> pending = [];
        for (int index = 0; index < drafts.Count; index++)
        {
            if (NeedsResolution(drafts[index]))
            {
                pending.Add(index);
            }
        }

        return await ResolvePendingAsync(drafts, pending, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks up the drafts at the listed positions, whatever identity they already carry, and returns every draft in
    /// its original order.
    /// </summary>
    private async Task<DeckDraftResolution> ResolvePendingAsync(
        IReadOnlyList<DeckEntryDraft> drafts,
        List<int> pending,
        CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return new DeckDraftResolution(drafts, []);
        }

        DeckEntryDraft[] resolved = [.. drafts];
        List<DeckWriteIssue> issues = [];
        for (int start = 0; start < pending.Count; start += LookupBatchSize)
        {
            List<int> batch = pending.GetRange(start, Math.Min(LookupBatchSize, pending.Count - start));
            string? skipped = await ResolveBatchAsync(drafts, batch, resolved, issues, cancellationToken)
                .ConfigureAwait(false);
            if (skipped is not null)
            {
                // Without card data every later batch would fail the same way; say so once.
                return new DeckDraftResolution(drafts, [new DeckWriteIssue(DeckWriteIssue.Skipped, skipped)]);
            }
        }

        return new DeckDraftResolution(resolved, issues);
    }

    /// <summary>
    /// Tells whether a draft has a name to look up and no card identity yet.
    /// </summary>
    private static bool NeedsResolution(DeckEntryDraft draft)
    {
        return draft.OracleId is null && draft.PrintingId is null && !string.IsNullOrWhiteSpace(draft.CardName);
    }

    /// <summary>
    /// Looks up one batch of drafts and writes each resolved draft into its original position. Returns why the
    /// lookup was skipped, or null when it ran.
    /// </summary>
    private async Task<string?> ResolveBatchAsync(
        IReadOnlyList<DeckEntryDraft> drafts,
        List<int> batch,
        DeckEntryDraft[] resolved,
        List<DeckWriteIssue> issues,
        CancellationToken cancellationToken)
    {
        List<DeckEntry> entries = [.. batch.Select(index => AsEntry(drafts[index]))];
        (IReadOnlyList<DeckIdentityLookupPlan> plans, IReadOnlyList<ScryfallEvidenceLookup> lookups) =
            DeckIdentityReconciliationCoordinator.BuildLookupPlans(entries);
        OperationResult<ScryfallExactCollectionEvidence> result;
        try
        {
            result = await scryfall.ResolveExactCollectionAsync(lookups, "cache-only", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The lookup only adds identity, so a card data file that cannot be opened must not fail the write.
            return LookupFailedMessage;
        }

        if (result is not OperationSuccess<ScryfallExactCollectionEvidence> success)
        {
            return LookupFailedMessage;
        }

        if (success.Data.Binding.CorpusGenerationId is null)
        {
            return NotInstalledMessage;
        }

        for (int position = 0; position < batch.Count; position++)
        {
            DeckEntryDraft draft = drafts[batch[position]];
            DeckIdentityLookupPlan plan = plans[position];
            ScryfallCollectionRow? row = plan.EvidenceIndex is int evidenceIndex
                ? success.Data.Rows[evidenceIndex]
                : null;
            if (row?.Status == "ambiguous")
            {
                issues.Add(new DeckWriteIssue(
                    DeckWriteIssue.Ambiguous,
                    row.Message ?? $"More than one card is named '{draft.CardName.Trim()}'.",
                    draft.EntryId,
                    draft.CardName.Trim()));
                continue;
            }

            if (row?.Card is null)
            {
                issues.Add(Unresolved(
                    draft,
                    plan.MatchMethod == DeckIdentityReconciliationCoordinator.MatchPrintingId
                        ? $"Printing {draft.PrintingId} was not found in installed card data."
                        : $"No card named '{draft.CardName.Trim()}' was found in installed card data."));
                continue;
            }

            if (plan.MatchMethod is DeckIdentityReconciliationCoordinator.MatchSetCollectorLanguage or
                    DeckIdentityReconciliationCoordinator.MatchPrintingId &&
                !NameAgrees(draft.CardName, row.Card))
            {
                string which = plan.MatchMethod == DeckIdentityReconciliationCoordinator.MatchPrintingId
                    ? $"Printing {draft.PrintingId}"
                    : $"Set {draft.SetCode} number {draft.CollectorNumber}";
                issues.Add(Unresolved(draft, $"{which} is '{row.Card.Name}', not '{draft.CardName.Trim()}'."));
                continue;
            }

            DeckEntryIdentity identity = DeckIdentityReconciliationCoordinator.ProposedIdentity(plan, row.Card);
            resolved[batch[position]] = draft with
            {
                CardName = identity.CardName,
                OracleId = identity.OracleId,
                PrintingId = identity.PrintingId,
                SetCode = identity.SetCode,
                CollectorNumber = identity.CollectorNumber,
                Language = identity.Language,
            };
        }

        return null;
    }

    /// <summary>
    /// Tells whether the name a caller typed names the card, or one face of it.
    /// </summary>
    private static bool NameAgrees(string typed, ScryfallCard card)
    {
        return CardNames.Match(typed, card.Name);
    }

    /// <summary>
    /// Builds the issue for a draft that could not be resolved.
    /// </summary>
    private static DeckWriteIssue Unresolved(DeckEntryDraft draft, string message)
    {
        return new DeckWriteIssue(DeckWriteIssue.Unresolved, message, draft.EntryId, draft.CardName.Trim());
    }

    /// <summary>
    /// Shapes a draft as an entry so it can use the shared lookup choice of identity reconciliation.
    /// </summary>
    private static DeckEntry AsEntry(DeckEntryDraft draft)
    {
        return new DeckEntry(
            draft.EntryId ?? Guid.Empty,
            draft.Quantity,
            draft.CardName,
            draft.OracleId,
            draft.PrintingId,
            draft.SetCode,
            draft.CollectorNumber,
            draft.Language,
            draft.Finish,
            draft.Zone,
            draft.SortOrder);
    }
}
