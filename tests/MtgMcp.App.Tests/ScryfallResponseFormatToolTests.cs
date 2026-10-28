using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the card tools publish the response format options, reject bad ones, and stay within the size limit.
/// </summary>
public sealed class ScryfallResponseFormatToolTests
{
    /// <summary>
    /// Names the four tools that take a response format in place of the old raw option.
    /// </summary>
    private static readonly string[] FormatTools =
        ["scryfall_search", "scryfall_card_get", "scryfall_card_collection", "scryfall_cards_by_tag"];

    /// <summary>
    /// Verifies each card tool publishes responseFormat as an enum and fields, and no longer offers includeRaw.
    /// </summary>
    [Fact]
    public void CardTools_PublishResponseFormatAndFieldsInsteadOfIncludeRaw()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        Dictionary<string, McpServerTool> tools = registered.Tools
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        foreach (string name in FormatTools)
        {
            JsonElement properties = tools[name].ProtocolTool.InputSchema.GetProperty("properties");
            Assert.False(properties.TryGetProperty("includeRaw", out _), name);
            Assert.Equal(
                ["concise", "standard", "full"],
                properties.GetProperty("responseFormat").GetProperty("enum").EnumerateArray()
                    .Select(value => value.GetString()));
            string description = properties.GetProperty("fields").GetProperty("description").GetString()!;
            Assert.All(
                ScryfallConciseFields.All,
                field => Assert.Contains(field, description, StringComparison.Ordinal));
        }

        Assert.True(tools["scryfall_card_prints"].ProtocolTool.InputSchema.GetProperty("properties")
            .TryGetProperty("includeRaw", out _));
    }

    /// <summary>
    /// Verifies the standard format is the default and the tool descriptions point to concise for broad queries.
    /// </summary>
    [Fact]
    public void SearchTool_DefaultsToStandardAndRecommendsConcise()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        McpServerTool search = registered.Tools.Single(tool => tool.ProtocolTool.Name == "scryfall_search");

        JsonElement format = search.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("responseFormat");

        Assert.Equal("standard", format.GetProperty("default").GetString());
        Assert.Contains("responseFormat concise", search.ProtocolTool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a bad format or a field list with a non-concise format returns invalid input from the tool.
    /// </summary>
    [Fact]
    public async Task SearchTool_BadFormatRequest_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = CreateService(temporary, 1);
        ScryfallReadTools tools = new(service);
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationInvalidInput badFormat = Assert.IsType<OperationInvalidInput>((await tools.SearchAsync(
            "t:elf",
            responseFormat: "tiny",
            cancellationToken: token)).Value);
        OperationInvalidInput badFields = Assert.IsType<OperationInvalidInput>((await tools.SearchAsync(
            "t:elf",
            responseFormat: "full",
            fields: ["name"],
            cancellationToken: token)).Value);

        Assert.Equal("invalid-response-format", badFormat.ReasonCode);
        Assert.Equal("fields-require-concise", badFields.ReasonCode);
    }

    /// <summary>
    /// Verifies a concise page of 100 cards, written with the server's serializer, stays within 40,000 bytes.
    /// </summary>
    [Fact]
    public async Task SearchTool_ConcisePageOfHundredCards_StaysWithinTheSizeLimit()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = CreateService(temporary, 100);
        ScryfallReadTools tools = new(service);

        OperationResult<ScryfallSearchResponse> result = await tools.SearchAsync(
            "t:elf",
            responseFormat: "concise",
            pageSize: 100,
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallSearchResponse page = Assert.IsType<OperationSuccess<ScryfallSearchResponse>>(result.Value).Data;
        int bytes = JsonSerializer.SerializeToUtf8Bytes(result, ToolCallPolicy.SerializerOptions).Length;
        Assert.True(bytes <= ResultSizeLimit.MaximumBytes, $"{bytes} bytes");
        Assert.True(page.Truncated);
        Assert.NotNull(page.Page.NextCursor);
        Assert.True(page.Page.Items.Count is > 20 and < 100, page.Page.Items.Count.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("concise", page.ResponseFormat);
    }

    /// <summary>
    /// Creates a service whose provider returns the given number of cards for every search.
    /// </summary>
    private static ScryfallService CreateService(TemporaryDirectory temporary, int cardCount)
    {
        return new ScryfallService(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: true,
            "0.9.0-preview.1",
            new Uri("https://scryfall.test/"),
            handler: new SearchHandler(cardCount));
    }

    /// <summary>
    /// Answers card searches with generated cards shaped like real Scryfall cards.
    /// </summary>
    private sealed class SearchHandler(int cardCount) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/cards/search")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}"),
                });
            }

            JsonArray data = [];
            for (int index = 0; index < cardCount; index++)
            {
                data.Add(Card(index));
            }

            JsonObject list = new()
            {
                ["object"] = "list",
                ["total_cards"] = cardCount,
                ["has_more"] = false,
                ["data"] = data,
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(list.ToJsonString(), Encoding.UTF8, "application/json"),
            });
        }

        /// <summary>
        /// Builds one generated card.
        /// </summary>
        private static JsonObject Card(int index)
        {
            string suffix = (index + 1).ToString("D12", CultureInfo.InvariantCulture);
            return new JsonObject
            {
                ["object"] = "card",
                ["id"] = $"00000000-0000-4000-8000-{suffix}",
                ["oracle_id"] = $"00000000-0000-4000-9000-{suffix}",
                ["name"] = $"Generated Card {index:D3}",
                ["set"] = "tst",
                ["collector_number"] = (index + 1).ToString(CultureInfo.InvariantCulture),
                ["lang"] = "en",
                ["released_at"] = "2026-07-04",
                ["mana_cost"] = "{2}{G}",
                ["cmc"] = 3.0,
                ["type_line"] = "Creature - Elf Druid",
                ["oracle_text"] = "{T}: Add one mana of any color. Whenever this creature attacks, you may search " +
                    "your library for a basic land card, put it onto the battlefield tapped, then shuffle.",
                ["power"] = "2",
                ["toughness"] = "2",
                ["colors"] = new JsonArray("G"),
                ["color_identity"] = new JsonArray("G"),
                ["keywords"] = new JsonArray("Flash", "Reach"),
                ["legalities"] = new JsonObject
                {
                    ["standard"] = "not_legal",
                    ["modern"] = "legal",
                    ["legacy"] = "legal",
                    ["commander"] = "legal",
                },
                ["games"] = new JsonArray("paper", "mtgo", "arena"),
                ["prices"] = new JsonObject { ["usd"] = "1.25", ["usd_foil"] = "2.50", ["eur"] = "1.10" },
                ["edhrec_rank"] = 1000 + index,
            };
        }
    }
}
