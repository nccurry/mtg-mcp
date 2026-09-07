namespace MtgMcp.Spellbook;

/// <summary>
/// Records the exact caller-visible Commander Spellbook request values for one evidence result.
/// </summary>
public sealed record SpellbookRequestDetails(
    string? SourceQuery,
    SpellbookPageRequest? Page,
    string? VariantId);
