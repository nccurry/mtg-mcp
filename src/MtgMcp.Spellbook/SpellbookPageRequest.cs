namespace MtgMcp.Spellbook;

/// <summary>
/// Describes the complete page controls actually sent to Commander Spellbook.
/// </summary>
public sealed record SpellbookPageRequest(int Limit, int Offset, bool GroupByCombo);
