namespace MtgMcp.Spellbook;

/// <summary>
/// Defines the Commander Spellbook deck-request limits shared by callers and the adapter.
/// </summary>
public static class SpellbookDeckRequestLimits
{
    /// <summary>
    /// Caps one card name before it is sent to Commander Spellbook.
    /// </summary>
    public const int MaximumCardNameLength = 256;

    /// <summary>
    /// Caps the number of distinct commander rows in one source request.
    /// </summary>
    public const int MaximumCommanderEntries = 12;

    /// <summary>
    /// Caps the number of distinct main-deck rows in one source request.
    /// </summary>
    public const int MaximumMainEntries = 600;
}
