namespace MtgMcp.Spellbook;

/// <summary>
/// Carries optional Commander Spellbook page controls before the adapter applies its explicit defaults.
/// </summary>
public sealed record SpellbookPageOptions(int? Limit = null, int? Offset = null, bool? GroupByCombo = null);
