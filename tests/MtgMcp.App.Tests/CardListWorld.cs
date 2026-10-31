using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.Archidekt;
using MtgMcp.Archidekt.Tests;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Builds the small world the card list tests share: installed card data for six cards, a saved search, a local deck,
/// and a fake Archidekt server.
/// </summary>
internal sealed class CardListWorld : IDisposable
{
    /// <summary>
    /// Holds the services released with the world.
    /// </summary>
    private readonly List<IDisposable> owned = [];

    /// <summary>
    /// Creates a world around a card data fixture and the six cards it installed.
    /// </summary>
    private CardListWorld(CardDataFixture data, IReadOnlyList<JsonObject> cards)
    {
        Data = data;
        owned.Add(data);
        (Alpha, Bravo, Charlie, Delta, Echo, Foxtrot) = (cards[0], cards[1], cards[2], cards[3], cards[4], cards[5]);
        CreateArchidektService();
    }

    /// <summary>
    /// Gets the installed card data, deck store, and Scryfall service.
    /// </summary>
    internal CardDataFixture Data { get; }

    /// <summary>
    /// Gets the card named Alpha: green, mana value 1, one dollar, tagged ramp.
    /// </summary>
    internal JsonObject Alpha { get; }

    /// <summary>
    /// Gets the card named Bravo: blue, mana value 2, two dollars, tagged draw.
    /// </summary>
    internal JsonObject Bravo { get; }

    /// <summary>
    /// Gets the card named Charlie: black, mana value 3, no price.
    /// </summary>
    internal JsonObject Charlie { get; }

    /// <summary>
    /// Gets the card named Delta: red creature, mana value 2, tagged ramp and draw.
    /// </summary>
    internal JsonObject Delta { get; }

    /// <summary>
    /// Gets the card named Echo: a land that makes green or blue mana.
    /// </summary>
    internal JsonObject Echo { get; }

    /// <summary>
    /// Gets the card named Foxtrot: a green sorcery tagged ramp, mana value 4.
    /// </summary>
    internal JsonObject Foxtrot { get; }

    /// <summary>
    /// Gets the Archidekt handler, to queue responses.
    /// </summary>
    internal ArchidektTestHttpHandler Archidekt { get; } = new();

    /// <summary>
    /// Gets the Archidekt service over the fake handler.
    /// </summary>
    internal ArchidektService ArchidektService { get; private set; } = null!;

    /// <summary>
    /// Gets every card of the world.
    /// </summary>
    internal IReadOnlyList<JsonObject> Cards => [Alpha, Bravo, Charlie, Delta, Echo, Foxtrot];

    /// <summary>
    /// Creates the world. The saved search holds Alpha, Bravo, and Charlie.
    /// </summary>
    internal static async Task<CardListWorld> CreateAsync(bool install = true)
    {
        JsonObject alpha = CardJson.Make("Alpha", manaValue: 1m, colors: ["G"], usd: "1.00", producedMana: ["G"]);
        JsonObject bravo = CardJson.Make("Bravo", "Instant", "{1}{U}", 2m, ["U"], usd: "2.00");
        JsonObject charlie = CardJson.Make("Charlie", "Sorcery", "{2}{B}", 3m, ["B"]);
        JsonObject delta = CardJson.Make("Delta", "Creature — Goblin", "{1}{R}", 2m, ["R"], usd: "0.50");
        JsonObject echo = CardJson.Make("Echo", "Land", null, 0m, producedMana: ["G", "U"], usd: "0.25");
        JsonObject foxtrot = CardJson.Make("Foxtrot", "Sorcery", "{3}{G}", 4m, ["G"], usd: "3.00");
        Dictionary<string, string[]> tags = new(StringComparer.Ordinal)
        {
            ["Alpha"] = ["ramp"],
            ["Bravo"] = ["draw"],
            ["Delta"] = ["ramp", "draw"],
            ["Foxtrot"] = ["ramp"],
        };
        JsonObject[] cards = [alpha, bravo, charlie, delta, echo, foxtrot];
        CardDataFixture data = await CardDataFixture.CreateAsync(cards, tags, [alpha, bravo, charlie], install);
        return new CardListWorld(data, cards);
    }

