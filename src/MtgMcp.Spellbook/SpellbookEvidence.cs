using System.Text.Json;

namespace MtgMcp.Spellbook;

/// <summary>
/// Wraps unchanged Commander Spellbook JSON with source, cache, and request facts.
/// </summary>
public sealed record SpellbookEvidence(
    string Name,
    string Operation,
    SpellbookRequestDetails Request,
    string Endpoint,
    string SourceApiVersion,
    string ContractChecksum,
    DateTimeOffset RetrievedAtUtc,
    string CacheStatus,
    string SourceChecksum,
    string SourceUrl,
    IReadOnlyList<string> Limitations,
    JsonElement Data)
{
    /// <summary>
    /// Gets an immutable copy of the limitations that apply to this result.
    /// </summary>
    public IReadOnlyList<string> Limitations { get; init; } = Array.AsReadOnly(Limitations.ToArray());

    /// <summary>
    /// Gets an independent copy of the complete provider JSON object.
    /// </summary>
    public JsonElement Data { get; init; } = Data.Clone();
}
