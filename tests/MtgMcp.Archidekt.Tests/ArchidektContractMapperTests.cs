using System.Text.Json;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Proves canonical deck, folder, snapshot, printing, extension, and drift mapping behavior.
/// </summary>
public sealed class ArchidektContractMapperTests
{
    /// <summary>
    /// Verifies the live direct-root folder-tree shape is flattened without requiring a wrapper collection.
    /// </summary>
    [Fact]
    public void MapFolderTree_DirectRootObjectPreservesHierarchy()
    {
        const string json = """
            {"id":1,"name":"Root","private":true,"children":[
              {"id":2,"name":"Child","private":true,"children":[]}
            ]}
            """;
        using JsonDocument document = JsonDocument.Parse(json);

        RemoteFolderTree tree = ArchidektFolderContractMapper.MapFolderTree(
            document.RootElement,
            json,
            DateTimeOffset.UtcNow,
            "GET");

        Assert.Equal(2, tree.Items.Count);
        Assert.Null(tree.Items.Single(value => value.FolderId == "1").ParentFolderId);
        Assert.Equal("1", tree.Items.Single(value => value.FolderId == "2").ParentFolderId);
    }

    /// <summary>
    /// Verifies a complete deck preserves exact identities, zones, finishes, categories, and unknown fields.
    /// </summary>
    [Fact]
    public void MapDeck_MapsCanonicalProviderEvidence()
    {
        using JsonDocument document = JsonDocument.Parse(ArchidektTestPayloads.Deck);

        RemoteDeckSnapshot deck = ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            ArchidektTestPayloads.Deck,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            "GET /api/decks/{deckId}/");

