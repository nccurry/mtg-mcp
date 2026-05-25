using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Contains Reddit search path, normalization, and JSON primitive helpers.
/// </summary>
public sealed partial class RedditDiscussionCorpusSignalProvider
{
    /// <summary>
    /// Builds a Reddit search phrase from the deck context.
    /// </summary>
    private static string BuildSearchText(CorpusSignalQuery query)
    {
        return string.Join(' ', new[]
            {
                query.Commander,
                query.Theme,
                query.Goal,
                "commander deck"
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()))
            .Trim();
    }

    /// <summary>
    /// Selects a fixed subreddit set for the requested analysis depth.
    /// </summary>
    private static IReadOnlyList<string> SelectSubreddits(RecommendationAnalysisBudget budget)
    {
        if (budget.AnalysisDepth.Equals(AnalysisDepths.Minimal, StringComparison.OrdinalIgnoreCase))
        {
            return ["EDH"];
        }

        return budget.AnalysisDepth.Equals(AnalysisDepths.Best, StringComparison.OrdinalIgnoreCase)
            ? ["EDH", "Commander", "Magicdeckbuilding", "BudgetBrews", "CompetitiveEDH"]
            : ["EDH", "Commander", "Magicdeckbuilding"];
    }

    /// <summary>
    /// Builds the deterministic search variants used for the current analysis depth.
    /// </summary>
    private static IReadOnlyList<RedditSearchRequest> BuildSearchRequests(RecommendationAnalysisBudget budget)
    {
        List<RedditSearchRequest> requests =
        [
            new("top", "year"),
        ];
        if (!budget.AnalysisDepth.Equals(AnalysisDepths.Minimal, StringComparison.OrdinalIgnoreCase))
        {
            requests.Add(new RedditSearchRequest("top", "all"));
        }

        if (budget.AnalysisDepth.Equals(AnalysisDepths.Best, StringComparison.OrdinalIgnoreCase))
        {
            requests.Add(new RedditSearchRequest("relevance", "all"));
        }

        return requests;
    }

    /// <summary>
    /// Builds one Reddit search endpoint path.
    /// </summary>
    private static string BuildSearchPath(
        string subreddit,
        string searchText,
        RedditSearchRequest searchRequest,
        int limit)
    {
        return $"r/{subreddit}/search.json?q={Uri.EscapeDataString(searchText)}"
            + $"&restrict_sr=1&sort={searchRequest.Sort}&t={searchRequest.TimeWindow}"
            + $"&limit={limit}&type=link&raw_json=1";
    }

    /// <summary>
    /// Checks whether a Reddit row falls inside the bounded recent-discussion window.
    /// </summary>
    private static bool IsRecentEnough(DateTimeOffset? createdAt, DateTimeOffset earliestCreatedAt)
    {
        return createdAt is null || createdAt >= earliestCreatedAt;
    }

    /// <summary>
    /// Gets configured Reddit source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return DecklistCorpusProviderSupport.SourceOptions(options, "Reddit", defaultEnabled: true);
    }

    /// <summary>
    /// Normalizes a card reference extracted from discussion text.
    /// </summary>
    private static string NormalizeCardReference(string value)
    {
        return value.Split('#', StringSplitOptions.TrimEntries)[0]
            .Trim();
    }

    /// <summary>
    /// Normalizes a loose title-case card candidate from sentence text.
    /// </summary>
    private static string NormalizePlainTextCardCandidate(string value)
    {
        return NormalizeCardReference(value)
            .Trim(',', '.', ';', ':', '!', '?', ')', ']', '}');
    }

    /// <summary>
    /// Checks whether an exception is cancellation-related.
    /// </summary>
    private static bool IsCancellation(Exception exception)
    {
        return exception is OperationCanceledException;
    }

    /// <summary>
    /// Builds an absolute Reddit URL from a permalink.
    /// </summary>
    private static string? AbsoluteRedditUri(string? permalink)
    {
        if (string.IsNullOrWhiteSpace(permalink))
        {
            return null;
        }

        if (Uri.TryCreate(permalink, UriKind.Absolute, out Uri? absolute))
        {
            return absolute.ToString();
        }

        return $"https://www.reddit.com{permalink}";
    }

    /// <summary>
    /// Truncates source text to a bounded payload size.
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    /// <summary>
    /// Reads a string property when present.
    /// </summary>
    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads an integer property when present.
    /// </summary>
    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    /// <summary>
    /// Reads a Unix timestamp property when present.
    /// </summary>
    private static DateTimeOffset? ReadUnixTime(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetDouble(out double result)
            ? DateTimeOffset.FromUnixTimeSeconds((long)result)
            : null;
    }

    /// <summary>
    /// Carries a Reddit post and its normalized discussion evidence.
    /// </summary>
    private sealed record RedditPost(string Id, string Title, DiscussionEvidence Evidence);

    /// <summary>
    /// Describes one Reddit search sort and time-window pair.
    /// </summary>
    private sealed record RedditSearchRequest(string Sort, string TimeWindow);
}