    /// <summary>
    /// Creates a deck whose entries point at the world's cards in several ways, plus two entries that cannot match.
    /// </summary>
    internal async Task<DeckDocument> CreateDeckAsync()
    {
        DeckCreateRequest request = new(
            "Test deck",
            Format: "commander",
            Entries:
            [
                new DeckEntryDraft(1, "Alpha", PrintingId: CardJson.PrintingOf(Alpha), Zone: "main"),
                new DeckEntryDraft(3, "Delta", OracleId: CardJson.OracleOf(Delta), Zone: "main"),
                new DeckEntryDraft(1, "Foxtrot", SetCode: "tst", CollectorNumber: PrintingNumber(Foxtrot),
                    Zone: "commander"),
                new DeckEntryDraft(1, "Home-made proxy", Zone: "main"),
                new DeckEntryDraft(1, "Unknown printing",
                    PrintingId: Guid.Parse("a9999999-9999-4999-8999-999999999999"), Zone: "main"),
            ]);
        return Assert.IsType<OperationSuccess<DeckDocument>>(
            (await Data.Store.CreateAsync(request, TestContext.Current.CancellationToken)).Value).Data;
    }

    /// <summary>
    /// Builds a coordinator with every source available.
    /// </summary>
    internal CardListCoordinator Coordinator(bool decks = true, bool archidekt = true)
    {
        return Data.Coordinator(Data.Access(decks, archidekt ? ArchidektService : null));
    }

    /// <summary>
    /// Builds the JSON of an Archidekt deck that holds the given cards, one copy each, in the Mainboard category.
    /// </summary>
    internal static string RemoteDeck(params JsonObject[] cards)
    {
        JsonArray entries = [];
        for (int index = 0; index < cards.Length; index++)
        {
            entries.Add(new JsonObject
            {
                ["deckRelationId"] = 100 + index,
                ["quantity"] = 1,
                ["categories"] = new JsonArray("Mainboard"),
                ["card"] = new JsonObject
                {
                    ["id"] = 500 + index,
                    ["uid"] = CardJson.PrintingOf(cards[index]).ToString(),
                    ["edition"] = new JsonObject { ["editioncode"] = "tst" },
                    ["collectorNumber"] = cards[index]["collector_number"]!.GetValue<string>(),
                    ["oracleCard"] = new JsonObject
                    {
                        ["uid"] = CardJson.OracleOf(cards[index]).ToString(),
                        ["name"] = cards[index]["name"]!.GetValue<string>(),
                    },
                },
            });
        }

        return new JsonObject
        {
            ["id"] = 42,
            ["name"] = "Remote deck",
            ["description"] = string.Empty,
            ["deckFormat"] = 3,
            ["private"] = true,
            ["categories"] = new JsonArray(new JsonObject
            {
                ["id"] = 10,
                ["name"] = "Mainboard",
                ["includedInDeck"] = true,
                ["includedInPrice"] = true,
                ["sortOrder"] = 0,
            }),
            ["cards"] = entries,
        }.ToJsonString();
    }

    /// <summary>
    /// Queues the login and one remote deck response.
    /// </summary>
    internal void QueueRemoteDeck(params JsonObject[] cards)
    {
        Archidekt.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck(cards));
    }

    /// <summary>
    /// Lists the card names of a compare page in order.
    /// </summary>
    internal static List<string> Names(CardListsCompareResult result)
    {
        List<string> names = [];
        foreach (ScryfallCardView view in result.Page.Items)
        {
            names.Add(view.Value switch
            {
                ScryfallConciseCard concise => (string)concise.Fields.First(field => field.Key == "name").Value!,
                ScryfallCard card => card.Name,
                _ => throw new InvalidOperationException("Unexpected card view."),
            });
        }

        return names;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable item in owned)
        {
            item.Dispose();
        }
    }

    /// <summary>
    /// Reads the collector number of a card object.
    /// </summary>
    private static string PrintingNumber(JsonObject card)
    {
        return card["collector_number"]!.GetValue<string>();
    }

    /// <summary>
    /// Builds the Archidekt service over the fake handler.
    /// </summary>
    private void CreateArchidektService()
    {
        Archidekt.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
        HttpClient client = new(Archidekt) { BaseAddress = options.BaseAddress };
        ArchidektService = new ArchidektService(
            new ArchidektSession(client, ownsHttpClient: true, options),
            options.MaximumRequestsPerOperation);
        owned.Add(ArchidektService);
    }
}
