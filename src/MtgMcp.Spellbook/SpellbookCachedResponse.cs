namespace MtgMcp.Spellbook;

/// <summary>
/// Carries one complete successful source response read from the local cache.
/// </summary>
internal sealed record SpellbookCachedResponse(
    string Json,
    string SourceChecksum,
    string SourceApiVersion,
    string ContractChecksum,
    DateTimeOffset RetrievedAtUtc);
