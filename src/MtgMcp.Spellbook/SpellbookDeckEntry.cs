namespace MtgMcp.Spellbook;

/// <summary>
/// Describes one card row sent in a Commander Spellbook deck request.
/// </summary>
public sealed record SpellbookDeckEntry(string Card, int Quantity);
