namespace MtgMcp.Core.Results;

/// <summary>
/// Names the reason codes that more than one tool family reports for stale state and unusable cursors, so every
/// family refuses the same situation with the same code.
/// </summary>
public static class SharedReasonCodes
{
    /// <summary>
    /// Names the conflict reported when a write names a local deck revision that is no longer the deck's revision.
    /// </summary>
    public const string StaleDeckRevision = "stale-deck-revision";

    /// <summary>
    /// Names the conflict reported when a preview or its fingerprint no longer matches the state it was built from.
    /// </summary>
    public const string PreviewChanged = "preview-changed";

    /// <summary>
    /// Names the conflict reported when the data behind a valid cursor changed after the first page was read.
    /// </summary>
    public const string CursorStale = "cursor-stale";

    /// <summary>
    /// Names the invalid-input reason reported when a cursor is malformed, edited, or belongs to another request.
    /// </summary>
    public const string InvalidCursor = "invalid-cursor";

    /// <summary>
    /// Names the invalid-input reason reported when an operation that destroys or replaces data lacks its exact
    /// confirmation phrase.
    /// </summary>
    public const string ConfirmationRequired = "confirmation-required";

    /// <summary>
    /// Names the invalid-input reason reported when a page size is outside the range a tool allows.
    /// </summary>
    public const string InvalidPageSize = "invalid-page-size";

    /// <summary>
    /// Names the conflict reported when an operation names a Scryfall card-data generation that is no longer the one
    /// it needs.
    /// </summary>
    public const string StaleScryfallGeneration = "stale-scryfall-generation";
}
