using System.Text.Json.Nodes;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks how new deck entries get their card identity from installed card data: which card an ambiguous or shared
/// name settles on, how a double-faced name resolves, that a batch of many names gives every entry its own identity,
/// and what happens when the lookup cannot run.
/// </summary>
public sealed class DeckEntryResolverTests
{
    /// <summary>
    /// Holds the full name of the recorded double-faced card.
    /// </summary>
    private const string DelverFullName = "Delver of Secrets // Insectile Aberration";

    /// <summary>
    /// Verifies a token and a card that share a name resolve to the card, even when the token is newer.
    /// </summary>
    [Fact]
    public async Task Resolve_TokenAndCardShareAName_ResolvesTheCard()
    {
        JsonObject card = CardJson.Make("Shared Name");
        JsonObject token = CardJson.Make("Shared Name", typeLine: "Token Creature — Goblin");
        token["layout"] = "token";
        token["released_at"] = "2026-06-01";
        using CardDataFixture data = await CardDataFixture.CreateAsync([card, token]);

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [new DeckEntryDraft(1, "Shared Name")],
            TestContext.Current.CancellationToken);

        Assert.Equal(CardJson.OracleOf(card), result.Drafts[0].OracleId);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Verifies a name that only a token has still resolves, because no playable card competes for it.
    /// </summary>
    [Fact]
    public async Task Resolve_OnlyATokenHasTheName_StillResolvesIt()
    {
        JsonObject token = CardJson.Make("Treasure Token", typeLine: "Token Artifact — Treasure");
        token["layout"] = "token";
        using CardDataFixture data = await CardDataFixture.CreateAsync([token]);

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [new DeckEntryDraft(1, "Treasure Token")],
            TestContext.Current.CancellationToken);

