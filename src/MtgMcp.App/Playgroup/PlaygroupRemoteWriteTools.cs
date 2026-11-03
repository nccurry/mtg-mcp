using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Playgroup;

/// <summary>
/// Exposes only the two documented Playgroup mutations in remote operation mode.
/// </summary>
internal sealed class PlaygroupRemoteWriteTools
{
    /// <summary>Provides validated single-attempt provider writes.</summary>
    private readonly PlaygroupService service;

    /// <summary>Stores effective invocation authority.</summary>
    private readonly OperationMode mode;

    /// <summary>Creates the remote-only write surface.</summary>
    internal PlaygroupRemoteWriteTools(PlaygroupService service, OperationMode mode)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.mode = mode;
    }

    /// <summary>Imports one caller-supplied event batch into an existing game.</summary>
    [McpServerTool(Name = "playgroup_game_events_batch_create", Title = "Create Playgroup Game Events", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Playgroup: submits one explicit event batch exactly once. Playgroup exposes no public cleanup operation, so " +
        "callers must inspect provider state before retrying." +
        UntrustedTextNotes.Playgroup)]
    internal Task<OperationResult<PlaygroupEvidence>> CreateGameEventsBatchAsync(
        [Description("Exact Playgroup game identifier receiving the events.")] int gameId,
        [Description(
            "Caller-supplied ordered event batch sent exactly once. An event's optional metadata is a free-form " +
            "JSON object, such as {\"tag\":\"good_sport\"} for an Endorsement.")]
        IReadOnlyList<PlaygroupEventImport> events,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () => InvalidInputPath.Default(
            await service.CreateGameEventsBatchAsync(gameId, events, cancellationToken).ConfigureAwait(false),
            "events"));
    }

    /// <summary>Creates one caller-configured live session exactly once.</summary>
    [McpServerTool(Name = "playgroup_live_session_create", Title = "Create Playgroup Live Session", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Playgroup: creates one explicit live session exactly once; it does not poll, monitor, join, close, or retry " +
        "the session." +
        UntrustedTextNotes.Playgroup)]
    internal Task<OperationResult<PlaygroupEvidence>> CreateLiveSessionAsync(
        [Description("Maximum number of players at the table, from 1 through 6; omitted uses 4.")]
        int? playerAmount = null,
        [Description("Starting life total for each player, above 0; omitted uses 40.")] int? lifeAmount = null,
        [Description("Commander bracket, from 1 (Exhibition) through 5 (cEDH); omit for none.")] int? bracket = null,
        [Description(
            "Playgroup the session belongs to, which makes it private to that playgroup; omit for a public session.")]
        int? playgroupId = null,
        [Description("League within the playgroup to attribute the session to; needs playgroupId.")]
        int? leagueId = null,
        [Description(
            "True lists the session in the public lobby and cannot be combined with playgroupId; omitted uses false.")]
        bool? discoverable = null,
        [Description("Up to 64 distinct positive Playgroup language IDs spoken at the table.")]
        IReadOnlyList<int>? languageIds = null,
        [Description("Optional label for the client that created the session, such as an integration name.")]
        string? clientIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        PlaygroupLiveSessionCreateRequest request = new(
            playerAmount ?? 4,
            lifeAmount ?? 40,
            bracket,
            playgroupId,
            leagueId,
            discoverable ?? false,
            languageIds,
            clientIdentifier);
        return ExecuteAsync(async () => InvalidInputPath.Default(
            await service.CreateLiveSessionAsync(request, cancellationToken).ConfigureAwait(false),
            SentArgument(playerAmount, lifeAmount, bracket, playgroupId, leagueId, discoverable, languageIds)));
    }

    /// <summary>
    /// Names the first live session argument the caller sent, or the player amount when none was sent.
    /// </summary>
    private static string SentArgument(
        int? playerAmount,
        int? lifeAmount,
        int? bracket,
        int? playgroupId,
        int? leagueId,
        bool? discoverable,
        IReadOnlyList<int>? languageIds)
    {
        if (playerAmount is not null)
        {
            return "playerAmount";
        }

        if (lifeAmount is not null)
        {
            return "lifeAmount";
        }

        if (bracket is not null)
        {
            return "bracket";
        }

        if (playgroupId is not null)
        {
            return "playgroupId";
        }

        if (leagueId is not null)
        {
            return "leagueId";
        }

        if (discoverable is not null)
        {
            return "discoverable";
        }

        return languageIds is not null ? "languageIds" : "playerAmount";
    }

    /// <summary>Enforces remote-write authority at invocation time.</summary>
    private Task<OperationResult<T>> ExecuteAsync<T>(Func<Task<OperationResult<T>>> operation)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.RemoteWrite))
        {
            return Task.FromResult<OperationResult<T>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit remote writes."));
        }

        return operation();
    }
}
