using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Proves an entry has one comparison form, used by the difference rows, the fingerprints, and the push plan: changes
/// Archidekt makes without a user edit show nothing, and a real change still shows.
/// </summary>
public sealed class ArchidektEntryComparisonTests
{
    /// <summary>
    /// Holds a deck with several categories, entries in more than one category, and order numbers.
    /// </summary>
    private const string BaseDeck = """
        {
          "id": 42, "name": "Compare", "description": "", "deckFormat": 3, "private": true,
          "categories": [
            { "id": 10, "name": "Removal", "includedInDeck": true, "includedInPrice": true, "sortOrder": 0 },
            { "id": 11, "name": "Ramp", "includedInDeck": true, "includedInPrice": true, "sortOrder": 1 },
            { "id": 12, "name": "Sideboard", "includedInDeck": false, "includedInPrice": true, "sortOrder": 2 },
            { "id": 13, "name": "Tokens", "includedInDeck": true, "includedInPrice": true, "sortOrder": 3 }
          ],
          "cards": [
            { "deckRelationId": 101, "quantity": 1, "order": 0, "categories": ["Removal", "Ramp", "Tokens"],
              "card": { "id": 501, "uid": "33333333-3333-3333-3333-333333333301", "edition": { "editioncode": "dmu" },
                "collectorNumber": "1", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444401", "name": "Bolt" } } },
            { "deckRelationId": 102, "quantity": 2, "order": 1, "categories": ["Ramp", "Sideboard", "Removal"],
              "card": { "id": 502, "uid": "33333333-3333-3333-3333-333333333302", "edition": { "editioncode": "dmu" },
                "collectorNumber": "2", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444402", "name": "Anger" } } },
            { "deckRelationId": 103, "quantity": 1, "order": 2, "categories": ["Tokens"],
              "card": { "id": 503, "uid": "33333333-3333-3333-3333-333333333303", "edition": { "editioncode": "dmu" },
                "collectorNumber": "3", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444403", "name": "Charm" } } },
            { "deckRelationId": 104, "quantity": 1, "order": 3, "categories": ["Removal", "Tokens", "Ramp"],
              "card": { "id": 504, "uid": "33333333-3333-3333-3333-333333333304", "edition": { "editioncode": "dmu" },
                "collectorNumber": "4", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444404", "name": "Dart" } } }
          ]
        }
        """;

    /// <summary>
    /// Verifies reordering category names, renumbering order numbers, recasing names, and reordering the lists leave
    /// the fingerprints equal, plan no operation either way, and show no difference row, for many seeded variants.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Variants_ThatAreOnlyOrderOrCase_ShowNothing(int seed)
    {
        RemoteDeckSnapshot original = Parse(BaseDeck);
        RemoteDeckSnapshot variant = Parse(Variant(BaseDeck, seed));

        Assert.Equal(original.ContentFingerprint, variant.ContentFingerprint);
        Assert.Equal(original.RemoteFingerprint, variant.RemoteFingerprint);
        Assert.Empty(ArchidektSyncPlanner.PlanRemoteApply(original, variant).PlannedOperations);
        Assert.Empty(ArchidektSyncPlanner.PlanRemoteApply(variant, original).PlannedOperations);
        Assert.Empty(ArchidektSyncPlanner.PlanRemoteVerification(variant, original).PlannedOperations);
        Assert.Empty(Diff(original, original, variant).Differences);
        Assert.False(Diff(original, original, variant).HasConflicts);
    }

    /// <summary>
    /// Verifies a real change still shows in the rows and the plan, even when the deck is also reordered.
    /// </summary>
    [Theory]
    [InlineData("addCategory")]
    [InlineData("changePrimary")]
    [InlineData("quantity")]
    public void Variants_WithARealChange_StillShowIt(string change)
    {
        RemoteDeckSnapshot original = Parse(BaseDeck);
        JsonNode changed = JsonNode.Parse(Variant(BaseDeck, seed: 7))!;
        JsonObject first = changed["cards"]!.AsArray()
            .Select(value => value!.AsObject())
            .Single(value => value["deckRelationId"]!.GetValue<int>() == 103);
        switch (change)
        {
            case "addCategory":
                first["categories"]!.AsArray().Add("Ramp");
                break;
            case "changePrimary":
                first["categories"] = new JsonArray("Removal");
                break;
            default:
                first["quantity"] = 5;
                break;
        }

        RemoteDeckSnapshot remote = Parse(changed.ToJsonString());

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(remote, original);
        Assert.Equal(["entry-update:Charm"], plan.PlannedOperations.Select(value => $"{value.Public.Kind}:{value.Public.Subject}"));
        ArchidektSyncDiff diff = Diff(original, original, remote);
        ArchidektDifference row = Assert.Single(diff.Differences);
        Assert.Equal("remote-changed", row.State);
        Assert.Equal("/entries/relation:103", row.Path);
    }