        Assert.Equal(CardJson.OracleOf(token), result.Drafts[0].OracleId);
    }

    /// <summary>
    /// Verifies two different cards that share a name are not resolved, and one ambiguous issue says how many cards
    /// carry the name.
    /// </summary>
    [Fact]
    public async Task Resolve_TwoCardsShareAName_ReportsAmbiguousAndResolvesNothing()
    {
        JsonObject first = CardJson.Make("Twin Name");
        JsonObject second = CardJson.Make("Twin Name");
        JsonObject other = CardJson.Make("Plain Name");
        using CardDataFixture data = await CardDataFixture.CreateAsync([first, second, other]);
        Guid twinId = Guid.CreateVersion7();

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [new DeckEntryDraft(1, "Twin Name", EntryId: twinId), new DeckEntryDraft(1, "Plain Name")],
            TestContext.Current.CancellationToken);

        Assert.Null(result.Drafts[0].OracleId);
        Assert.Equal(CardJson.OracleOf(other), result.Drafts[1].OracleId);
        DeckWriteIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DeckWriteIssue.Ambiguous, issue.Kind);
        Assert.Equal(twinId, issue.EntryId);
        Assert.Contains("2 different cards", issue.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies identity reconciliation uses the same lookup: a name shared by a token and a card resolves to the card,
    /// and a name shared by two cards is reported unresolved with the count.
    /// </summary>
    [Fact]
    public async Task Reconcile_SharedAndAmbiguousNames_FollowTheSameLookup()
    {
        JsonObject card = CardJson.Make("Shared Name");
        JsonObject token = CardJson.Make("Shared Name", typeLine: "Token Creature — Goblin");
        token["layout"] = "token";
        token["released_at"] = "2026-06-01";
        using CardDataFixture data = await CardDataFixture.CreateAsync(
            [card, token, CardJson.Make("Twin Name"), CardJson.Make("Twin Name")]);
        DeckDocument deck = Success(await data.Store.CreateAsync(
            new DeckCreateRequest(
                "Reconcile",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "Shared Name"), new DeckEntryDraft(1, "Twin Name")]),
            TestContext.Current.CancellationToken));

        DeckIdentityReconciliationPreview preview = Success(await new DeckIdentityReconciliationCoordinator(
            data.Store,
            data.Scryfall).PreviewAsync(
                deck.DeckId,
                deck.Revision,
                null,
                "cache-only",
                TestContext.Current.CancellationToken));

        DeckIdentityReconciliationRow shared = preview.Rows[0];
        DeckIdentityReconciliationRow twin = preview.Rows[1];
        Assert.Equal("resolved", shared.Status);
        Assert.Equal(CardJson.OracleOf(card), shared.After!.OracleId);
        Assert.Equal("unresolved", twin.Status);
        Assert.Contains("2 different cards", twin.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a copy of a card in another printing does not make its name ambiguous: one Oracle ID is one card.
    /// </summary>
    [Fact]
    public async Task Resolve_ManyPrintingsOfOneCard_IsNotAmbiguous()
    {
        Guid oracle = Guid.CreateVersion7();
        JsonObject first = CardJson.Make("Reprinted", oracleId: oracle);
        JsonObject second = CardJson.Make("Reprinted", oracleId: oracle);
        using CardDataFixture data = await CardDataFixture.CreateAsync([first, second]);

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [new DeckEntryDraft(1, "Reprinted")],
            TestContext.Current.CancellationToken);

        Assert.Equal(oracle, result.Drafts[0].OracleId);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Verifies a double-faced card typed by its front face resolves, and the entry takes Scryfall's full name.
    /// </summary>
    [Fact]
    public async Task Resolve_FrontFaceName_ResolvesToTheFullName()
    {
        JsonObject delver = RecordedCards.Object(RecordedCards.Transform);
        using CardDataFixture data = await CardDataFixture.CreateAsync([delver]);

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [new DeckEntryDraft(1, "Delver of Secrets"), new DeckEntryDraft(1, "insectile aberration")],
            TestContext.Current.CancellationToken);

        Assert.All(result.Drafts, draft => Assert.Equal(DelverFullName, draft.CardName));
        Assert.All(result.Drafts, draft => Assert.Equal(CardJson.OracleOf(delver), draft.OracleId));
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Verifies a front-face name with the set code and collector number of the card resolves the printing, and a
    /// name that is not a face of that card is reported.
    /// </summary>
    [Fact]
    public async Task Resolve_FrontFaceNameWithSetAndNumber_ResolvesThePrintingOrReportsAMismatch()
    {
        JsonObject delver = RecordedCards.Object(RecordedCards.Transform);
        using CardDataFixture data = await CardDataFixture.CreateAsync([delver]);

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [
                new DeckEntryDraft(1, "Delver of Secrets", SetCode: "inr", CollectorNumber: "60"),
                new DeckEntryDraft(1, "Lightning Bolt", SetCode: "inr", CollectorNumber: "60"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CardJson.PrintingOf(delver), result.Drafts[0].PrintingId);
        Assert.Equal(DelverFullName, result.Drafts[0].CardName);
        Assert.Null(result.Drafts[1].PrintingId);
        DeckWriteIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DeckWriteIssue.Unresolved, issue.Kind);
        Assert.Contains("not 'Lightning Bolt'", issue.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies names with spaces around them resolve like the same names without, and the entry keeps the trimmed
    /// card name from the card data.
    /// </summary>
    [Fact]
    public async Task Resolve_WhitespacePaddedNames_Resolve()
    {
        JsonObject card = CardJson.Make("Padded Card");
        using CardDataFixture data = await CardDataFixture.CreateAsync([card]);

        DeckDraftResolution result = await new DeckEntryResolver(data.Scryfall).ResolveAsync(
            [new DeckEntryDraft(1, "  Padded Card "), new DeckEntryDraft(1, "\tpadded card\n")],
            TestContext.Current.CancellationToken);

        Assert.All(result.Drafts, draft => Assert.Equal("Padded Card", draft.CardName));
        Assert.All(result.Drafts, draft => Assert.Equal(CardJson.OracleOf(card), draft.OracleId));
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Verifies a batch of more than 250 names, mixed with unknown names, entries that already have an identity, and
    /// update and remove changes, gives every added entry the identity of its own name across the lookup batches, and
    /// reports exactly the unknown ones.
    /// </summary>
    [Fact]
    public async Task ResolveChanges_ManyNamesAcrossBatches_GiveEachEntryItsOwnIdentity()
    {
        List<JsonObject> cards = [.. Enumerable.Range(0, 300).Select(index => CardJson.Make($"Batch Card {index:000}"))];
        using CardDataFixture data = await CardDataFixture.CreateAsync(cards);
        List<DeckChange> changes = [];
        List<(int Position, string Name, Guid? Expected, bool Pending)> adds = [];
        HashSet<Guid> unknownIds = [];
        Guid prefilled = Guid.CreateVersion7();
        for (int index = 0; index < 280; index++)
        {
            if (index % 40 == 20)
            {
                changes.Add(new UpdateDeckEntryChange(new DeckEntry(
                    Guid.CreateVersion7(), 1, "Updated", null, null, null, null, "en", "nonfoil", "main", 0)));
            }

            if (index % 50 == 25)
            {
                changes.Add(new RemoveDeckEntryChange(Guid.CreateVersion7()));
            }

            bool unknown = index % 13 == 5;
            bool alreadyKnown = !unknown && index % 17 == 3;
            string name = unknown ? $"Unknown Card {index:000}" : $"Batch Card {index:000}";
            Guid id = Guid.CreateVersion7();
            if (unknown)
            {
                unknownIds.Add(id);
            }

            adds.Add((changes.Count, name, unknown ? null : alreadyKnown ? prefilled : CardJson.OracleOf(cards[index]), !alreadyKnown));
            changes.Add(new AddDeckEntryChange(new DeckEntryDraft(
                1,
                name,
                OracleId: alreadyKnown ? prefilled : null,
                SortOrder: index,
                EntryId: id)));
        }

        DeckChangeResolution result = await new DeckEntryResolver(data.Scryfall)
            .ResolveChangesAsync(changes, TestContext.Current.CancellationToken);

        Assert.Equal(changes.Count, result.Changes.Count);
        foreach ((int position, string name, Guid? expected, _) in adds)
        {
            DeckEntryDraft draft = Assert.IsType<AddDeckEntryChange>(result.Changes[position].Value).Entry;
            Assert.Equal(expected, draft.OracleId);
            Assert.Equal(name, draft.CardName);
        }

        List<int> pending = [.. adds.Where(value => value.Pending).Select(value => value.Position)];
        Assert.True(pending.Count > 250, $"{pending.Count} names were looked up.");
        foreach (int pendingIndex in new[] { 0, 99, 100, pending.Count - 1 })
        {
            (int position, string name, Guid? expected, _) = adds.Single(value => value.Position == pending[pendingIndex]);
            Assert.Equal(expected, Assert.IsType<AddDeckEntryChange>(result.Changes[position].Value).Entry.OracleId);
        }

        Assert.All(result.Changes.Where(value => value.Value is not AddDeckEntryChange),
            change => Assert.Contains(change, changes));
        Assert.Equal(unknownIds, [.. result.Issues.Select(issue => issue.EntryId!.Value)]);
        Assert.All(result.Issues, issue => Assert.Equal(DeckWriteIssue.Unresolved, issue.Kind));
    }

    /// <summary>
    /// Verifies 60 unknown names add 60 unresolved entries, count 60 issues, list only the first 30, and say the
    /// list was cut.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_SixtyUnknownNames_CountSixtyIssuesAndListThirty()
    {
        using CardDataFixture data = await CardDataFixture.CreateAsync([CardJson.Make("Known Card")]);
        DeckBatchWriteTools tools = new(data.Store, OperationMode.Local, new DeckEntryResolver(data.Scryfall));
        DeckDocument deck = Success(await data.Store.CreateAsync(
            new DeckCreateRequest("Empty", Format: "custom"),
            TestContext.Current.CancellationToken));

        DeckWriteResult summary = Success(await tools.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [.. Enumerable.Range(0, 60).Select(index =>
                (DeckChangeInput)new AddDeckEntryInput(new DeckEntryDraft(1, $"Missing Card {index:00}")))],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(60, summary.IssueCount);
        Assert.Equal(DeckWriteResults.MaximumListedIssues, summary.Issues!.Count);
        Assert.True(summary.Truncated);
        Assert.Equal(60, summary.ChangeCounts.EntriesAdded);
        Assert.Equal(60, summary.EntryCount);
    }

    /// <summary>
    /// Verifies that when the installed card data cannot be read, the entries are still added without an identity and
    /// one skipped issue says why, instead of the write failing.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UnreadableCardData_AddsEntriesWithOneSkippedIssue()
    {
        using CardDataFixture data = await CardDataFixture.CreateAsync([CardJson.Make("Known Card")]);
        foreach (string file in Directory.GetFiles(data.Root, "scryfall.db", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(
                file,
                "This is not a database.",
                TestContext.Current.CancellationToken);
        }

        DeckBatchWriteTools tools = new(data.Store, OperationMode.Local, new DeckEntryResolver(data.Scryfall));
        DeckDocument deck = Success(await data.Store.CreateAsync(
            new DeckCreateRequest("Empty", Format: "custom"),
            TestContext.Current.CancellationToken));

        DeckWriteResult summary = Success(await tools.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new AddDeckEntryInput(new DeckEntryDraft(1, "Known Card")),
                new AddDeckEntryInput(new DeckEntryDraft(1, "Another Card")),
            ],
            returnDeck: true,
            TestContext.Current.CancellationToken));

        Assert.Equal(2, summary.Deck!.Entries.Count);
        Assert.All(summary.Deck.Entries, entry => Assert.Null(entry.OracleId));
        DeckWriteIssue issue = Assert.Single(summary.Issues!);
        Assert.Equal(DeckWriteIssue.Skipped, issue.Kind);
        Assert.Contains("could not be read", issue.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
