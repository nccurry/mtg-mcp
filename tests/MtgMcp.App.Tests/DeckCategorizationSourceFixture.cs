using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Supplies the smallest Scryfall-format card-data set needed for category-rule integration tests.
/// </summary>
internal static class DeckCategorizationSourceFixture
{
    /// <summary>
    /// Identifies the reviewed official Scryfall ramp tag used by common-v1.
    /// </summary>
    internal static readonly Guid RampTagId = Guid.Parse("2f3e4ad7-5e60-41b4-bdbc-653f16869cf6");

    /// <summary>
    /// Identifies the reviewed official Scryfall draw tag used by common-v1.
    /// </summary>
    internal static readonly Guid DrawTagId = Guid.Parse("b6448c45-ce65-4848-aa98-2151e4e07437");

    /// <summary>
    /// Identifies the reviewed official Scryfall removal tag used by common-v1.
    /// </summary>
    internal static readonly Guid RemovalTagId = Guid.Parse("444f824c-f910-4530-9dbe-ede7a84cd7f9");

    /// <summary>
    /// Identifies the reviewed official Scryfall recursion tag used by common-v1.
    /// </summary>
    internal static readonly Guid RecursionTagId = Guid.Parse("82b824ad-648f-467f-a190-2e0fa9a795d2");

    /// <summary>
    /// Identifies the fixture child tag assigned directly to the fixture card.
    /// </summary>
    internal static readonly Guid RampChildTagId = Guid.Parse("a1111111-1111-4111-8111-111111111111");

    /// <summary>
    /// Identifies the fixture card printing.
    /// </summary>
    internal static readonly Guid CardId = Guid.Parse("a2222222-2222-4222-8222-222222222222");

    /// <summary>
    /// Identifies the fixture card Oracle object.
    /// </summary>
    internal static readonly Guid OracleId = Guid.Parse("a3333333-3333-4333-8333-333333333333");

    /// <summary>
    /// Identifies the fixture card artwork.
    /// </summary>
    internal static readonly Guid IllustrationId = Guid.Parse("a4444444-4444-4444-8444-444444444444");

    /// <summary>
    /// Defines the required Scryfall data types in provider order.
    /// </summary>
    private static readonly string[] DatasetTypes = ["all_cards", "rulings", "oracle_tags", "art_tags"];

    /// <summary>
    /// Holds deterministic compressed Scryfall-format data for all required source types.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, byte[]> CompressedDatasets = CreateCompressedDatasets();

    /// <summary>
    /// Creates a source service that can install this fixture's data.
    /// </summary>
    internal static ScryfallService CreateService(string dataRoot, HttpMessageHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            new Uri("https://category.fixture/", UriKind.Absolute),
            TimeSpan.FromHours(24),
            timeProvider: null,
            handler);
    }

    /// <summary>
    /// Creates one fake provider handler for this source fixture.
    /// </summary>
    internal static DeckCategorizationSourceHandler CreateHandler()
    {
        return new DeckCategorizationSourceHandler();
    }

    /// <summary>
    /// Builds the official bulk-data metadata response for this fixture.
    /// </summary>
    private static string BulkMetadata()
    {
        object[] data = DatasetTypes.Select((type, index) => new
        {
            @object = "bulk_data",
            id = Guid.Parse($"b0000000-0000-4000-8000-{index + 1:D12}"),
            type,
            name = type.Replace('_', ' '),
            description = "Category-rule source fixture.",
            updated_at = "2026-09-07T21:00:33+00:00",
            size = 1024,
            content_type = "application/json",
            content_encoding = "gzip",
            uri = $"https://category.fixture/bulk-data/{type}",
            download_uri = $"https://category.fixture/download/{type}.json",
            jsonl_download_uri = $"https://category.fixture/download/{type}.jsonl.gz",
        }).ToArray();
        return JsonSerializer.Serialize(new { @object = "list", has_more = false, data });
    }

    /// <summary>
    /// Builds the complete compressed source-data set.
    /// </summary>
    private static IReadOnlyDictionary<string, byte[]> CreateCompressedDatasets()
    {
        string card = JsonSerializer.Serialize(new
        {
            @object = "card",
            id = CardId,
            oracle_id = OracleId,
            illustration_id = IllustrationId,
            name = "Fixture Ramp Card",
            set = "tst",
            collector_number = "1",
            lang = "en",
            released_at = "2026-09-07",
            mana_cost = "{1}",
            cmc = 1.0m,
            type_line = "Artifact",
            oracle_text = "Fixture text.",
            colors = Array.Empty<string>(),
            color_identity = Array.Empty<string>(),
            keywords = Array.Empty<string>(),
            legalities = new Dictionary<string, string> { ["commander"] = "legal" },
            image_uris = new Dictionary<string, string>(),
            prices = new Dictionary<string, string?>(),
        });
        string ruling = JsonSerializer.Serialize(new
        {
            @object = "ruling",
            oracle_id = OracleId,
            source = "wotc",
            published_at = "2026-09-07",
            comment = "Fixture ruling.",
        });
        string ramp = OracleTag(RampTagId, "Ramp", "ramp", [], [RampChildTagId], []);
        string draw = OracleTag(DrawTagId, "Draw", "draw", [], [], []);
        string removal = OracleTag(RemovalTagId, "Removal", "removal", [], [], []);
        string recursion = OracleTag(RecursionTagId, "Recursion", "recursion", [], [], []);
        string rampChild = OracleTag(
            RampChildTagId,
            "Fixture Ramp Child",
            "fixture-ramp-child",
            [RampTagId],
            [],
            [new { oracle_id = OracleId, weight = "strong" }]);
        string art = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = Guid.Parse("a5555555-5555-4555-8555-555555555555"),
            label = "Fixture Art",
            slug = "fixture-art",
            type = "illustration",
            description = "Fixture artwork tag.",
            parent_ids = Array.Empty<Guid>(),
            child_ids = Array.Empty<Guid>(),
            aliases = Array.Empty<string>(),
            taggings = new[] { new { illustration_id = IllustrationId, weight = "weak" } },
        });
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["all_cards"] = GzipLines([card]),
            ["rulings"] = GzipLines([ruling]),
            ["oracle_tags"] = GzipLines([ramp, draw, removal, recursion, rampChild]),
            ["art_tags"] = GzipLines([art]),
        };
    }

    /// <summary>
    /// Builds one Oracle-tag source object.
    /// </summary>
    private static string OracleTag(
        Guid id,
        string label,
        string slug,
        IReadOnlyList<Guid> parentIds,
        IReadOnlyList<Guid> childIds,
        IReadOnlyList<object> taggings)
    {
        return JsonSerializer.Serialize(new
        {
            @object = "tag",
            id,
            label,
            slug,
            type = "oracle",
            description = "Category-rule source fixture.",
            parent_ids = parentIds,
            child_ids = childIds,
            aliases = Array.Empty<string>(),
            taggings,
        });
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
    /// Handles the fixture's metadata and data-download requests.
    /// </summary>
    internal sealed class DeckCategorizationSourceHandler : HttpMessageHandler
    {
        /// <summary>
        /// Counts all requests sent through this handler.
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

            if (path.StartsWith("/download/", StringComparison.Ordinal))
            {
                string type = Path.GetFileName(path).Replace(".jsonl.gz", string.Empty, StringComparison.Ordinal);
                return Task.FromResult(Bytes(CompressedDatasets[type]));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
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

        /// <summary>
        /// Creates a binary source-data response.
        /// </summary>
        private static HttpResponseMessage Bytes(byte[] content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
        }
    }
}
