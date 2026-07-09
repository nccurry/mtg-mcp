using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
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
    [Description("Submits one explicit event batch exactly once. Playgroup exposes no public cleanup operation, so callers must inspect provider state before retrying.")]
    internal Task<OperationResult<PlaygroupEvidence>> CreateGameEventsBatchAsync(
        [Description("Exact Playgroup game identifier receiving the events.")] int gameId,
        [Description("Caller-supplied ordered event batch sent exactly once.")]
        IReadOnlyList<PlaygroupEventImport> events,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.CreateGameEventsBatchAsync(gameId, events, cancellationToken));
    }

    /// <summary>Creates one caller-configured live session exactly once.</summary>
    [McpServerTool(Name = "playgroup_live_session_create", Title = "Create Playgroup Live Session", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Creates one explicit live session exactly once; it does not poll, monitor, join, close, or retry the session.")]
    internal Task<OperationResult<PlaygroupEvidence>> CreateLiveSessionAsync(
        [Description("Complete caller-supplied live-session creation request sent exactly once.")]
        PlaygroupLiveSessionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.CreateLiveSessionAsync(request, cancellationToken));
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
