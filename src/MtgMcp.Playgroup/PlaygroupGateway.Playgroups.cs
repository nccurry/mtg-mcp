using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Playgroup;

/// <summary>
/// Sends Playgroup.gg public API requests and maps responses to Core models.
/// </summary>
public sealed partial class PlaygroupGateway
{
    /// <summary>
    /// Gets the user associated with the configured API key.
    /// </summary>
    public async Task<PlaygroupUser> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync(
                "me",
                requiresAuthentication: true,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MapUser(document.RootElement);
    }

    /// <summary>
    /// Gets a playgroup visible to the specified user.
    /// </summary>
    public async Task<PlaygroupSummary> GetUserPlaygroupAsync(
        long userId,
        long playgroupId,
        CancellationToken cancellationToken
    )
    {
        using JsonDocument document = await GetJsonAsync(
                $"users/{Escape(userId)}/playgroups/{Escape(playgroupId)}",
                requiresAuthentication: true,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MapPlaygroup(document.RootElement);
    }

    /// <summary>
    /// Lists games recorded in a playgroup.
    /// </summary>
    public async Task<IReadOnlyList<PlaygroupGame>> ListPlaygroupGamesAsync(
        long playgroupId,
        int page,
        int limit,
        bool includeEvents,
        CancellationToken cancellationToken
    )
    {
        int normalizedPage = Math.Max(1, page);
        int normalizedLimit = Math.Min(Math.Max(1, limit), 100);
        string includeEventsValue = includeEvents ? "true" : "false";
        using JsonDocument document = await GetJsonAsync(
                $"playgroups/{Escape(playgroupId)}/games?page={Escape(normalizedPage)}&limit={Escape(normalizedLimit)}&include_events={includeEventsValue}",
                requiresAuthentication: true,
                cancellationToken
            )
            .ConfigureAwait(false);

        return EnumerateCollection(document.RootElement).Select(MapGame).ToList();
    }
}
