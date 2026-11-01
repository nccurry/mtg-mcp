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
            foreach (JsonElement result in root.GetProperty("results").EnumerateArray())
            {
                ArchidektCardPrinting printing = Assert.IsType<ArchidektCardPrinting>(ArchidektJsonContract.ReadPrinting(result));
                AssertPrintingMatchesRaw(result, printing.SetCode, printing.CollectorNumber);
            }

            return;
        }

        switch (entry.Route)
        {
            case "/api/decks/v3/":
                AssertDeckList(root, entry);
                return;
            case "/api/decks/folderTree/":
                AssertFolderTree(root, entry);
                return;
            case "/api/decks/folders/{folderId}/":
                AssertFolderDetail(root, entry);
                return;
            case "/api/decks/{deckId}/snapshots/":
                AssertSnapshotList(root, entry);
                return;
            case "/api/decks/snapshots/{snapshotId}/":
                RemoteNamedSnapshot snapshot = ArchidektSnapshotContractMapper.MapSnapshot(
                    root, "22959335", entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route);
                Assert.Equal(root.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.Summary.SnapshotId);
                AssertDeckCards(root, snapshot.Deck);
                return;
        }

        AssertDeckCards(root, ArchidektDeckContractMapper.MapDeck(root, entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route));
        Assert.Equal("22959335", ArchidektDeckContractMapper.MapDeck(root, entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route).RemoteId);
    }

    /// <summary>
    /// Verifies the deck list maps every recorded row and keeps the full count the recording trimmed away.
    /// </summary>
    private static void AssertDeckList(JsonElement root, RecordedEntry entry)
    {
        RemoteDeckPage page = ArchidektDeckContractMapper.MapDeckPage(root, entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route);

        JsonElement results = root.GetProperty("results");
        Assert.Equal(results.GetArrayLength(), page.Items.Count);
        Assert.True(root.GetProperty("count").GetInt32() > page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal("0", item.ParentFolderId));
        Assert.Equal(
            results.EnumerateArray().Select(row => row.GetProperty("name").GetString()).Order(StringComparer.OrdinalIgnoreCase),
            page.Items.Select(item => item.Name));
    }

    /// <summary>
    /// Verifies the folder tree maps the root, nested folders, and the parent link of each child.
    /// </summary>
    private static void AssertFolderTree(JsonElement root, RecordedEntry entry)
    {
        RemoteFolderTree tree = ArchidektFolderContractMapper.MapFolderTree(root, entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route);

        Assert.Equal(CountNodes(root), tree.Items.Count);
        RemoteFolderRecord rootFolder = tree.Items.Single(folder => folder.ParentFolderId is null);
        Assert.Equal(root.GetProperty("children").GetArrayLength(), rootFolder.ChildFolderIds.Count);
        Assert.Contains(tree.Items, folder => folder.ParentFolderId is not null && folder.ChildFolderIds.Count > 0);
    }

    /// <summary>
    /// Verifies a folder read maps its folder, its trimmed deck rows, and the subfolder it names.
    /// </summary>
    private static void AssertFolderDetail(JsonElement root, RecordedEntry entry)
    {
        RemoteFolderTree detail = ArchidektFolderContractMapper.MapFolderDetail(root, entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route);

        RemoteFolderRecord folder = detail.Items.Single(value => value.FolderId == root.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(root.GetProperty("decks").GetArrayLength(), folder.Decks.Count);
        Assert.Equal(root.GetProperty("subfolders").GetArrayLength(), folder.ChildFolderIds.Count);
    }

    /// <summary>
    /// Verifies the snapshot list maps every row to the deck it was requested for.
    /// </summary>
    private static void AssertSnapshotList(JsonElement root, RecordedEntry entry)
    {
        RemoteNamedSnapshotPage page = ArchidektSnapshotContractMapper.MapSnapshotPage(root, "22959335", entry.ReadText(), DateTimeOffset.UnixEpoch, entry.Route);

        Assert.Equal(root.GetArrayLength(), page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal("22959335", item.DeckId));
        Assert.All(page.Items, item => Assert.NotNull(item.CreatedAtUtc));
    }

    /// <summary>
    /// Counts a folder and every folder below it.
    /// </summary>
    private static int CountNodes(JsonElement folder)
    {
        int count = 1;
        if (folder.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
        {
            count += children.EnumerateArray().Sum(CountNodes);
        }

        return count;
    }

    /// <summary>
    /// Verifies each raw card of a deck or snapshot maps to entries with the printing the raw card names.
    /// </summary>
    private static void AssertDeckCards(JsonElement root, RemoteDeckSnapshot deck)
    {
        foreach (JsonElement card in root.GetProperty("cards").EnumerateArray())
        {
            JsonElement raw = card.GetProperty("card");
            string cardId = raw.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
            RemoteDeckEntry[] mapped = [.. deck.Entries.Where(value => value.ProviderCardId == cardId)];
            Assert.NotEmpty(mapped);
            Assert.All(mapped, value => AssertPrintingMatchesRaw(raw, value.SetCode, value.CollectorNumber));
        }
    }

    /// <summary>
    /// Verifies no recording holds an owner name or avatar, and that a recorded deck has its owner id and folder id
    /// zeroed and its deck text emptied.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Recording_Body_HoldsNoOwnerIdentity(string id)
    {
        using JsonDocument document = JsonDocument.Parse(RecordedManifest.Get(id).ReadText());
        JsonElement root = document.RootElement;

        Assert.Empty(PropertyNamesAnywhere(root).Where(name => name is "username" or "avatar" or "avatarUrl" or "email" or "token" or "password" or "frame"));
        foreach ((string name, JsonElement value) in PropertiesAnywhere(root))
        {
            if (name == "owner")
            {
                Assert.Equal(0, value.GetProperty("id").GetInt32());
            }

            if (name == "parentFolderId")
            {
                Assert.Equal(0, value.GetInt32());
            }

            if (name == "parentFolder" && value.ValueKind == JsonValueKind.Object)
            {
                Assert.Equal(0, value.GetProperty("id").GetInt32());
            }

            if (name == "parentFolder" && value.ValueKind == JsonValueKind.Number)
            {
                Assert.Equal(0, value.GetInt32());
            }

            if (name == "description" && value.ValueKind == JsonValueKind.String)
            {
                Assert.Equal(string.Empty, value.GetString());
            }
        }

        Assert.DoesNotMatch("(?i)(jwt|bearer) [a-z0-9._-]{16,}", RecordedManifest.Get(id).ReadText());
    }

    /// <summary>
    /// Verifies every recorded Archidekt deck, list, folder, and snapshot file stays under 50 KB.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Recording_File_IsUnderFiftyKilobytes(string id)
    {
        Assert.InRange(RecordedManifest.Get(id).ReadBytes().Length, 1, 50 * 1024 - 1);
    }

    /// <summary>
    /// Verifies the manifest lists a login-only recording with the auth flag and no owner name in its URL.
    /// </summary>
    [Fact]
    public void Manifest_LoginOnlyRecordings_AreFlaggedAndKeepTheUsernamePlaceholder()
    {
        string[] ids = ["deck-list", "folder-tree", "folder-401005", "snapshot-list-22959335", "snapshot-152482"];

        Assert.All(ids, id => Assert.True(RecordedManifest.Get(id).Auth));
        Assert.Contains("{username}", RecordedManifest.Get("deck-list").Url, StringComparison.Ordinal);
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
    /// Verifies the recorded deck maps its commander, cards in two categories, and an excluded primary category out of
    /// main while an excluded secondary category does not.
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
    /// Asserts a mapped set code and collector number equal the ones in the raw printing JSON.
    /// </summary>
    private static void AssertPrintingMatchesRaw(JsonElement raw, string? setCode, string? collectorNumber)
    {
        Assert.Equal(raw.GetProperty("edition").GetProperty("editioncode").GetString(), setCode);
        Assert.Equal(raw.GetProperty("collectorNumber").GetString(), collectorNumber);
    }

    /// <summary>
    /// Lists every property name at any depth of a JSON value.
    /// </summary>
    private static IEnumerable<string> PropertyNamesAnywhere(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (string name in PropertyNamesAnywhere(property.Value))
                {
                    yield return name;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach (string name in PropertyNamesAnywhere(item))
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>
    /// Lists every property name and value at any depth of a JSON value.
    /// </summary>
    private static IEnumerable<(string Name, JsonElement Value)> PropertiesAnywhere(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                yield return (property.Name, property.Value);
                foreach ((string Name, JsonElement Value) inner in PropertiesAnywhere(property.Value))
                {
                    yield return inner;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach ((string Name, JsonElement Value) inner in PropertiesAnywhere(item))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>
    /// Finds one mapped entry by card name.
    /// </summary>
    private static RemoteDeckEntry Entry(RemoteDeckSnapshot deck, string cardName)
    {
        return deck.Entries.Single(entry => entry.CardName == cardName);
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
