using System.Globalization;

namespace MtgMcp.Core;

/// <summary>
/// Provides shared Playgroup parsing, paging, and reference helpers.
/// </summary>
public sealed partial class PlaygroupService
{
    /// <summary>
    /// Fetches enough game pages to satisfy the bounded maxGames request.
    /// </summary>
    private async Task<IReadOnlyList<PlaygroupGame>> FetchGamesAsync(
        long playgroupId,
        int maxGames,
        CancellationToken cancellationToken
    )
    {
        List<PlaygroupGame> games = [];
        int page = 1;
        while (games.Count < maxGames)
        {
            int remaining = maxGames - games.Count;
            int pageSize = Math.Min(ApiPageLimit, remaining);
            IReadOnlyList<PlaygroupGame> pageGames = await gateway
                .ListPlaygroupGamesAsync(
                    playgroupId,
                    page,
                    pageSize,
                    includeEvents: false,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (pageGames.Count == 0)
            {
                break;
            }

            games.AddRange(pageGames);
            if (pageGames.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return games;
    }

    /// <summary>
    /// Parses a numeric Playgroup id from an id, slug, or Playgroup URL.
    /// </summary>
    private static long ParsePlaygroupId(string playgroupIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(playgroupIdOrUrl))
        {
            throw new ArgumentException("Playgroup id or URL is required.", nameof(playgroupIdOrUrl));
        }

        string trimmed = playgroupIdOrUrl.Trim();
        if (long.TryParse(
                trimmed,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long directId
            )
            && directId > 0)
        {
            return directId;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            string[] segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int playgroupsIndex = Array.FindIndex(
                segments,
                segment => segment.Equals("playgroups", StringComparison.OrdinalIgnoreCase)
            );
            if (playgroupsIndex >= 0 && playgroupsIndex + 1 < segments.Length)
            {
                long? id = ParseLeadingPositiveLong(segments[playgroupsIndex + 1]);
                if (id.HasValue)
                {
                    return id.Value;
                }
            }
        }

        long? slugId = ParseLeadingPositiveLong(trimmed);
        return slugId
            ?? throw new FormatException(
                $"Could not find a numeric Playgroup id in '{playgroupIdOrUrl}'."
            );
    }

    /// <summary>
    /// Reads the positive numeric prefix from a slug.
    /// </summary>
    private static long? ParseLeadingPositiveLong(string value)
    {
        int length = 0;
        while (length < value.Length && char.IsDigit(value[length]))
        {
            length++;
        }

        return
            length > 0
            && long.TryParse(
                value[..length],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long id
            )
            && id > 0
            ? id
            : null;
    }

    /// <summary>
    /// Restricts numeric tool inputs to safe service bounds.
    /// </summary>
    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    /// <summary>
    /// Returns the later non-null timestamp.
    /// </summary>
    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second)
    {
        return first > second ? first : second;
    }

    /// <summary>
    /// Tracks one deck observed in fetched playgroup game participations.
    /// </summary>
    private sealed class DeckReference
    {
        /// <summary>
        /// Creates a reference for a discovered deck id.
        /// </summary>
        public DeckReference(long deckId)
        {
            DeckId = deckId;
        }

        /// <summary>
        /// Gets the Playgroup deck id.
        /// </summary>
        public long DeckId { get; }

        /// <summary>
        /// Gets or sets the observed owner user id.
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// Gets or sets the observed deck name.
        /// </summary>
        public string? DeckName { get; set; }

        /// <summary>
        /// Gets or sets the observed owner name.
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Gets or sets how many fetched games included the deck.
        /// </summary>
        public int GamesSeen { get; set; }

        /// <summary>
        /// Gets or sets how many fetched games the deck won.
        /// </summary>
        public int WinsSeen { get; set; }

        /// <summary>
        /// Gets or sets the latest fetched game timestamp for the deck.
        /// </summary>
        public DateTimeOffset? LastPlayedAt { get; set; }
    }

    /// <summary>
    /// Tracks one user observed in fetched playgroup game participations.
    /// </summary>
    private sealed class UserReference
    {
        /// <summary>
        /// Creates a reference for a discovered user id.
        /// </summary>
        public UserReference(long userId)
        {
            UserId = userId;
        }

        /// <summary>
        /// Gets the Playgroup user id.
        /// </summary>
        public long UserId { get; }

        /// <summary>
        /// Gets or sets the observed username.
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Gets or sets how many fetched games included the user.
        /// </summary>
        public int GamesSeen { get; set; }

        /// <summary>
        /// Gets observed deck ids for the user.
        /// </summary>
        public HashSet<long> DeckIds { get; } = [];

        /// <summary>
        /// Gets or sets the latest fetched game timestamp for the user.
        /// </summary>
        public DateTimeOffset? LastPlayedAt { get; set; }
    }
}