        Assert.Equal("42", deck.RemoteId);
        Assert.Equal("Rate Safe Weenies", deck.Name);
        Assert.Equal("commander", deck.Format);
        Assert.Equal("private", deck.Visibility);
        Assert.Equal("9", deck.ParentFolderId);
        Assert.Equal(2, deck.Categories.Count);
        Assert.Equal(2, deck.Entries.Count);
        Assert.Equal("commander", deck.Entries[0].Zone);
        Assert.Equal("foil", deck.Entries[0].Finish);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), deck.Entries[0].PrintingId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), deck.Entries[0].OracleId);
        Assert.Equal("main", deck.Entries[1].Zone);
        Assert.Equal("dmu", deck.Entries[1].SetCode);
        Assert.Contains("customExtension", deck.Extensions.Keys);
        Assert.Equal("archidekt", deck.Evidence.Source);
        Assert.Equal("observed-2026-07-04", deck.Evidence.ContractVersion);
        Assert.Equal(TimeSpan.Zero, deck.Evidence.RetrievedAtUtc.Offset);
        Assert.Equal(64, deck.ContentFingerprint.Length);
        Assert.Equal(64, deck.RemoteFingerprint.Length);
    }

    /// <summary>
    /// Verifies semantically equivalent provider order produces the same canonical fingerprints.
    /// </summary>
    [Fact]
    public void MapDeck_EquivalentOrderProducesSameFingerprint()
    {
        using JsonDocument firstDocument = JsonDocument.Parse(ArchidektTestPayloads.Deck);
        string reordered = ArchidektTestPayloads.Deck
            .Replace(
                "\"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0",
                "\"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 1",
                StringComparison.Ordinal)
            .Replace(
                "\"name\": \"Commander\", \"includedInDeck\": true, \"includedInPrice\": true, \"isPremier\": true, \"sortOrder\": 1",
                "\"name\": \"Commander\", \"includedInDeck\": true, \"includedInPrice\": true, \"isPremier\": true, \"sortOrder\": 0",
                StringComparison.Ordinal);
        using JsonDocument secondDocument = JsonDocument.Parse(reordered);

        RemoteDeckSnapshot first = ArchidektDeckContractMapper.MapDeck(
            firstDocument.RootElement,
            ArchidektTestPayloads.Deck,
            DateTimeOffset.UtcNow,
            "GET");
        RemoteDeckSnapshot second = ArchidektDeckContractMapper.MapDeck(
            secondDocument.RootElement,
            reordered,
            DateTimeOffset.UtcNow,
            "GET");

        Assert.Equal(first.ContentFingerprint, second.ContentFingerprint);
        Assert.NotEqual(first.RemoteFingerprint, second.RemoteFingerprint);
    }

    /// <summary>
    /// Verifies a provider view counter remains observable without invalidating a content guard.
    /// </summary>
    [Fact]
    public void MapDeck_ViewCountChangePreservesRemoteFingerprint()
    {
        const string marker = "\"customExtension\": { \"kept\": true },";
        string firstJson = ArchidektTestPayloads.Deck.Replace(
            marker,
            "\"customExtension\": { \"kept\": true }, \"viewCount\": 1,",
            StringComparison.Ordinal);
        string secondJson = ArchidektTestPayloads.Deck.Replace(
            marker,
            "\"customExtension\": { \"kept\": true }, \"viewCount\": 2,",
            StringComparison.Ordinal);
        using JsonDocument firstDocument = JsonDocument.Parse(firstJson);
        using JsonDocument secondDocument = JsonDocument.Parse(secondJson);

        RemoteDeckSnapshot first = ArchidektDeckContractMapper.MapDeck(
            firstDocument.RootElement,
            firstJson,
            DateTimeOffset.UtcNow,
            "GET");
        RemoteDeckSnapshot second = ArchidektDeckContractMapper.MapDeck(
            secondDocument.RootElement,
            secondJson,
            DateTimeOffset.UtcNow,
            "GET");

        Assert.Contains("viewCount", first.Extensions.Keys);
        Assert.Equal(first.ContentFingerprint, second.ContentFingerprint);
        Assert.Equal(first.RemoteFingerprint, second.RemoteFingerprint);
    }

    /// <summary>
    /// Verifies a bounded deck page maps folders, counts, timestamps, and opaque continuation.
    /// </summary>
    [Fact]
    public void MapDeckPage_PreservesSummaryEvidence()
    {
        using JsonDocument document = JsonDocument.Parse(ArchidektTestPayloads.DeckList);

        RemoteDeckPage page = ArchidektDeckContractMapper.MapDeckPage(
            document.RootElement,
            ArchidektTestPayloads.DeckList,
            DateTimeOffset.UtcNow,
            "GET /api/decks/");

        RemoteDeckSummary item = Assert.Single(page.Items);
        Assert.Equal("42", item.RemoteId);
        Assert.Equal("9", item.ParentFolderId);
        Assert.Equal("Root/Tests", item.ParentFolderPath);
        Assert.Equal(4, item.CardCount);
        Assert.Equal("opaque-next", page.NextCursor);
    }

    /// <summary>
    /// Verifies recursive folders flatten into explicit parent paths with contained deck summaries and extensions.
    /// </summary>
    [Fact]
    public void MapFolderTree_PreservesHierarchyAndUnknownFields()
    {
        using JsonDocument document = JsonDocument.Parse(ArchidektTestPayloads.FolderTree);

        RemoteFolderTree tree = ArchidektFolderContractMapper.MapFolderTree(
            document.RootElement,
            ArchidektTestPayloads.FolderTree,
            DateTimeOffset.UtcNow,
            "GET /api/decks/folderTree/");

        Assert.Equal(2, tree.Items.Count);
        RemoteFolderRecord root = tree.Items.Single(value => value.FolderId == "9");
        RemoteFolderRecord child = tree.Items.Single(value => value.FolderId == "12");
        Assert.Equal(["12"], root.ChildFolderIds);
        Assert.Single(root.Decks);
        Assert.Equal("9", child.ParentFolderId);
        Assert.Equal("Tests/Child", child.Path);
        Assert.Contains("unknown", root.Extensions.Keys);
        Assert.Equal(64, tree.TreeFingerprint.Length);
    }

    /// <summary>
    /// Verifies snapshot list metadata remains distinct from a complete snapshot deck.
    /// </summary>
    [Fact]
    public void MapSnapshots_PreservesMetadataAndFullSavedState()
    {
        using JsonDocument listDocument = JsonDocument.Parse(ArchidektTestPayloads.SnapshotList);
        using JsonDocument snapshotDocument = JsonDocument.Parse(ArchidektTestPayloads.Snapshot);

        RemoteNamedSnapshotPage page = ArchidektSnapshotContractMapper.MapSnapshotPage(
            listDocument.RootElement,
            "42",
            ArchidektTestPayloads.SnapshotList,
            DateTimeOffset.UtcNow,
            "GET snapshots");
        RemoteNamedSnapshot snapshot = ArchidektSnapshotContractMapper.MapSnapshot(
            snapshotDocument.RootElement,
            "42",
            ArchidektTestPayloads.Snapshot,
            DateTimeOffset.UtcNow,
            "GET snapshot");

        RemoteNamedSnapshotSummary summary = Assert.Single(page.Items);
        Assert.Equal("77", summary.SnapshotId);
        Assert.Contains("extra", summary.Extensions.Keys);
        Assert.Equal("42", snapshot.Summary.DeckId);
        Assert.Equal(2, snapshot.Deck.Entries.Count);
        Assert.Equal("Before test", snapshot.Summary.Name);
    }

    /// <summary>
    /// Verifies missing required card identity is classified as provider contract drift.
    /// </summary>
    [Theory]
    [InlineData("{ \"id\": 42, \"name\": \"Deck\", \"cards\": [ { \"id\": 1 } ] }")]
    [InlineData("{ \"name\": \"Deck\", \"cards\": [] }")]
    [InlineData("{ \"id\": 42, \"cards\": [] }")]
    public void MapDeck_MissingRequiredContractFailsClosed(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        ArchidektProviderException exception = Assert.Throws<ArchidektProviderException>(() =>
            ArchidektDeckContractMapper.MapDeck(
                document.RootElement,
                json,
                DateTimeOffset.UtcNow,
                "GET"));

        Assert.Equal("provider-contract-unsupported", exception.ReasonCode);
    }

    /// <summary>
    /// Verifies a snapshot whose provider ownership differs from the requested deck fails closed.
    /// </summary>
    [Fact]
    public void MapSnapshot_DifferentOwningDeckFailsClosed()
    {
        string json = ArchidektTestPayloads.Snapshot.Replace(
            "\"id\": 42,",
            "\"id\": 99,",
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);

        ArchidektProviderException exception = Assert.Throws<ArchidektProviderException>(() =>
            ArchidektSnapshotContractMapper.MapSnapshot(
                document.RootElement,
                "42",
                json,
                DateTimeOffset.UtcNow,
                "GET"));

        Assert.Equal("provider-contract-unsupported", exception.ReasonCode);
    }
}
