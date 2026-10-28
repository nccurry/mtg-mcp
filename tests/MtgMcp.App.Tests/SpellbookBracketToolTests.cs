using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Hosting;
using MtgMcp.App.Spellbook;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>spellbook_deck_bracket_facts_get</c> over a local deck: the facts carry the deck entry IDs, a stale
/// revision conflicts, and a large result stays under the size limit (BRACKET-001, SIZE-001).
/// </summary>
public sealed class SpellbookBracketToolTests
{
    /// <summary>
    /// Verifies the recorded response is mapped to flags with the local entry IDs, with no bracket in the result
    /// (cases B01 and B02).
    /// </summary>
    [Fact]
    public async Task GetFacts_RecordedResponse_ReturnsFlagsWithEntryIdsAndNoBracket()
    {
        string raw = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "spellbook", "estimate-bracket-response.json"),
            TestContext.Current.CancellationToken);
        using Setup setup = new(raw);
        DeckDocument deck = await setup.CreateDeckAsync(
        [
            new DeckEntryDraft(1, "Kinnan, Bonder Prodigy", Zone: "commander"),
            new DeckEntryDraft(1, "Thassa's Oracle", Zone: "main"),
            new DeckEntryDraft(1, "Armageddon", Zone: "main"),
            new DeckEntryDraft(1, "Not in the answer", Zone: "sideboard"),
        ]);

        SpellbookBracketFactsResult result = Success(await setup.Tools.GetFactsAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken));

        SpellbookBracketCardView oracle = result.Cards.Single(card => card.Name == "Thassa's Oracle");
        Assert.True(oracle.GameChanger);
        Assert.Equal(deck.Entries.Single(entry => entry.CardName == "Thassa's Oracle").EntryId,
            Assert.Single(oracle.EntryIds));
        Assert.Empty(result.Cards.Single(card => card.Name == "Sol Ring").EntryIds);
        Assert.Equal(deck.DeckId, result.Deck.DeckId);
        Assert.Contains(result.Deck.SkippedEntries, skipped => skipped.CardName == "Not in the answer");
        Assert.Equal(3, result.Combos.Count);
        string json = JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions);
        Assert.DoesNotContain("bracketTag", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.UnflaggedCardsOmitted);
    }

    /// <summary>
    /// Verifies a stale revision, a missing deck, and an outage are returned as their own failure kinds.
    /// </summary>
    [Fact]
    public async Task GetFacts_StaleMissingAndOutage_AreTypedFailures()
    {
        using Setup outage = new(null);
        DeckDocument deck = await outage.CreateDeckAsync([new DeckEntryDraft(1, "Sol Ring", Zone: "main")]);

        OperationConflict stale = Assert.IsType<OperationConflict>((await outage.Tools.GetFactsAsync(
            deck.DeckId,
            deck.Revision + 1,
            TestContext.Current.CancellationToken)).Value);
        OperationResult<SpellbookBracketFactsResult> missing = await outage.Tools.GetFactsAsync(
            Guid.NewGuid(),
            1,
            TestContext.Current.CancellationToken);
        OperationUnavailable down = Assert.IsType<OperationUnavailable>((await outage.Tools.GetFactsAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken)).Value);

        Assert.Equal(deck.Revision, stale.Current!.Revision);
        Assert.IsType<OperationNotFound>(missing.Value);
        Assert.Equal("source-unavailable", down.ReasonCode);
    }

    /// <summary>
    /// Verifies a very large answer keeps only the flagged cards and says how many it left out (SIZE-001).
    /// </summary>
    [Fact]
    public async Task GetFacts_VeryLargeAnswer_KeepsFlaggedCardsUnderTheLimit()
    {
        JsonArray cards = [];
        List<DeckEntryDraft> drafts = [];
        for (int index = 0; index < 300; index++)
        {
            string name = $"A card with a rather long name to take space {index:D3}";
            cards.Add(new JsonObject
            {
                ["card"] = new JsonObject { ["name"] = name, ["oracleId"] = Guid.NewGuid().ToString() },
                ["quantity"] = 1,
                ["banned"] = false,
                ["gameChanger"] = index == 7,
                ["massLandDenial"] = false,
                ["extraTurn"] = false,
            });
            drafts.Add(new DeckEntryDraft(1, name, Zone: "main"));
        }

        string raw = new JsonObject
        {
            ["bracketTag"] = "R",
            ["cards"] = cards,
            ["combos"] = new JsonArray()
        }.ToJsonString();
        using Setup setup = new(raw);
        DeckDocument deck = await setup.CreateDeckAsync(drafts);

        SpellbookBracketFactsResult result = Success(await setup.Tools.GetFactsAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken));

        int bytes = JsonSerializer.SerializeToUtf8Bytes(result, ToolCallPolicy.SerializerOptions).Length;
        Assert.True(bytes <= 40_000, $"The result is {bytes} bytes.");
        Assert.Equal(299, result.UnflaggedCardsOmitted);
        Assert.True(Assert.Single(result.Cards).GameChanger);
    }

    /// <summary>
    /// Verifies a local entry is matched to a Spellbook card by Oracle ID when both have one, and by name with the
    /// front face of a two-faced name matching either spelling, so a two-faced card is not left without its entries.
    /// </summary>
    [Fact]
    public async Task GetFacts_EntriesMatchByOracleIdThenByFrontFaceName()
    {
        Guid oracle = Guid.NewGuid();
        string raw = new JsonObject
        {
            ["bracketTag"] = "R",
            ["cards"] = new JsonArray(
                Card("Delver of Secrets // Insectile Aberration", null),
                Card("Renamed upstream", oracle),
                Card("Plain Card", null)),
            ["combos"] = new JsonArray(),
        }.ToJsonString();
        using Setup setup = new(raw);
        DeckDocument deck = await setup.CreateDeckAsync(
        [
            new DeckEntryDraft(1, "Delver of Secrets", Zone: "main"),
            new DeckEntryDraft(1, "An older name", OracleId: oracle, Zone: "main"),
            new DeckEntryDraft(1, "plain   CARD", Zone: "commander"),
        ]);

        SpellbookBracketFactsResult result = Success(await setup.Tools.GetFactsAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken));

        Guid EntryOf(string cardName)
        {
            return deck.Entries.Single(entry => entry.CardName == cardName).EntryId;
        }

        Assert.Equal(
            [EntryOf("Delver of Secrets")],
            result.Cards.Single(card => card.Name.StartsWith("Delver", StringComparison.Ordinal)).EntryIds);
        Assert.Equal(
            [EntryOf("An older name")],
            result.Cards.Single(card => card.Name == "Renamed upstream").EntryIds);
        Assert.Equal(
            [EntryOf("plain   CARD")],
            result.Cards.Single(card => card.Name == "Plain Card").EntryIds);
    }

    /// <summary>
    /// Verifies a provider answer with very many combos is cut to the size limit after the unflagged cards are
    /// dropped: the size is measured again, combos are cut from the end, and the result says how many it left out
    /// (SIZE-001).
    /// </summary>
    [Fact]
    public async Task GetFacts_VeryManyCombos_AreCutToTheSizeLimitAndCounted()
    {
        JsonArray cards = [];
        List<DeckEntryDraft> drafts = [];
        for (int index = 0; index < 120; index++)
        {
            string name = $"A flagged card with a rather long name to take space {index:D3}";
            cards.Add(Card(name, null, gameChanger: true));
            drafts.Add(new DeckEntryDraft(1, name, Zone: "main"));
        }

        JsonArray combos = [];
        for (int index = 0; index < 400; index++)
        {
            combos.Add(new JsonObject
            {
                ["definitelyTwoCard"] = false,
                ["arguablyTwoCard"] = false,
                ["speed"] = 1,
                ["combo"] = new JsonObject
                {
                    ["id"] = $"{index}-{index + 1}-{index + 2}",
                    ["manaNeeded"] = "{W}{U}{B}{R}{G}{W}{U}{B}{R}{G}",
                    ["manaValueNeeded"] = 10,
                    ["uses"] = new JsonArray(
                        UseOf($"Combo piece one with a long name {index:D3}"),
                        UseOf($"Combo piece two with a long name {index:D3}"),
                        UseOf($"Combo piece three with a long name {index:D3}")),
                },
            });
        }

        string raw = new JsonObject { ["bracketTag"] = "R", ["cards"] = cards, ["combos"] = combos }.ToJsonString();
        using Setup setup = new(raw);
        DeckDocument deck = await setup.CreateDeckAsync(drafts);

        SpellbookBracketFactsResult result = Success(await setup.Tools.GetFactsAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken));

        int bytes = JsonSerializer.SerializeToUtf8Bytes(result, ToolCallPolicy.SerializerOptions).Length;
        Assert.True(bytes <= 40_000, $"The result is {bytes} bytes.");
        Assert.Equal(120, result.Cards.Count);
        Assert.True(result.CombosOmitted > 0);
        Assert.Equal(400, result.Combos.Count + result.CombosOmitted);
        Assert.True(result.Truncated);
        Assert.False(string.IsNullOrEmpty(result.Narrow));
    }

    /// <summary>
    /// Builds one classified card of a Spellbook answer.
    /// </summary>
    private static JsonObject Card(string name, Guid? oracleId, bool gameChanger = false)
    {
        JsonObject card = new() { ["name"] = name };
        if (oracleId is Guid id)
        {
            card["oracleId"] = id.ToString();
        }

        return new JsonObject
        {
            ["card"] = card,
            ["quantity"] = 1,
            ["banned"] = false,
            ["gameChanger"] = gameChanger,
            ["massLandDenial"] = false,
            ["extraTurn"] = false,
        };
    }

    /// <summary>
    /// Builds one card use of a combo.
    /// </summary>
    private static JsonObject UseOf(string name)
    {
        return new JsonObject { ["card"] = new JsonObject { ["name"] = name } };
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return result.Value is OperationSuccess<T> success
            ? success.Data
            : throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(result.Value));
    }

    /// <summary>
    /// Builds the services behind the tool, with a fake Commander Spellbook that answers with one fixed body, or with
    /// an outage when the body is null.
    /// </summary>
    private sealed class Setup : IDisposable
    {
        /// <summary>
        /// Holds the folder behind the stores.
        /// </summary>
        private readonly TemporaryDirectory directory = new();

        /// <summary>
        /// Holds the deck store.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Holds the service whose transport is the fake.
        /// </summary>
        private readonly SpellbookService service;

        /// <summary>
        /// Creates the services.
        /// </summary>
        internal Setup(string? body)
        {
            store = new SqliteDeckStore(directory.Path, "test");
            SpellbookTransport transport = new(
                new HttpClient(new FakeSpellbook(body)),
                ownsHttpClient: true,
                TimeProvider.System);
            service = new SpellbookService(
                SpellbookOptions.CreateDefault(directory.Path),
                new SpellbookDatabase(directory.Path),
                transport,
                TimeProvider.System);
            Tools = new SpellbookBracketTools(service, new SpellbookDeckInputResolver(store));
        }

        /// <summary>
        /// Gets the tool under test.
        /// </summary>
        internal SpellbookBracketTools Tools { get; }

        /// <summary>
        /// Creates a deck from drafts.
        /// </summary>
        internal async Task<DeckDocument> CreateDeckAsync(IReadOnlyList<DeckEntryDraft> entries)
        {
            return Success(await store.CreateAsync(
                new DeckCreateRequest("Bracket", Format: "commander", Entries: entries),
                TestContext.Current.CancellationToken));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
            store.Dispose();
            directory.Dispose();
        }
    }

    /// <summary>
    /// Answers every request with one body, or with a 503 when there is none.
    /// </summary>
    private sealed class FakeSpellbook(string? body) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
        }
    }
}
