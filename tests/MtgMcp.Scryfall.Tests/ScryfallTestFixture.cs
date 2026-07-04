using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Provides a dated, sanitized miniature of the official 2026-07-04 Scryfall bulk and API contracts.
/// </summary>
internal static class ScryfallTestFixture
{
    /// <summary>
    /// Reuses stable tag and catalog arrays across fixture serialization.
    /// </summary>
    private static readonly string[] BeatdownAliases = ["beatdown"];

    /// <summary>
    /// Reuses the child tag alias array across fixture serialization.
    /// </summary>
    private static readonly string[] WeenieAliases = ["weenie"];

    /// <summary>
    /// Reuses the provider warning array across responses.
    /// </summary>
    private static readonly string[] ProviderWarnings = ["fixture warning"];

    /// <summary>
    /// Reuses catalog values across responses.
    /// </summary>
    private static readonly string[] CatalogValues = ["Human", "Knight"];

    /// <summary>
    /// Reuses autocomplete values across responses.
    /// </summary>
    private static readonly string[] AutocompleteValues = ["Venerable Knight", "Venerable Warsinger"];

    /// <summary>
    /// Identifies the first fixture printing.
    /// </summary>
    internal static readonly Guid WhiteCardId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    /// <summary>
    /// Identifies the first fixture Oracle card.
    /// </summary>
    internal static readonly Guid WhiteOracleId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <summary>
    /// Identifies the first fixture illustration.
    /// </summary>
    internal static readonly Guid WhiteIllustrationId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <summary>
    /// Identifies the second fixture printing.
    /// </summary>
    internal static readonly Guid RedCardId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>
    /// Identifies the second fixture Oracle card.
    /// </summary>
    internal static readonly Guid RedOracleId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>
    /// Identifies the second fixture illustration.
    /// </summary>
    internal static readonly Guid RedIllustrationId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    /// <summary>
    /// Identifies the parent creature-role tag.
    /// </summary>
    internal static readonly Guid AggroTagId = Guid.Parse("55555555-5555-4555-8555-555555555555");

    /// <summary>
    /// Identifies the child white-weenie tag.
    /// </summary>
    internal static readonly Guid WeenieTagId = Guid.Parse("66666666-6666-4666-8666-666666666666");

    /// <summary>
    /// Identifies the fixture artwork tag.
    /// </summary>
    internal static readonly Guid ArtTagId = Guid.Parse("77777777-7777-4777-8777-777777777777");

    /// <summary>
    /// Gets the stable fake API origin.
    /// </summary>
    internal static Uri ApiBaseUri { get; } = new("https://fixture.test/");

    /// <summary>
    /// Builds a representative single-face card with an unknown extension field.
    /// </summary>
    internal static string Card(
        Guid id,
        Guid oracleId,
        Guid illustrationId,
        string name,
        string set,
        string collector,
        string manaCost,
        IReadOnlyList<string> colors)
    {
        return JsonSerializer.Serialize(new
        {
            @object = "card",
            id,
            oracle_id = oracleId,
            illustration_id = illustrationId,
            name,
            set,
            collector_number = collector,
            lang = "en",
            released_at = "2026-07-04",
            mana_cost = manaCost,
            cmc = 1.0m,
            type_line = "Creature — Human",
            oracle_text = "Fixture rules text.",
            colors,
            color_identity = colors,
            keywords = Array.Empty<string>(),
            legalities = new Dictionary<string, string> { ["commander"] = "legal" },
            image_uris = new Dictionary<string, string> { ["normal"] = $"https://img.test/{id:D}.jpg" },
            prices = new Dictionary<string, string?> { ["usd"] = "0.10", ["usd_foil"] = null },
            edhrec_rank = 42,
            penny_rank = 12,
            fixture_extension = new { retained = true },
        });
    }

    /// <summary>
    /// Builds the white fixture card.
    /// </summary>
    internal static string WhiteCard(string name = "Venerable Knight")
    {
        return Card(WhiteCardId, WhiteOracleId, WhiteIllustrationId, name, "eld", "35", "{W}", ["W"]);
    }

    /// <summary>
    /// Builds the red fixture card.
    /// </summary>
    internal static string RedCard()
    {
        return Card(RedCardId, RedOracleId, RedIllustrationId, "Monastery Swiftspear", "bro", "144", "{R}", ["R"]);
    }

