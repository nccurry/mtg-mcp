namespace MtgMcp.Spellbook;

/// <summary>
/// Describes the two Commander Spellbook deck zones supported by its public request contract.
/// </summary>
public sealed record SpellbookDeckRequest(
    IReadOnlyList<SpellbookDeckEntry> Commanders,
    IReadOnlyList<SpellbookDeckEntry> Main);
