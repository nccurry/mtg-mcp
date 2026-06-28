using System.Text.Json;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Playgroup;

/// <summary>
/// Sends Playgroup.gg public API requests and maps responses to Core models.
/// </summary>
public sealed partial class PlaygroupGateway
{
    /// <summary>
    /// Gets Playgroup deck details when the deck is accessible.
    /// </summary>
    public async Task<PlaygroupDeck> GetDeckAsync(
        long deckId,
        CancellationToken cancellationToken
    )
    {
        using JsonDocument document = await GetJsonAsync(
                $"decks/{Escape(deckId)}",
                requiresAuthentication: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MapDeck(document.RootElement);
    }

    /// <summary>
    /// Lists accessible decks for a Playgroup user.
    /// </summary>
    public async Task<IReadOnlyList<PlaygroupDeck>> ListUserDecksAsync(
        long userId,
        CancellationToken cancellationToken
    )
    {
        using JsonDocument document = await GetJsonAsync(
                $"users/{Escape(userId)}/decks",
                requiresAuthentication: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        return EnumerateCollection(document.RootElement).Select(MapDeck).ToList();
    }

    /// <summary>
    /// Gets a deck's Elo history in a global, playgroup, or league scope.
    /// </summary>
    public async Task<PlaygroupEloHistory> GetDeckEloHistoryAsync(
        long deckId,
        long? playgroupId,
        long? leagueId,
        CancellationToken cancellationToken
    )
    {
        List<string> parameters = [];
        if (playgroupId.HasValue)
        {
            parameters.Add($"playgroup_id={Escape(playgroupId.Value)}");
        }

        if (leagueId.HasValue)
        {
            parameters.Add($"league_id={Escape(leagueId.Value)}");
        }

        string query = parameters.Count > 0 ? $"?{string.Join("&", parameters)}" : "";
        using JsonDocument document = await GetJsonAsync(
                $"decks/{Escape(deckId)}/elo_history{query}",
                requiresAuthentication: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        return MapEloHistory(document.RootElement);
    }
}
