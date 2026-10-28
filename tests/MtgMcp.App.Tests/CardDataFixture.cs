using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Builds Scryfall card objects for tests, either small synthetic ones or the real cards recorded for the Commander
/// rule tests.
/// </summary>
internal static class CardJson
{
    /// <summary>
    /// Counts the cards built so far, so every card gets its own IDs.
    /// </summary>
    private static int counter;

    /// <summary>
    /// Builds one synthetic card object in Scryfall's shape.
    /// </summary>
    internal static JsonObject Make(
        string name,
        string typeLine = "Creature — Human",
        string? manaCost = "{1}",
        decimal manaValue = 1m,
        string[]? colors = null,
        string[]? colorIdentity = null,
        string[]? producedMana = null,
        string? usd = null,
        string[]? keywords = null,
        string? oracleText = null,
        string? power = null,
        string commander = "legal",
        bool? gameChanger = false,
        Guid? oracleId = null,
        Guid? printingId = null)
    {
        int number = Interlocked.Increment(ref counter);
        JsonObject card = new()
        {
            ["object"] = "card",
            ["id"] = (printingId ?? Guid.Parse($"00000000-0000-4000-8000-{number:D12}")).ToString(),
            ["oracle_id"] = (oracleId ?? Guid.Parse($"11111111-0000-4000-8000-{number:D12}")).ToString(),
            ["name"] = name,
            ["set"] = "tst",
            ["collector_number"] = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["lang"] = "en",
            ["released_at"] = "2026-01-01",
            ["layout"] = "normal",
            ["type_line"] = typeLine,
            ["cmc"] = manaValue,
            ["colors"] = Array(colors ?? []),
            ["color_identity"] = Array(colorIdentity ?? colors ?? []),
            ["keywords"] = Array(keywords ?? []),
            ["games"] = Array(["paper"]),
            ["reserved"] = false,
            ["rarity"] = "common",
            ["legalities"] = new JsonObject { ["commander"] = commander },
            ["prices"] = new JsonObject { ["usd"] = usd },
        };
        if (gameChanger is bool flag)
        {
            card["game_changer"] = flag;
        }

        if (manaCost is not null)
        {
            card["mana_cost"] = manaCost;
        }

        if (producedMana is not null)
        {
            card["produced_mana"] = Array(producedMana);
        }

        if (oracleText is not null)
        {
            card["oracle_text"] = oracleText;
        }

        if (power is not null)
        {
            card["power"] = power;
            card["toughness"] = power;
        }

        return card;
    }

    /// <summary>
    /// Reads the real cards recorded from Scryfall for the Commander rule tests, keyed by card name.
    /// </summary>
    internal static IReadOnlyDictionary<string, JsonObject> Recorded()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "commander-rules", "cards.jsonl");
        Dictionary<string, JsonObject> cards = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            JsonObject card = JsonNode.Parse(line)!.AsObject();
            cards[card["name"]!.GetValue<string>()] = card;
        }

        return cards;
    }

    /// <summary>
    /// Reads the oracle ID of a card object, using its first face when the card has none of its own.
    /// </summary>
    internal static Guid OracleOf(JsonObject card)
    {
        JsonNode? id = card["oracle_id"] ?? card["card_faces"]?[0]?["oracle_id"];
        return Guid.Parse(id!.GetValue<string>());
    }

    /// <summary>
    /// Reads the printing ID of a card object.
    /// </summary>
    internal static Guid PrintingOf(JsonObject card)
    {
        return Guid.Parse(card["id"]!.GetValue<string>());
    }

    /// <summary>
    /// Converts text values to a JSON array.
    /// </summary>
    private static JsonArray Array(string[] values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }
}

/// <summary>
/// Gives a test a deck store and a Scryfall service whose installed card data holds exactly the cards given, served by
/// a fake provider. A search request returns the cards given as search results, so a test can save a search.
/// </summary>
internal sealed class CardDataFixture : IDisposable
{
    /// <summary>
    /// Names the data sets a card-data install needs.
    /// </summary>
    private static readonly string[] DatasetTypes = ["all_cards", "rulings", "oracle_tags", "art_tags"];

    /// <summary>
    /// Holds the folder behind the stores, released with the fixture.
    /// </summary>
    private readonly List<IDisposable> owned = [];

    /// <summary>
    /// Creates a fixture around services that are already built.
    /// </summary>
    private CardDataFixture(
        SqliteDeckStore store,
        ScryfallService scryfall,
        FakeProvider provider,
        TemporaryDirectory folder)
    {
        Store = store;
        Scryfall = scryfall;
        Provider = provider;
        Root = folder.Path;
        owned.Add(folder);
    }

    /// <summary>
    /// Gets the deck store.
    /// </summary>
    internal SqliteDeckStore Store { get; }

