using MtgMcp.Core.Tests.Recorded;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the fake Archidekt to the recorded real responses: for a card search with many printings, a deck read, the
/// deck list, the folder tree and folder reads, and the named snapshot list and read it must keep every recorded key path with a value kind the recording shows and every array-size bucket the recording
/// has, for every array. A recorded field the fake leaves out must be listed with a reason, and each listed path must
/// exist in the recording.
/// </summary>
public sealed class ArchidektFakeContractTests
{
    /// <summary>
    /// Holds the card printing fields the adapter never reads; it takes only the card id, printing id, set code,
    /// collector number, and Oracle name and id.
    /// </summary>
    private static readonly string[] PrintingIgnored =
    [
        "artist", "cardTraderSku", "ckFoilId", "ckNormalId", "cmEd", "contentWarning", "displayName", "flavor",
        "games", "globalCategories", "mtgoFoilId", "mtgoNormalId", "multiverseid", "options", "owned",
        "pinnedStatus", "prices", "rarity", "releasedAt", "scgFoilSku", "scgSku", "scryfallImageHash",
        "tcgProductId", "edition.editiondate", "edition.editionname", "edition.editiontype", "edition.mtgoCode",
        "oracleCard.atomicCombos", "oracleCard.canlanderPoints", "oracleCard.cmc", "oracleCard.colorIdentity",
        "oracleCard.colors", "oracleCard.defaultCategory", "oracleCard.edhrecRank", "oracleCard.extraTurns",
        "oracleCard.faces", "oracleCard.gameChanger", "oracleCard.id", "oracleCard.inheritedTags",
        "oracleCard.isPDHCommander", "oracleCard.keywords", "oracleCard.lang", "oracleCard.layout",
        "oracleCard.legalities", "oracleCard.loyalty", "oracleCard.manaCost", "oracleCard.manaProduction",
        "oracleCard.massLandDenial", "oracleCard.oTags", "oracleCard.potentialCombos", "oracleCard.power",
        "oracleCard.salt", "oracleCard.subTypes", "oracleCard.superTypes", "oracleCard.text", "oracleCard.tokens",
        "oracleCard.toughness", "oracleCard.tutor", "oracleCard.twoCardComboIds",
        "oracleCard.twoCardComboSingelton", "oracleCard.types",
    ];

    /// <summary>
    /// Holds the deck fields the adapter keeps only as snapshot extensions; the fake omits them on purpose.
    /// </summary>
    private static readonly string[] DeckIgnored =
    [
        "$.bookmarked", "$.cardPackage", "$.createdAt", "$.customCards", "$.customFeatured", "$.deckHelp",
        "$.deckTags", "$.edhBracket", "$.editors", "$.featured", "$.game", "$.hasPrimer",
        "$.intentionallySkippedCardData", "$.owner", "$.playgroupDeckUrl", "$.points", "$.theorycrafted",
        "$.userInput", "$.cards[].companion", "$.cards[].createdAt", "$.cards[].customCmc", "$.cards[].deletedAt",
        "$.cards[].flippedDefault", "$.cards[].label", "$.cards[].notes", "$.cards[].removedCategories",
        "$.cards[].updatedAt",
    ];

    /// <summary>
    /// Holds the card fields a named snapshot keeps only as extensions or never reads; the fake omits them on purpose.
    /// </summary>
    private static readonly string[] SnapshotCardIgnored =
    [
        "$.cards[].companion", "$.cards[].createdAt", "$.cards[].customCmc", "$.cards[].deletedAt",
        "$.cards[].flippedDefault", "$.cards[].label", "$.cards[].removedCategories", "$.cards[].updatedAt",
    ];

    /// <summary>
    /// Verifies the first fake search page for a card with many printings has the shape of the recorded first page.
    /// </summary>
    [Fact]
    public async Task SearchPage_FirstFakePageOfManyPrintings_MatchesTheRecordedFirstPage()
    {
        string actual = await GetAsync(SolRingFake(), "api/cards/v2/?name=Sol%20Ring&exact=true&pageSize=25");

        Assert.Empty(JsonShape.Divergences(Recorded("search-sol-ring-page-1"), actual, PrintingIgnoredUnder("$.results[]")));
    }

    /// <summary>
    /// Verifies the fake's second search page has the shape of the recorded second page, which has a previous link.
    /// </summary>
    [Fact]
    public async Task SearchPage_SecondFakePageOfManyPrintings_MatchesTheRecordedSecondPage()
    {
        string actual = await GetAsync(SolRingFake(), "api/cards/v2/?name=Sol%20Ring&exact=true&page=2&pageSize=25");

        Assert.Empty(JsonShape.Divergences(Recorded("search-sol-ring-page-2"), actual, PrintingIgnoredUnder("$.results[]")));
    }

    /// <summary>
    /// Verifies the fake deck read has the shape of the recorded deck: its category flags, its entries with one and
    /// with several categories, and its printing identity.
    /// </summary>
    [Fact]
    public async Task DeckRead_FakeDeckWithCategoriesAndCards_MatchesTheRecordedDeck()
    {
        string actual = await GetAsync(WithCards(), "api/decks/42/");

        Assert.Empty(JsonShape.Divergences(Recorded("deck-22959335"), actual, [.. DeckIgnored, .. PrintingIgnoredUnder("$.cards[].card")]));
    }

    /// <summary>
    /// Holds the deck summary fields whose value kind or array size changes from deck to deck (bracket and game are a
    /// number or null; a deck has zero to several tags); the fake always sends null or an empty array, and the
    /// adapter reads none of them.
    /// </summary>
    private static readonly string[] RowIgnored = ["edhBracket", "game", "tags"];

