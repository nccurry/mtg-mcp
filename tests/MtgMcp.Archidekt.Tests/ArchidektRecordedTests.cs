using System.Security.Cryptography;
using System.Text.Json;
using MtgMcp.Core.Results;
using MtgMcp.Core.Tests.Recorded;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Verifies the recorded Archidekt responses are intact, parse through the adapter, and drive its search paging and
/// deck mapping.
/// </summary>
public sealed class ArchidektRecordedTests
{
    /// <summary>
    /// Lists every manifest id so each recording is checked by its own test case.
    /// </summary>
    public static TheoryData<string> Ids { get; } = ManifestIds();

    /// <summary>
    /// Verifies the stored bytes of each recording hash to the manifest value.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Recording_StoredBytes_MatchManifestHash(string id)
    {
        RecordedEntry entry = RecordedManifest.Get(id);

        Assert.Equal(entry.Sha256, Convert.ToHexStringLower(SHA256.HashData(entry.ReadBytes())));
    }

    /// <summary>
    /// Verifies no recording file exists without a manifest entry.
    /// </summary>
    [Fact]
    public void Manifest_RecordingFilesOnDisk_AreAllListed()
    {
        HashSet<string> listed = [.. RecordedManifest.Entries.Select(entry => RecordedManifest.PathOf(entry.File))];

        string[] unlisted = [.. Directory
            .EnumerateFiles(RecordedManifest.Root, "*.json", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "manifest.json" && !listed.Contains(path))];

        Assert.Empty(unlisted);
    }

    /// <summary>
    /// Verifies each recording parses through the adapter mapper for its route.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Recording_Body_ParsesThroughAdapter(string id)
    {
        RecordedEntry entry = RecordedManifest.Get(id);
        using JsonDocument document = JsonDocument.Parse(entry.ReadText());
        JsonElement root = document.RootElement;

        if (entry.Route == "/api/cards/v2/")
        {
            Assert.All(
                root.GetProperty("results").EnumerateArray(),
                result => Assert.NotNull(ArchidektJsonContract.ReadPrinting(result)));
            return;
        }

        RemoteDeckSnapshot deck = ArchidektDeckContractMapper.MapDeck(root, entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route);
        Assert.Equal("22959335", deck.RemoteId);
    }

