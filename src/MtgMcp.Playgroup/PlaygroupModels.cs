using System.ComponentModel;
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
    [property: Description("Event type name, such as Damage, Kill, PassTurn, or Endorsement.")] string Name,
    [property: Description("HUD index of the source player, from \"0\" through \"5\".")] string SourcePlayerId,
    [property: Description("Client-side event ID. Not needed for Endorsement events.")] int? Id = null,
    [property: Description("HUD index of the target player, from \"0\" through \"5\".")] string? TargetPlayerId = null,
    [property: Description("Unix timestamp of the event.")] long? Time = null,
    [property: Description("Turn number of the event.")] int? Turn = null,
    [property: Description("Amount the event carries, such as damage dealt.")] int? Amount = null,
    [property: Description("Playgroup commander ID the event relates to.")] int? CommanderId = null,
    JsonElement? Metadata = null);

/// <summary>
/// Describes one caller-selected live-session creation request.
/// </summary>
public sealed record PlaygroupLiveSessionCreateRequest(
    [property: Description("Maximum number of players at the table, from 1 through 6. Default 4.")]
    int PlayerAmount = 4,
    [property: Description("Starting life total for each player. Must be above 0. Default 40.")]
    int LifeAmount = 40,
    [property: Description("Commander bracket, from 1 (Exhibition) through 5 (cEDH). Omit for none.")]
    int? Bracket = null,
    [property: Description("Playgroup the session belongs to; makes it private to that playgroup. Omit for a public session.")]
    int? PlaygroupId = null,
    [property: Description("League within the playgroup to attribute the session to. Needs playgroupId.")]
    int? LeagueId = null,
    [property: Description("True lists the session in the public lobby. Cannot be true with playgroupId. Default false.")]
    bool Discoverable = false,
    [property: Description("Up to 64 distinct positive Playgroup language IDs spoken at the table.")]
    IReadOnlyList<int>? LanguageIds = null,
    [property: Description("Optional label for the client that created the session, such as an integration name.")]
    string? ClientIdentifier = null);