    /// <summary>
    /// Gets the Scryfall service.
    /// </summary>
    internal ScryfallService Scryfall { get; }

    /// <summary>
    /// Gets the fake provider, to read how many requests it saw.
    /// </summary>
    internal FakeProvider Provider { get; }

    /// <summary>
    /// Gets the data root folder.
    /// </summary>
    internal string Root { get; }

    /// <summary>
    /// Creates a fixture; the card data is installed unless <paramref name="install"/> is false.
    /// </summary>
    internal static async Task<CardDataFixture> CreateAsync(
        IReadOnlyList<JsonObject> cards,
        IReadOnlyDictionary<string, string[]>? tagsByName = null,
        IReadOnlyList<JsonObject>? searchResults = null,
        bool install = true)
    {
        TemporaryDirectory folder = new();
        FakeProvider provider = new(cards, tagsByName ?? new Dictionary<string, string[]>(), searchResults ?? []);
        ScryfallService scryfall = new(
            folder.Path,
            allowLocalWrites: true,
            "test",
            ToolCallPolicy.SerializerOptions,
            new Uri("https://cards.fixture/", UriKind.Absolute),
            TimeSpan.FromHours(24),
            timeProvider: null,
            provider);
        SqliteDeckStore store = new(folder.Path, "test");
        if (install)
        {
            _ = await scryfall.SyncCorpusAsync("refresh", null, TestContext.Current.CancellationToken);
        }

        return new CardDataFixture(store, scryfall, provider, folder);
    }

    /// <summary>
    /// Builds the list source access for this fixture's deck store.
    /// </summary>
    internal CardListAccess Access(bool decks = true, MtgMcp.Archidekt.ArchidektService? archidekt = null)
    {
        return new CardListAccess(decks ? Store : null, archidekt);
    }

    /// <summary>
    /// Builds a coordinator over this fixture's services.
    /// </summary>
    internal CardListCoordinator Coordinator(CardListAccess? access = null)
    {
        return new CardListCoordinator(new CardListResolver(Scryfall, access ?? Access()), Scryfall);
    }

