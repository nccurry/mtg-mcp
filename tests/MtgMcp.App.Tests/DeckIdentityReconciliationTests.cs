using System.Net;
using System.Text;
using System.Text.Json;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies exact identity preview, evidence replay, and atomic deck-only application.
/// </summary>
public sealed class DeckIdentityReconciliationTests
{
    /// <summary>
    /// Identifies the stable fake Scryfall printing.
    /// </summary>
    private static readonly Guid CardId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    /// <summary>
    /// Identifies the stable fake Oracle card.
    /// </summary>
    private static readonly Guid OracleId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <summary>
    /// Verifies every precedence path, deduplication, replay, and preservation of non-identity fields.
    /// </summary>
    [Fact]
    public async Task PreviewAndApply_UseExactPrecedenceAndPreserveUnrelatedDeckState()
    {
        using TemporaryDirectory temporary = new();
        IdentityProviderHandler handler = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = CreateScryfall(temporary.Path, handler);
        DeckIdentityReconciliationCoordinator coordinator = new(store, scryfall);
        Guid categoryId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        Guid printingEntryId = Guid.CreateVersion7();
        Guid setEntryId = Guid.CreateVersion7();
        Guid oracleEntryId = Guid.CreateVersion7();
        Guid nameEntryId = Guid.CreateVersion7();
        Guid duplicateNameEntryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Identity Fixture",
                Format: "custom",
                Entries:
                [
                    new DeckEntryDraft(2, "Canonical Card", PrintingId: CardId, Language: "en", Finish: "foil", Zone: "sideboard", SortOrder: 4, EntryId: printingEntryId),
                    new DeckEntryDraft(1, "Canonical Card", SetCode: "tst", CollectorNumber: "7", Language: "en", EntryId: setEntryId),
                    new DeckEntryDraft(1, "Canonical Card", OracleId: OracleId, Language: "en", EntryId: oracleEntryId),
                    new DeckEntryDraft(1, "canonical card", Language: "en", EntryId: nameEntryId),
                    new DeckEntryDraft(1, "canonical card", Language: "en", EntryId: duplicateNameEntryId),
                ],
                Categories: [new DeckCategoryDraft("Evidence", CategoryId: categoryId)],
                CategoryAssignments: [new DeckCategoryAssignment(printingEntryId, categoryId, true)],
                ProviderBindings:
                [
                    new DeckProviderBinding(bindingId, "fixture", "remote-1", null, null, null, null, null),
                ]),
            TestContext.Current.CancellationToken));

        DeckIdentityReconciliationPreview preview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            null,
            "default",
            TestContext.Current.CancellationToken));

        Assert.True(preview.IsComplete);
        Assert.Equal(4, preview.ProposedChangeCount);
        Dictionary<Guid, DeckIdentityReconciliationRow> rows = preview.Rows.ToDictionary(value => value.EntryId);
        Assert.Equal("scryfall-printing-id", rows[printingEntryId].MatchedBy);
        Assert.Equal("resolved", rows[printingEntryId].Status);
        Assert.Equal("set-collector-language", rows[setEntryId].MatchedBy);
        Assert.Equal("resolved", rows[setEntryId].Status);
        Assert.Equal("oracle-id", rows[oracleEntryId].MatchedBy);
        Assert.Equal("unchanged", rows[oracleEntryId].Status);
        Assert.Equal("exact-name", rows[nameEntryId].MatchedBy);
        Assert.Equal("resolved", rows[nameEntryId].Status);
        Assert.Equal("exact-name", rows[duplicateNameEntryId].MatchedBy);
        Assert.Equal("resolved", rows[duplicateNameEntryId].Status);
        Assert.NotNull(preview.Evidence?.Snapshot);
        Assert.Single(handler.RequestBodies);
        using (JsonDocument request = JsonDocument.Parse(handler.RequestBodies[0]))
        {
            Assert.Equal(4, request.RootElement.GetProperty("identifiers").GetArrayLength());
        }

        DeckDocument applied = RequireSuccess(await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            allowPartial: false,
            TestContext.Current.CancellationToken));

        Assert.Equal(deck.Revision + 1, applied.Revision);
        Assert.Single(handler.RequestBodies);
        DeckEntry printing = Assert.Single(applied.Entries, value => value.EntryId == printingEntryId);
        Assert.Equal(2, printing.Quantity);
        Assert.Equal("foil", printing.Finish);
        Assert.Equal("sideboard", printing.Zone);
        Assert.Equal(4, printing.SortOrder);
        Assert.Equal(deck.Categories.ToArray(), applied.Categories.ToArray());
        Assert.Equal(deck.CategoryAssignments.ToArray(), applied.CategoryAssignments.ToArray());
        Assert.Equal(deck.ProviderBindings.ToArray(), applied.ProviderBindings.ToArray());
        DeckEntry setResolved = Assert.Single(applied.Entries, value => value.EntryId == setEntryId);
        Assert.Equal(CardId, setResolved.PrintingId);
        Assert.Equal(OracleId, setResolved.OracleId);
        DeckEntry nameResolved = Assert.Single(applied.Entries, value => value.EntryId == nameEntryId);
        Assert.Equal("Canonical Card", nameResolved.CardName);
        Assert.Equal(OracleId, nameResolved.OracleId);
        Assert.Null(nameResolved.PrintingId);
        Assert.Null(nameResolved.SetCode);
    }

    /// <summary>
    /// Verifies conflicts require partial authorization and tampered or stale requests never mutate.
    /// </summary>
    [Fact]
    public async Task Apply_RejectsTamperingStaleRevisionsAndUnauthorizedPartialResults()
    {
        using TemporaryDirectory temporary = new();
        IdentityProviderHandler handler = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = CreateScryfall(temporary.Path, handler);
        DeckIdentityReconciliationCoordinator coordinator = new(store, scryfall);
        Guid resolvedId = Guid.CreateVersion7();
        Guid conflictId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Partial Fixture",
                Entries:
                [
                    new DeckEntryDraft(1, "canonical card", EntryId: resolvedId),
                    new DeckEntryDraft(1, "Conflicting Name", OracleId: OracleId, EntryId: conflictId),
                ]),
            TestContext.Current.CancellationToken));
        DeckIdentityReconciliationPreview preview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            null,
            "default",
            TestContext.Current.CancellationToken));

        Assert.False(preview.IsComplete);
        Assert.Equal(["resolved", "conflict"], preview.Rows.Select(value => value.Status));
        string tampered = preview.ApplyToken[..^1] + (preview.ApplyToken[^1] == 'A' ? 'B' : 'A');
        Assert.IsType<OperationInvalidInput>((await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            tampered,
            true,
            TestContext.Current.CancellationToken)).Value);
        string incompleteEnvelope = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Assert.IsType<OperationInvalidInput>((await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            incompleteEnvelope,
            true,
            TestContext.Current.CancellationToken)).Value);
        DeckIdentityReconciliationCoordinator restartedCoordinator = new(store, scryfall);
        OperationInvalidInput processBound = Assert.IsType<OperationInvalidInput>(
            (await restartedCoordinator.ApplyAsync(
                deck.DeckId,
                deck.Revision,
                preview.PreviewFingerprint,
                preview.ApplyToken,
                true,
                TestContext.Current.CancellationToken)).Value);
        Assert.Equal("invalid-identity-apply-token", processBound.ReasonCode);
        OperationInvalidInput partial = Assert.IsType<OperationInvalidInput>((await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            false,
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("partial-identity-reconciliation-not-allowed", partial.ReasonCode);
        Assert.Equal(deck.Revision, RequireSuccess(await store.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken)).Revision);

        DeckDocument applied = RequireSuccess(await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            true,
            TestContext.Current.CancellationToken));
        Assert.Equal("Canonical Card", Assert.Single(applied.Entries, value => value.EntryId == resolvedId).CardName);
        Assert.Equal("Conflicting Name", Assert.Single(applied.Entries, value => value.EntryId == conflictId).CardName);
        OperationConflict stale = Assert.IsType<OperationConflict>((await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            true,
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("deck-revision-conflict", stale.ReasonCode);
    }

    /// <summary>
    /// Verifies deleted preview evidence produces an explicit unavailable result without deck mutation.
    /// </summary>
    [Fact]
    public async Task Apply_PrunedEvidenceReturnsUnavailableWithoutMutation()
    {
        using TemporaryDirectory temporary = new();
        IdentityProviderHandler handler = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = CreateScryfall(temporary.Path, handler);
        DeckIdentityReconciliationCoordinator coordinator = new(store, scryfall);
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Pruned Fixture", Entries: [new DeckEntryDraft(1, "canonical card")]),
            TestContext.Current.CancellationToken));
        DeckIdentityReconciliationPreview preview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            null,
            "default",
            TestContext.Current.CancellationToken));
        ScryfallSnapshotReference snapshot = preview.Evidence!.Snapshot!;
        _ = RequireSuccess(await scryfall.DeleteSnapshotAsync(
            snapshot.SnapshotId,
            snapshot.Checksum,
            true,
            TestContext.Current.CancellationToken));

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>((await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            preview.PreviewFingerprint,
            preview.ApplyToken,
            false,
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("identity-evidence-unavailable", unavailable.ReasonCode);
        DeckDocument unchanged = RequireSuccess(await store.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken));
        Assert.Equal(deck.Revision, unchanged.Revision);
        Assert.Equal(deck.Entries.ToArray(), unchanged.Entries.ToArray());
    }

    /// <summary>
    /// Verifies invalid selections and cache-only misses are explicit before mutation.
    /// </summary>
    [Fact]
    public async Task Preview_RejectsInvalidSelectionAndReportsCacheOnlyMiss()
    {
        using TemporaryDirectory temporary = new();
        IdentityProviderHandler handler = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = CreateScryfall(temporary.Path, handler);
        DeckIdentityReconciliationCoordinator coordinator = new(store, scryfall);
        Guid entryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Cache Fixture", Entries: [new DeckEntryDraft(1, "canonical card", EntryId: entryId)]),
            TestContext.Current.CancellationToken));

        Assert.IsType<OperationInvalidInput>((await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            [entryId, entryId],
            "default",
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            [Guid.CreateVersion7()],
            "default",
            TestContext.Current.CancellationToken)).Value);
        Assert.Empty(handler.RequestBodies);

        DeckIdentityReconciliationPreview cached = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            [entryId],
            "cache-only",
            TestContext.Current.CancellationToken));
        Assert.False(cached.IsComplete);
        Assert.Equal("not-cached", Assert.Single(cached.Rows).Status);
        Assert.Empty(handler.RequestBodies);

        DeckDocument oversized = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Oversized Identity Fixture",
                Format: "custom",
                Entries: Enumerable.Range(0, 151)
                    .Select(index => new DeckEntryDraft(1, $"Exact Fixture {index}"))
                    .ToArray()),
            TestContext.Current.CancellationToken));
        OperationInvalidInput tooMany = Assert.IsType<OperationInvalidInput>((await coordinator.PreviewAsync(
            oversized.DeckId,
            oversized.Revision,
            null,
            "default",
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("invalid-identity-entry-selection", tooMany.ReasonCode);
        Assert.Empty(handler.RequestBodies);
    }

    /// <summary>
    /// Creates one write-enabled Scryfall service over the fake collection contract.
    /// </summary>
    private static ScryfallService CreateScryfall(string dataRoot, HttpMessageHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            new Uri("https://identity.fixture/", UriKind.Absolute),
            handler: handler);
    }

    /// <summary>
    /// Builds the complete representative card object returned by the fake provider.
    /// </summary>
    private static string CardJson()
    {
        return JsonSerializer.Serialize(new
        {
            @object = "card",
            id = CardId,
            oracle_id = OracleId,
            name = "Canonical Card",
            set = "tst",
            collector_number = "7",
            lang = "en",
            released_at = "2026-07-06",
            mana_cost = "{1}",
            cmc = 1.0m,
            type_line = "Artifact",
            oracle_text = "Fixture text.",
            colors = Array.Empty<string>(),
            color_identity = Array.Empty<string>(),
            keywords = Array.Empty<string>(),
            legalities = new Dictionary<string, string>(),
            image_uris = new Dictionary<string, string>(),
            prices = new Dictionary<string, string?>(),
        });
    }

    /// <summary>
    /// Extracts one successful result or fails the current assertion.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Captures collection requests and returns one stable card for every batch.
    /// </summary>
    private sealed class IdentityProviderHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets the captured collection request bodies in call order.
        /// </summary>
        internal List<string> RequestBodies { get; } = [];

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath != "/cards/collection")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            }

            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            string response = JsonSerializer.Serialize(new
            {
                @object = "list",
                data = new[] { JsonSerializer.Deserialize<JsonElement>(CardJson()) },
                not_found = Array.Empty<object>(),
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
