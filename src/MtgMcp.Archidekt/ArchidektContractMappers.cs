using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Maps Archidekt deck and deck-list payloads into deterministic evidence models.
/// </summary>
internal static class ArchidektDeckContractMapper
{
    /// <summary>Maps one complete deck payload.</summary>
    internal static RemoteDeckSnapshot MapDeck(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        return ArchidektJsonContract.MapDeck(root, sourceJson, retrievedAtUtc, method);
    }

    /// <summary>Maps one page of provider deck summaries.</summary>
    internal static RemoteDeckPage MapDeckPage(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        return ArchidektJsonContract.MapDeckPage(root, sourceJson, retrievedAtUtc, method);
    }
}

/// <summary>
/// Maps Archidekt folder tree and folder-detail payloads into deterministic evidence models.
/// </summary>
internal static class ArchidektFolderContractMapper
{
    /// <summary>Maps the complete recursive folder tree.</summary>
    internal static RemoteFolderTree MapFolderTree(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        return ArchidektJsonContract.MapFolderTree(root, sourceJson, retrievedAtUtc, method);
    }

    /// <summary>Maps one folder detail payload.</summary>
    internal static RemoteFolderTree MapFolderDetail(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        return ArchidektJsonContract.MapFolderDetail(root, sourceJson, retrievedAtUtc, method);
    }
}

/// <summary>
/// Maps Archidekt named snapshot list and detail payloads into deterministic evidence models.
/// </summary>
internal static class ArchidektSnapshotContractMapper
{
    /// <summary>Maps one named snapshot collection.</summary>
    internal static RemoteNamedSnapshotPage MapSnapshotPage(
        JsonElement root,
        string deckId,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        return ArchidektJsonContract.MapSnapshotPage(
            root,
            deckId,
            sourceJson,
            retrievedAtUtc,
            method);
    }

    /// <summary>Maps one complete named snapshot and saved deck.</summary>
    internal static RemoteNamedSnapshot MapSnapshot(
        JsonElement root,
        string expectedDeckId,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        return ArchidektJsonContract.MapSnapshot(
            root,
            expectedDeckId,
            sourceJson,
            retrievedAtUtc,
            method);
    }
}
