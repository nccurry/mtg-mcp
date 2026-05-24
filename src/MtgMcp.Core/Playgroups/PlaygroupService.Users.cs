using System.Globalization;

namespace MtgMcp.Core;

/// <summary>
/// Provides Playgroup user discovery and user deck lookup behavior.
/// </summary>
public sealed partial class PlaygroupService
{
    /// <summary>
    /// Lists users seen in fetched games for a playgroup.
    /// </summary>
    public async Task<PlaygroupUserListResult> ListUsersAsync(
        string playgroupIdOrUrl,
        int maxGames,
        int limit,
        CancellationToken cancellationToken
    )
    {
        long playgroupId = ParsePlaygroupId(playgroupIdOrUrl);
        int normalizedMaxGames = Clamp(maxGames, min: 1, max: MaximumGameFetchLimit);
        int normalizedLimit = Clamp(limit, min: 1, max: MaximumUserLimit);

        IReadOnlyList<PlaygroupGame> games = await FetchGamesAsync(
                playgroupId,
                normalizedMaxGames,
                cancellationToken
            )
            .ConfigureAwait(false);
        IReadOnlyList<UserReference> references = ExtractUserReferences(games);
        List<PlaygroupUserSummary> users = references
            .Take(normalizedLimit)
            .Select(reference => new PlaygroupUserSummary
            {
                UserId = reference.UserId,
                UserName = reference.UserName ?? $"Playgroup User {reference.UserId}",
                FetchedPlaygroupGames = reference.GamesSeen,
                DecksSeen = reference.DeckIds.Count,
                LastPlayedAt = reference.LastPlayedAt,
            })
            .ToList();

        List<string> warnings =
        [
            "Playgroup does not expose a direct member lookup endpoint in the public API; this result is derived from fetched game participations.",
        ];
        if (games.Count >= normalizedMaxGames)
        {
            warnings.Add($"User discovery stopped after the requested maxGames value of {normalizedMaxGames}.");
        }

        if (references.Count > normalizedLimit)
        {
            warnings.Add(
                $"Only the first {normalizedLimit} of {references.Count} discovered users are returned."
            );
        }

        return new PlaygroupUserListResult
        {
            PlaygroupId = playgroupId,
            FetchedGames = games.Count,
            Users = users,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Lists accessible decks for a user resolved from a playgroup user id or observed name.
    /// </summary>
    public async Task<PlaygroupUserDeckListResult> ListUserDecksAsync(
        string playgroupIdOrUrl,
        string userIdOrName,
        string? source,
        int maxGames,
        int limit,
        CancellationToken cancellationToken
    )
    {
        long playgroupId = ParsePlaygroupId(playgroupIdOrUrl);
        int normalizedMaxGames = Clamp(maxGames, min: 1, max: MaximumGameFetchLimit);
        int normalizedLimit = Clamp(limit, min: 1, max: MaximumDeckLimit);
        string normalizedSource = NormalizeUserDeckSource(source);
        IReadOnlyList<PlaygroupGame> games = await FetchGamesAsync(
                playgroupId,
                normalizedMaxGames,
                cancellationToken
            )
            .ConfigureAwait(false);
        IReadOnlyList<UserReference> userReferences = ExtractUserReferences(games);
        UserReference user = ResolveUser(userIdOrName, userReferences);
        Dictionary<long, DeckReference> observedDecks = ExtractDeckReferences(games, user.UserId)
            .ToDictionary(reference => reference.DeckId);

        IReadOnlyList<PlaygroupDeck> decks = await gateway
            .ListUserDecksAsync(user.UserId, cancellationToken)
            .ConfigureAwait(false);
        List<PlaygroupDeckSummary> summaries = decks
            .Where(deck => MatchesUserDeckSource(deck, normalizedSource))
            .Take(normalizedLimit)
            .Select(deck =>
            {
                observedDecks.TryGetValue(deck.Id, out DeckReference? reference);
                return BuildDeckSummary(deck, reference, user.UserName);
            })
            .ToList();

        int filteredBySource = decks.Count(deck => !MatchesUserDeckSource(deck, normalizedSource));
        List<string> warnings =
        [
            "User-name resolution and playgroup-seen counts are derived from fetched game participations.",
        ];
        if (user.GamesSeen == 0)
        {
            warnings.Add("The requested user id was not observed in fetched playgroup games.");
        }

        if (games.Count >= normalizedMaxGames)
        {
            warnings.Add($"User resolution stopped after the requested maxGames value of {normalizedMaxGames}.");
        }

        if (filteredBySource > 0)
        {
            warnings.Add($"{filteredBySource} decks were excluded by the source filter '{normalizedSource}'.");
        }

        if (decks.Count - filteredBySource > normalizedLimit)
        {
            warnings.Add($"Only the first {normalizedLimit} matching user decks are returned.");
        }

        return new PlaygroupUserDeckListResult
        {
            PlaygroupId = playgroupId,
            UserId = user.UserId,
            UserName = user.UserName,
            Source = normalizedSource,
            Decks = summaries,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Builds unique user references from game participations in first-seen order.
    /// </summary>
    private static IReadOnlyList<UserReference> ExtractUserReferences(
        IReadOnlyList<PlaygroupGame> games
    )
    {
        Dictionary<long, UserReference> references = [];
        foreach (PlaygroupGame game in games)
        {
            DateTimeOffset? playedAt = game.EndedAt ?? game.StartedAt;
            foreach (PlaygroupParticipation participation in game.Participations)
            {
                if (!participation.UserId.HasValue || participation.UserId.Value <= 0)
                {
                    continue;
                }

                if (!references.TryGetValue(participation.UserId.Value, out UserReference? reference))
                {
                    reference = new UserReference(participation.UserId.Value);
                    references.Add(reference.UserId, reference);
                }

                reference.UserName = FirstNonEmpty(reference.UserName, participation.UserName);
                reference.GamesSeen++;
                if (participation.DeckId is > 0)
                {
                    reference.DeckIds.Add(participation.DeckId.Value);
                }

                reference.LastPlayedAt = Later(reference.LastPlayedAt, playedAt);
            }
        }

        return references.Values.ToList();
    }

    /// <summary>
    /// Resolves a user id or observed username from fetched playgroup participation data.
    /// </summary>
    private static UserReference ResolveUser(
        string userIdOrName,
        IReadOnlyList<UserReference> users
    )
    {
        if (string.IsNullOrWhiteSpace(userIdOrName))
        {
            throw new ArgumentException("Playgroup user id or name is required.", nameof(userIdOrName));
        }

        string trimmed = userIdOrName.Trim();
        if (long.TryParse(
                trimmed,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long userId
            )
            && userId > 0)
        {
            return users.FirstOrDefault(user => user.UserId == userId) ?? new UserReference(userId);
        }

        List<UserReference> exact = users
            .Where(user => user.UserName?.Equals(trimmed, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        if (exact.Count == 1)
        {
            return exact[0];
        }

        if (exact.Count > 1)
        {
            throw CreateAmbiguousUserException(trimmed, exact);
        }

        List<UserReference> partial = users
            .Where(user => user.UserName?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        if (partial.Count == 1)
        {
            return partial[0];
        }

        if (partial.Count > 1)
        {
            throw CreateAmbiguousUserException(trimmed, partial);
        }

        throw new InvalidOperationException(
            $"Could not resolve Playgroup user '{userIdOrName}' from fetched game participations."
        );
    }

    /// <summary>
    /// Creates a resolution error that includes non-secret candidate ids and names.
    /// </summary>
    private static InvalidOperationException CreateAmbiguousUserException(
        string requested,
        IReadOnlyList<UserReference> candidates
    )
    {
        string candidateText = string.Join(
            ", ",
            candidates.Select(candidate => $"{candidate.UserId}:{candidate.UserName}")
        );
        return new InvalidOperationException(
            $"Playgroup user '{requested}' is ambiguous. Candidates: {candidateText}."
        );
    }

    /// <summary>
    /// Normalizes a caller-supplied user deck source filter.
    /// </summary>
    private static string NormalizeUserDeckSource(string? source)
    {
        string normalized = string.IsNullOrWhiteSpace(source)
            ? PlaygroupUserDeckSources.Any
            : source.Trim().ToLowerInvariant();
        if (normalized == "all")
        {
            return PlaygroupUserDeckSources.Any;
        }

        if (!PlaygroupUserDeckSources.All.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unsupported Playgroup user deck source '{source}'. Supported sources: {string.Join(", ", PlaygroupUserDeckSources.All)}.",
                nameof(source)
            );
        }

        return normalized;
    }

    /// <summary>
    /// Checks whether a deck matches a source filter.
    /// </summary>
    private static bool MatchesUserDeckSource(PlaygroupDeck deck, string source)
    {
        return source == PlaygroupUserDeckSources.Any
            || (
                source == PlaygroupUserDeckSources.Archidekt
                && IsArchidektDecklistUrl(deck.DecklistUrl)
            );
    }

    /// <summary>
    /// Checks whether a decklist URL points at Archidekt.
    /// </summary>
    private static bool IsArchidektDecklistUrl(string? decklistUrl)
    {
        return
            Uri.TryCreate(decklistUrl, UriKind.Absolute, out Uri? uri)
            && (
                uri.Host.Equals("archidekt.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".archidekt.com", StringComparison.OrdinalIgnoreCase)
            );
    }
}
