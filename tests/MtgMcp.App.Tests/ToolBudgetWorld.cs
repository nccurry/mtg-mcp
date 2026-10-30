using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MtgMcp.App.Archidekt;
using MtgMcp.App.CardLists;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.App.Statistics;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;
using MtgMcp.Statistics;

namespace MtgMcp.App.Tests;

/// <summary>
/// Runs the deck, Scryfall, statistics, and Archidekt toolsets in remote mode behind a real MCP client and server
/// joined by in-memory pipes, over the largest fixtures the offline fakes can hold: a 300-entry deck with a
/// maybeboard, 320 installed cards, a 120-item Archidekt deck list, and a 300-card Archidekt deck.
/// </summary>
internal sealed class ToolBudgetWorld : IAsyncDisposable
{
    /// <summary>
    /// Names the number of cards in the main fixture deck and in every large request.
    /// </summary>
    internal const int DeckSize = 300;

    /// <summary>
    /// Holds the tag every fixture card in the first third carries.
    /// </summary>
    internal const string TagSlug = "budget-ramp";

    /// <summary>
    /// Holds the services and files the world owns, released in reverse order.
    /// </summary>
    private readonly List<IDisposable> owned = [];

    /// <summary>
    /// Holds the fake Scryfall provider, which can announce newer card data.
    /// </summary>
    private ReferenceFixtureHandler? referenceHandler;

    /// <summary>
    /// Holds how many cards the fake Archidekt deck starts with.
    /// </summary>
    private int remoteDeckSize;

    /// <summary>
    /// Holds the running server host.
    /// </summary>
    private IHost? host;

    /// <summary>
    /// Creates an empty world; use <see cref="CreateAsync"/>.
    /// </summary>
    private ToolBudgetWorld()
    {
    }

    /// <summary>
    /// Gets the MCP client connected to the server.
    /// </summary>
    internal McpClient Client { get; private set; } = null!;

    /// <summary>
    /// Gets the deck store the server and the test share.
    /// </summary>
    internal SqliteDeckStore Store { get; private set; } = null!;

    /// <summary>
    /// Gets the fake Archidekt the server talks to.
    /// </summary>
    internal FakeArchidekt Fake { get; } = new();

    /// <summary>
    /// Gets the fixture cards, in order.
    /// </summary>
    internal IReadOnlyList<JsonObject> Cards { get; private set; } = [];

    /// <summary>
    /// Gets the name of every tool call made through <see cref="CallAsync"/>, in order.
    /// </summary>
    internal List<string> CalledTools { get; } = [];

    /// <summary>
    /// Gets the names of every tool the server published to the client.
    /// </summary>
    internal IReadOnlyList<string> PublishedTools { get; private set; } = [];