    /// <summary>
    /// Builds the complete four-dataset JSONL fixture for one generation.
    /// </summary>
    internal static IReadOnlyDictionary<string, byte[]> CompressedCorpus(string whiteName = "Venerable Knight")
    {
        string ruling = JsonSerializer.Serialize(new
        {
            @object = "ruling",
            oracle_id = WhiteOracleId,
            source = "wotc",
            published_at = "2026-07-04",
            comment = "Fixture ruling.",
            fixture_extension = 1,
        });
        string emptyCommentRuling = JsonSerializer.Serialize(new
        {
            @object = "ruling",
            oracle_id = WhiteOracleId,
            source = "wotc",
            published_at = "2026-07-05",
            comment = string.Empty,
            fixture_extension = 2,
        });
        string parentTag = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = AggroTagId,
            label = "Aggro",
            slug = "aggro",
            type = "oracle",
            description = "Aggressive cards.",
            parent_ids = Array.Empty<Guid>(),
            child_ids = new[] { WeenieTagId },
            aliases = BeatdownAliases,
            taggings = Array.Empty<object>(),
        });
        string childTag = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = WeenieTagId,
            label = "White Weenie",
            slug = "white-weenie",
            type = "oracle",
            description = "Small white attackers.",
            parent_ids = new[] { AggroTagId },
            child_ids = Array.Empty<Guid>(),
            aliases = WeenieAliases,
            taggings = new[]
            {
                new { oracle_id = WhiteOracleId, weight = "strong", annotation = "fixture" },
            },
        });
        string artTag = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = ArtTagId,
            label = "Running",
            slug = "running",
            type = "illustration",
            description = (string?)null,
            parent_ids = Array.Empty<Guid>(),
            child_ids = Array.Empty<Guid>(),
            aliases = Array.Empty<string>(),
            taggings = new[] { new { illustration_id = RedIllustrationId, weight = "very_strong" } },
        });
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["all_cards"] = GzipLines([WhiteCard(whiteName), RedCard()]),
            ["rulings"] = GzipLines([ruling, emptyCommentRuling]),
            ["oracle_tags"] = GzipLines([parentTag, childTag]),
            ["art_tags"] = GzipLines([artTag]),
        };
    }

    /// <summary>
    /// Builds official bulk metadata for the fixed dataset profile.
    /// </summary>
    internal static string BulkMetadata(int revision = 1)
    {
        string[] types = ["all_cards", "rulings", "oracle_tags", "art_tags"];
        object[] datasets = types.Select((type, index) => new
        {
            @object = "bulk_data",
            id = Guid.Parse($"0000000{revision}-0000-4000-8000-{index + 1:D12}"),
            type,
            updated_at = $"2026-07-{revision:D2}T09:00:00+00:00",
            uri = $"https://fixture.test/bulk-data/{type}",
            name = type.Replace('_', ' '),
            description = $"Fixture {type} dataset.",
            size = 4096,
            download_uri = $"https://fixture.test/download/{type}.json",
            jsonl_download_uri = $"https://fixture.test/download/{type}.jsonl.gz",
            content_type = "application/json",
            content_encoding = "gzip",
            fixture_extension = revision,
        }).ToArray();
        return JsonSerializer.Serialize(new { @object = "list", has_more = false, data = datasets });
    }

    /// <summary>
    /// Builds the ordinary fake provider with optional generation revision and card-name changes.
    /// </summary>
    internal static RecordingHandler Provider(
        int revision = 1,
        string whiteName = "Venerable Knight",
        Func<HttpRequestMessage, HttpResponseMessage?>? intercept = null)
    {
        IReadOnlyDictionary<string, byte[]> corpus = CompressedCorpus(whiteName);
        return new RecordingHandler(request =>
        {
            HttpResponseMessage? intercepted = intercept?.Invoke(request);
            if (intercepted is not null)
            {
                return intercepted;
            }

            string path = request.RequestUri!.AbsolutePath;
            string query = request.RequestUri.Query;
            if (path == "/bulk-data")
            {
                return Json(BulkMetadata(revision));
            }

            if (path.StartsWith("/download/", StringComparison.Ordinal))
            {
                string type = Path.GetFileName(path).Replace(".jsonl.gz", string.Empty, StringComparison.Ordinal);
                return Bytes(corpus[type]);
            }

            if (path == "/cards/search")
            {
                return Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    total_cards = 2,
                    has_more = false,
                    data = new[]
                    {
                        JsonSerializer.Deserialize<JsonElement>(WhiteCard(whiteName)),
                        JsonSerializer.Deserialize<JsonElement>(RedCard()),
                    },
                    warnings = ProviderWarnings,
                }));
            }

            if (path == "/cards/named")
            {
                return Json(query.Contains("Monastery", StringComparison.OrdinalIgnoreCase) ? RedCard() : WhiteCard(whiteName));
            }

            if (path == $"/cards/{WhiteCardId:D}" || path == "/cards/eld/35")
            {
                return Json(WhiteCard(whiteName));
            }

            if (path == "/cards/collection")
            {
                return Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    data = new[] { JsonSerializer.Deserialize<JsonElement>(RedCard()) },
                    not_found = Array.Empty<object>(),
                }));
            }

            if (path == $"/cards/{WhiteCardId:D}/rulings")
            {
                return Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    has_more = false,
                    data = new[]
                    {
                        new { @object = "ruling", oracle_id = WhiteOracleId, source = "wotc", published_at = "2026-07-04", comment = "Provider ruling." },
                    },
                }));
            }

            if (path == "/sets")
            {
                return Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    has_more = false,
                    data = new[] { SetObject() },
                }));
            }

            if (path.StartsWith("/sets/", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(SetObject()));
            }

            if (path.StartsWith("/catalog/", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(new { @object = "catalog", uri = request.RequestUri, total_values = 2, data = CatalogValues }));
            }

            if (path == "/cards/autocomplete")
            {
                return Json(JsonSerializer.Serialize(new { @object = "catalog", total_values = 2, data = AutocompleteValues }));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
    }

    /// <summary>
    /// Builds one representative set object.
    /// </summary>
    internal static object SetObject()
    {
        return new
        {
            @object = "set",
            id = Guid.Parse("99999999-9999-4999-8999-999999999999"),
            code = "tst",
            name = "Test Set",
            set_type = "expansion",
            released_at = "2026-07-04",
            card_count = 2,
            digital = false,
            fixture_extension = true,
        };
    }

    /// <summary>
    /// Creates a JSON response.
    /// </summary>
    internal static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Creates a binary response.
    /// </summary>
    internal static HttpResponseMessage Bytes(byte[] content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
    }

    /// <summary>
    /// Compresses complete UTF-8 JSON lines with terminal newlines.
    /// </summary>
    internal static byte[] GzipLines(IReadOnlyList<string> lines)
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
}

