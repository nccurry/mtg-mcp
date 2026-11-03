namespace MtgMcp.Core.Paging;

/// <summary>
/// Says how a cursor's binding differs from the state it was read against.
/// </summary>
public enum PageCursorMismatch
{
    /// <summary>
    /// The cursor was issued for another request or subject, or binds other parts than this request does.
    /// </summary>
    OtherRequest,

    /// <summary>
    /// The cursor was issued for this request, but the state it names has changed.
    /// </summary>
    Stale,
}