    /// <summary>
    /// Saves a search through the fake provider and returns its snapshot ID.
    /// </summary>
    internal async Task<Guid> SaveSearchAsync()
    {
        MtgMcp.Core.Results.OperationResult<ScryfallSearchResponse> result = await Scryfall.SearchAsync(
            "fixture",
            responseFormat: "concise",
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.IsType<MtgMcp.Core.Results.OperationSuccess<ScryfallSearchResponse>>(result.Value)
            .Data.Snapshot.SnapshotId;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Scryfall.Dispose();
        Store.Dispose();
        foreach (IDisposable item in owned)
        {
            item.Dispose();
        }
    }

    /// <summary>
    /// Serves card data downloads, bulk metadata, and one search result list.
    /// </summary>
    internal sealed class FakeProvider : HttpMessageHandler
    {
        /// <summary>
        /// Holds the compressed data sets by type.
        /// </summary>
        private readonly Dictionary<string, byte[]> datasets;

        /// <summary>
        /// Holds the search results as one list response.
        /// </summary>
        private readonly string searchJson;

        /// <summary>
        /// Creates the provider for a set of cards and tags.
        /// </summary>
        internal FakeProvider(
            IReadOnlyList<JsonObject> cards,
            IReadOnlyDictionary<string, string[]> tagsByName,
            IReadOnlyList<JsonObject> searchResults)
        {
            datasets = BuildDatasets(cards, tagsByName);
            JsonArray data = [];
            foreach (JsonObject card in searchResults)
            {
                data.Add(JsonNode.Parse(card.ToJsonString()));
            }

            searchJson = new JsonObject
            {
                ["object"] = "list",
                ["total_cards"] = searchResults.Count,
                ["has_more"] = false,
                ["data"] = data,
            }.ToJsonString();
        }

        /// <summary>
        /// Gets the number of requests the provider served.
        /// </summary>
        internal int RequestCount { get; private set; }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            RequestCount++;
            string path = request.RequestUri.AbsolutePath;
            if (path == "/bulk-data")
            {
                return Task.FromResult(Json(BulkMetadata()));
            }

            if (path == "/cards/search")
            {
                return Task.FromResult(Json(searchJson));
            }

            if (path.StartsWith("/download/", StringComparison.Ordinal))
            {
                string type = Path.GetFileName(path).Replace(".jsonl.gz", string.Empty, StringComparison.Ordinal);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(datasets[type]),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }

        /// <summary>
        /// Builds the compressed data sets for a set of cards and tags.
        /// </summary>
        private static Dictionary<string, byte[]> BuildDatasets(
            IReadOnlyList<JsonObject> cards,
            IReadOnlyDictionary<string, string[]> tagsByName)
        {
            Dictionary<string, List<Guid>> oraclesBySlug = new(StringComparer.Ordinal);
            foreach (JsonObject card in cards)
            {
                string name = card["name"]!.GetValue<string>();
                if (!tagsByName.TryGetValue(name, out string[]? slugs))
                {
                    continue;
                }

                foreach (string slug in slugs)
                {
                    if (!oraclesBySlug.TryGetValue(slug, out List<Guid>? oracles))
                    {
                        oracles = [];
                        oraclesBySlug[slug] = oracles;
                    }

                    oracles.Add(CardJson.OracleOf(card));
                }
            }

            oraclesBySlug.TryAdd("fixture-unused", []);
            List<string> tagLines = [];
            foreach ((string slug, List<Guid> oracles) in oraclesBySlug.OrderBy(pair => pair.Key,
                StringComparer.Ordinal))
            {
                JsonArray taggings = [];
                foreach (Guid oracle in oracles.Distinct())
                {
                    taggings.Add(new JsonObject { ["oracle_id"] = oracle.ToString(), ["weight"] = "strong" });
                }

                tagLines.Add(new JsonObject
                {
                    ["object"] = "tag",
                    ["id"] = TagId(slug).ToString(),
                    ["label"] = slug,
                    ["slug"] = slug,
                    ["type"] = "oracle",
                    ["description"] = "Fixture tag.",
                    ["parent_ids"] = new JsonArray(),
                    ["child_ids"] = new JsonArray(),
                    ["aliases"] = new JsonArray(),
                    ["taggings"] = taggings,
                }.ToJsonString());
            }

            Guid illustration = Guid.Parse("a4444444-4444-4444-8444-444444444444");
            string ruling = new JsonObject
            {
                ["object"] = "ruling",
                ["oracle_id"] = CardJson.OracleOf(cards[0]).ToString(),
                ["source"] = "wotc",
                ["published_at"] = "2026-01-01",
                ["comment"] = "Fixture ruling.",
            }.ToJsonString();
            string art = new JsonObject
            {
                ["object"] = "tag",
                ["id"] = "a5555555-5555-4555-8555-555555555555",
                ["label"] = "Fixture Art",
                ["slug"] = "fixture-art",
                ["type"] = "illustration",
                ["description"] = "Fixture art tag.",
                ["parent_ids"] = new JsonArray(),
                ["child_ids"] = new JsonArray(),
                ["aliases"] = new JsonArray(),
                ["taggings"] = new JsonArray(
                    new JsonObject { ["illustration_id"] = illustration.ToString(), ["weight"] = "weak" }),
            }.ToJsonString();
            return new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["all_cards"] = GzipLines([.. cards.Select(card => card.ToJsonString())]),
                ["rulings"] = GzipLines([ruling]),
                ["oracle_tags"] = GzipLines(tagLines),
                ["art_tags"] = GzipLines([art]),
            };
        }

        /// <summary>
        /// Derives a stable tag ID from a slug.
        /// </summary>
        private static Guid TagId(string slug)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(slug))[..16];
            hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            return new Guid(hash);
        }

        /// <summary>
        /// Builds the bulk metadata response for the fixture data sets.
        /// </summary>
        private static string BulkMetadata()
        {
            JsonArray data = [];
            for (int index = 0; index < DatasetTypes.Length; index++)
            {
                string type = DatasetTypes[index];
                data.Add(new JsonObject
                {
                    ["object"] = "bulk_data",
                    ["id"] = Guid.Parse($"b0000000-0000-4000-8000-{index + 1:D12}").ToString(),
                    ["type"] = type,
                    ["name"] = type.Replace('_', ' '),
                    ["description"] = "Fixture data.",
                    ["updated_at"] = "2026-09-07T21:00:33+00:00",
                    ["compressed_size"] = 1024,
                    ["uri"] = $"https://cards.fixture/bulk-data/{type}",
                    ["jsonl_download_uri"] = $"https://cards.fixture/download/{type}.jsonl.gz",
                });
            }

            return new JsonObject { ["object"] = "list", ["has_more"] = false, ["data"] = data }.ToJsonString();
        }

        /// <summary>
        /// Compresses UTF-8 JSON lines.
        /// </summary>
        private static byte[] GzipLines(IReadOnlyList<string> lines)
        {
            using MemoryStream output = new();
            using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
            using (StreamWriter writer = new(gzip, new UTF8Encoding(false)))
            {
                foreach (string line in lines)
                {
                    writer.WriteLine(line);
                }
            }

            return output.ToArray();
        }

        /// <summary>
        /// Creates a JSON response.
        /// </summary>
        private static HttpResponseMessage Json(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }
    }
}
