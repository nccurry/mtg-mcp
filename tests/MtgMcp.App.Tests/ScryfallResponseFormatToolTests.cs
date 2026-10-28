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
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the card tools publish the response format options, reject bad ones, and stay within the size limit
/// when written with the server's own serializer.
/// </summary>
public sealed class ScryfallResponseFormatToolTests
{
    /// <summary>
    /// Names the four tools that take a response format in place of the old raw option.
    /// </summary>
    private static readonly string[] FormatTools =
        ["scryfall_search", "scryfall_card_get", "scryfall_card_collection", "scryfall_cards_by_tag"];

    /// <summary>
    /// Verifies each card tool publishes responseFormat as an enum, fields with enum items, and no includeRaw.
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
            Assert.Equal(ScryfallConciseFields.All, ItemEnum(properties.GetProperty("fields")));
            string description = properties.GetProperty("fields").GetProperty("description").GetString()!;
            Assert.All(
                ScryfallConciseFields.All,
                field => Assert.Contains(field, description, StringComparison.Ordinal));
        }

        JsonElement prints = tools["scryfall_card_prints"].ProtocolTool.InputSchema.GetProperty("properties");
        Assert.True(prints.TryGetProperty("includeRaw", out _));
    }

    /// <summary>
    /// Verifies <c>deck_get</c> publishes enum items for include and fields.
    /// </summary>
    [Fact]
    public void DeckGet_PublishesEnumItemsForIncludeAndFields()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        McpServerTool deckGet = registered.Tools.Single(tool => tool.ProtocolTool.Name == "deck_get");

        JsonElement properties = deckGet.ProtocolTool.InputSchema.GetProperty("properties");

        Assert.Equal(["cards", "tags"], ItemEnum(properties.GetProperty("include")));
        Assert.Equal(ScryfallConciseFields.All, ItemEnum(properties.GetProperty("fields")));
    }

    /// <summary>
    /// Verifies the standard format is the default and the tool descriptions point to concise for broad queries.
    /// </summary>
    [Fact]
    public void CardTools_DefaultToStandardAndWarnAboutSize()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        Dictionary<string, McpServerTool> tools = registered.Tools
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        foreach (string name in FormatTools)
        {
            JsonElement format = tools[name].ProtocolTool.InputSchema.GetProperty("properties")
                .GetProperty("responseFormat");
            Assert.Equal("standard", format.GetProperty("default").GetString());
            Assert.Contains("concise", tools[name].ProtocolTool.Description, StringComparison.Ordinal);
        }

        foreach (string name in new[] { "scryfall_search", "scryfall_card_collection", "scryfall_cards_by_tag" })
        {
            Assert.Contains("40,000 bytes", tools[name].ProtocolTool.Description, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pins that the card tools publish the same open output schema as every other tool that returns an operation
    /// result: the result type has its own converter, so its schema is not generated. The card view union adds no
    /// new gap.
    /// </summary>
    [Fact]
    public void CardTools_OutputSchema_IsTheSameOpenSchemaAsOtherTools()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        Dictionary<string, McpServerTool> tools = registered.Tools
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);

        string unchanged = tools["deck_list"].ProtocolTool.OutputSchema!.Value.GetRawText();

        Assert.Equal("true", unchanged);
        foreach (string name in FormatTools)
        {
            Assert.Equal(unchanged, tools[name].ProtocolTool.OutputSchema!.Value.GetRawText());
        }
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
    /// Verifies a concise page of 100 recorded-shape cards whose text is full of non-ASCII and escaped characters
    /// stays within 40,000 bytes under the server's serializer options, and the pages continue without gaps.
    /// </summary>
    [Fact]
    public async Task SearchTool_ConcisePagesOfEscapedText_StayWithinTheSizeLimitUnderServerOptions()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = CreateService(temporary, 100);
        ScryfallReadTools tools = new(service);
        List<string> names = [];
        int pages = 0;
        string? cursor = null;

        do
        {
            OperationResult<ScryfallSearchResponse> result = await tools.SearchAsync(
                "t:elf",
                responseFormat: "concise",
                cursor: cursor,
                pageSize: 100,
                cancellationToken: TestContext.Current.CancellationToken);

            ScryfallSearchResponse page = Assert.IsType<OperationSuccess<ScryfallSearchResponse>>(result.Value).Data;
            int bytes = JsonSerializer.SerializeToUtf8Bytes(result, ToolCallPolicy.SerializerOptions).Length;
            Assert.True(bytes <= ResultSizeLimit.MaximumBytes, $"{bytes} bytes");
            Assert.Equal(page.Page.NextCursor is not null, page.Truncated == true);
            names.AddRange(page.Page.Items.Select(NameOf));
            cursor = page.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 100).Select(CardName), names);
    }

    /// <summary>
    /// Reads the allowed item values published for a list parameter.
    /// </summary>
    private static string[] ItemEnum(JsonElement parameter)
    {
        JsonElement items = parameter.TryGetProperty("items", out JsonElement direct)
            ? direct
            : parameter.GetProperty("anyOf").EnumerateArray()
                .First(alternative => alternative.TryGetProperty("items", out _)).GetProperty("items");
        return [.. items.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>
    /// Reads the name of a card view.
    /// </summary>
    private static string NameOf(ScryfallCardView view)
    {
        return JsonSerializer.SerializeToElement(view, ToolCallPolicy.SerializerOptions)
            .GetProperty("name").GetString()!;
    }

    /// <summary>
    /// Builds the name of the generated card at one position, with characters that need escaping.
    /// </summary>
    private static string CardName(int index)
    {
        return $"Æther Lim-Dûl's \"Vault\" {index:D3}";
    }

    /// <summary>
    /// Creates a service, written with the server's result options, whose provider returns the given number of cards.
    /// </summary>
    private static ScryfallService CreateService(TemporaryDirectory temporary, int cardCount)
    {
        return new ScryfallService(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions,
            new Uri("https://scryfall.test/"),
            handler: new SearchHandler(cardCount));
    }

    /// <summary>
    /// Answers card searches with copies of recorded cards whose names and text carry escaped characters.
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
        /// Builds one card from a recorded card, with its own IDs and escaped text.
        /// </summary>
        private static JsonObject Card(int index)
        {
            string suffix = (index + 1).ToString("D12", CultureInfo.InvariantCulture);
            JsonObject card = RecordedCards.Object(RecordedCards.LightningBolt);
            card["id"] = $"00000000-0000-4000-8000-{suffix}";
            card["oracle_id"] = $"00000000-0000-4000-9000-{suffix}";
            card["name"] = CardName(index);
            card["oracle_text"] = "• Choose one —\n• Deal 3 damage − \"quoted\" Æther.\n" +
                "• Lim-Dûl's − tab\there.";
            return card;
        }
    }
}
