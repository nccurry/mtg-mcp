using MtgMcp.App.Hosting;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Writes and reads the page cursors of the card list tools. A cursor binds the request without its cursor and page
/// size, and the marker of every source, so a source that changed between pages is noticed.
/// </summary>
internal static class CardListCursors
{
    /// <summary>
    /// Reads the offset of a cursor against the sources read now. A missing cursor starts at the first item. A
    /// cursor that is malformed or belongs to another request is invalid input at <c>cursor</c>, and one whose source
    /// changed is a stale-cursor conflict that carries the new revision or remote fingerprint of the source.
    /// </summary>
    internal static OperationResult<int> Read(
        string? cursor,
        string fingerprint,
        IReadOnlyList<CardListSourceInfo> sources)
    {
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            Binding(fingerprint, sources),
            current: Current(sources));
        return decoded is OperationSuccess<PageCursorPosition> position
            ? new OperationSuccess<int>(position.Data.Offset)
            : OperationFailure.Forward<PageCursorPosition, int>(decoded);
    }

    /// <summary>
    /// Binds the request and the state of every source.
    /// </summary>
    internal static PageCursorBinding Binding(string fingerprint, IReadOnlyList<CardListSourceInfo> sources)
    {
        string markers = RequestHash.Of((sources.Select(source => source.Marker).ToArray(), typeof(string[])));
        return PageCursorBinding.ForRequest(fingerprint).And(PageCursorBinding.ForSource(markers));
    }

    /// <summary>
    /// Names the first deck revision and live Archidekt fingerprint among the sources, so a caller can see what the
    /// sources are now.
    /// </summary>
    private static ConflictState? Current(IReadOnlyList<CardListSourceInfo> sources)
    {
        long? revision = sources.FirstOrDefault(source => source is { Kind: "deck", Revision: not null })?.Revision;
        string? remote = sources.FirstOrDefault(source => source is { Kind: "archidekt-deck", StateMarker: not null })
            ?.StateMarker;
        return revision is null && remote is null ? null : new ConflictState(revision, remote);
    }
}