    /// <summary>
    /// Verifies renumbered category ranks plan no operation and change no entry row; the deck still reads as changed on
    /// Archidekt, as a reordering of its categories.
    /// </summary>
    [Fact]
    public void CategoryRenumbering_PlansNothing_AndChangesNoEntryRow()
    {
        RemoteDeckSnapshot original = Parse(BaseDeck);
        RemoteDeckSnapshot renumbered = Parse(BaseDeck
            .Replace("\"sortOrder\": 0", "\"sortOrder\": 9", StringComparison.Ordinal));

        Assert.Empty(ArchidektSyncPlanner.PlanRemoteApply(renumbered, original).PlannedOperations);
        Assert.DoesNotContain(
            Diff(original, original, renumbered).Differences,
            value => value.Path.StartsWith("/entries", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies the fingerprints still tell two different decks apart.
    /// </summary>
    [Fact]
    public void Fingerprints_OfDifferentDecks_Differ()
    {
        RemoteDeckSnapshot original = Parse(BaseDeck);
        RemoteDeckSnapshot renamed = Parse(BaseDeck.Replace("\"Charm\"", "\"Charms\"", StringComparison.Ordinal));
        RemoteDeckSnapshot regrouped = Parse(BaseDeck.Replace(
            "\"categories\": [\"Tokens\"]",
            "\"categories\": [\"Ramp\"]",
            StringComparison.Ordinal));

        Assert.NotEqual(original.ContentFingerprint, renamed.ContentFingerprint);
        Assert.NotEqual(original.ContentFingerprint, regrouped.ContentFingerprint);
    }

    /// <summary>
    /// Verifies the comparison form keeps the primary category first and sorts the rest.
    /// </summary>
    [Fact]
    public void Canonical_KeepsPrimaryFirst_SortsTheRest_ClearsOrder()
    {
        RemoteDeckEntry entry = Parse(BaseDeck).Entries.Single(value => value.ProviderRelationId == "101");

        RemoteDeckEntry canonical = ArchidektEntryComparison.Canonical(entry with { SortOrder = 9 });

        Assert.Equal(["Removal", "Ramp", "Tokens"], canonical.CategoryNames);
        Assert.Equal(0, canonical.SortOrder);
        Assert.True(ArchidektEntryComparison.SameCategories(
            entry,
            entry with { CategoryNames = ["Removal", "Tokens", "Ramp"] },
            ignoreCase: false));
        Assert.False(ArchidektEntryComparison.SameCategories(
            entry,
            entry with { CategoryNames = ["Ramp", "Removal", "Tokens"], PrimaryCategoryName = "Ramp" },
            ignoreCase: true));
    }

    /// <summary>
    /// Gives each seed to the variant test.
    /// </summary>
    public static TheoryData<int> Seeds()
    {
        TheoryData<int> seeds = [];
        for (int seed = 1; seed <= 40; seed++)
        {
            seeds.Add(seed);
        }

        return seeds;
    }

    /// <summary>
    /// Rewrites a deck payload the way Archidekt may: other category names in another order and case, new order
    /// numbers for entries, and the card and category lists in another order. The primary (first)
    /// category of each entry stays first.
    /// </summary>
    private static string Variant(string json, int seed)
    {
        Random random = new(seed);
        JsonNode root = JsonNode.Parse(json)!;
        JsonArray categories = root["categories"]!.AsArray();
        JsonArray cards = root["cards"]!.AsArray();
        int[] cardOrder = Permutation(random, cards.Count);
        for (int index = 0; index < cards.Count; index++)
        {
            JsonObject card = cards[index]!.AsObject();
            card["order"] = cardOrder[index];
            string[] entryNames = [.. card["categories"]!.AsArray().Select(value => value!.GetValue<string>())];
            string[] rest = [.. entryNames.Skip(1).OrderBy(_ => random.Next())];
            JsonArray recast = [entryNames[0]];
            foreach (string name in rest)
            {
                recast.Add(random.Next(2) == 0 ? name.ToLowerInvariant() : name.ToUpperInvariant());
            }

            card["categories"] = recast;
        }

        Shuffle(random, categories);
        Shuffle(random, cards);
        return root.ToJsonString();
    }

    /// <summary>
    /// Returns the numbers 0 to count minus one in a seeded random order.
    /// </summary>
    private static int[] Permutation(Random random, int count)
    {
        return [.. Enumerable.Range(0, count).OrderBy(_ => random.Next())];
    }

    /// <summary>
    /// Reorders a JSON array in place, in a seeded random order.
    /// </summary>
    private static void Shuffle(Random random, JsonArray array)
    {
        JsonNode[] items = [.. array.Select(value => value!)];
        array.Clear();
        foreach (JsonNode item in items.OrderBy(_ => random.Next()))
        {
            array.Add(item.DeepClone());
        }
    }

    /// <summary>
    /// Parses one provider deck payload.
    /// </summary>
    private static RemoteDeckSnapshot Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            json,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            "GET");
    }

    /// <summary>
    /// Compares a local deck equal to the baseline with a fresh remote deck.
    /// </summary>
    private static ArchidektSyncDiff Diff(
        RemoteDeckSnapshot baselineSnapshot,
        RemoteDeckSnapshot local,
        RemoteDeckSnapshot remote)
    {
        ArchidektSyncBaseline baseline = new(
            2,
            baselineSnapshot.RemoteId,
            baselineSnapshot.RemoteFingerprint,
            "local-fingerprint",
            baselineSnapshot,
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            "invisible-fingerprint");
        return ArchidektSyncPlanner.Diff(
            Guid.NewGuid(),
            1,
            "local-fingerprint",
            local,
            remote,
            baseline,
            "invisible-fingerprint");
    }
}
