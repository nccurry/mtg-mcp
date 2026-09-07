namespace MtgMcp.Spellbook;

/// <summary>
/// Carries one bounded, successful source response before cache persistence.
/// </summary>
internal sealed record SpellbookNetworkResponse(
    string Json,
    string SourceChecksum,
    DateTimeOffset RetrievedAtUtc);
