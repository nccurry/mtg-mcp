using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Checks the field-level form of a difference row, built from the same rows as the full form: only the fields whose
/// values differ are listed, with the value each side holds.
/// </summary>
public sealed class ArchidektConciseDifferenceTests
{
    /// <summary>
    /// Holds a deck with three categories and three entries, one in the Sideboard category.
    /// </summary>
    private const string BaseDeck = """
        {
          "id": 42, "name": "Compare", "description": "", "deckFormat": 3, "private": true,
          "categories": [
            { "id": 10, "name": "Removal", "includedInDeck": true, "includedInPrice": true, "sortOrder": 0 },
            { "id": 11, "name": "Ramp", "includedInDeck": true, "includedInPrice": true, "sortOrder": 1 },
            { "id": 12, "name": "Sideboard", "includedInDeck": false, "includedInPrice": true, "sortOrder": 2 }
          ],
          "cards": [
            { "deckRelationId": 101, "quantity": 1, "order": 0, "categories": ["Removal", "Ramp"],
              "card": { "id": 501, "uid": "33333333-3333-3333-3333-333333333301", "edition": { "editioncode": "dmu" },
                "collectorNumber": "1", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444401", "name": "Bolt" } } },
            { "deckRelationId": 102, "quantity": 2, "order": 1, "categories": ["Ramp"],
              "card": { "id": 502, "uid": "33333333-3333-3333-3333-333333333302", "edition": { "editioncode": "dmu" },
                "collectorNumber": "2", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444402", "name": "Anger" } } },
            { "deckRelationId": 103, "quantity": 1, "order": 2, "categories": ["Removal"],
              "card": { "id": 503, "uid": "33333333-3333-3333-3333-333333333303", "edition": { "editioncode": "dmu" },
                "collectorNumber": "3", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444403", "name": "Charm" } } }
          ]
        }
        """;

    /// <summary>
    /// Verifies an entry only Archidekt added lists every field of the side that holds it, and no value for the others.
    /// </summary>
    [Fact]
    public void AddedEntry_ListsEveryFieldOfTheSideThatHoldsIt()
    {
        RemoteDeckSnapshot baseline = Parse(BaseDeck);
        JsonNode added = JsonNode.Parse(BaseDeck)!;
        added["cards"]!.AsArray().Add(JsonNode.Parse("""
            { "deckRelationId": 104, "quantity": 3, "order": 3, "categories": ["Ramp"],
              "card": { "id": 504, "uid": "33333333-3333-3333-3333-333333333304", "edition": { "editioncode": "dmu" },
                "collectorNumber": "4", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444404", "name": "Dart" } } }
            """));

        ArchidektDifference row = Assert.Single(
            Diff(baseline, baseline, Parse(added.ToJsonString()), localChanged: false).Differences);
        ArchidektConciseDifference concise = ArchidektConciseDifferences.From(row);

        Assert.Equal("remote-added", concise.State);
        Assert.Equal(row.Path, concise.Path);
        Assert.All(concise.Fields, field =>
        {
            Assert.Null(field.Baseline);
            Assert.Null(field.Local);
            Assert.NotNull(field.Remote);
        });
        Assert.Equal("Dart", Field(concise, "cardName").Remote!.Value.GetString());
        Assert.Equal(3, Field(concise, "quantity").Remote!.Value.GetInt32());
        Assert.Equal("main", Field(concise, "zone").Remote!.Value.GetString());
    }

    /// <summary>
    /// Verifies an entry only Archidekt removed lists the fields of the baseline and the local side, which still hold
    /// it, and no remote value.
    /// </summary>
    [Fact]
    public void RemovedEntry_ListsTheFieldsOfTheSidesThatHoldIt()
    {
        RemoteDeckSnapshot baseline = Parse(BaseDeck);
        JsonNode removed = JsonNode.Parse(BaseDeck)!;
        removed["cards"]!.AsArray().RemoveAt(1);

        ArchidektDifference row = Assert.Single(
            Diff(baseline, baseline, Parse(removed.ToJsonString()), localChanged: false).Differences);
        ArchidektConciseDifference concise = ArchidektConciseDifferences.From(row);

        Assert.Equal("remote-removed", concise.State);
        Assert.All(concise.Fields, field =>
        {
            Assert.NotNull(field.Baseline);
            Assert.NotNull(field.Local);
            Assert.Null(field.Remote);
        });
        Assert.Equal("Anger", Field(concise, "cardName").Baseline!.Value.GetString());
        Assert.Equal(2, Field(concise, "quantity").Local!.Value.GetInt32());
    }