    /// <summary>
    /// Builds the fixtures, starts the server, and connects the client.
    /// </summary>
    internal static async Task<ToolBudgetWorld> CreateAsync(CancellationToken cancellationToken, int remoteDeckSize = DeckSize)
    {
        ToolBudgetWorld world = new() { remoteDeckSize = remoteDeckSize };
        try
        {
            await world.StartAsync(cancellationToken).ConfigureAwait(false);
            return world;
        }
        catch
        {
            await world.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Calls one tool and returns its structured result and the longest text it sent.
    /// </summary>
    internal async Task<ToolOutcome> CallAsync(string tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        CalledTools.Add(tool);
        Dictionary<string, object?> sent = [];
        foreach ((string key, JsonNode? argument) in arguments)
        {
            sent[key] = argument is null ? null : JsonSerializer.Deserialize<JsonElement>(argument.ToJsonString());
        }

        CallToolResult result = await Client.CallToolAsync(tool, sent, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        int characters = result.Content.OfType<TextContentBlock>().Sum(block => block.Text.Length);
        JsonElement structured = result.StructuredContent is JsonElement element ? element : default;
        if (structured.ValueKind == JsonValueKind.Object)
        {
            characters = Math.Max(characters, structured.GetRawText().Length);
        }

        string kind = structured.ValueKind == JsonValueKind.Object && structured.TryGetProperty("kind", out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
        return new ToolOutcome(tool, result.IsError == true, kind, characters, structured);
    }

    /// <summary>
    /// Makes the fake Scryfall provider announce newer card data, so the next sync installs a second generation.
    /// </summary>
    internal void PublishNewerCardData()
    {
        referenceHandler!.BulkVersion++;
    }

    /// <summary>
    /// Creates a new local deck, 300 entries unless told otherwise, with a maybeboard, categories, and assignments, and returns it.
    /// </summary>
    internal async Task<DeckDocument> CreateLocalDeckAsync(
        string name,
        bool resolved,
        CancellationToken cancellationToken,
        int size = DeckSize)
    {
        string[] categoryNames = ["Ramp", "Draw", "Removal", "Threats", "Lands", "Protection", "Finishers", "Utility"];
        List<DeckCategoryDraft> categories =
            [.. categoryNames.Select((value, index) => new DeckCategoryDraft(value, SortOrder: index, CategoryId: Guid.CreateVersion7()))];
        List<DeckEntryDraft> entries = [];
        List<DeckCategoryAssignment> assignments = [];
        for (int index = 0; index < size; index++)
        {
            JsonObject card = Cards[index];
            Guid entryId = Guid.CreateVersion7();
            entries.Add(new DeckEntryDraft(
                1,
                card["name"]!.GetValue<string>(),
                resolved ? CardJson.OracleOf(card) : null,
                resolved ? CardJson.PrintingOf(card) : null,
                resolved ? "tst" : null,
                resolved ? card["collector_number"]!.GetValue<string>() : null,
                Zone: index < size - (size / 8) ? "main" : "maybeboard",
                SortOrder: index,
                EntryId: entryId));
            assignments.Add(new DeckCategoryAssignment(entryId, categories[index % categories.Count].CategoryId!.Value, true));
            if (index % 2 == 0)
            {
                assignments.Add(new DeckCategoryAssignment(entryId, categories[(index + 3) % categories.Count].CategoryId!.Value, false));
            }
        }

        OperationResult<DeckDocument> created = await Store.CreateAsync(
            new DeckCreateRequest(name, Format: "commander", Entries: entries, Categories: categories, CategoryAssignments: assignments),
            cancellationToken).ConfigureAwait(false);
        return Assert.IsType<OperationSuccess<DeckDocument>>(created.Value).Data;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync().ConfigureAwait(false);
        }

        if (host is not null)
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }

        for (int index = owned.Count - 1; index >= 0; index--)
        {
            owned[index].Dispose();
        }
    }

    /// <summary>
    /// Builds the services, registers the toolsets the way the server does, and connects the client.
    /// </summary>
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        TemporaryDirectory folder = Own(new TemporaryDirectory());
        Cards = [.. Enumerable.Range(0, 320).Select(MakeCard)];
        Dictionary<string, string[]> tags = [];
        for (int index = 0; index < 110; index++)
        {
            tags[Cards[index]["name"]!.GetValue<string>()] = [TagSlug];
        }

        CardDataFixture.FakeProvider provider = new(Cards, tags, [.. Cards.Take(175)]);
        ScryfallService scryfall = Own(new ScryfallService(
            folder.Path,
            allowLocalWrites: true,
            "test",
            ToolCallPolicy.SerializerOptions,
            new Uri("https://cards.fixture/", UriKind.Absolute),
            TimeSpan.FromHours(24),
            timeProvider: null,
            referenceHandler = new ReferenceFixtureHandler(provider, Cards)));
        _ = await scryfall.SyncCorpusAsync("refresh", null, cancellationToken).ConfigureAwait(false);
        Store = Own(new SqliteDeckStore(folder.Path, "test"));
        ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 100_000,
            MaximumRequestsPerOperation = 2_000,
        };
        ArchidektService archidekt = Own(new ArchidektService(
            new ArchidektSession(new HttpClient(Fake) { BaseAddress = options.BaseAddress }, ownsHttpClient: true, options),
            options.MaximumRequestsPerOperation));
        FillFakeArchidekt();

        Pipe clientToServer = new();
        Pipe serverToClient = new();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        IMcpServerBuilder mcp = builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolCallPolicy.Filter));
        DeckToolsetManifest.Register(mcp, Store, scryfall, OperationMode.Remote);
        ScryfallToolsetManifest.Register(mcp, scryfall, OperationMode.Remote, new CardListAccess(Store, archidekt));
        StatisticsToolsetManifest.Register(mcp, Store, new ExactStatisticsCalculator("test"), scryfall);
        ArchidektToolsetManifest.Register(mcp, archidekt, Store, OperationMode.Remote);
        host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        Client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            new McpClientOptions
            {
                ProtocolVersion = "2026-07-28",
                DiscoverProbeTimeout = Timeout.InfiniteTimeSpan,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        IList<McpClientTool> tools = await Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        PublishedTools = [.. tools.Select(tool => tool.Name)];
    }

    /// <summary>
    /// Puts a 300-card deck, in six categories each, into the fake Archidekt.
    /// </summary>
    private void FillFakeArchidekt()
    {
        string[] names = ["Removal", "Ramp", "Draw", "Threats", "Lands", "Protection"];
        foreach (string name in names)
        {
            Fake.AddCategory(name);
        }

        for (int index = 0; index < remoteDeckSize; index++)
        {
            Fake.AddEntry(
                Fake.AddCardToCatalog(1000 + index, $"Remote Card {index:000}"),
                1,
                names[index % names.Length],
                names[(index + 1) % names.Length],
                names[(index + 2) % names.Length]);
        }
    }

    /// <summary>
    /// Builds one fixture card with enough text to look like a real printed card.
    /// </summary>
    private static JsonObject MakeCard(int index)
    {
        string[] types = ["Creature — Elf Druid", "Instant", "Sorcery", "Artifact", "Enchantment", "Land", "Creature — Goblin Warrior"];
        string[] colors = ["G", "U", "B", "R", "W"];
        string color = colors[index % colors.Length];
        return CardJson.Make(
            $"Budget Card {index:000}",
            types[index % types.Length],
            manaCost: "{1}{" + color + "}",
            manaValue: 2 + (index % 5),
            colors: [color],
            colorIdentity: [color],
            usd: (1 + (index % 20)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            keywords: ["Flying", "Trample"],
            oracleText: "When this enters, draw a card, then each opponent loses 1 life. " +
                "Whenever you cast a spell with the same color as this card, put a +1/+1 counter on target creature you control.",
            power: "2");
    }

    /// <summary>
    /// Registers a service for release when the world is disposed and returns it.
    /// </summary>
    private T Own<T>(T item)
        where T : IDisposable
    {
        owned.Add(item);
        return item;
    }

    /// <summary>
    /// Describes what one tool call returned.
    /// </summary>
    internal sealed record ToolOutcome(string Tool, bool IsError, string Kind, int Characters, JsonElement Structured)
    {
        /// <summary>
        /// Gets the data of a successful result.
        /// </summary>
        internal JsonElement Data
        {
            get
            {
                Assert.True(
                    Kind == "success",
                    $"{Tool} returned {Kind}: {Structured.GetRawText()[..Math.Min(600, Structured.GetRawText().Length)]}");
                return Structured.GetProperty("data");
            }
        }
    }

    /// <summary>
    /// Answers the reference routes the card data fake does not know and passes everything else to it.
    /// </summary>
    private sealed class ReferenceFixtureHandler(HttpMessageHandler inner, IReadOnlyList<JsonObject> cards)
        : DelegatingHandler(inner)
    {
        /// <summary>
        /// Gets or sets how many times newer card data was announced.
        /// </summary>
        internal int BulkVersion { get; set; }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/bulk-data" && BulkVersion > 0)
            {
                HttpResponseMessage bulk = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string body = await bulk.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Json(JsonNode.Parse(body.Replace("2026-09-07T21:00:33", $"2026-10-0{BulkVersion}T21:00:33", StringComparison.Ordinal))!.AsObject());
            }

            if (path == "/sets")
            {
                JsonArray sets = [];
                for (int index = 0; index < 400; index++)
                {
                    sets.Add(new JsonObject
                    {
                        ["object"] = "set",
                        ["id"] = Guid.Parse($"c0000000-0000-4000-8000-{index:D12}").ToString(),
                        ["code"] = $"s{index:000}",
                        ["name"] = $"Budget Set {index:000}",
                        ["set_type"] = "expansion",
                        ["released_at"] = "2026-01-01",
                        ["card_count"] = 250,
                        ["digital"] = false,
                        ["foil_only"] = false,
                        ["nonfoil_only"] = false,
                    });
                }

                return Json(new JsonObject { ["object"] = "list", ["has_more"] = false, ["data"] = sets });
            }

            if (path == "/cards/named")
            {
                string wanted = Uri.UnescapeDataString(request.RequestUri.Query.Split("exact=")[1].Split('&')[0]);
                JsonObject card = cards.First(value => value["name"]!.GetValue<string>() == wanted);
                return Json(JsonNode.Parse(card.ToJsonString())!.AsObject());
            }

            if (path.EndsWith("/rulings", StringComparison.Ordinal))
            {
                JsonArray rulings = [];
                for (int index = 0; index < 60; index++)
                {
                    rulings.Add(new JsonObject
                    {
                        ["object"] = "ruling",
                        ["oracle_id"] = CardJson.OracleOf(cards[0]).ToString(),
                        ["source"] = "wotc",
                        ["published_at"] = "2026-01-01",
                        ["comment"] = $"Budget ruling {index:00}: a long explanation of how this card works with other cards.",
                    });
                }

                return Json(new JsonObject { ["object"] = "list", ["has_more"] = false, ["data"] = rulings });
            }

            if (path.StartsWith("/catalog/", StringComparison.Ordinal))
            {
                JsonArray names = [];
                for (int index = 0; index < 600; index++)
                {
                    names.Add($"Budget Catalog Entry {index:000}");
                }

                return Json(new JsonObject { ["object"] = "catalog", ["total_values"] = 600, ["data"] = names });
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds one JSON response.
        /// </summary>
        private static HttpResponseMessage Json(JsonObject body)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
