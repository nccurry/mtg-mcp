using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Supplies small installed Scryfall card facts for on-curve App integration tests.
/// </summary>
internal static class DeckOnCurveSourceFixture
{
    /// <summary>
    /// Identifies the fixture target printing.
    /// </summary>
    internal static readonly Guid TargetPrintingId = Guid.Parse("10000000-0000-4000-8000-000000000001");

    /// <summary>
    /// Identifies the fixture same-turn green land printing.
    /// </summary>
    internal static readonly Guid GreenLandPrintingId = Guid.Parse("10000000-0000-4000-8000-000000000002");

    /// <summary>
    /// Identifies the fixture known-empty land printing.
    /// </summary>
    internal static readonly Guid KnownEmptyLandPrintingId = Guid.Parse("10000000-0000-4000-8000-000000000003");

    /// <summary>
    /// Identifies the fixture null produced-mana land printing.
    /// </summary>
    internal static readonly Guid NullLandPrintingId = Guid.Parse("10000000-0000-4000-8000-000000000004");

    /// <summary>
    /// Identifies the fixture multi-face printing.
    /// </summary>
    internal static readonly Guid MultiFacePrintingId = Guid.Parse("10000000-0000-4000-8000-000000000005");

    /// <summary>
    /// Identifies the fixture missing produced-mana land printing.
    /// </summary>
    internal static readonly Guid MissingLandPrintingId = Guid.Parse("10000000-0000-4000-8000-000000000006");

