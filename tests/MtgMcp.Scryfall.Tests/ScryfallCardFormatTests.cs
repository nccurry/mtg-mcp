using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the concise, standard, and full card response formats, the facts they carry, and their size limit.
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
    /// Lists the top-level fields of a mapped card before the response format work, which standard must keep.
    /// </summary>
    private static readonly string[] BaselineCardFields =
    [
        "id", "oracleId", "illustrationId", "name", "setCode", "collectorNumber", "language", "releasedAt",
        "manaCost", "manaValue", "typeLine", "oracleText", "colors", "producedMana", "colorIdentity", "keywords",
        "legalities", "imageUris", "prices", "priceEvidence", "rankEvidence", "faces", "tags", "tagCoverage",
        "evidence",
    ];

    /// <summary>
    /// Lists the fields standard adds to the baseline.
    /// </summary>
    private static readonly string[] AddedCardFields =
    [
        "rarity", "power", "toughness", "loyalty", "layout", "games", "reserved", "gameChanger",
    ];

    /// <summary>
    /// Verifies the default concise set holds exactly the published fields, in order.
    /// </summary>
    [Fact]
    public void Project_DefaultSelection_WritesPublishedFieldsInOrder()
    {
        ScryfallCard card = MapFixtureCard();
        IReadOnlySet<string> selected = ParseSelected(null);
        ScryfallCardExtras extras = new(["white-weenie"], "0.05");

        ScryfallConciseCard concise = ScryfallConciseProjection.Project(card, extras, selected);

        string[] expected = [.. ScryfallConciseFields.Default
            .Select(name => name.StartsWith("prices.", StringComparison.Ordinal) ? "prices" : name)
            .Distinct()];
        Assert.Equal(expected, concise.Fields.Select(field => field.Key));
        Assert.Equal(card.Id, concise.Fields[0].Value);
        Assert.Equal("Venerable Knight", Value(concise, "name"));
        Assert.Equal(["white-weenie"], StringsOf(Value(concise, "tags")));
        Assert.Equal("0.05", Value(concise, "cheapestPriceUsd"));
        Assert.Equal(42L, Value(concise, "edhrecRank"));
        Assert.Equal(["commander"], StringsOf(Value(concise, "legalities")));
        Assert.Equal("0.10", Assert.IsAssignableFrom<IReadOnlyDictionary<string, string?>>(Value(concise, "prices"))["usd"]);
    }

    /// <summary>
    /// Verifies a field list keeps the always-included fields and writes the rest in the published order.
    /// </summary>
    [Fact]
    public void Project_FieldList_KeepsAlwaysFieldsAndPublishedOrder()
    {
        ScryfallCard card = MapFixtureCard();

        ScryfallConciseCard concise = ScryfallConciseProjection.Project(
            card,
            null,
            ParseSelected(["typeLine", "manaCost"]));

        Assert.Equal(["scryfallId", "oracleId", "name", "manaCost", "typeLine"], concise.Fields.Select(field => field.Key));
    }

    /// <summary>
    /// Verifies the extra fields are available by name and a known-null price stays an explicit null.
    /// </summary>
    [Fact]
    public void Project_ExtraFields_AreAvailableByName()
    {
        ScryfallCard card = MapFixtureCard();

        ScryfallConciseCard concise = ScryfallConciseProjection.Project(
            card,
            null,
            ParseSelected(["prices.usd", "prices.usdFoil", "prices.tix", "legalitiesAll", "games", "reserved", "layout"]));

        IReadOnlyDictionary<string, string?> prices =
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, string?>>(Value(concise, "prices"));
        Assert.Equal("0.10", prices["usd"]);
        Assert.Null(prices["usdFoil"]);
        Assert.Null(prices["tix"]);
        Assert.Equal("legal", Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(Value(concise, "legalitiesAll"))["commander"]);
        Assert.Equal(["paper", "mtgo"], StringsOf(Value(concise, "games")));
        Assert.False(Assert.IsType<bool>(Value(concise, "reserved")));
        Assert.Equal("normal", Value(concise, "layout"));
    }

    /// <summary>
    /// Verifies the concise card reads text, cost, colors, and power from the faces when the card has none of its own.
    /// </summary>
    [Fact]
    public void Project_TwoFacedCard_FallsBackToFaces()
    {
        JsonObject raw = JsonNode.Parse(ScryfallTestFixture.WhiteCard())!.AsObject();
        raw.Remove("mana_cost");
        raw.Remove("oracle_text");
        raw.Remove("colors");
        raw.Remove("type_line");
        raw["layout"] = "transform";
        raw["card_faces"] = new JsonArray
        {
            Face("Front Side", "{1}{W}", "Creature - Human", "Front text.", "2", "3", ["W"]),
            Face("Back Side", string.Empty, "Creature - Werewolf", "Back text.", "4", "5", ["R"]),
        };
        ScryfallCard card = Map(raw);

        ScryfallConciseCard concise = ScryfallConciseProjection.Project(card, null, ParseSelected(null));

        Assert.Equal("{1}{W}", Value(concise, "manaCost"));
        Assert.Equal("Front text.\n//\nBack text.", Value(concise, "oracleText"));
        Assert.Equal("Creature - Human // Creature - Werewolf", Value(concise, "typeLine"));
        Assert.Equal(["W"], StringsOf(Value(concise, "colors")));
        Assert.Equal("2", Value(concise, "power"));
        Assert.Equal("3", Value(concise, "toughness"));
    }

    /// <summary>
    /// Verifies tags and the cheapest price are explicit nulls when installed data is not available.
    /// </summary>
    [Fact]
    public void Project_WithoutInstalledFacts_WritesNullTagsAndPrice()
    {
        ScryfallConciseCard concise = ScryfallConciseProjection.Project(MapFixtureCard(), null, ParseSelected(null));

        Assert.Null(Value(concise, "tags"));
        Assert.Null(Value(concise, "cheapestPriceUsd"));
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
            Assert.All(ScryfallConciseFields.All, name => Assert.Contains(name, invalid.Message, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Verifies the mapper reads the new fields when present and gives neutral values when absent.
    /// </summary>
    [Fact]
    public void Mapper_NewFields_ArePresentAndAbsent()
    {
        JsonObject raw = JsonNode.Parse(ScryfallTestFixture.WhiteCard())!.AsObject();
        raw["games"] = new JsonArray("paper", "arena");
        raw["reserved"] = true;
        raw["layout"] = "saga";
        raw["game_changer"] = true;
        raw["rarity"] = "rare";
        raw["power"] = "*";
        raw["toughness"] = "1+*";
        raw["loyalty"] = "4";

        ScryfallCard present = Map(raw);
        ScryfallCard absent = Map(JsonNode.Parse(ScryfallTestFixture.RedCard())!.AsObject());

        Assert.Equal(["paper", "arena"], present.Games);
        Assert.True(present.Reserved);
        Assert.Equal("saga", present.Layout);
        Assert.True(present.GameChanger);
        Assert.Equal("rare", present.Rarity);
        Assert.Equal(("*", "1+*", "4"), (present.Power, present.Toughness, present.Loyalty));
        Assert.Empty(absent.Games);
        Assert.False(absent.Reserved);
        Assert.Null(absent.Layout);
        Assert.Null(absent.GameChanger);
        Assert.Null(absent.Rarity);
    }

    /// <summary>
    /// Verifies a standard card keeps every field of the baseline output and only adds the published new ones.
    /// </summary>
    [Fact]
    public void StandardCard_KeepsBaselineFieldsAndOnlyAddsPublishedOnes()
    {
        ScryfallCard card = MapFixtureCard();

        JsonElement json = JsonSerializer.SerializeToElement(card, ResultOptions);

        string[] names = [.. json.EnumerateObject().Select(property => property.Name)];
        Assert.All(BaselineCardFields, name => Assert.Contains(name, names));
        Assert.Empty(names.Except(BaselineCardFields).Except(AddedCardFields));
        Assert.Equal("Venerable Knight", json.GetProperty("name").GetString());
        Assert.Equal("{W}", json.GetProperty("manaCost").GetString());
        Assert.Equal("0.10", json.GetProperty("prices").GetProperty("usd").GetString());
        Assert.False(json.TryGetProperty("raw", out _));
    }

    /// <summary>
    /// Verifies the standard format returns the same page of cards as the service call that never raw-expanded.
    /// </summary>
    [Fact]
    public async Task Search_Standard_ReturnsTheMappedCardsOfTheEarlierCall()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, ScryfallTestFixture.Provider());
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResult mapped = RequireSuccess(await service.SearchAsync("name:knight", cancellationToken: token));
        ScryfallSearchResponse response = RequireSuccess(await service.SearchResponseAsync("name:knight", cancellationToken: token));

        Assert.Equal("standard", response.ResponseFormat);
        Assert.Null(response.Limits);
        Assert.Null(response.Truncated);
        JsonElement expected = JsonSerializer.SerializeToElement(mapped.Page, ResultOptions);
        JsonElement actual = JsonSerializer.SerializeToElement(response.Page, ResultOptions);
        Assert.Equal(expected.GetProperty("totalCount").GetInt32(), actual.GetProperty("totalCount").GetInt32());
        for (int index = 0; index < expected.GetProperty("items").GetArrayLength(); index++)
        {
            JsonElement before = expected.GetProperty("items")[index];
            JsonElement after = actual.GetProperty("items")[index];
            foreach (JsonProperty property in before.EnumerateObject())
            {
                Assert.Equal(property.Value.GetRawText(), after.GetProperty(property.Name).GetRawText());
            }
        }
    }

    /// <summary>
    /// Verifies a concise page of 25 realistic cards is at most 40 percent of the standard page's size.
    /// </summary>
    [Fact]
    public async Task Search_ConcisePage_IsAtMostFortyPercentOfStandard()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, SearchProvider(25));
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResponse standard = RequireSuccess(await service.SearchResponseAsync(
            "t:creature",
            responseFormat: "standard",
            cancellationToken: token));
        ScryfallSearchResponse concise = RequireSuccess(await service.SearchResponseAsync(
            "t:creature",
            responseFormat: "concise",
            cancellationToken: token));

        int standardBytes = JsonSerializer.SerializeToUtf8Bytes(standard.Page, ResultOptions).Length;
        int conciseBytes = JsonSerializer.SerializeToUtf8Bytes(concise.Page, ResultOptions).Length;
        Assert.Equal(25, concise.Page.Items.Count);
        Assert.True(conciseBytes * 10 <= standardBytes * 4, $"concise {conciseBytes} bytes, standard {standardBytes} bytes");
    }

    /// <summary>
    /// Verifies the full format returns the same cards, including raw objects, as the earlier raw option did.
    /// </summary>
    [Fact]
    public async Task Search_Full_MatchesTheRawCallAndKeepsThePageCap()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, ScryfallTestFixture.Provider());
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResult raw = RequireSuccess(await service.SearchAsync(
            "name:knight",
            includeRaw: true,
            cancellationToken: token));
        ScryfallSearchResponse full = RequireSuccess(await service.SearchResponseAsync(
            "name:knight",
            responseFormat: "full",
            cancellationToken: token));
        OperationInvalidInput tooLarge = Assert.IsType<OperationInvalidInput>((await service.SearchResponseAsync(
            "name:knight",
            pageSize: 26,
            responseFormat: "full",
            cancellationToken: token)).Value);

        Assert.Equal("full", full.ResponseFormat);
        Assert.Equal(
            JsonSerializer.Serialize(raw.Page.Items, ResultOptions),
            JsonSerializer.Serialize(full.Page.Items, ResultOptions));
        Assert.All(full.Page.Items, view => Assert.NotNull(Assert.IsType<ScryfallCard>(view.Value).Raw));
        Assert.Equal("invalid-page-size", tooLarge.ReasonCode);
    }

    /// <summary>
    /// Verifies a concise search without installed card data returns null tags and price with a stated reason.
    /// </summary>
    [Fact]
    public async Task Search_Concise_WithoutInstalledData_ReportsTheReason()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, ScryfallTestFixture.Provider());

        ScryfallSearchResponse response = RequireSuccess(await service.SearchResponseAsync(
            "name:knight",
            responseFormat: "concise",
            cancellationToken: TestContext.Current.CancellationToken));

        JsonElement first = JsonSerializer.SerializeToElement(response.Page.Items[0], ResultOptions);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("tags").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("cheapestPriceUsd").ValueKind);
        string limit = Assert.Single(response.Limits!);
        Assert.Contains("tags and cheapestPriceUsd are null", limit, StringComparison.Ordinal);
        Assert.Contains("scryfall_corpus_sync", limit, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the limit note is not written when the chosen fields need no installed data.
    /// </summary>
    [Fact]
    public async Task Search_Concise_FieldsWithoutInstalledFacts_HaveNoLimit()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, ScryfallTestFixture.Provider());

        ScryfallSearchResponse response = RequireSuccess(await service.SearchResponseAsync(
            "name:knight",
            responseFormat: "concise",
            fields: ["manaCost"],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(response.Limits);
        JsonElement first = JsonSerializer.SerializeToElement(response.Page.Items[0], ResultOptions);
        Assert.Equal(
            ["scryfallId", "oracleId", "name", "manaCost"],
            first.EnumerateObject().Select(property => property.Name));
    }

    /// <summary>
    /// Verifies installed card data supplies tags and the lowest USD price across printings to every card format.
    /// </summary>
    [Fact]
    public async Task ConciseCards_WithInstalledData_CarryTagsAndCheapestPrice()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResponse search = RequireSuccess(await service.SearchResponseAsync(
            "ci<=rw mv=1",
            responseFormat: "concise",
            cancellationToken: token));
        ScryfallCardResponse single = RequireSuccess(await service.GetCardResponseAsync(
            new ScryfallCardLookup("exact-name", "Venerable Knight"),
            "cache-only",
            "concise",
            null,
            token));
        ScryfallCollectionResponse collection = RequireSuccess(await service.GetCollectionResponseAsync(
            [
                new ScryfallCardLookup("exact-name", "Monastery Swiftspear"),
                new ScryfallCardLookup("scryfall-id", Guid.Empty.ToString("D")),
            ],
            "cache-only",
            null,
            25,
            "concise",
            null,
            token));
        ScryfallCardsByTagResponse byTag = RequireSuccess(await service.GetCardsByTagResponseAsync(
            "aggro",
            "oracle",
            true,
            "weak",
            null,
            25,
            "concise",
            null,
            token));

        Assert.Null(search.Limits);
        JsonElement knight = JsonSerializer.SerializeToElement(single.Card, ResultOptions);
        Assert.Equal(["white-weenie"], Strings(knight.GetProperty("tags")));
        Assert.Equal("0.05", knight.GetProperty("cheapestPriceUsd").GetString());
        JsonElement searchKnight = JsonSerializer.SerializeToElement(
            search.Page.Items.First(view => Name(view) == "Venerable Knight"),
            ResultOptions);
        Assert.Equal("0.05", searchKnight.GetProperty("cheapestPriceUsd").GetString());
        JsonElement swiftspear = JsonSerializer.SerializeToElement(collection.Page.Items[0].Card, ResultOptions);
        Assert.Empty(Strings(swiftspear.GetProperty("tags")));
        Assert.Equal("0.10", swiftspear.GetProperty("cheapestPriceUsd").GetString());
        Assert.Equal("not-cached", collection.Page.Items[1].Status);
        Assert.Null(collection.Page.Items[1].Card);
        Assert.Equal(2, byTag.Page.Items.Count);
        foreach (ScryfallCardView tagged in byTag.Page.Items)
        {
            JsonElement element = JsonSerializer.SerializeToElement(tagged, ResultOptions);
            Assert.Equal(["white-weenie"], Strings(element.GetProperty("tags")));
        }

        Assert.All(byTag.Assignments, assignment => Assert.Equal("inherited", assignment.Relationship));
    }

    /// <summary>
    /// Verifies the full format works on the other card tools and still names its format.
    /// </summary>
    [Fact]
    public async Task FullFormat_OnCardAndTagTools_ReturnsRawObjects()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallCardResponse single = RequireSuccess(await service.GetCardResponseAsync(
            new ScryfallCardLookup("exact-name", "Venerable Knight"),
            "cache-only",
            "full",
            null,
            token));
        ScryfallCardsByTagResponse byTag = RequireSuccess(await service.GetCardsByTagResponseAsync(
            "aggro",
            "oracle",
            true,
            "weak",
            null,
            25,
            "full",
            null,
            token));

        Assert.NotNull(Assert.IsType<ScryfallCard>(single.Card.Value).Raw);
        Assert.NotNull(byTag.Tag.Raw);
        Assert.All(byTag.Page.Items, view => Assert.NotNull(Assert.IsType<ScryfallCard>(view.Value).Raw));
        Assert.Equal("full", byTag.ResponseFormat);
    }

    /// <summary>
    /// Verifies the field list is rejected before any provider read when it is not valid.
    /// </summary>
    [Fact]
    public async Task Search_InvalidFormatRequest_FailsBeforeReadingTheProvider()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await service.SearchResponseAsync(
            "name:knight",
            responseFormat: "standard",
            fields: ["name"],
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal("fields-require-concise", invalid.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Verifies a concise page that would pass 40,000 bytes is cut, says so, and the next page continues without gaps.
    /// </summary>
    [Fact]
    public async Task Search_ConcisePageOverTheLimit_IsCutAndContinues()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, SearchProvider(100));
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> names = [];
        string? cursor = null;
        int pages = 0;

        do
        {
            ScryfallSearchResponse page = RequireSuccess(await service.SearchResponseAsync(
                "t:creature",
                cursor: cursor,
                pageSize: 100,
                responseFormat: "concise",
                cancellationToken: token));
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, ResultOptions).Length <= ResultSizeLimit.MaximumBytes);
            if (page.Page.NextCursor is not null)
            {
                Assert.True(page.Truncated);
                Assert.Equal(ResultSizeLimit.CardNarrowHint, page.Narrow);
            }

            names.AddRange(page.Page.Items.Select(Name));
            cursor = page.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 100).Select(index => $"Test Card {index:D3}"), names);
    }

    /// <summary>
    /// Verifies a collection page over the limit is cut the same way and its cursor reads the next rows.
    /// </summary>
    [Fact]
    public async Task Collection_ConcisePageOverTheLimit_IsCutAndContinues()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, CollectionProvider(75));
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup[] lookups =
            [.. Enumerable.Range(0, 75).Select(index => new ScryfallCardLookup("scryfall-id", CardId(index).ToString("D")))];
        List<int> indexes = [];
        string? cursor = null;
        bool sawTruncation = false;

        do
        {
            ScryfallCollectionResponse page = RequireSuccess(await service.GetCollectionResponseAsync(
                lookups,
                "default",
                cursor,
                100,
                "concise",
                null,
                token));
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, ResultOptions).Length <= ResultSizeLimit.MaximumBytes);
            sawTruncation |= page.Truncated == true;
            indexes.AddRange(page.Page.Items.Select(row => row.Index));
            cursor = page.Page.NextCursor;
        }
        while (cursor is not null);

        Assert.True(sawTruncation);
        Assert.Equal(Enumerable.Range(0, 75), indexes);
    }

    /// <summary>
    /// Verifies a tag page over the limit is cut with its assignments and continues from the right card.
    /// </summary>
    [Fact]
    public async Task CardsByTag_ConcisePageOverTheLimit_IsCutAndContinues()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = await TaggedCorpusServiceAsync(temporary.Path, 100);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> names = [];
        string? cursor = null;
        bool sawTruncation = false;

        do
        {
            ScryfallCardsByTagResponse page = RequireSuccess(await service.GetCardsByTagResponseAsync(
                "bulk",
                "oracle",
                false,
                "weak",
                cursor,
                100,
                "concise",
                null,
                token));
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, ResultOptions).Length <= ResultSizeLimit.MaximumBytes);
            Assert.Equal(page.Page.Items.Count, page.Assignments.Count);
            sawTruncation |= page.Truncated == true;
            names.AddRange(page.Page.Items.Select(Name));
            cursor = page.Page.NextCursor;
        }
        while (cursor is not null);

        Assert.True(sawTruncation);
        Assert.Equal(100, names.Distinct().Count());
    }

    /// <summary>
    /// Verifies deck-entry lookups report installed, unresolved, and not-installed states and carry card and tags.
    /// </summary>
    [Fact]
    public async Task InstalledCardViews_ReportEachStateWithoutGuessing()
    {
        using TemporaryScryfallDirectory notInstalledDirectory = new();
        using ScryfallService empty = CreateService(notInstalledDirectory.Path, ScryfallTestFixture.Provider());
        using TemporaryScryfallDirectory installedDirectory = new();
        using ScryfallService installed = await InstalledServiceAsync(installedDirectory.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup[] lookups =
        [
            new ScryfallCardLookup("scryfall-id", ScryfallTestFixture.WhiteCardId.ToString("D")),
            new ScryfallCardLookup("scryfall-id", Guid.Empty.ToString("D")),
        ];

        ScryfallInstalledCardViews missing = RequireSuccess(await empty.ReadInstalledCardViewsAsync(
            lookups,
            null,
            includeCards: true,
            includeTags: true,
            token));
        ScryfallInstalledCardViews found = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [lookups[0], null, lookups[1]],
            null,
            includeCards: true,
            includeTags: true,
            token));
        ScryfallInstalledCardViews tagsOnly = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [lookups[0]],
            null,
            includeCards: false,
            includeTags: true,
            token));

        Assert.False(missing.Installed);
        Assert.All(missing.Cards, view => Assert.Equal("not-installed", view.Status));
        Assert.True(found.Installed);
        Assert.Equal(["installed", "unresolved", "unresolved"], found.Cards.Select(view => view.Status));
        Assert.Equal(["white-weenie"], found.Cards[0].Tags);
        Assert.False(found.Cards[0].Card!.TryGetValue("tags", out _));
        Assert.Equal("0.05", found.Cards[0].Card!.TryGetValue("cheapestPriceUsd", out object? price) ? price : null);
        Assert.Null(found.Cards[1].Tags);
        Assert.Null(tagsOnly.Cards[0].Card);
        Assert.Equal(["white-weenie"], tagsOnly.Cards[0].Tags);
    }

    /// <summary>
    /// Verifies naming tags as a field puts them on the card as well, and bad names are rejected.
    /// </summary>
    [Fact]
    public async Task InstalledCardViews_FieldList_ChoosesCardFieldsAndValidatesNames()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService installed = await InstalledServiceAsync(temporary.Path);
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup white = new("scryfall-id", ScryfallTestFixture.WhiteCardId.ToString("D"));

        ScryfallInstalledCardViews tagged = RequireSuccess(await installed.ReadInstalledCardViewsAsync(
            [white],
            ["tags", "layout"],
            includeCards: true,
            includeTags: false,
            token));
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await installed.ReadInstalledCardViewsAsync(
            [white],
            ["nope"],
            includeCards: true,
            includeTags: false,
            token)).Value);

        Assert.Equal(
            ["scryfallId", "oracleId", "name", "tags", "layout"],
            tagged.Cards[0].Card!.Fields.Select(field => field.Key));
        Assert.Null(tagged.Cards[0].Tags);
        Assert.Equal("invalid-fields", invalid.ReasonCode);
    }

    /// <summary>
    /// Maps the white fixture card with its default fixture data.
    /// </summary>
    private static ScryfallCard MapFixtureCard()
    {
        JsonObject raw = JsonNode.Parse(ScryfallTestFixture.WhiteCard())!.AsObject();
        raw["games"] = new JsonArray("paper", "mtgo");
        raw["layout"] = "normal";
        raw["rarity"] = "common";
        return Map(raw);
    }

    /// <summary>
    /// Maps one card object as the service does for provider cards.
    /// </summary>
    private static ScryfallCard Map(JsonObject raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw.ToJsonString());
        return ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "snapshot", includeRaw: false);
    }

    /// <summary>
    /// Builds one card-face object.
    /// </summary>
    private static JsonObject Face(
        string name,
        string manaCost,
        string typeLine,
        string oracleText,
        string power,
        string toughness,
        string[] colors)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["mana_cost"] = manaCost,
            ["type_line"] = typeLine,
            ["oracle_text"] = oracleText,
            ["power"] = power,
            ["toughness"] = toughness,
            ["colors"] = new JsonArray([.. colors.Select(color => (JsonNode?)JsonValue.Create(color))]),
        };
    }

    /// <summary>
    /// Parses the field selection the same way a request does.
    /// </summary>
    private static IReadOnlySet<string> ParseSelected(string[]? fields)
    {
        return Assert.IsType<OperationSuccess<ScryfallCardFormat>>(
            ScryfallCardFormat.Parse("concise", fields).Value).Data.SelectedFields!;
    }

    /// <summary>
    /// Reads one named field of a concise card.
    /// </summary>
    private static object? Value(ScryfallConciseCard card, string name)
    {
        Assert.True(card.TryGetValue(name, out object? value), name);
        return value;
    }

    /// <summary>
    /// Reads a concise field value that is a list of strings.
    /// </summary>
    private static string[] StringsOf(object? value)
    {
        return [.. Assert.IsAssignableFrom<IEnumerable<string>>(value)];
    }

    /// <summary>
    /// Reads the name of a card view.
    /// </summary>
    private static string Name(ScryfallCardView view)
    {
        return JsonSerializer.SerializeToElement(view, ResultOptions).GetProperty("name").GetString()!;
    }

    /// <summary>
    /// Reads a JSON array of strings.
    /// </summary>
    private static string[] Strings(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>
    /// Builds the stable ID of the generated test card at one position.
    /// </summary>
    private static Guid CardId(int index)
    {
        return Guid.Parse($"00000000-0000-4000-8000-{index + 1:D12}");
    }

    /// <summary>
    /// Builds a card object the size and shape of a real Scryfall card, so size checks are realistic.
    /// </summary>
    private static JsonObject RealisticCard(int index)
    {
        string[] formats =
        [
            "standard", "future", "historic", "timeless", "gladiator", "pioneer", "modern", "legacy", "pauper",
            "vintage", "penny", "commander", "oathbreaker", "standardbrawl", "brawl", "alchemy", "paupercommander",
            "duel", "oldschool", "premodern", "predh",
        ];
        JsonObject legalities = [];
        foreach (string format in formats)
        {
            legalities[format] = format is "commander" or "modern" or "legacy" ? "legal" : "not_legal";
        }

        Guid id = CardId(index);
        return new JsonObject
        {
            ["object"] = "card",
            ["id"] = id.ToString("D"),
            ["oracle_id"] = Guid.Parse($"00000000-0000-4000-9000-{index + 1:D12}").ToString("D"),
            ["illustration_id"] = Guid.Parse($"00000000-0000-4000-a000-{index + 1:D12}").ToString("D"),
            ["name"] = $"Test Card {index:D3}",
            ["set"] = "tst",
            ["collector_number"] = (index + 1).ToString("D", CultureInfo.InvariantCulture),
            ["lang"] = "en",
            ["released_at"] = "2026-07-04",
            ["mana_cost"] = "{2}{G}",
            ["cmc"] = 3.0,
            ["type_line"] = "Creature - Elf Druid",
            ["oracle_text"] = "{T}: Add one mana of any color. Whenever this creature attacks, you may search your library " +
                "for a basic land card, put it onto the battlefield tapped, then shuffle.",
            ["power"] = "2",
            ["toughness"] = "2",
            ["colors"] = new JsonArray("G"),
            ["color_identity"] = new JsonArray("G"),
            ["keywords"] = new JsonArray("Flash", "Reach"),
            ["produced_mana"] = new JsonArray("W", "U", "B", "R", "G"),
            ["legalities"] = legalities,
            ["games"] = new JsonArray("paper", "mtgo", "arena"),
            ["reserved"] = false,
            ["game_changer"] = false,
            ["layout"] = "normal",
            ["rarity"] = "rare",
            ["image_uris"] = new JsonObject
            {
                ["small"] = $"https://cards.scryfall.io/small/front/0/0/{id:D}.jpg?1700000000",
                ["normal"] = $"https://cards.scryfall.io/normal/front/0/0/{id:D}.jpg?1700000000",
                ["large"] = $"https://cards.scryfall.io/large/front/0/0/{id:D}.jpg?1700000000",
                ["png"] = $"https://cards.scryfall.io/png/front/0/0/{id:D}.png?1700000000",
                ["art_crop"] = $"https://cards.scryfall.io/art_crop/front/0/0/{id:D}.jpg?1700000000",
                ["border_crop"] = $"https://cards.scryfall.io/border_crop/front/0/0/{id:D}.jpg?1700000000",
            },
            ["prices"] = new JsonObject
            {
                ["usd"] = "1.25",
                ["usd_foil"] = "2.50",
                ["usd_etched"] = null,
                ["eur"] = "1.10",
                ["eur_foil"] = "2.20",
                ["tix"] = "0.03",
            },
            ["edhrec_rank"] = 1000 + index,
            ["penny_rank"] = 2000 + index,
        };
    }

    /// <summary>
    /// Builds a fake provider whose search returns the given number of realistic cards.
    /// </summary>
    private static RecordingHandler SearchProvider(int count)
    {
        return ScryfallTestFixture.Provider(intercept: request =>
        {
            if (request.RequestUri!.AbsolutePath != "/cards/search")
            {
                return null;
            }

            JsonArray data = [.. Enumerable.Range(0, count).Select(index => (JsonNode?)RealisticCard(index))];
            return ScryfallTestFixture.Json(new JsonObject
            {
                ["object"] = "list",
                ["total_cards"] = count,
                ["has_more"] = false,
                ["data"] = data,
            }.ToJsonString());
        });
    }

    /// <summary>
    /// Builds a fake provider whose collection endpoint returns the given number of realistic cards.
    /// </summary>
    private static RecordingHandler CollectionProvider(int count)
    {
        return ScryfallTestFixture.Provider(intercept: request =>
        {
            if (request.RequestUri!.AbsolutePath != "/cards/collection")
            {
                return null;
            }

            JsonArray data = [.. Enumerable.Range(0, count).Select(index => (JsonNode?)RealisticCard(index))];
            return ScryfallTestFixture.Json(new JsonObject
            {
                ["object"] = "list",
                ["data"] = data,
                ["not_found"] = new JsonArray(),
            }.ToJsonString());
        });
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
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);
    }

    /// <summary>
    /// Creates a service with the fixture corpus installed, where the white card has a second, cheaper printing.
    /// </summary>
    private static async Task<ScryfallService> InstalledServiceAsync(string dataRoot)
    {
        JsonObject cheaper = JsonNode.Parse(ScryfallTestFixture.WhiteCard())!.AsObject();
        cheaper["id"] = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc").ToString("D");
        cheaper["set"] = "tst";
        cheaper["collector_number"] = "9";
        cheaper["prices"] = new JsonObject { ["usd"] = "0.05", ["usd_foil"] = null };
        byte[] allCards = ScryfallTestFixture.GzipLines(
            [ScryfallTestFixture.WhiteCard(), ScryfallTestFixture.RedCard(), cheaper.ToJsonString()]);
        return await InstallAsync(dataRoot, allCards, null);
    }

    /// <summary>
    /// Creates a service whose installed corpus has many cards tagged with one oracle tag.
    /// </summary>
    private static async Task<ScryfallService> TaggedCorpusServiceAsync(string dataRoot, int count)
    {
        JsonObject[] cards = [.. Enumerable.Range(0, count).Select(RealisticCard)];
        string tag = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
            label = "Bulk",
            slug = "bulk",
            type = "oracle",
            description = (string?)null,
            parent_ids = Array.Empty<Guid>(),
            child_ids = Array.Empty<Guid>(),
            aliases = Array.Empty<string>(),
            taggings = cards
                .Select(card => new { oracle_id = Guid.Parse(card["oracle_id"]!.GetValue<string>()), weight = "strong" })
                .ToArray(),
        });
        return await InstallAsync(
            dataRoot,
            ScryfallTestFixture.GzipLines([.. cards.Select(card => card.ToJsonString())]),
            ScryfallTestFixture.GzipLines([tag]));
    }

    /// <summary>
    /// Installs a corpus built from the given card lines and optional tag lines through the fake provider.
    /// </summary>
    private static async Task<ScryfallService> InstallAsync(string dataRoot, byte[] allCards, byte[]? oracleTags)
    {
        RecordingHandler handler = ScryfallTestFixture.Provider(intercept: request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/download/all_cards.jsonl.gz")
            {
                return ScryfallTestFixture.Bytes(allCards);
            }

            return path == "/download/oracle_tags.jsonl.gz" && oracleTags is not null
                ? ScryfallTestFixture.Bytes(oracleTags)
                : null;
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
