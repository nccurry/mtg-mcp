using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the concise, standard, and full card response formats and the facts they carry, using real card
/// objects recorded from Scryfall.
/// </summary>
public sealed class ScryfallCardFormatTests
{
    /// <summary>
    /// Writes results the way the server does, so measured sizes match what a client receives.
    /// </summary>
    private static readonly JsonSerializerOptions ResultOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Lists the concise card keys in published order for the default field set.
    /// </summary>
    private static readonly string[] DefaultConciseKeys =
    [
        "scryfallId", "oracleId", "name", "manaCost", "manaValue", "typeLine", "colors", "colorIdentity",
        "producedMana", "keywords", "oracleText", "power", "toughness", "loyalty", "legalities", "prices",
        "cheapestPriceUsd", "rarity", "set", "tags", "gameChanger", "edhrecRank",
    ];

    /// <summary>
    /// Lists the card properties that standard added after the baseline output was recorded.
    /// </summary>
    private static readonly string[] AddedCardProperties =
        ["rarity", "power", "toughness", "loyalty", "layout", "games", "reserved", "gameChanger"];

    /// <summary>
    /// Verifies the default concise set holds exactly the published fields, in the published order.
    /// </summary>
    [Fact]
    public void Project_DefaultSelection_WritesTheLiteralFieldList()
    {
        ScryfallCard card = Map(RecordedCards.LightningBolt);
        ScryfallInstalledCardFacts facts = new(["burn"], "0.25");

        ScryfallConciseCard concise = ScryfallConciseProjection.Project(card, facts, Selected(null));

        Assert.Equal(DefaultConciseKeys, concise.Fields.Select(field => field.Key));
        JsonElement json = JsonSerializer.SerializeToElement(concise, ResultOptions);
        Assert.Equal("Lightning Bolt", json.GetProperty("name").GetString());
        Assert.Equal("{R}", json.GetProperty("manaCost").GetString());
        Assert.Equal(["burn"], Strings(json.GetProperty("tags")));
        Assert.Equal("0.25", json.GetProperty("cheapestPriceUsd").GetString());
        Assert.Equal(15, json.GetProperty("legalities").GetArrayLength());
        Assert.Equal(card.Prices["usd"], json.GetProperty("prices").GetProperty("usd").GetString());
        Assert.True(json.GetProperty("edhrecRank").GetInt64() > 0);
    }

    /// <summary>
    /// Verifies a field list keeps the always-included fields and writes the rest in the published order.
    /// </summary>
    [Fact]
    public void Project_FieldList_KeepsAlwaysFieldsAndPublishedOrder()
    {
        ScryfallConciseCard concise = ScryfallConciseProjection.Project(
            Map(RecordedCards.LightningBolt),
            null,
            Selected(["typeLine", "manaCost"]));

        Assert.Equal(
            ["scryfallId", "oracleId", "name", "manaCost", "typeLine"],
            concise.Fields.Select(field => field.Key));
    }

    /// <summary>
    /// Verifies the extra fields are available by name and a known-null price stays an explicit null.
    /// </summary>
    [Fact]
    public void Project_ExtraFields_AreAvailableByName()
    {
        ScryfallCard card = Map(RecordedCards.LightningBolt);
        string[] fields = ["prices.usd", "prices.usdEtched", "layout", "games", "reserved", "legalitiesAll"];

        ScryfallConciseCard concise = ScryfallConciseProjection.Project(card, null, Selected(fields));

        JsonElement json = JsonSerializer.SerializeToElement(concise, ResultOptions);
        Assert.Equal(
            ["scryfallId", "oracleId", "name", "prices", "legalitiesAll", "games", "reserved", "layout"],
            json.EnumerateObject().Select(property => property.Name));
        JsonElement prices = json.GetProperty("prices");
        Assert.Equal(card.Prices["usd"], prices.GetProperty("usd").GetString());
        Assert.Equal(JsonValueKind.Null, prices.GetProperty("usdEtched").ValueKind);
        Assert.Equal("normal", json.GetProperty("layout").GetString());
        Assert.False(json.GetProperty("reserved").GetBoolean());
        Assert.Contains("paper", Strings(json.GetProperty("games")));
        Assert.Equal("legal", json.GetProperty("legalitiesAll").GetProperty("commander").GetString());
    }

    /// <summary>
    /// Verifies a transform card, whose top-level cost is empty, reads cost, text, colors, and power from its faces.
    /// </summary>
    [Fact]
    public void Project_TransformCard_FallsBackToFaces()
    {
        ScryfallCard card = Map(RecordedCards.Transform);

        JsonElement json = JsonSerializer.SerializeToElement(
            ScryfallConciseProjection.Project(card, null, Selected(null)),
            ResultOptions);

        Assert.True(string.IsNullOrEmpty(card.ManaCost));
        Assert.Equal(card.Faces[0].ManaCost, json.GetProperty("manaCost").GetString());
        string expectedText = string.Join("\n//\n", card.Faces.Select(face => face.OracleText));
        Assert.Equal(expectedText, json.GetProperty("oracleText").GetString());
        Assert.Equal(card.Faces[0].Colors, Strings(json.GetProperty("colors")));
        Assert.Equal(card.Faces[0].Power, json.GetProperty("power").GetString());
        Assert.Equal(card.Faces[0].Toughness, json.GetProperty("toughness").GetString());
    }

    /// <summary>
    /// Verifies an adventure card keeps the cost and colors it has at the top level.
    /// </summary>
    [Fact]
    public void Project_AdventureCard_KeepsTopLevelCostAndColors()
    {
        ScryfallCard card = Map(RecordedCards.Adventure);

        JsonElement json = JsonSerializer.SerializeToElement(
            ScryfallConciseProjection.Project(card, null, Selected(null)),
            ResultOptions);

        Assert.Equal("{2}{R} // {1}{R}", json.GetProperty("manaCost").GetString());
        Assert.Equal(["R"], Strings(json.GetProperty("colors")));
        string faceText = string.Join("\n//\n", card.Faces.Select(face => face.OracleText));
        Assert.Equal(faceText, json.GetProperty("oracleText").GetString());
    }