/// <summary>
/// Captures fake provider requests while delegating deterministic responses.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    /// <summary>
    /// Produces one response for each captured request.
    /// </summary>
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

    /// <summary>
    /// Stores immutable observations rather than request objects that callers later dispose.
    /// </summary>
    private readonly List<RecordedRequest> requests = [];

    /// <summary>
    /// Creates a handler around one deterministic response delegate.
    /// </summary>
    internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        this.respond = (request, _) => Task.FromResult(respond(request));
    }

    /// <summary>
    /// Creates a handler around one asynchronous response delegate for coordination tests.
    /// </summary>
    internal RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        this.respond = respond;
    }

    /// <summary>
    /// Gets captured requests in start order.
    /// </summary>
    internal IReadOnlyList<RecordedRequest> Requests => requests;

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.UserAgent.ToString(),
            request.Headers.Accept.ToString(),
            body,
            DateTimeOffset.UtcNow));
        return await respond(request, cancellationToken);
    }
}

/// <summary>
/// Preserves the safe portions of one captured fake request.
/// </summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string UserAgent,
    string Accept,
    string? Body,
    DateTimeOffset StartedAtUtc);

/// <summary>
/// Owns one isolated temporary application-data root.
/// </summary>
internal sealed class TemporaryScryfallDirectory : IDisposable
{
    /// <summary>
    /// Creates a unique root without creating the database.
    /// </summary>
    internal TemporaryScryfallDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mtg-mcp-scryfall-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Gets the private root used only by tests.
    /// </summary>
    internal string Path { get; }

    /// <summary>
    /// Removes the isolated root after handles are released.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>
/// Supplies a controllable UTC clock while retaining real timers for short pacing delays.
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    /// <summary>
    /// Stores the current fake instant.
    /// </summary>
    private DateTimeOffset utcNow;

    /// <summary>
    /// Creates a clock at one exact UTC instant.
    /// </summary>
    internal MutableTimeProvider(DateTimeOffset utcNow)
    {
        this.utcNow = utcNow.ToUniversalTime();
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }

    /// <summary>
    /// Advances the fake instant without sleeping.
    /// </summary>
    internal void Advance(TimeSpan duration)
    {
        utcNow += duration;
    }
}
