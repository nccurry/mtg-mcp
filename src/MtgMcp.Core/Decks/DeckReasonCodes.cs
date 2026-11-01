namespace MtgMcp.Core.Decks;

/// <summary>
/// Names the reason codes that deck write refusals carry in structured invalid-input results.
/// </summary>
public static class DeckReasonCodes
{
    /// <summary>
    /// Names the refusal of a change, or of a draft, that cannot be applied to the deck as it stands.
    /// </summary>
    public const string InvalidDeckChange = "invalid-deck-change";

    /// <summary>
    /// Names the refusal of caller-owned deck input that is malformed whatever the deck holds.
    /// </summary>
    public const string InvalidDeckInput = "invalid-deck-input";
}