    /// <summary>
    /// Verifies the fake deck list has the shape of the recorded list: a count, a next link, and full summary rows.
    /// </summary>
    [Fact]
    public async Task DeckList_FakeListOfManyDecks_MatchesTheRecordedList()
    {
        string actual = await GetAsync(new FakeArchidekt(), "api/decks/v3/?ownerUsername=owner");

        Assert.Empty(JsonShape.Divergences(Recorded("deck-list"), actual, RowIgnoredUnder("$.results[]")));
    }

    /// <summary>
    /// Verifies the fake folder tree has the shape of the recorded tree: a root with nested children, where a leaf has
    /// null children.
    /// </summary>
    [Fact]
    public async Task FolderTree_FakeTreeWithNestedFolders_MatchesTheRecordedTree()
    {
        string actual = await GetAsync(new FakeArchidekt(), "api/decks/folderTree/");

        Assert.Empty(JsonShape.Divergences(Recorded("folder-tree"), actual, []));
    }

    /// <summary>
    /// Verifies the fake folder read has the shape of the recorded nested folder, whose parent is an object and
    /// whose deck rows are full summaries. The recording has one subfolder, a bucket the other folder recording
    /// covers, so its subfolder paths are skipped here.
    /// </summary>
    [Fact]
    public async Task FolderRead_FakeFolderWithParentAndDecks_MatchesTheRecordedNestedFolder()
    {
        string actual = await GetAsync(new FakeArchidekt(), "api/decks/folders/9/");

        Assert.Empty(JsonShape.Divergences(Recorded("folder-401005"), actual, ["$.subfolders", .. RowIgnoredUnder("$.decks[]")]));
    }

    /// <summary>
    /// Verifies the fake folder read lists many subfolders as the recorded root folder does. That recording has no
    /// parent and no decks, which the nested folder recording covers, so those paths are skipped here.
    /// </summary>
    [Fact]
    public async Task FolderRead_FakeFolderWithManySubfolders_MatchesTheRecordedRootFolder()
    {
        string actual = await GetAsync(new FakeArchidekt(), "api/decks/folders/9/");

        Assert.Empty(JsonShape.Divergences(Recorded("folder-397567"), actual, ["$.parentFolder", "$.decks"]));
    }

    /// <summary>
    /// Verifies the fake snapshot list is a bare array of rows with the recorded fields.
    /// </summary>
    [Fact]
    public async Task SnapshotList_FakeListOfManySnapshots_MatchesTheRecordedList()
    {
        FakeArchidekt fake = new();

        string actual = await GetAsync(fake, "api/decks/42/snapshots/");

        Assert.Empty(JsonShape.Divergences(Recorded("snapshot-list-22959335"), actual, []));
    }

    /// <summary>
    /// Verifies the fake snapshot read is the saved deck flattened beside the snapshot's own fields, with the parent
    /// deck named in its metadata, as the recorded snapshot is.
    /// </summary>
    [Fact]
    public async Task SnapshotRead_FakeSnapshotOfDeckWithCards_MatchesTheRecordedSnapshot()
    {
        FakeArchidekt fake = WithCards();

        string actual = await GetAsync(fake, "api/decks/snapshots/77/");

        Assert.Empty(JsonShape.Divergences(Recorded("snapshot-152482"), actual, [.. SnapshotCardIgnored, .. PrintingIgnoredUnder("$.cards[].card")]));
    }

    /// <summary>
    /// Builds a fake whose deck has categories and cards with one and with several categories.
    /// </summary>
    private static FakeArchidekt WithCards()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Ramp");
        fake.AddCategory("Maybe", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(1, "Sol Ring", "c21", "263"), 1, "Ramp");
        fake.AddEntry(fake.AddCardToCatalog(2, "Command Tower", "c21", "263"), 1, "Ramp", "Maybe");
        fake.AddEntry(fake.AddCardToCatalog(3, "Ghostly Prison", "ima", "27"), 1, "Maybe");
        return fake;
    }

    /// <summary>
    /// Reads one recorded response as text.    /// </summary>
    private static string Recorded(string id)
    {
        return RecordedManifest.Get(id).ReadText();
    }

    /// <summary>
    /// Builds a fake that knows 60 printings of Sol Ring, so two full search pages and a next link exist.
    /// </summary>
    private static FakeArchidekt SolRingFake()
    {
        FakeArchidekt fake = new();
        for (int number = 1; number <= 60; number++)
        {
            _ = fake.AddCardToCatalog(1000 + number, "Sol Ring", "c21", number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return fake;
    }

    /// <summary>
    /// Sends one GET to the fake and returns the response body.
    /// </summary>
    private static async Task<string> GetAsync(FakeArchidekt fake, string path)
    {
        using HttpClient client = new(fake) { BaseAddress = new Uri("https://archidekt.com/") };

        return await client.GetStringAsync(path, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Lists the deck summary fields the fake skips as paths under one row object.
    /// </summary>
    private static string[] RowIgnoredUnder(string parent)
    {
        return [.. RowIgnored.Select(name => $"{parent}.{name}")];
    }

    /// <summary>
    /// Lists the printing fields the adapter ignores as paths under one card object.
    /// </summary>
    private static string[] PrintingIgnoredUnder(string parent)
    {
        return [.. PrintingIgnored.Select(name => $"{parent}.{name}")];
    }
}