    /// <summary>
    /// Verifies the owner's identity and folder id and the deck text that names a migration source are absent from the
    /// recorded deck.
    /// </summary>
    [Fact]
    public void Deck_RecordedBody_HoldsNoOwnerIdentity()
    {
        string text = RecordedManifest.Get("deck-22959335").ReadText();

        Assert.DoesNotContain("username", text, StringComparison.Ordinal);
        Assert.DoesNotContain("avatar", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace", text, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement owner = document.RootElement.GetProperty("owner");
        Assert.Equal(0, owner.GetProperty("id").GetInt32());
        Assert.Single(owner.EnumerateObject());
        Assert.Equal(0, document.RootElement.GetProperty("parentFolder").GetInt32());
    }

    /// <summary>
    /// Verifies the trimmed deck recording stays under 50 KB.
    /// </summary>
    [Fact]
    public void Deck_RecordedFile_IsUnderFiftyKilobytes()
    {
        Assert.InRange(RecordedManifest.Get("deck-22959335").ReadBytes().Length, 1, 50 * 1024 - 1);
    }

    /// <summary>
    /// Verifies a search for a card with many printings reports the full count, a next link, and each printing's set.
    /// </summary>
    [Fact]
    public async Task SearchCards_RecordedFirstPage_ReportsCountNextAndSet()
    {
        RecordedEntry first = RecordedManifest.Get("search-sol-ring-page-1");
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(first);
        using ArchidektSession session = CreateSession(handler);
        ArchidektDeckTransport transport = new(session);
        using JsonDocument recorded = JsonDocument.Parse(first.ReadText());
        JsonElement.ArrayEnumerator results = recorded.RootElement.GetProperty("results").EnumerateArray();

        ArchidektCardSearchPage page = await transport.SearchCardsAsync(
            new ArchidektCardSearch("Sol Ring", SetCode: null),
            nextQuery: null,
            new ArchidektOperationBudget(10),
            TestContext.Current.CancellationToken);

        Assert.Equal(recorded.RootElement.GetProperty("count").GetInt32(), page.Count);
        Assert.True(page.Count > page.ResultCount);
        Assert.NotNull(page.NextQuery);
        Assert.All(page.Results, printing => Assert.Equal("Sol Ring", printing.CardName));
        Assert.Equal(
            results.Select(result => result.GetProperty("edition").GetProperty("editioncode").GetString()),
            page.Results.Select(printing => printing.SetCode));
    }

    /// <summary>
    /// Verifies the next query of the first recorded page reads the second recorded page.
    /// </summary>
    [Fact]
    public async Task SearchCards_RecordedNextQuery_ReadsSecondPage()
    {
        RecordedEntry first = RecordedManifest.Get("search-sol-ring-page-1");
        RecordedEntry second = RecordedManifest.Get("search-sol-ring-page-2");
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(first).Add(second);
        using ArchidektSession session = CreateSession(handler);
        ArchidektDeckTransport transport = new(session);
        ArchidektCardSearch search = new("Sol Ring", SetCode: null);
        ArchidektOperationBudget budget = new(10);

        ArchidektCardSearchPage firstPage = await transport.SearchCardsAsync(
            search,
            nextQuery: null,
            budget,
            TestContext.Current.CancellationToken);
        ArchidektCardSearchPage secondPage = await transport.SearchCardsAsync(
            search,
            firstPage.NextQuery,
            budget,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            ReplayHttpHandler.Key(HttpMethod.Get, new Uri(second.Url)),
            ReplayHttpHandler.Key(HttpMethod.Get, handler.Requests[1].Uri));
        Assert.Equal(firstPage.Count, secondPage.Count);
        Assert.NotEmpty(secondPage.Results);
    }

    /// <summary>
    /// Verifies the recorded deck maps its commander, cards in two categories, and an excluded primary category out of main while an excluded secondary category does not.
    /// </summary>
    [Fact]
    public async Task GetDeck_RecordedDeck_MapsZonesAndCategories()
    {
        using ReplayHttpHandler handler = new ReplayHttpHandler().Add(RecordedManifest.Get("deck-22959335"));
        using ArchidektService service = new(CreateSession(handler), 150);

        OperationResult<RemoteDeckSnapshot> result = await service.GetDeckAsync(
            "22959335",
            TestContext.Current.CancellationToken);

        RemoteDeckSnapshot deck = Assert.IsType<OperationSuccess<RemoteDeckSnapshot>>(result.Value).Data;
        Assert.Equal(5, deck.Entries.Count);
        Assert.Equal("commander", Entry(deck, "Sam, Loyal Attendant").Zone);
        Assert.Equal(["Food", "Ramp"], Entry(deck, "Tireless Provisioner").CategoryNames);
        Assert.Equal("main", Entry(deck, "Tireless Provisioner").Zone);
        Assert.False(deck.Categories.Single(category => category.Name == "Maybeboard").IncludedInDeck);
        Assert.Equal("maybeboard", Entry(deck, "Ghostly Prison").Zone);
        Assert.Equal(["Maybeboard", "Protection"], Entry(deck, "Ghostly Prison").CategoryNames);
        Assert.False(deck.Categories.Single(category => category.Name == "Multiplayer").IncludedInDeck);
        Assert.Equal("main", Entry(deck, "Grasp of Fate").Zone);
    }

    /// <summary>
    /// Finds one mapped entry by card name.
    /// </summary>
    private static RemoteDeckEntry Entry(RemoteDeckSnapshot deck, string cardName)
    {
        return deck.Entries.Single(entry => entry.CardName == cardName);
    }

    /// <summary>
    /// Collects the manifest ids for theory data.
    /// </summary>
    private static TheoryData<string> ManifestIds()
    {
        TheoryData<string> ids = [];
        foreach (RecordedEntry entry in RecordedManifest.Entries)
        {
            ids.Add(entry.Id);
        }

        return ids;
    }

    /// <summary>
    /// Creates a session that reads only from the replay handler and never waits between requests.
    /// </summary>
    private static ArchidektSession CreateSession(ReplayHttpHandler handler)
    {
        ArchidektOptions options = ArchidektOptions.CreateDefault(null, null) with
        {
            BaseAddress = new Uri("https://archidekt.com/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
        return new ArchidektSession(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            ownsHttpClient: true,
            options);
    }
}
