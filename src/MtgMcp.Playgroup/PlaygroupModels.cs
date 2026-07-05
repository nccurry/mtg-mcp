using System.Text.Json;

namespace MtgMcp.Playgroup;

/// <summary>
/// Reports whether a Playgroup key is configured without exposing the key or account identity.
/// </summary>
public sealed record PlaygroupAuthStatus(string State, bool CredentialsConfigured, string Message);

/// <summary>
/// Wraps one lossless provider-shaped response with explicit retrieval and contract evidence.
/// </summary>
public sealed record PlaygroupEvidence(
    string OperationId,
    string Endpoint,
    string ApiVersion,
    string ContractChecksum,
    DateTimeOffset RetrievedAtUtc,
    string SourceChecksum,
    IReadOnlyList<string> Limitations,
    JsonElement Data)
{
    /// <summary>
    /// Gets an immutable limitations snapshot in stable order.
    /// </summary>
    public IReadOnlyList<string> Limitations { get; init; } = Array.AsReadOnly(Limitations.ToArray());

    /// <summary>
    /// Gets a detached provider document that preserves nullable and unknown fields.
    /// </summary>
    public JsonElement Data { get; init; } = Data.Clone();
}

/// <summary>
/// Describes one event submitted to the documented batch-import endpoint.
/// </summary>
public sealed record PlaygroupEventImport(
    string Name,
    string SourcePlayerId,
    int? Id = null,
    string? TargetPlayerId = null,
    long? Time = null,
    int? Turn = null,
    int? Amount = null,
    int? CommanderId = null,
    JsonElement? Metadata = null);

/// <summary>
/// Describes one caller-selected live-session creation request.
/// </summary>
public sealed record PlaygroupLiveSessionCreateRequest(
    int PlayerAmount = 4,
    int LifeAmount = 40,
    int? Bracket = null,
    int? PlaygroupId = null,
    int? LeagueId = null,
    bool Discoverable = false,
    IReadOnlyList<int>? LanguageIds = null,
    string? ClientIdentifier = null);
