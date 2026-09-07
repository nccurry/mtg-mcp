namespace MtgMcp.Spellbook;

/// <summary>
/// Describes one source-authoritative Commander Spellbook variant search.
/// </summary>
public sealed record SpellbookVariantSearchRequest(string SourceQuery, SpellbookPageOptions? Page = null);