    /// <summary>
    /// Verifies a change of zone and categories lists the fields that changed and leaves out the card name, quantity,
    /// and every other field that did not.
    /// </summary>
    [Fact]
    public void ZoneAndCategoryChange_ListsOnlyTheFieldsThatChanged()
    {
        RemoteDeckSnapshot baseline = Parse(BaseDeck);
        JsonNode moved = JsonNode.Parse(BaseDeck)!;
        moved["cards"]!.AsArray()[2]!["categories"] = new JsonArray("Sideboard");

        ArchidektDifference row = Assert.Single(
            Diff(baseline, Parse(moved.ToJsonString()), baseline, localChanged: true).Differences);
        ArchidektConciseDifference concise = ArchidektConciseDifferences.From(row);

        Assert.Equal("local-changed", concise.State);
        Assert.Equal(["categoryNames", "primaryCategoryName", "zone"], concise.Fields.Select(value => value.Name).Order(StringComparer.Ordinal));
        ArchidektDifferenceField zone = Field(concise, "zone");
        Assert.Equal("main", zone.Baseline!.Value.GetString());
        Assert.Equal("sideboard", zone.Local!.Value.GetString());
        Assert.Equal("main", zone.Remote!.Value.GetString());
        Assert.Equal(["Sideboard"], Field(concise, "categoryNames").Local!.Value.EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// Verifies a row both sides changed lists the field with the value of the baseline, the local side, and the remote
    /// side.
    /// </summary>
    [Fact]
    public void Conflict_ListsTheFieldWithAllThreeValues()
    {
        RemoteDeckSnapshot baseline = Parse(BaseDeck);
        RemoteDeckSnapshot local = Parse(WithQuantity(103, 2));
        RemoteDeckSnapshot remote = Parse(WithQuantity(103, 3));

        ArchidektSyncDiff diff = Diff(baseline, local, remote, localChanged: true);
        ArchidektConciseDifference concise = ArchidektConciseDifferences.From(Assert.Single(diff.Differences));

        Assert.True(diff.HasConflicts);
        Assert.Equal("concurrent-changed", concise.State);
        ArchidektDifferenceField quantity = Assert.Single(concise.Fields);
        Assert.Equal("quantity", quantity.Name);
        Assert.Equal(1, quantity.Baseline!.Value.GetInt32());
        Assert.Equal(2, quantity.Local!.Value.GetInt32());
        Assert.Equal(3, quantity.Remote!.Value.GetInt32());
    }

    /// <summary>
    /// Verifies a row for one text value, such as the deck name, lists that value under the last part of its path.
    /// </summary>
    [Fact]
    public void TextRow_ListsTheValueUnderItsName()
    {
        ArchidektDifference row = new("/metadata/name", "local-changed", "Old", "New", "Old");

        ArchidektDifferenceField field = Assert.Single(ArchidektConciseDifferences.From(row).Fields);

        Assert.Equal("name", field.Name);
        Assert.Equal("Old", field.Baseline!.Value.GetString());
        Assert.Equal("New", field.Local!.Value.GetString());
        Assert.Equal("Old", field.Remote!.Value.GetString());
    }

    /// <summary>
    /// Verifies the field-level form of a row is much smaller than the whole values it came from.
    /// </summary>
    [Fact]
    public void ConciseRow_IsSmallerThanTheFullRow()
    {
        RemoteDeckSnapshot baseline = Parse(BaseDeck);
        ArchidektDifference row = Assert.Single(
            Diff(baseline, Parse(WithQuantity(101, 4)), baseline, localChanged: true).Differences);

        int full = JsonSerializer.Serialize<ArchidektDifferenceRow>(row, ArchidektContract.JsonOptions).Length;
        int concise = JsonSerializer.Serialize<ArchidektDifferenceRow>(
            ArchidektConciseDifferences.From(row),
            ArchidektContract.JsonOptions).Length;

        Assert.True(concise * 2 < full, $"concise {concise} full {full}");
    }

    /// <summary>
    /// Finds the field of a row by name.
    /// </summary>
    private static ArchidektDifferenceField Field(ArchidektConciseDifference row, string name)
    {
        return Assert.Single(row.Fields, value => value.Name == name);
    }

    /// <summary>
    /// Rewrites the quantity of one entry of the base deck.
    /// </summary>
    private static string WithQuantity(int relationId, int quantity)
    {
        JsonNode root = JsonNode.Parse(BaseDeck)!;
        JsonObject card = root["cards"]!.AsArray()
            .Select(value => value!.AsObject())
            .Single(value => value["deckRelationId"]!.GetValue<int>() == relationId);
        card["quantity"] = quantity;
        return root.ToJsonString();
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
    /// Compares a local deck and a fresh remote deck with a baseline; <paramref name="localChanged"/> says whether the
    /// local deck differs from the baseline.
    /// </summary>
    private static ArchidektSyncDiff Diff(
        RemoteDeckSnapshot baselineSnapshot,
        RemoteDeckSnapshot local,
        RemoteDeckSnapshot remote,
        bool localChanged)
    {
        ArchidektSyncBaseline baseline = new(
            2,
            baselineSnapshot.RemoteId,
            baselineSnapshot.RemoteFingerprint,
            "baseline-local",
            baselineSnapshot,
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            "invisible-fingerprint");
        return ArchidektSyncPlanner.Diff(
            Guid.NewGuid(),
            1,
            localChanged ? "edited-local" : "baseline-local",
            local,
            remote,
            baseline,
            "invisible-fingerprint");
    }
}
