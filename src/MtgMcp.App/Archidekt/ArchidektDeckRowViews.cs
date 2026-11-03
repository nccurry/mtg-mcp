using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Shows one owned deck as a tool result row, naming the identifier <c>remoteDeckId</c> as every Archidekt tool does.
/// The adapter record keeps its own property name because the stored sync baseline is written with it.
/// </summary>
internal sealed record ArchidektDeckRowView(
    string RemoteDeckId,
    string Name,
    string? Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    string? ParentFolderName,
    string? ParentFolderPath,
    int? CardCount,
    DateTimeOffset? UpdatedAtUtc,
    string Fingerprint)
{
    /// <summary>
    /// Gets true when <c>description</c> was cut to keep a page of rows under the size limit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DescriptionTruncated { get; init; }

    /// <summary>
    /// Shows one adapter summary as a row.
    /// </summary>
    internal static ArchidektDeckRowView From(RemoteDeckSummary deck)
    {
        return new ArchidektDeckRowView(
            deck.RemoteId,
            deck.Name,
            deck.Description,
            deck.Format,
            deck.Visibility,
            deck.ParentFolderId,
            deck.ParentFolderName,
            deck.ParentFolderPath,
            deck.CardCount,
            deck.UpdatedAtUtc,
            deck.Fingerprint)
        {
            DescriptionTruncated = deck.DescriptionTruncated,
        };
    }
}

/// <summary>
/// Returns one page of the owned decks. When <see cref="NextCursor"/> is set, call again with that <c>cursor</c>.
/// </summary>
internal sealed record ArchidektDeckListView(
    IReadOnlyList<ArchidektDeckRowView> Items,
    string? NextCursor,
    ArchidektRetrievalEvidence Evidence);

/// <summary>
/// Shows one folder with its direct relationships and its decks as rows.
/// </summary>
internal sealed record ArchidektFolderView(
    string FolderId,
    string Name,
    string Visibility,
    string? ParentFolderId,
    string Path,
    IReadOnlyList<string> ChildFolderIds,
    IReadOnlyList<ArchidektDeckRowView> Decks,
    IReadOnlyDictionary<string, JsonElement> Extensions)
{
    /// <summary>
    /// Shows one adapter folder record.
    /// </summary>
    internal static ArchidektFolderView From(RemoteFolderRecord folder)
    {
        List<ArchidektDeckRowView> decks = [];
        foreach (RemoteDeckSummary deck in folder.Decks)
        {
            decks.Add(ArchidektDeckRowView.From(deck));
        }

        return new ArchidektFolderView(
            folder.FolderId,
            folder.Name,
            folder.Visibility,
            folder.ParentFolderId,
            folder.Path,
            folder.ChildFolderIds,
            decks,
            folder.Extensions);
    }

    /// <summary>
    /// Shows a folder result, or carries its failure through.
    /// </summary>
    internal static OperationResult<ArchidektFolderView> Show(OperationResult<RemoteFolderRecord> result)
    {
        return result is OperationSuccess<RemoteFolderRecord> success
            ? new OperationSuccess<ArchidektFolderView>(From(success.Data))
            : OperationFailure.Forward<RemoteFolderRecord, ArchidektFolderView>(result);
    }
}

/// <summary>
/// Shows a newly created deck as the tool result, naming the identifier <c>remoteDeckId</c>.
/// </summary>
internal sealed record ArchidektCreatedDeckView(
    string RemoteDeckId,
    string RemoteUri,
    string Name,
    string Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    IReadOnlyList<RemoteDeckCategory> Categories,
    IReadOnlyList<RemoteDeckEntry> Entries,
    IReadOnlyDictionary<string, JsonElement> Extensions,
    ArchidektRetrievalEvidence Evidence,
    string ContentFingerprint,
    string RemoteFingerprint)
{
    /// <summary>
    /// Shows a deck creation result, or carries its failure through.
    /// </summary>
    internal static OperationResult<ArchidektCreatedDeckView> Show(OperationResult<RemoteDeckSnapshot> result)
    {
        if (result is not OperationSuccess<RemoteDeckSnapshot> success)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ArchidektCreatedDeckView>(result);
        }

        RemoteDeckSnapshot deck = success.Data;
        return new OperationSuccess<ArchidektCreatedDeckView>(new ArchidektCreatedDeckView(
            deck.RemoteId,
            deck.RemoteUri,
            deck.Name,
            deck.Description,
            deck.Format,
            deck.Visibility,
            deck.ParentFolderId,
            deck.Categories,
            deck.Entries,
            deck.Extensions,
            deck.Evidence,
            deck.ContentFingerprint,
            deck.RemoteFingerprint));
    }
}