    /// <summary>
    /// Verifies a planeswalker carries loyalty and no power or toughness.
    /// </summary>
    [Fact]
    public void Project_Planeswalker_CarriesLoyalty()
    {
        JsonElement json = JsonSerializer.SerializeToElement(
            ScryfallConciseProjection.Project(Map(RecordedCards.Planeswalker), null, Selected(null)),
            ResultOptions);

        Assert.Equal("3", json.GetProperty("loyalty").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("power").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("toughness").ValueKind);
    }

    /// <summary>
    /// Verifies a card whose prices are all null writes a null USD price rather than dropping the field.
    /// </summary>
    [Fact]
    public void Project_CardWithNullPrices_WritesNullUsd()
    {
        ScryfallConciseCard concise = ScryfallConciseProjection.Project(
            Map(RecordedCards.NoPrices),
            null,
            Selected(["prices.usd"]));

        JsonElement json = JsonSerializer.SerializeToElement(concise, ResultOptions);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("prices").GetProperty("usd").ValueKind);
    }

    /// <summary>
    /// Verifies tags and the cheapest price are explicit nulls when installed data is not available.
    /// </summary>
    [Fact]
    public void Project_WithoutInstalledFacts_WritesNullTagsAndPrice()
    {
        ScryfallConciseCard concise = ScryfallConciseProjection.Project(
            Map(RecordedCards.LightningBolt),
            null,
            Selected(null));

        string json = JsonSerializer.Serialize(concise, ResultOptions);
        Assert.Contains("\"tags\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"cheapestPriceUsd\":null", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies bad formats and field lists return invalid input that names the allowed values.
    /// </summary>
    [Theory]
    [InlineData("tiny", null, "invalid-response-format")]
    [InlineData("standard", new[] { "name" }, "fields-require-concise")]
    [InlineData("full", new[] { "name" }, "fields-require-concise")]
    [InlineData("concise", new[] { "name", "rulesText" }, "invalid-fields")]
    public void Parse_InvalidRequest_ReturnsInvalidInput(string format, string[]? fields, string reasonCode)
    {
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(
            ScryfallCardFormat.Parse(format, fields).Value);

        Assert.Equal(reasonCode, invalid.ReasonCode);
        if (reasonCode == "invalid-fields")
        {
            Assert.Equal("fields[1]", invalid.Path);
            Assert.Contains("rulesText", invalid.Message, StringComparison.Ordinal);
            Assert.All(
                ScryfallConciseFields.All,
                name => Assert.Contains(name, invalid.Message, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Verifies the published field lists are the literal default and extra sets, from the one field table.
    /// </summary>
    [Fact]
    public void ConciseFields_DefaultAndExtraSets_AreThePublishedLists()
    {
        Assert.Equal(
            [
                "scryfallId", "oracleId", "name", "manaCost", "manaValue", "typeLine", "colors", "colorIdentity",
                "producedMana", "keywords", "oracleText", "power", "toughness", "loyalty", "legalities",
                "prices.usd", "cheapestPriceUsd", "rarity", "set", "tags", "gameChanger", "edhrecRank",
            ],
            ScryfallConciseFields.Default);
        Assert.Equal(
            [
                "prices.usdFoil", "prices.usdEtched", "prices.eur", "prices.eurFoil", "prices.tix",
                "legalitiesAll", "games", "reserved", "layout",
            ],
            ScryfallConciseFields.Extra);
        Assert.Equal([.. ScryfallConciseFields.Default, .. ScryfallConciseFields.Extra], ScryfallConciseFields.All);
    }

    /// <summary>
    /// Verifies the mapper reads the new fields when present and gives neutral values when absent.
    /// </summary>
    [Fact]
    public void Mapper_NewFields_ArePresentAndAbsent()
    {
        ScryfallCard walker = Map(RecordedCards.Planeswalker);
        JsonObject withoutOptional = RecordedCards.Object(RecordedCards.LightningBolt);
        foreach (string name in new[] { "game_changer", "games", "layout", "rarity", "reserved" })
        {
            withoutOptional.Remove(name);
        }

        ScryfallCard absent = Map(withoutOptional);

        Assert.Equal("mythic", walker.Rarity);
        Assert.Equal("3", walker.Loyalty);
        Assert.Equal("normal", walker.Layout);
        Assert.Contains("paper", walker.Games);
        Assert.False(walker.Reserved);
        Assert.False(walker.GameChanger);
        Assert.Empty(absent.Games);
        Assert.False(absent.Reserved);
        Assert.Null(absent.Layout);
        Assert.Null(absent.GameChanger);
        Assert.Null(absent.Rarity);
        Assert.Null(Map(RecordedCards.Reversible).OracleId);
        Guid? faceId = Map(RecordedCards.Reversible).Faces[0].OracleId;
        Assert.Equal(FaceOracleId(RecordedCards.Reversible), faceId.ToString());
    }

    /// <summary>
    /// Verifies the mapped standard cards equal the cards recorded from the code before this change, apart from the
    /// published added properties.
    /// </summary>
    [Fact]
    public void StandardCards_MatchTheBaselineGoldenFile()
    {
        JsonArray actual = [];
        foreach (string name in RecordedCards.All)
        {
            using JsonDocument document = JsonDocument.Parse(RecordedCards.Raw(name));
            ScryfallCard card = ScryfallMapper.Card(
                document.RootElement,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "golden-snapshot",
                includeRaw: false);
            actual.Add(JsonSerializer.SerializeToNode(card, ResultOptions));
        }

        RemoveAddedProperties(actual);
        JsonNode? goldenCards = Golden("standard-cards");
        Assert.True(JsonNode.DeepEquals(goldenCards, actual), Difference(goldenCards, actual, "$"));
    }

    /// <summary>
    /// Verifies the standard search page and the standard single card match the baseline, apart from evidence stamps
    /// and the published added properties.
    /// </summary>
    [Fact]
    public async Task StandardSearchAndCard_MatchTheBaselineGoldenFiles()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, RecordedProvider());
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResponse search = RequireSuccess(await service.SearchAsync("x", cancellationToken: token));
        ScryfallCardResponse card = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Delver of Secrets"),
            cancellationToken: token));

        JsonNode? page = JsonSerializer.SerializeToNode(search.Page.Items, ResultOptions);
        JsonNode? single = JsonSerializer.SerializeToNode(card.Card, ResultOptions);
        foreach (JsonNode? node in new[] { page, single })
        {
            RemoveEvidence(node);
            RemoveAddedProperties(node);
        }

        JsonNode? goldenPage = Golden("standard-search-page");
        JsonNode? goldenSingle = Golden("standard-card-get");
        Assert.True(JsonNode.DeepEquals(goldenPage, page), Difference(goldenPage, page, "$"));
        Assert.True(JsonNode.DeepEquals(goldenSingle, single), Difference(goldenSingle, single, "$"));
    }

    /// <summary>
    /// Verifies a concise card is at most 40 percent of the size of a standard card, comparing the average size of the
    /// cards each page kept, because a page is cut to the size limit.
    /// </summary>
    [Fact]
    public async Task Search_ConcisePage_IsAtMostFortyPercentOfStandard()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, VariantProvider(25));
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResponse standard = RequireSuccess(await service.SearchAsync(
            "t:creature",
            responseFormat: "standard",
            cancellationToken: token));
        ScryfallSearchResponse concise = RequireSuccess(await service.SearchAsync(
            "t:creature",
            responseFormat: "concise",
            cancellationToken: token));

        int standardBytes = JsonSerializer.SerializeToUtf8Bytes(standard.Page, ResultOptions).Length;
        int conciseBytes = JsonSerializer.SerializeToUtf8Bytes(concise.Page, ResultOptions).Length;
        Assert.Equal(25, concise.Page.Items.Count);
        Assert.True(
            conciseBytes * 10 * standard.Page.Items.Count <= standardBytes * 4 * concise.Page.Items.Count,
            $"concise {conciseBytes} for {concise.Page.Items.Count}, standard {standardBytes} for {standard.Page.Items.Count}");
    }

    /// <summary>
    /// Verifies the full format carries each recorded raw card object unchanged, cuts a page to the size limit with a
    /// note and a cursor, reaches every card through the cursor, and keeps the 25-row page cap.
    /// </summary>
    [Fact]
    public async Task Search_Full_CarriesTheRecordedRawObjectsAndKeepsThePageCap()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, RecordedProvider());
        CancellationToken token = TestContext.Current.CancellationToken;

        List<ScryfallCard> cards = [];
        ScryfallSearchResponse first = RequireSuccess(await service.SearchAsync(
            "x",
            responseFormat: "full",
            cancellationToken: token));
        ScryfallSearchResponse page = first;
        while (true)
        {
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, ResultOptions).Length < 50_000);
            cards.AddRange(page.Page.Items.Select(item => Assert.IsType<ScryfallCard>(item.Value)));
            if (page.Page.NextCursor is null)
            {
                break;
            }

            Assert.True(page.Truncated);
            Assert.False(string.IsNullOrWhiteSpace(page.Narrow));
            page = RequireSuccess(await service.SearchAsync(
                "x",
                cursor: page.Page.NextCursor,
                responseFormat: "full",
                cancellationToken: token));
        }

        OperationInvalidInput tooLarge = Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "x",
            pageSize: 26,
            responseFormat: "full",
            cancellationToken: token)).Value);

        Assert.True(first.Truncated);
        Assert.True(first.Page.Items.Count < RecordedCards.All.Count);
        Assert.Equal(RecordedCards.All.Count, cards.Count);
        for (int index = 0; index < RecordedCards.All.Count; index++)
        {
            JsonNode? raw = JsonNode.Parse(cards[index].Raw!.Value.GetRawText());
            Assert.True(JsonNode.DeepEquals(RecordedCards.Object(RecordedCards.All[index]), raw));
        }

        Assert.Equal("invalid-page-size", tooLarge.ReasonCode);
    }

    /// <summary>
    /// Verifies every card method names its format and carries raw objects only in the full format.
    /// </summary>
    [Theory]
    [InlineData("concise")]
    [InlineData("standard")]
    [InlineData("full")]
    public async Task EveryCardMethod_NamesItsFormatAndCarriesRawOnlyInFull(string format)
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup bolt = new("exact-name", "Lightning Bolt");

        ScryfallSearchResponse search = RequireSuccess(await service.SearchAsync(
            "x",
            responseFormat: format,
            cancellationToken: token));
        ScryfallCardResponse single = RequireSuccess(await service.GetCardAsync(
            bolt,
            "cache-only",
            format,
            null,
            token));
        ScryfallCollectionResponse collection = RequireSuccess(await service.GetCollectionAsync(
            [bolt],
            "cache-only",
            null,
            25,
            format,
            null,
            token));
        ScryfallCardsByTagResponse byTag = RequireSuccess(await service.GetCardsByTagAsync(
            "burn",
            "oracle",
            false,
            "weak",
            null,
            25,
            format,
            null,
            token));

        string[] formats =
            [search.ResponseFormat, single.ResponseFormat, collection.ResponseFormat, byTag.ResponseFormat];
        Assert.All(formats, value => Assert.Equal(format, value));
        JsonElement[] cards =
        [
            Element(search.Page.Items[0]),
            Element(single.Card),
            Element(collection.Page.Items[0].Card!.Value),
            Element(byTag.Page.Items[0]),
        ];
        Assert.All(cards, card => Assert.Equal(format == "full", card.TryGetProperty("raw", out _)));
        Assert.All(cards, card => Assert.Equal(format != "concise", card.TryGetProperty("imageUris", out _)));
    }

    /// <summary>
    /// Verifies a field list narrows the cards of every card method in the same published order.
    /// </summary>
    [Fact]
    public async Task EveryCardMethod_FieldListNarrowsCardsInPublishedOrder()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        string[] fields = ["tags", "typeLine"];
        ScryfallCardLookup bolt = new("exact-name", "Lightning Bolt");

        ScryfallSearchResponse search = RequireSuccess(await service.SearchAsync(
            "x",
            responseFormat: "concise",
            fields: fields,
            cancellationToken: token));
        ScryfallCardResponse single = RequireSuccess(await service.GetCardAsync(
            bolt,
            "cache-only",
            "concise",
            fields,
            token));
        ScryfallCollectionResponse collection = RequireSuccess(await service.GetCollectionAsync(
            [bolt],
            "cache-only",
            null,
            25,
            "concise",
            fields,
            token));
        ScryfallCardsByTagResponse byTag = RequireSuccess(await service.GetCardsByTagAsync(
            "burn",
            "oracle",
            false,
            "weak",
            null,
            25,
            "concise",
            fields,
            token));

        JsonElement[] cards =
        [
            Element(search.Page.Items[0]),
            Element(single.Card),
            Element(collection.Page.Items[0].Card!.Value),
            Element(byTag.Page.Items[0]),
        ];
        string[] expected = ["scryfallId", "oracleId", "name", "typeLine", "tags"];
        Assert.All(cards, card => Assert.Equal(expected, card.EnumerateObject().Select(property => property.Name)));
    }

    /// <summary>
    /// Verifies a full collection page keeps the 25-row cap.
    /// </summary>
    [Fact]
    public async Task Collection_Full_RejectsMoreThanTwentyFiveRows()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);

        OperationInvalidInput tooLarge = Assert.IsType<OperationInvalidInput>((await service.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "Lightning Bolt")],
            "cache-only",
            null,
            26,
            "full",
            null,
            TestContext.Current.CancellationToken)).Value);

        Assert.Equal("invalid-page-size", tooLarge.ReasonCode);
    }

    /// <summary>
    /// Verifies the provider-backed card methods report the not-installed reason in concise, and the tag tool, which
    /// needs installed data, fails instead of returning empty facts.
    /// </summary>
    [Fact]
    public async Task ConciseCards_WithoutInstalledData_ReportTheReason()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, RecordedProvider());
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup bolt = new("scryfall-id", Id(RecordedCards.LightningBolt));

        ScryfallSearchResponse search = RequireSuccess(await service.SearchAsync(
            "x",
            responseFormat: "concise",
            cancellationToken: token));
        ScryfallCardResponse single = RequireSuccess(await service.GetCardAsync(
            bolt,
            "default",
            "concise",
            null,
            token));
        ScryfallCollectionResponse collection = RequireSuccess(await service.GetCollectionAsync(
            [bolt],
            "default",
            null,
            25,
            "concise",
            null,
            token));
        OperationResult<ScryfallCardsByTagResponse> byTag = await service.GetCardsByTagAsync(
            "burn",
            "oracle",
            false,
            "weak",
            null,
            25,
            "concise",
            null,
            token);

        foreach (IReadOnlyList<string>? limits in new[] { search.Limits, single.Limits, collection.Limits })
        {
            string limit = Assert.Single(limits!, text => text.Contains("not installed", StringComparison.Ordinal));
            Assert.Contains("tags and cheapestPriceUsd are null", limit, StringComparison.Ordinal);
            Assert.Contains("card-data sync tool", limit, StringComparison.Ordinal);
        }

        JsonElement first = Element(search.Page.Items[0]);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("tags").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("cheapestPriceUsd").ValueKind);
        Assert.IsNotType<OperationSuccess<ScryfallCardsByTagResponse>>(byTag.Value);
    }

    /// <summary>
    /// Verifies the limit note is not written when the chosen fields need no installed data.
    /// </summary>
    [Fact]
    public async Task Search_Concise_FieldsWithoutInstalledFacts_HaveNoLimit()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, RecordedProvider());

        ScryfallSearchResponse response = RequireSuccess(await service.SearchAsync(
            "x",
            responseFormat: "concise",
            fields: ["manaCost"],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(response.Limits);
    }

    /// <summary>
    /// Verifies installed data supplies tags and the lowest USD price among English printings: numeric order, skipped
    /// null prices, and skipped non-English printings.
    /// </summary>
    [Fact]
    public async Task ConciseCards_WithInstalledData_CarryTagsAndCheapestEnglishPrice()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallCardResponse walker = await ConciseCardAsync(service, "Jace, the Mind Sculptor", token);
        ScryfallCardResponse bolt = await ConciseCardAsync(service, "Lightning Bolt", token);
        ScryfallCardResponse noPrices = await ConciseCardAsync(service, "Abandoned Sarcophagus", token);

        Assert.Null(walker.Limits);
        JsonElement walkerJson = Element(walker.Card);
        Assert.Equal("9.00", walkerJson.GetProperty("cheapestPriceUsd").GetString());
        Assert.Empty(Strings(walkerJson.GetProperty("tags")));
        Assert.Equal(["burn"], Strings(Element(bolt.Card).GetProperty("tags")));
        Assert.Equal(JsonValueKind.Null, Element(noPrices.Card).GetProperty("cheapestPriceUsd").ValueKind);
    }

    /// <summary>
    /// Verifies a reversible card, which has no top-level Oracle ID, is looked up by its front face and says so.
    /// </summary>
    [Fact]
    public async Task ConciseCards_ReversibleCard_UseTheFrontFaceAndSayWhy()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);

        ScryfallCardResponse response = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("scryfall-id", Id(RecordedCards.Reversible)),
            "cache-only",
            "concise",
            null,
            TestContext.Current.CancellationToken));

        JsonElement json = Element(response.Card);
        Assert.Equal(FaceOracleId(RecordedCards.Reversible), json.GetProperty("oracleId").GetString());
        Assert.Equal(["twincasters"], Strings(json.GetProperty("tags")));
        Assert.Contains("front face", Assert.Single(response.Limits!), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies card facts for a generation that is not kept report not installed instead of empty facts.
    /// </summary>
    [Fact]
    public async Task InstalledFacts_ForAGenerationThatIsNotKept_AreNotInstalled()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        ScryfallCardDataStore store = new(new ScryfallDatabase(temporary.Path));

        ScryfallInstalledCardFactsRead gone = await store.ReadInstalledCardFactsAsync(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            true,
            true,
            TestContext.Current.CancellationToken);

        Assert.False(gone.Installed);
        Assert.Empty(gone.ByOracleId);
    }

    /// <summary>
    /// Verifies a concise search page over the limit is cut, says so, and the next pages continue in exact order.
    /// </summary>
    [Fact]
    public async Task Search_ConcisePageOverTheLimit_IsCutAndContinuesInOrder()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, VariantProvider(100));
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> names = [];
        int pages = 0;
        string? cursor = null;

        do
        {
            ScryfallSearchResponse page = RequireSuccess(await service.SearchAsync(
                "t:creature",
                cursor: cursor,
                pageSize: 100,
                responseFormat: "concise",
                cancellationToken: token));

            Assert.True(Bytes(page) <= ResultSizeLimit.MaximumBytes);
            Assert.Equal(page.Page.NextCursor is not null, page.Truncated == true);
            names.AddRange(page.Page.Items.Select(Name));
            cursor = page.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 100).Select(VariantName), names);
    }

    /// <summary>
    /// Verifies a collection page over the limit is cut the same way and its cursor reads the next rows in order.
    /// </summary>
    [Fact]
    public async Task Collection_ConcisePageOverTheLimit_IsCutAndContinuesInOrder()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, VariantProvider(75));
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup[] lookups =
            [.. Enumerable.Range(0, 75).Select(index => new ScryfallCardLookup("scryfall-id", VariantId(index)))];
        List<string> names = [];
        List<int> indexes = [];
        int pages = 0;
        string? cursor = null;

        do
        {
            ScryfallCollectionResponse page = RequireSuccess(await service.GetCollectionAsync(
                lookups,
                "default",
                cursor,
                100,
                "concise",
                null,
                token));

            Assert.True(Bytes(page) <= ResultSizeLimit.MaximumBytes);
            Assert.Equal(page.Page.NextCursor is not null, page.Truncated == true);
            indexes.AddRange(page.Page.Items.Select(row => row.Index));
            names.AddRange(page.Page.Items.Select(row => Name(row.Card!.Value)));
            cursor = page.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 75), indexes);
        Assert.Equal(Enumerable.Range(0, 75).Select(VariantName), names);
    }

    /// <summary>
    /// Verifies a tag page over the limit is cut with its assignments and continues from the right card, in order.
    /// </summary>
    [Fact]
    public async Task CardsByTag_ConcisePageOverTheLimit_IsCutAndContinuesInOrder()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await TaggedVariantServiceAsync(temporary.Path, 100);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> names = [];
        int pages = 0;
        string? cursor = null;

        do
        {
            ScryfallCardsByTagResponse page = RequireSuccess(await service.GetCardsByTagAsync(
                "bulk",
                "oracle",
                false,
                "weak",
                cursor,
                100,
                "concise",
                null,
                token));

            Assert.True(Bytes(page) <= ResultSizeLimit.MaximumBytes);
            Assert.Equal(page.Page.Items.Count, page.Assignments.Count);
            Assert.Equal(page.Page.NextCursor is not null, page.Truncated == true);
            names.AddRange(page.Page.Items.Select(Name));
            cursor = page.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        // The installed tag order is by Oracle ID, which the variants give in the same order as their names.
        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 100).Select(VariantName), names);
    }

    /// <summary>
    /// Verifies deck-entry lookups report found, unresolved, and not-installed states and carry card and tags.
    /// </summary>
    [Fact]
    public async Task InstalledCardViews_ReportEachStateWithoutGuessing()
    {
        using TemporaryScryfallDirectory emptyDirectory = new();
        using ScryfallService empty = CreateService(emptyDirectory.Path, RecordedProvider());
        using TemporaryScryfallDirectory installedDirectory = new();
        using ScryfallService installed = await InstalledServiceAsync(installedDirectory.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup bolt = new("scryfall-id", Id(RecordedCards.LightningBolt));
        ScryfallCardLookup missing = new("scryfall-id", Guid.Empty.ToString("D"));

        ScryfallInstalledCardViews notInstalled = RequireSuccess(await empty.ReadInstalledCardViewsAsync(
            [bolt],
            null,
            includeCards: true,
            includeTags: true,
            token));
        ScryfallInstalledCardViews found = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [bolt, null, missing],
            null,
            includeCards: true,
            includeTags: true,
            token));
        ScryfallInstalledCardViews tagsOnly = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [bolt],
            null,
            includeCards: false,
            includeTags: true,
            token));

        Assert.False(notInstalled.Installed);
        Assert.Equal("not-installed", Assert.Single(notInstalled.Cards).Status);
        Assert.Equal(["found", "unresolved", "unresolved"], found.Cards.Select(view => view.Status));
        Assert.Equal(["burn"], found.Cards[0].Tags);
        Assert.DoesNotContain(found.Cards[0].Card!.Fields, field => field.Key == "tags");
        Assert.Null(found.Cards[1].Tags);
        Assert.Null(tagsOnly.Cards[0].Card);
        Assert.Equal(["burn"], tagsOnly.Cards[0].Tags);
    }

    /// <summary>
    /// Verifies the card keeps its tags when the entry does not also carry them, and a field list is validated.
    /// </summary>
    [Fact]
    public async Task InstalledCardViews_TagsRuleAndFieldList()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService installed = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup bolt = new("scryfall-id", Id(RecordedCards.LightningBolt));

        ScryfallInstalledCardViews cardsOnly = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [bolt],
            null,
            includeCards: true,
            includeTags: false,
            token));
        ScryfallInstalledCardViews named = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [bolt],
            ["tags", "layout"],
            includeCards: true,
            includeTags: true,
            token));
        OperationResult<ScryfallInstalledCardViews> bad = await installed.ReadInstalledCardViewsAsync(
            [bolt],
            ["nope"],
            includeCards: true,
            includeTags: false,
            token);

        Assert.Equal(DefaultConciseKeys, cardsOnly.Cards[0].Card!.Fields.Select(field => field.Key));
        Assert.Equal(
            ["scryfallId", "oracleId", "name", "tags", "layout"],
            named.Cards[0].Card!.Fields.Select(field => field.Key));
        Assert.Equal(["burn"], named.Cards[0].Tags);
        Assert.Equal("invalid-fields", Assert.IsType<OperationInvalidInput>(bad.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a bad format request fails before any provider read.
    /// </summary>
    [Fact]
    public async Task Search_InvalidFormatRequest_FailsBeforeReadingTheProvider()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = RecordedProvider();
        using ScryfallService service = CreateService(temporary.Path, handler);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "x",
            responseFormat: "standard",
            fields: ["name"],
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal("fields-require-concise", invalid.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Verifies a reversible card never takes the price of a normal printing that shares its front face's Oracle ID.
    /// </summary>
    [Fact]
    public async Task ReversibleCard_DoesNotTakeAPriceFromAnotherCard()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup reversible = new("scryfall-id", Id(RecordedCards.Reversible));

        ScryfallCardResponse single = RequireSuccess(await service.GetCardAsync(
            reversible,
            "cache-only",
            "concise",
            null,
            token));
        ScryfallInstalledCardViews deck = RequireSuccess(await service.ReadInstalledCardViewsAsync(
            [reversible],
            null,
            includeCards: true,
            includeTags: true,
            token));

        JsonElement json = Element(single.Card);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("cheapestPriceUsd").ValueKind);
        Assert.Contains("front face", Assert.Single(single.Limits!), StringComparison.Ordinal);
        ScryfallConciseCard card = deck.Cards[0].Card!;
        Assert.Null(Assert.Single(card.Fields, field => field.Key == "cheapestPriceUsd").Value);
        Assert.Equal(["twincasters"], deck.Cards[0].Tags);
    }

    /// <summary>
    /// Verifies a lookup against a generation that is no longer complete finds nothing instead of missing cards.
    /// </summary>
    [Fact]
    public async Task FindCardsInCompleteGeneration_ForAGenerationThatIsNotKept_ReturnsNull()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        ScryfallCardDataStore store = new(new ScryfallDatabase(temporary.Path));
        ScryfallEvidenceLookup lookup = new(new ScryfallCardLookup("scryfall-id", Id(RecordedCards.LightningBolt)));

        List<StoredCardDataObject?>? gone = await store.FindCardsInCompleteGenerationAsync(
            [lookup],
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        Guid? active = await store.GetActiveGenerationIdAsync(TestContext.Current.CancellationToken);
        List<StoredCardDataObject?>? kept = await store.FindCardsInCompleteGenerationAsync(
            [lookup],
            active!.Value,
            TestContext.Current.CancellationToken);

        Assert.Null(gone);
        Assert.NotNull(Assert.Single(kept!));
    }

    /// <summary>
    /// Verifies the limit notes of a cut page describe only the cards that were kept.
    /// </summary>
    [Fact]
    public async Task ConcisePageCut_LimitNotesDescribeOnlyTheKeptCards()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider(intercept: request =>
        {
            if (request.RequestUri!.AbsolutePath != "/cards/search")
            {
                return null;
            }

            JsonArray data = [.. Enumerable.Range(0, 100).Select(index => (JsonNode?)Variant(index))];
            data.Add(RecordedCards.Object(RecordedCards.Reversible));
            return ListResponse(data, true);
        });
        using ScryfallService service = CreateService(temporary.Path, handler);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallSearchResponse first = RequireSuccess(await service.SearchAsync(
            "x",
            pageSize: 100,
            responseFormat: "concise",
            cancellationToken: token));
        string? cursor = first.Page.NextCursor;
        List<ScryfallSearchResponse> rest = [];

        while (cursor is not null)
        {
            ScryfallSearchResponse page = RequireSuccess(await service.SearchAsync(
                "x",
                cursor: cursor,
                pageSize: 100,
                responseFormat: "concise",
                cancellationToken: token));
            rest.Add(page);
            cursor = page.Page.NextCursor;
        }

        Assert.DoesNotContain(first.Limits!, note => note.Contains("front face", StringComparison.Ordinal));
        Assert.Contains(rest[^1].Limits!, note => note.Contains("front face", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reads one card by exact name in the concise format from installed data only.
    /// </summary>
    private static async Task<ScryfallCardResponse> ConciseCardAsync(
        ScryfallService service,
        string name,
        CancellationToken token)
    {
        return RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", name),
            "cache-only",
            "concise",
            null,
            token));
    }

    /// <summary>
    /// Maps one recorded card the way the service does for provider cards.
    /// </summary>
    private static ScryfallCard Map(string recordedName)
    {
        return Map(RecordedCards.Object(recordedName));
    }

    /// <summary>
    /// Maps one card object the way the service does for provider cards.
    /// </summary>
    private static ScryfallCard Map(JsonObject raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw.ToJsonString());
        return ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "snapshot", includeRaw: false);
    }

    /// <summary>
    /// Parses the field selection the same way a request does.
    /// </summary>
    private static IReadOnlySet<string> Selected(string[]? fields)
    {
        return Assert.IsType<OperationSuccess<ScryfallCardFormat>>(
            ScryfallCardFormat.Parse("concise", fields).Value).Data.SelectedFields!;
    }

    /// <summary>
    /// Measures a result as the server writes it.
    /// </summary>
    private static int Bytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, ResultOptions).Length;
    }

    /// <summary>
    /// Reads a card view as a JSON element.
    /// </summary>
    private static JsonElement Element(ScryfallCardView view)
    {
        return JsonSerializer.SerializeToElement(view, ResultOptions);
    }

    /// <summary>
    /// Reads the name of a card view.
    /// </summary>
    private static string Name(ScryfallCardView view)
    {
        return Element(view).GetProperty("name").GetString()!;
    }

    /// <summary>
    /// Reads a JSON array of strings.
    /// </summary>
    private static string[] Strings(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>
    /// Reads the Scryfall ID of one recorded card.
    /// </summary>
    private static string Id(string recordedName)
    {
        return RecordedCards.Object(recordedName)["id"]!.GetValue<string>();
    }

    /// <summary>
    /// Reads the Oracle ID of one recorded card.
    /// </summary>
    private static string Oracle(string recordedName)
    {
        return RecordedCards.Object(recordedName)["oracle_id"]!.GetValue<string>();
    }

    /// <summary>
    /// Reads the Oracle ID of the front face of one recorded card.
    /// </summary>
    private static string FaceOracleId(string recordedName)
    {
        return RecordedCards.Object(recordedName)["card_faces"]![0]!["oracle_id"]!.GetValue<string>();
    }

    /// <summary>
    /// Names the generated variant card at one position.
    /// </summary>
    private static string VariantName(int index)
    {
        return $"Variant {index:D3}";
    }

    /// <summary>
    /// Builds the Scryfall ID of the generated variant at one position.
    /// </summary>
    private static string VariantId(int index)
    {
        return $"00000000-0000-4000-8000-{index + 1:D12}";
    }

    /// <summary>
    /// Builds a copy of a recorded card with its own IDs and name, so a test can list many distinct cards.
    /// </summary>
    private static JsonObject Variant(int index)
    {
        JsonObject card = RecordedCards.Object(RecordedCards.All[index % 4]);
        card["id"] = VariantId(index);
        card["oracle_id"] = $"00000000-0000-4000-9000-{index + 1:D12}";
        card["name"] = VariantName(index);
        return card;
    }

    /// <summary>
    /// Reads a golden file recorded from the code before this change.
    /// </summary>
    private static JsonNode? Golden(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden", name + ".golden.json");
        return JsonNode.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Describes where two JSON values first differ, for a failure message.
    /// </summary>
    private static string Difference(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is JsonObject left && actual is JsonObject right)
        {
            foreach (string name in left.Select(property => property.Key).Union(right.Select(property => property.Key)))
            {
                string inner = Difference(left[name], right[name], $"{path}.{name}");
                if (inner.Length > 0)
                {
                    return inner;
                }
            }

            return string.Empty;
        }

        if (expected is JsonArray first && actual is JsonArray second && first.Count == second.Count)
        {
            for (int index = 0; index < first.Count; index++)
            {
                string inner = Difference(first[index], second[index], $"{path}[{index}]");
                if (inner.Length > 0)
                {
                    return inner;
                }
            }

            return string.Empty;
        }

        return JsonNode.DeepEquals(expected, actual)
            ? string.Empty
            : $"{path}: {expected?.ToJsonString()} vs {actual?.ToJsonString()}";
    }

    /// <summary>
    /// Removes the properties that standard added since the golden files were recorded.
    /// </summary>
    private static void RemoveAddedProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (string name in AddedCardProperties)
            {
                obj.Remove(name);
            }

            // A card face gained its own Oracle ID; the card keeps the one it always had.
            if (obj["faces"] is JsonArray faces)
            {
                foreach (JsonNode? face in faces)
                {
                    face?.AsObject().Remove("oracleId");
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
            {
                RemoveAddedProperties(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                RemoveAddedProperties(item);
            }
        }
    }

    /// <summary>
    /// Removes the evidence stamps, which carry retrieval times and random snapshot IDs.
    /// </summary>
    private static void RemoveEvidence(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("evidence");
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
            {
                RemoveEvidence(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                RemoveEvidence(item);
            }
        }
    }

    /// <summary>
    /// Builds a fake provider that answers with the six recorded cards.
    /// </summary>
    private static RecordingHandler RecordedProvider()
    {
        return ScryfallTestFixture.Provider(intercept: request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            JsonArray all = [.. RecordedCards.All.Select(name => (JsonNode?)RecordedCards.Object(name))];
            if (path is "/cards/search" or "/cards/collection")
            {
                return ListResponse(all, path == "/cards/search");
            }

            if (path == "/cards/named")
            {
                return ScryfallTestFixture.Json(RecordedCards.Raw(RecordedCards.Transform));
            }

            string? recorded = RecordedCards.All.FirstOrDefault(name => path == $"/cards/{Id(name)}");
            return recorded is null ? null : ScryfallTestFixture.Json(RecordedCards.Raw(recorded));
        });
    }

    /// <summary>
    /// Builds a fake provider whose search and collection answers hold the given number of generated variants.
    /// </summary>
    private static RecordingHandler VariantProvider(int count)
    {
        return ScryfallTestFixture.Provider(intercept: request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path is not ("/cards/search" or "/cards/collection"))
            {
                return null;
            }

            JsonArray data = [.. Enumerable.Range(0, count).Select(index => (JsonNode?)Variant(index))];
            return ListResponse(data, path == "/cards/search");
        });
    }

    /// <summary>
    /// Builds a provider list response for a search or a collection call.
    /// </summary>
    private static HttpResponseMessage ListResponse(JsonArray data, bool search)
    {
        JsonObject list = new() { ["object"] = "list", ["data"] = data };
        if (search)
        {
            list["total_cards"] = data.Count;
            list["has_more"] = false;
        }
        else
        {
            list["not_found"] = new JsonArray();
        }

        return ScryfallTestFixture.Json(list.ToJsonString());
    }

    /// <summary>
    /// Creates a service over a fake provider with local writes allowed.
    /// </summary>
    private static ScryfallService CreateService(string dataRoot, RecordingHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ResultOptions,
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);
    }

    /// <summary>
    /// Creates a service with a corpus installed from the recorded cards plus extra printings: planeswalker prices
    /// that test numeric order, a null price, and a cheaper Spanish printing, a cheaper Japanese Bolt, a tag on
    /// Bolt, and a tag on the reversible card's front face.
    /// </summary>
    private static async Task<ScryfallService> InstalledServiceAsync(string dataRoot)
    {
        List<string> lines = [.. RecordedCards.All.Select(RecordedCards.Raw)];
        lines.Add(Printing(RecordedCards.Planeswalker, 1, "10.00", "en").ToJsonString());
        lines.Add(Printing(RecordedCards.Planeswalker, 2, "9.00", "en").ToJsonString());
        lines.Add(Printing(RecordedCards.Planeswalker, 3, null, "en").ToJsonString());
        lines.Add(Printing(RecordedCards.Planeswalker, 4, "1.00", "es").ToJsonString());
        lines.Add(Printing(RecordedCards.LightningBolt, 5, "0.01", "ja").ToJsonString());
        lines.Add(FrontFaceTwin().ToJsonString());
        string burn = Tag("burn", [Oracle(RecordedCards.LightningBolt)]);
        string twincasters = Tag("twincasters", [FaceOracleId(RecordedCards.Reversible)]);
        return await InstallAsync(dataRoot, lines, [burn, twincasters]);
    }

    /// <summary>
    /// Builds a normal card that shares the reversible card's front face Oracle ID and has a USD price.
    /// </summary>
    private static JsonObject FrontFaceTwin()
    {
        JsonObject card = Printing(RecordedCards.LightningBolt, 6, "5.00", "en");
        card["oracle_id"] = FaceOracleId(RecordedCards.Reversible);
        card["name"] = "Front Face Twin";
        return card;
    }

    /// <summary>
    /// Creates a service whose installed corpus holds generated variants that all carry one oracle tag.
    /// </summary>
    private static async Task<ScryfallService> TaggedVariantServiceAsync(string dataRoot, int count)
    {
        JsonObject[] variants = [.. Enumerable.Range(0, count).Select(Variant)];
        string tag = Tag("bulk", [.. variants.Select(card => card["oracle_id"]!.GetValue<string>())]);
        return await InstallAsync(dataRoot, [.. variants.Select(card => card.ToJsonString())], [tag]);
    }

    /// <summary>
    /// Copies a recorded card as another printing with its own ID, USD price, and language.
    /// </summary>
    private static JsonObject Printing(string recordedName, int number, string? usd, string language)
    {
        JsonObject card = RecordedCards.Object(recordedName);
        card["id"] = $"00000000-0000-4000-b000-{number:D12}";
        card["lang"] = language;
        card["collector_number"] = $"extra-{number}";
        card["prices"] = new JsonObject { ["usd"] = usd };
        return card;
    }

    /// <summary>
    /// Builds one oracle tag line that is assigned to the given Oracle IDs.
    /// </summary>
    private static string Tag(string slug, IReadOnlyList<string> oracleIds)
    {
        JsonArray taggings = [];
        foreach (string id in oracleIds)
        {
            taggings.Add(new JsonObject { ["oracle_id"] = id, ["weight"] = "strong" });
        }

        return new JsonObject
        {
            ["object"] = "tag",
            ["id"] = Guid.CreateVersion7().ToString("D"),
            ["label"] = slug,
            ["slug"] = slug,
            ["type"] = "oracle",
            ["description"] = null,
            ["parent_ids"] = new JsonArray(),
            ["child_ids"] = new JsonArray(),
            ["aliases"] = new JsonArray(),
            ["taggings"] = taggings,
        }.ToJsonString();
    }

    /// <summary>
    /// Installs a corpus built from the given card lines and oracle tag lines through the fake provider.
    /// </summary>
    private static async Task<ScryfallService> InstallAsync(
        string dataRoot,
        IReadOnlyList<string> cardLines,
        IReadOnlyList<string> tagLines)
    {
        byte[] allCards = ScryfallTestFixture.GzipLines(cardLines);
        byte[] oracleTags = ScryfallTestFixture.GzipLines(tagLines);
        RecordingHandler handler = ScryfallTestFixture.Provider(intercept: request =>
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/download/all_cards.jsonl.gz" => ScryfallTestFixture.Bytes(allCards),
                "/download/oracle_tags.jsonl.gz" => ScryfallTestFixture.Bytes(oracleTags),
                _ => null,
            };
        });
        ScryfallService service = CreateService(dataRoot, handler);
        _ = RequireSuccess(await service.SyncCorpusAsync("refresh", null, TestContext.Current.CancellationToken));
        return service;
    }

    /// <summary>
    /// Extracts successful data or fails the test with the outcome.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
