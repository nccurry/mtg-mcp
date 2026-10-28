using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Playgroup;

/// <summary>
/// Names where a playgroup statistics result came from, without the provider JSON.
/// </summary>
internal sealed record PlaygroupStatsEvidence(
    string OperationId,
    string ApiVersion,
    string ContractChecksum,
    DateTimeOffset RetrievedAtUtc,
    string SourceChecksum,
    int PagesRead);

/// <summary>
/// Returns the totals of a playgroup's games by deck or by player, with what was read and what was left out. It ranks
/// nothing and judges nothing.
/// </summary>
internal sealed record PlaygroupGameStatsView(
    int PlaygroupId,
    string GroupBy,
    string? From,
    string? To,
    int MaxGames,
    int GamesRead,
    int GamesCounted,
    int GamesSkipped,
    IReadOnlyList<PlaygroupSkippedGame> Skipped,
    int GamesOutsideRange,
    bool LimitReached,
    IReadOnlyList<PlaygroupGameStatsRow> Rows,
    IReadOnlyList<string> Notes,
    PlaygroupStatsEvidence Evidence)
{
    /// <summary>
    /// Gets how many skipped games the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("skippedOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkippedOmitted { get; init; }

    /// <summary>
    /// Gets true when rows were left out to stay under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to see every row when the result was cut.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}

/// <summary>
/// Exposes the playgroup statistics tool: totals computed from the provider's game list.
/// </summary>
internal sealed class PlaygroupStatsTools
{
    /// <summary>
    /// Gives the most skipped games one result lists.
    /// </summary>
    private const int MaximumSkippedListed = 50;

    /// <summary>
    /// Reads the games.
    /// </summary>
    private readonly PlaygroupService service;

    /// <summary>
    /// Creates the tool around one service.
    /// </summary>
    internal PlaygroupStatsTools(PlaygroupService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>
    /// Totals one playgroup's games by deck or by player.
    /// </summary>
    [McpServerTool(
        Name = "playgroup_games_stats_get",
        Title = "Get Playgroup Game Statistics",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Playgroup: totals one playgroup's games by deck or by player, read from the games list up to maxGames. " +
        "Each row has games, wins, win rate, the pod-size baseline (the mean of 1 divided by the number of " +
        "players), and the average total rounds, as exact decimal strings rounded to six places. from and to " +
        "(YYYY-MM-DD, inclusive, as UTC days) filter on each game's ended_at after reading, because the list has " +
        "no date filter and no stated order; games outside the range, games with no winner, and games with a " +
        "missing deck or player are counted separately and never hidden. If maxGames stops the read, the result " +
        "says the date range may be incomplete. Game data has no commander field: use playgroup_deck_get to map a " +
        "deck to its commander. " +
        "It does not rank rows or say which deck is better." + UntrustedTextNotes.Playgroup +
        " Example: {\"playgroupId\":567,\"groupBy\":\"deck\",\"from\":\"2026-07-01\",\"maxGames\":200}.")]
    internal async Task<OperationResult<PlaygroupGameStatsView>> GetStatsAsync(
        [Description("Exact playgroup identifier visible to the authenticated key.")] int playgroupId,
        [Description("What to total by: deck or player."), AllowedValues("deck", "player")] string groupBy,
        [Description("Required: the most games to read, from 1 through 1000.")] int maxGames,
        [Description("Optional first day to count, YYYY-MM-DD, compared as a UTC day with each game's " +
            "ended_at.")] string? from = null,
        [Description("Optional last day to count, YYYY-MM-DD, compared as a UTC day with each game's " +
            "ended_at.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        PlaygroupGameGroupBy group;
        switch (groupBy)
        {
            case "deck":
                group = PlaygroupGameGroupBy.Deck;
                break;
            case "player":
                group = PlaygroupGameGroupBy.Player;
                break;
            default:
                return new OperationInvalidInput(
                    "invalid-group-by",
                    "groupBy must be deck or player. Game data has no commander field.",
                    "groupBy");
        }

        OperationInvalidInput? dateProblem = ReadDates(from, to, out DateOnly? first, out DateOnly? last);
        if (dateProblem is not null)
        {
            return dateProblem;
        }

        OperationResult<PlaygroupGameRead> read = await service.ReadGamesAsync(playgroupId, maxGames, cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<PlaygroupGameRead> success)
        {
            return OperationFailure.Forward<PlaygroupGameRead, PlaygroupGameStatsView>(read);
        }

        PlaygroupGameStatsResult stats = PlaygroupGameStats.Aggregate(success.Data.Games, group, first, last);
        return new OperationSuccess<PlaygroupGameStatsView>(Present(
            playgroupId,
            groupBy,
            from,
            to,
            maxGames,
            success.Data,
            stats));
    }

    /// <summary>
    /// Reads the optional dates and checks that the range is in order.
    /// </summary>
    private static OperationInvalidInput? ReadDates(string? from, string? to, out DateOnly? first, out DateOnly? last)
    {
        first = null;
        last = null;
        if (from is not null)
        {
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateOnly day))
            {
                return new OperationInvalidInput("invalid-date", "from must be a date written YYYY-MM-DD.", "from");
            }

            first = day;
        }

        if (to is not null)
        {
            if (!DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateOnly day))
            {
                return new OperationInvalidInput("invalid-date", "to must be a date written YYYY-MM-DD.", "to");
            }

            last = day;
        }

        return first is DateOnly start && last is DateOnly end && start > end
            ? new OperationInvalidInput("invalid-date-range", "from must not be after to.", "from")
            : null;
    }

    /// <summary>
    /// Builds the result and cuts rows to the size limit.
    /// </summary>
    private static PlaygroupGameStatsView Present(
        int playgroupId,
        string groupBy,
        string? from,
        string? to,
        int maxGames,
        PlaygroupGameRead read,
        PlaygroupGameStatsResult stats)
    {
        List<string> notes =
        [
            "Rates are exact fractions rounded to six decimal places. The baseline is the mean of 1 divided by the " +
            "number of players in the games counted, so it is what chance alone would give.",
            "averageTotalRounds covers only games that report total_rounds.",
            "The game list has no date filter and no stated order, so dates are applied after reading.",
        ];
        if (read.LimitReached && (from is not null || to is not null))
        {
            notes.Add(
                "The read stopped at maxGames, so the date range may be incomplete. Raise maxGames to read more " +
                    "games.");
        }
        else if (read.LimitReached)
        {
            notes.Add("The read stopped at maxGames, so older or newer games may not be counted.");
        }

        List<PlaygroupSkippedGame> skipped = [.. stats.Skipped.Take(MaximumSkippedListed)];
        PlaygroupGameStatsView view = new(
            playgroupId,
            groupBy,
            from,
            to,
            maxGames,
            stats.GamesRead,
            stats.GamesCounted,
            stats.GamesSkipped,
            skipped,
            stats.GamesOutsideRange,
            read.LimitReached,
            [],
            notes,
            new PlaygroupStatsEvidence(
                read.Evidence.OperationId,
                read.Evidence.ApiVersion,
                read.Evidence.ContractChecksum,
                read.Evidence.RetrievedAtUtc,
                read.Evidence.SourceChecksum,
                read.PagesRead))
        {
            SkippedOmitted = stats.GamesSkipped > skipped.Count ? stats.GamesSkipped - skipped.Count : null,
        };
        int headerBytes = ResultSize.Measure(view);
        List<int> sizes = [.. stats.Rows.Select(row => ResultSize.Measure(row) + 1)];
        int keep = ResultSizeLimit.CountFitting(sizes, headerBytes);
        return view with
        {
            Rows = [.. stats.Rows.Take(keep)],
            Truncated = keep < stats.Rows.Count ? true : null,
            Narrow = keep < stats.Rows.Count
                ? "Rows were left out to stay under the size limit. Use from and to to narrow the games."
                : null,
        };
    }
}
