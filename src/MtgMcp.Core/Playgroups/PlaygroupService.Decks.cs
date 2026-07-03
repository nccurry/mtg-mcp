namespace MtgMcp.Core;

/// <summary>
/// Provides Playgroup deck discovery and summary enrichment.
/// </summary>
public sealed partial class PlaygroupService
{
    /// <summary>
    /// Lists decks seen in fetched games for a playgroup.
    /// </summary>
    public async Task<PlaygroupDeckListResult> ListDecksAsync(
        string playgroupIdOrUrl,
        int maxGames,
        int limit,
        CancellationToken cancellationToken
    )
    {
        long playgroupId = ParsePlaygroupId(playgroupIdOrUrl);
        int normalizedMaxGames = Clamp(maxGames, min: 1, max: MaximumGameFetchLimit);
        int normalizedLimit = Clamp(limit, min: 1, max: MaximumDeckLimit);

        IReadOnlyList<PlaygroupGame> games = await FetchGamesAsync(
                playgroupId,
                normalizedMaxGames,
                cancellationToken
            )
            .ConfigureAwait(false);
        IReadOnlyList<DeckReference> references = ExtractDeckReferences(games, userId: null);
        IReadOnlyList<DeckReference> limitedReferences = references.Take(normalizedLimit).ToList();
        List<PlaygroupDeckSummary> summaries = await BuildDeckSummariesAsync(
                playgroupId,
                limitedReferences,
                cancellationToken)
            .ConfigureAwait(false);

        List<string> warnings =
        [
            "Playgroup does not expose a direct playgroup deck list; this result is derived from fetched game participations.",
        ];
        if (games.Count >= normalizedMaxGames)
        {
            warnings.Add($"Deck discovery stopped after the requested maxGames value of {normalizedMaxGames}.");
        }

        if (references.Count > normalizedLimit)
        {
            warnings.Add(
                $"Only the first {normalizedLimit} of {references.Count} discovered decks are returned."
            );
        }

        return new PlaygroupDeckListResult
        {
            PlaygroupId = playgroupId,
            FetchedGames = games.Count,
            Decks = summaries,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Enriches discovered deck references using bounded API concurrency while preserving source order.
    /// </summary>
    private async Task<List<PlaygroupDeckSummary>> BuildDeckSummariesAsync(
        long playgroupId,
        IReadOnlyList<DeckReference> references,
        CancellationToken cancellationToken
    )
    {
        using SemaphoreSlim gate = new(DeckSummaryParallelism);

        async Task<PlaygroupDeckSummary> BuildWithGateAsync(DeckReference reference)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await BuildDeckSummaryAsync(playgroupId, reference, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        PlaygroupDeckSummary[] summaries = await Task
            .WhenAll(references.Select(BuildWithGateAsync))
            .ConfigureAwait(false);
        return summaries.ToList();
    }

    /// <summary>
    /// Enriches one discovered deck reference with deck details and scoped Elo.
    /// </summary>
    private async Task<PlaygroupDeckSummary> BuildDeckSummaryAsync(
        long playgroupId,
        DeckReference reference,
        CancellationToken cancellationToken
    )
    {
        List<string> warnings = [];
        PlaygroupDeck? deck = null;
        PlaygroupEloHistory? elo = null;

        try
        {
            deck = await gateway.GetDeckAsync(reference.DeckId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            warnings.Add($"Deck details could not be fetched: {exception.Message}");
        }

        try
        {
            elo = await gateway
                .GetDeckEloHistoryAsync(reference.DeckId, playgroupId, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            warnings.Add($"Playgroup-scoped Elo could not be fetched: {exception.Message}");
        }

        if (deck?.ConfidenceFactor is < LowConfidenceThreshold)
        {
            warnings.Add("Playgroup reports a low confidence factor for this deck's estimated power.");
        }

        PlaygroupDeckSummary summary = BuildDeckSummary(deck, reference, reference.UserName);
        summary.Elo = elo?.CurrentRating;
        summary.Warnings = warnings;
        return summary;
    }

    /// <summary>
    /// Builds a deck summary from a user deck response and optional playgroup observations.
    /// </summary>
    private static PlaygroupDeckSummary BuildDeckSummary(
        PlaygroupDeck? deck,
        DeckReference? observed,
        string? ownerName
    )
    {
        long deckId = deck?.Id ?? observed?.DeckId ?? 0;
        int? wins = deck?.GamesWon;
        int? losses = deck?.GamesLost;
        int? games = wins.HasValue || losses.HasValue ? (wins ?? 0) + (losses ?? 0) : null;

        return new PlaygroupDeckSummary
        {
            DeckId = deckId,
            Name = MtgMcpText.FirstNonEmpty(deck?.Name, observed?.DeckName) ?? $"Playgroup Deck {deckId}",
            UserId = deck?.UserId ?? observed?.UserId,
            OwnerName = MtgMcpText.FirstNonEmpty(ownerName, observed?.UserName),
            CommanderNames = GetCommanderNames(deck),
            ColorIdentity = deck?.ColorIdentity ?? [],
            DecklistUrl = deck?.DecklistUrl,
            Url = deck?.Url,
            Games = games,
            Wins = wins,
            Losses = losses,
            WinRatePercentage = deck?.WinRatePercentage,
            FetchedPlaygroupGames = observed?.GamesSeen ?? 0,
            FetchedPlaygroupWins = observed?.WinsSeen ?? 0,
            EstimatedPower = deck?.PowerLevel,
            ConfidenceFactor = deck?.ConfidenceFactor,
            CompetitivenessRating = deck?.CompetitivenessRating,
            AverageWinsByRound = deck?.AverageWinsByRound,
            LastPlayedAt = deck?.LastGamePlayedAt ?? observed?.LastPlayedAt,
        };
    }

    /// <summary>
    /// Builds unique deck references from game participations in first-seen order.
    /// </summary>
    private static IReadOnlyList<DeckReference> ExtractDeckReferences(
        IReadOnlyList<PlaygroupGame> games,
        long? userId
    )
    {
        Dictionary<long, DeckReference> references = [];
        foreach (PlaygroupGame game in games)
        {
            DateTimeOffset? playedAt = game.EndedAt ?? game.StartedAt;
            foreach (PlaygroupParticipation participation in game.Participations)
            {
                if (userId.HasValue && participation.UserId != userId)
                {
                    continue;
                }

                if (!participation.DeckId.HasValue || participation.DeckId.Value <= 0)
                {
                    continue;
                }

                if (!references.TryGetValue(participation.DeckId.Value, out DeckReference? reference))
                {
                    reference = new DeckReference(participation.DeckId.Value);
                    references.Add(reference.DeckId, reference);
                }

                reference.UserId ??= participation.UserId;
                reference.DeckName = MtgMcpText.FirstNonEmpty(reference.DeckName, participation.DeckName);
                reference.UserName = MtgMcpText.FirstNonEmpty(reference.UserName, participation.UserName);
                reference.GamesSeen++;
                reference.WinsSeen += participation.Winner ? 1 : 0;
                reference.LastPlayedAt = Later(reference.LastPlayedAt, playedAt);
            }
        }

        return references.Values.ToList();
    }

    /// <summary>
    /// Collects primary and partner commander names without duplicates.
    /// </summary>
    private static IReadOnlyList<string> GetCommanderNames(PlaygroupDeck? deck)
    {
        if (deck is null)
        {
            return [];
        }

        List<string> names = [];
        AddCommanderName(names, deck.Commander);
        AddCommanderName(names, deck.Partner);
        return names;
    }

    /// <summary>
    /// Adds one commander name when it is present and not already listed.
    /// </summary>
    private static void AddCommanderName(List<string> names, PlaygroupCommander? commander)
    {
        if (
            commander is not null
            && !string.IsNullOrWhiteSpace(commander.Name)
            && !names.Contains(commander.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            names.Add(commander.Name);
        }
    }
}
