using System.Text.Json;
using MtgMcp.Core.Results;
using MtgMcp.Core.Tests.Recorded;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the recorded Scryfall responses are intact, parse through the adapter, and drive its paging and failure paths.
/// </summary>
public sealed class ScryfallRecordedTests
{
    /// <summary>
    /// Serves recordings from the official origin so recorded next_page links stay same-origin.
    /// </summary>
    private static readonly Uri OfficialApi = new("https://api.scryfall.com/");

    /// <summary>
    /// Lists every manifest id so each recording is checked by its own test case.
    /// </summary>
    public static TheoryData<string> Ids { get; } = RecordedManifest.Ids();

    /// <summary>
    /// Verifies the stored bytes of each recording hash to the manifest value.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Recording_StoredBytes_MatchManifestHash(string id)
    {
        RecordedEntry entry = RecordedManifest.Get(id);

        Assert.Equal(entry.Sha256, entry.ComputeSha256());
    }

    /// <summary>
    /// Verifies no recording file exists without a manifest entry.
    /// </summary>
    [Fact]
    public void Manifest_RecordingFilesOnDisk_AreAllListed()
    {
        Assert.Empty(RecordedManifest.UnlistedFiles());
    }

    /// <summary>
    /// Verifies each recording parses through the adapter mapper or is the documented error body.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Recording_Body_ParsesThroughAdapter(string id)
    {
        RecordedEntry entry = RecordedManifest.Get(id);
        using JsonDocument document = JsonDocument.Parse(entry.ReadText());
        JsonElement root = document.RootElement;

        switch (root.GetProperty("object").GetString())
        {
            case "card":
                Assert.NotEmpty(MapCard(root).Name);
                break;
            case "list":
                Assert.All(root.GetProperty("data").EnumerateArray(), card => Assert.NotEmpty(MapCard(card).Name));
                break;
            case "error":
                Assert.Equal(entry.Status, root.GetProperty("status").GetInt32());
                Assert.Equal("not_found", root.GetProperty("code").GetString());
                break;
            default:
                Assert.Fail($"Unexpected recorded object in {id}.");
                break;
        }
    }

    /// <summary>
    /// Verifies a search follows the recorded next_page link and returns the cards of both recorded pages.
    /// </summary>
    [Fact]
    public async Task Search_RecordedFirstPage_FollowsRecordedNextPage()
    {
        RecordedEntry first = RecordedManifest.Get("search-angel-page-1");
        RecordedEntry second = RecordedManifest.Get("search-angel-page-2");
        using JsonDocument firstPage = JsonDocument.Parse(first.ReadText());
        using JsonDocument secondPage = JsonDocument.Parse(second.ReadText());
        Assert.Equal(
            ReplayHttpHandler.Key(HttpMethod.Get, new Uri(second.Url)),
            ReplayHttpHandler.Key(HttpMethod.Get, new Uri(firstPage.RootElement.GetProperty("next_page").GetString()!)));
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(first).Add(second);
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, handler);

        OperationResult<ScryfallSearchResult> result = await service.ReadSearchAsync(
            "t:angel",
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallSearchResult search = Assert.IsType<OperationSuccess<ScryfallSearchResult>>(result.Value).Data;
        Assert.Equal(4, search.Page.TotalCount);
        Assert.Equal(
            [.. DataNames(firstPage), .. DataNames(secondPage)],
            search.Page.Items.Select(card => card.Name));
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies the adapter sends the requested names and the recorded collection response maps found cards and reports the missing identifier.
    /// </summary>
    [Fact]
    public async Task Collection_RecordedResponse_ReturnsFoundAndMissingRows()
    {
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(RecordedManifest.Get("collection-bolt-and-miss"));
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, handler);

        OperationResult<ScryfallCollectionResponse> result = await service.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "Lightning Bolt"), new ScryfallCardLookup("exact-name", "Zzzz Not A Card")],
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallCollectionResponse collection = Assert.IsType<OperationSuccess<ScryfallCollectionResponse>>(result.Value).Data;
        using JsonDocument sent = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(
            ["Lightning Bolt", "Zzzz Not A Card"],
            sent.RootElement.GetProperty("identifiers").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("Lightning Bolt", Written(collection, "page", "items")[0].GetProperty("card").GetProperty("name").GetString());
        Assert.Null(collection.Page.Items[1].Card);
    }

    /// <summary>
    /// Verifies a named-card lookup returns the recorded card.
    /// </summary>
    [Fact]
    public async Task GetCard_RecordedNamedLookup_ReturnsCard()
    {
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(RecordedManifest.Get("named-lightning-bolt"));
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, handler);

        OperationResult<ScryfallCardResponse> result = await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Lightning Bolt"),
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallCardResponse card = Assert.IsType<OperationSuccess<ScryfallCardResponse>>(result.Value).Data;
        Assert.Equal("Lightning Bolt", Written(card, "card", "name").GetString());
    }

    /// <summary>
    /// Verifies the recorded 404 maps to a not-found result.
    /// </summary>
    [Fact]
    public async Task GetCard_Recorded404_MapsToNotFound()
    {
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(RecordedManifest.Get("named-not-found"));
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = CreateService(temporary.Path, handler);

        OperationResult<ScryfallCardResponse> result = await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Zzzz Not A Card"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("scryfall-not-found", Assert.IsType<OperationNotFound>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Lists the card names of a recorded list response in order.
    /// </summary>
    private static IEnumerable<string?> DataNames(JsonDocument list)
    {
        return list.RootElement.GetProperty("data").EnumerateArray().Select(card => card.GetProperty("name").GetString());
    }

    /// <summary>
    /// Reads one property per level of the path from a result written the way the MCP server writes it.
    /// </summary>
    private static JsonElement Written(object result, params string[] path)
    {
        JsonElement element = JsonSerializer.SerializeToElement(result, ScryfallTestFixture.ResultOptions);
        foreach (string name in path)
        {
            element = element.GetProperty(name);
        }

        return element;
    }

    /// <summary>
    /// Maps one recorded card object the way the service does for provider cards.
    /// </summary>
    private static ScryfallCard MapCard(JsonElement raw)
    {
        return ScryfallMapper.Card(raw, DateTimeOffset.UnixEpoch, "snapshot", includeRaw: false);
    }

    /// <summary>
    /// Creates a service that reads only from the replay handler.
    /// </summary>
    private static ScryfallService CreateService(string dataRoot, ReplayHttpHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ResultOptions,
            OfficialApi,
            handler: handler);
    }
}
