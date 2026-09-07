namespace MtgMcp.Spellbook;

/// <summary>
/// Describes one Commander Spellbook find-my-combos request built from a local deck selection.
/// </summary>
public sealed record SpellbookDeckComboRequest(
    SpellbookDeckRequest Deck,
    string? SourceQuery = null,
    SpellbookPageOptions? Page = null);