    /// <summary>
    /// Creates a write-enabled Scryfall service over the local fixture provider.
    /// </summary>
    internal static ScryfallService CreateService(string dataRoot, DeckOnCurveSourceHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            new Uri("https://on-curve.fixture/", UriKind.Absolute),
            handler: handler);
    }

    /// <summary>
    /// Creates one handler that serves all fixture card-data downloads.
    /// </summary>
    internal static DeckOnCurveSourceHandler CreateHandler()
    {
        return new DeckOnCurveSourceHandler();
    }

    /// <summary>
    /// Handles fixture metadata and compressed card-data downloads while counting every request.
    /// </summary>
    internal sealed class DeckOnCurveSourceHandler : HttpMessageHandler
    {
        /// <summary>
        /// Defines the fixed card-data types required for one installed generation.
        /// </summary>
        private static readonly string[] DatasetTypes = ["all_cards", "rulings", "oracle_tags", "art_tags"];

        /// <summary>
        /// Holds the compressed dataset bytes returned by this handler.
        /// </summary>
        private readonly IReadOnlyDictionary<string, byte[]> datasets = CreateDatasets();

        /// <summary>
        /// Counts every request received by the fake provider.
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
            if (string.Equals(path, "/bulk-data", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(BulkMetadata()));
            }

            if (path.StartsWith("/download/", StringComparison.Ordinal))
            {
                string datasetType = Path.GetFileName(path).Replace(".jsonl.gz", string.Empty, StringComparison.Ordinal);
                return Task.FromResult(Bytes(datasets[datasetType]));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }

        /// <summary>
        /// Creates the complete Scryfall bulk-data metadata response.
        /// </summary>
        private static string BulkMetadata()
        {
            object[] data = DatasetTypes.Select((datasetType, index) => new
            {
                @object = "bulk_data",
                id = Guid.Parse($"20000000-0000-4000-8000-{index + 1:D12}"),
                type = datasetType,
                name = datasetType.Replace('_', ' '),
                description = "On-curve source fixture.",
                updated_at = "2026-09-10T00:00:00+00:00",
                compressed_size = 1024,
                uri = $"https://on-curve.fixture/bulk-data/{datasetType}",
                jsonl_download_uri = $"https://on-curve.fixture/download/{datasetType}.jsonl.gz",
            }).ToArray();
            return JsonSerializer.Serialize(new { @object = "list", has_more = false, data });
        }

        /// <summary>
        /// Creates compressed lines for every required source dataset.
        /// </summary>
        private static IReadOnlyDictionary<string, byte[]> CreateDatasets()
        {
            IReadOnlyList<JsonObject> cards =
            [
                Card(TargetPrintingId, "Fixture Growth", "{1}{G}", "Creature", ProducedManaKind.Missing),
                Card(GreenLandPrintingId, "Fixture Forest", null, "Basic Land — Forest", ProducedManaKind.Values, ["G"]),
                Card(KnownEmptyLandPrintingId, "Fixture Empty Land", null, "Land", ProducedManaKind.Values, []),
                Card(NullLandPrintingId, "Fixture Null Land", null, "Land", ProducedManaKind.Null),
                Card(MultiFacePrintingId, "Fixture Front // Fixture Back", "{G}", "Land", ProducedManaKind.Values, ["G"], true),
                Card(MissingLandPrintingId, "Fixture Missing Land", null, "Land", ProducedManaKind.Missing),
            ];
            IReadOnlyList<string> rulings = cards.Select(card => JsonSerializer.Serialize(new
            {
                @object = "ruling",
                oracle_id = card["oracle_id"]!.GetValue<string>(),
                source = "wotc",
                published_at = "2026-09-10",
                comment = "Fixture ruling.",
            })).ToArray();
            string oracleTag = JsonSerializer.Serialize(new
            {
                @object = "tag",
                id = Guid.Parse("30000000-0000-4000-8000-000000000001"),
                label = "Fixture Oracle Tag",
                slug = "fixture-oracle-tag",
                type = "oracle",
                description = "Fixture tag.",
                parent_ids = Array.Empty<Guid>(),
                child_ids = Array.Empty<Guid>(),
                aliases = Array.Empty<string>(),
                taggings = Array.Empty<object>(),
            });
            string artTag = JsonSerializer.Serialize(new
            {
                @object = "tag",
                id = Guid.Parse("30000000-0000-4000-8000-000000000002"),
                label = "Fixture Art Tag",
                slug = "fixture-art-tag",
                type = "illustration",
                description = "Fixture tag.",
                parent_ids = Array.Empty<Guid>(),
                child_ids = Array.Empty<Guid>(),
                aliases = Array.Empty<string>(),
                taggings = Array.Empty<object>(),
            });
            return new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["all_cards"] = GzipLines(cards.Select(card => card.ToJsonString()).ToArray()),
                ["rulings"] = GzipLines(rulings),
                ["oracle_tags"] = GzipLines([oracleTag]),
                ["art_tags"] = GzipLines([artTag]),
            };
        }

        /// <summary>
        /// Builds one minimal card object with a chosen direct produced-mana source state.
        /// </summary>
        private static JsonObject Card(
            Guid printingId,
            string name,
            string? manaCost,
            string typeLine,
            ProducedManaKind producedManaKind,
            IReadOnlyList<string>? producedMana = null,
            bool multiFace = false)
        {
            JsonObject card = new()
            {
                ["object"] = "card",
                ["id"] = printingId.ToString("D"),
                ["oracle_id"] = OracleId(printingId).ToString("D"),
                ["name"] = name,
                ["set"] = "tst",
                ["collector_number"] = printingId.ToString("N")[..6],
                ["lang"] = "en",
                ["released_at"] = "2026-09-10",
                ["mana_cost"] = manaCost,
                ["cmc"] = 1.0m,
                ["type_line"] = typeLine,
                ["oracle_text"] = "Fixture text.",
                ["colors"] = new JsonArray(),
                ["color_identity"] = new JsonArray(),
                ["keywords"] = new JsonArray(),
                ["legalities"] = new JsonObject(),
                ["image_uris"] = new JsonObject(),
                ["prices"] = new JsonObject(),
            };
            if (producedManaKind == ProducedManaKind.Null)
            {
                card["produced_mana"] = null;
            }
            else if (producedManaKind == ProducedManaKind.Values)
            {
                JsonArray colors = new();
                foreach (string color in producedMana ?? [])
                {
                    colors.Add(color);
                }

                card["produced_mana"] = colors;
            }

            if (multiFace)
            {
                card["card_faces"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Fixture Front" },
                    new JsonObject { ["name"] = "Fixture Back" },
                };
            }

            return card;
        }

        /// <summary>
        /// Derives a stable distinct Oracle identity for each fixture printing.
        /// </summary>
        private static Guid OracleId(Guid printingId)
        {
            return new Guid(
                printingId.ToByteArray().Select((value, index) => index == 0 ? (byte)(value ^ 0xff) : value).ToArray());
        }

        /// <summary>
        /// Compresses complete UTF-8 JSON lines with terminal newlines.
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
        /// Creates a JSON fixture response.
        /// </summary>
        private static HttpResponseMessage Json(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
        }

        /// <summary>
        /// Creates a binary fixture response.
        /// </summary>
        private static HttpResponseMessage Bytes(byte[] content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
        }
    }

    /// <summary>
    /// Names the three direct source states needed by this fixture.
    /// </summary>
    private enum ProducedManaKind
    {
        /// <summary>
        /// Omits the provider field.
        /// </summary>
        Missing,

        /// <summary>
        /// Writes the provider field as null.
        /// </summary>
        Null,

        /// <summary>
        /// Writes the provider field as an array.
        /// </summary>
        Values,
    }
}
