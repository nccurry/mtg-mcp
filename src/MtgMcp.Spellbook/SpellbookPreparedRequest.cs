namespace MtgMcp.Spellbook;

/// <summary>
/// Carries one validated source request from adapter input validation to cache and transport work.
/// </summary>
internal sealed record SpellbookPreparedRequest(
    string Operation,
    HttpMethod Method,
    string PathAndQuery,
    string Endpoint,
    string? Body,
    SpellbookRequestDetails Details,
    SpellbookRequestIdentity Identity);
