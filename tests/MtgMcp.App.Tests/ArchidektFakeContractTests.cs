using MtgMcp.Core.Tests.Recorded;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the fake Archidekt to the recorded real responses: for a card search with many printings and a deck read it
/// must keep every recorded key path with a value kind the recording shows, the same array-size bucket for the
/// result and card lists, and the paging keys. A recorded field the fake leaves out must be listed with a reason.
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
    /// Holds the deck fields the adapter keeps only as unreviewed extensions or never reads.
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
    /// Holds the paths whose array size bucket must match the recording exactly.
    /// </summary>
    private static readonly string[] BucketedArrays = ["$.results", "$.cards", "$.categories"];

    /// <summary>
    /// Verifies the first fake search page for a card with many printings has the shape of the recorded first page.
    /// </summary>
    [Fact]
    public async Task SearchPage_FirstFakePageOfManyPrintings_MatchesTheRecordedFirstPage()
    {
        string actual = await GetAsync(SolRingFake(), "api/cards/v2/?name=Sol%20Ring&exact=true&pageSize=25");

        Assert.Empty(Divergences("search-sol-ring-page-1", actual, PrintingIgnoredUnder("$.results[]")));
    }

    /// <summary>
    /// Verifies the fake's second search page has the shape of the recorded second page, which has a previous link.
    /// </summary>
    [Fact]
    public async Task SearchPage_SecondFakePageOfManyPrintings_MatchesTheRecordedSecondPage()
    {
        string actual = await GetAsync(SolRingFake(), "api/cards/v2/?name=Sol%20Ring&exact=true&page=2&pageSize=25");

        Assert.Empty(Divergences("search-sol-ring-page-2", actual, PrintingIgnoredUnder("$.results[]")));
    }

    /// <summary>
    /// Verifies the fake deck read has the shape of the recorded deck: its category flags, its entries with one and
    /// with several categories, and its printing identity.
    /// </summary>
    [Fact]
    public async Task DeckRead_FakeDeckWithCategoriesAndCards_MatchesTheRecordedDeck()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Ramp");
        fake.AddCategory("Maybe", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(1, "Sol Ring", "c21", "263"), 1, "Ramp");
        fake.AddEntry(fake.AddCardToCatalog(2, "Command Tower", "c21", "263"), 1, "Ramp", "Maybe");
        fake.AddEntry(fake.AddCardToCatalog(3, "Ghostly Prison", "ima", "27"), 1, "Maybe");

        string actual = await GetAsync(fake, "api/decks/42/");

        Assert.Empty(Divergences("deck-22959335", actual, [.. DeckIgnored, .. PrintingIgnoredUnder("$.cards[].card")]));
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
    /// Lists the printing fields the adapter ignores as paths under one card object.
    /// </summary>
    private static string[] PrintingIgnoredUnder(string parent)
    {
        return [.. PrintingIgnored.Select(name => $"{parent}.{name}")];
    }

    /// <summary>
    /// Lists how the actual JSON departs from a recording: a recorded path the actual JSON lacks, a value kind the
    /// recording does not show for a path, or an array size bucket that differs. Paths under an ignored prefix are
    /// skipped.
    /// </summary>
    private static List<string> Divergences(string recordingId, string actualJson, string[] ignored)
    {
        Dictionary<string, HashSet<string>> recorded = KindsByPath(JsonShape.Of(RecordedManifest.Get(recordingId).ReadText()));
        Dictionary<string, HashSet<string>> actual = KindsByPath(JsonShape.Of(actualJson));
        List<string> problems = [];
        foreach ((string path, HashSet<string> kinds) in recorded)
        {
            if (ignored.Any(prefix => path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal) ||
                path.StartsWith(prefix + "[]", StringComparison.Ordinal)))
            {
                continue;
            }

            if (!actual.TryGetValue(path, out HashSet<string>? seen))
            {
                problems.Add($"{path} is missing; recorded {string.Join("|", kinds)}");
                continue;
            }

            bool bucketed = BucketedArrays.Contains(path);
            foreach (string kind in seen)
            {
                if (!kinds.Contains(kind) && (bucketed || !kind.StartsWith("array", StringComparison.Ordinal) ||
                    !kinds.Any(value => value.StartsWith("array", StringComparison.Ordinal))))
                {
                    problems.Add($"{path} is {kind}; recorded {string.Join("|", kinds)}");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Groups shape pairs by path into the value kinds seen there; an array kind carries its size bucket.
    /// </summary>
    private static Dictionary<string, HashSet<string>> KindsByPath(IEnumerable<string> pairs)
    {
        Dictionary<string, HashSet<string>> kinds = new(StringComparer.Ordinal);
        foreach (string pair in pairs)
        {
            int split = pair.IndexOf(':', StringComparison.Ordinal);
            string path = pair[..split];
            if (!kinds.TryGetValue(path, out HashSet<string>? set))
            {
                set = [];
                kinds[path] = set;
            }

            set.Add(pair[(split + 1)..]);
        }

        return kinds;
    }
}
