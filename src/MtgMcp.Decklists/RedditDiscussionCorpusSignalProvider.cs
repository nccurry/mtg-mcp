using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Produces bounded raw discussion evidence from Reddit JSON endpoints.
/// </summary>
public sealed class RedditDiscussionCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the default Reddit base address.
    /// </summary>
    private static readonly Uri DefaultBaseAddress = new("https://www.reddit.com/");

    /// <summary>
    /// Finds explicit MTGCardFetcher-style card references.
    /// </summary>
    private static readonly Regex CardReferencePattern = new(
        @"\[\[(?<name>[^\]\|\r\n]{2,120})(?:\|[^\]\r\n]*)?\]\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Sends requests to Reddit.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores source facts for reuse between prompts.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores source and cache configuration.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a Reddit discussion corpus provider.
    /// </summary>
    public RedditDiscussionCorpusSignalProvider(
        HttpClient httpClient,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.httpClient = httpClient;
        this.cache = cache;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= SourceOptions().BaseAddress ?? DefaultBaseAddress;
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (this.httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("mtg-mcp/1.0");
        }
    }

    /// <summary>
    /// Gets Reddit source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        bool enabled = sourceOptions.Enabled && sourceOptions.AllowUnofficialApi;
        return new CorpusSourceStatus
        {
            Key = "reddit-discussions",
            Name = "Reddit discussion search",
            Kind = "discussion-api",
            Enabled = enabled,
            StableApi = false,
            ApiType = CorpusSourceApiTypes.UnofficialApi,
            UnofficialApi = true,
            PermissionSensitive = true,
            AttributionRequired = true,
            Status = sourceOptions.Enabled
                ? enabled ? CorpusSourceStatuses.Available : CorpusSourceStatuses.Disabled
                : CorpusSourceStatuses.Disabled,
            Uri = "https://www.reddit.com/dev/api/",
            Notes =
            [
                "Queries Reddit JSON endpoints for bounded post and comment evidence.",
                "Set AllowUnofficialApi=true for the Reddit source before querying; OAuth support can replace this adapter later."
            ]
        };
    }

    /// <summary>
    /// Gets bounded Reddit discussion evidence for a deck context.
    /// </summary>
    public async Task<CorpusSignalReport> GetSignalsAsync(
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget,
        CancellationToken cancellationToken)
    {
        CorpusSourceStatus status = GetStatus();
        CorpusSignalReport report = new() { Sources = [status] };
        if (!status.Enabled)
        {
            return report;
        }

        string searchText = BuildSearchText(query);
        if (string.IsNullOrWhiteSpace(searchText))
        {
            report.Notes.Add("Reddit discussion evidence requires a commander, theme, or goal.");
            return report;
        }

        IReadOnlyList<string> subreddits = SelectSubreddits(budget);
        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "search.json/comments.json",
            Query = $"{searchText}|{string.Join(',', subreddits)}|{budget.AnalysisDepth}|{budget.MaxDecksPerSource}",
            AdapterVersion = "1",
            Budget = budget.AnalysisDepth
        };
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(options.Intelligence.Cache.Ttls.CorpusSignals, TimeSpan.FromHours(6));
        if (!query.Refresh)
        {
            CorpusSignalReport? cached = await cache.GetAsync<CorpusSignalReport>(cacheKey, ttl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                cached.Notes.Add("Reddit discussion evidence returned from source-fact cache.");
                return cached;
            }
        }

        int postsPerSubreddit = Math.Clamp(budget.MaxDecksPerSource, 2, 10);
        int commentsPerPost = Math.Clamp(budget.MaxEvidencePerRecommendation, 1, 8);
        foreach (string subreddit in subreddits)
        {
            using JsonDocument searchDocument = await GetJsonAsync(
                $"r/{subreddit}/search.json?q={Uri.EscapeDataString(searchText)}&restrict_sr=1&sort=relevance&t=year&limit={postsPerSubreddit}&raw_json=1",
                cancellationToken).ConfigureAwait(false);
            List<RedditPost> posts = ReadPosts(searchDocument.RootElement, status, query, searchText);
            foreach (RedditPost post in posts.Take(postsPerSubreddit))
            {
                report.Discussions.Add(post.Evidence);
                report.Discussions.AddRange(await GetCommentsAsync(post.Id, post.Title, searchText, status, query, commentsPerPost, cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        AddSignalsFromDiscussions(report, status, budget.MaxCandidates);
        report.Notes.Add("Reddit evidence is raw bounded discussion data with exact card-reference extraction; mtg-mcp does not infer sentiment or card quality from comment text.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Reads JSON from Reddit and rejects HTML payloads.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (DecklistCorpusProviderSupport.LooksLikeHtml(payload))
        {
            throw new InvalidOperationException("Reddit returned HTML; corpus providers only accept structured API payloads.");
        }

        return JsonDocument.Parse(payload);
    }

    /// <summary>
    /// Gets top comments for one Reddit post.
    /// </summary>
    private async Task<List<DiscussionEvidence>> GetCommentsAsync(
        string postId,
        string title,
        string searchText,
        CorpusSourceStatus status,
        CorpusSignalQuery query,
        int commentsPerPost,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postId))
        {
            return [];
        }

        using JsonDocument commentsDocument = await GetJsonAsync(
            $"comments/{Uri.EscapeDataString(postId)}.json?limit={commentsPerPost}&sort=top&raw_json=1",
            cancellationToken).ConfigureAwait(false);
        return ReadComments(commentsDocument.RootElement, title, searchText, status, query)
            .Take(commentsPerPost)
            .ToList();
    }

    /// <summary>
    /// Reads Reddit search posts from a listing response.
    /// </summary>
    private static List<RedditPost> ReadPosts(
        JsonElement root,
        CorpusSourceStatus status,
        CorpusSignalQuery query,
        string searchText)
    {
        List<RedditPost> posts = [];
        foreach (JsonElement child in EnumerateListingChildren(root))
        {
            if (!child.TryGetProperty("kind", out JsonElement kind)
                || !string.Equals(kind.GetString(), "t3", StringComparison.Ordinal)
                || !child.TryGetProperty("data", out JsonElement data))
            {
                continue;
            }

            string title = ReadString(data, "title") ?? "";
            string body = ReadString(data, "selftext") ?? "";
            DiscussionEvidence evidence = CreateEvidence(status, query, searchText, data, title, body);
            posts.Add(new RedditPost(ReadString(data, "id") ?? "", title, evidence));
        }

        return posts;
    }

    /// <summary>
    /// Reads comment evidence rows from a Reddit comments response.
    /// </summary>
    private static IEnumerable<DiscussionEvidence> ReadComments(
        JsonElement root,
        string title,
        string searchText,
        CorpusSourceStatus status,
        CorpusSignalQuery query)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
        {
            yield break;
        }

        foreach (JsonElement child in EnumerateListingChildren(root[1]))
        {
            if (!child.TryGetProperty("kind", out JsonElement kind)
                || !string.Equals(kind.GetString(), "t1", StringComparison.Ordinal)
                || !child.TryGetProperty("data", out JsonElement data))
            {
                continue;
            }

            string body = ReadString(data, "body") ?? "";
            if (string.IsNullOrWhiteSpace(body)
                || body.Equals("[deleted]", StringComparison.OrdinalIgnoreCase)
                || body.Equals("[removed]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return CreateEvidence(status, query, searchText, data, title, body);
        }
    }

    /// <summary>
    /// Enumerates Reddit listing children from supported response envelopes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateListingChildren(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement data)
            && data.TryGetProperty("children", out JsonElement children)
            && children.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Creates one bounded discussion evidence row.
    /// </summary>
    private static DiscussionEvidence CreateEvidence(
        CorpusSourceStatus status,
        CorpusSignalQuery query,
        string searchText,
        JsonElement data,
        string title,
        string body)
    {
        string combined = $"{title}\n{body}";
        return new DiscussionEvidence
        {
            Source = status.Name,
            Query = searchText,
            Community = ReadString(data, "subreddit"),
            Title = Truncate(title, 220),
            Body = Truncate(body, 1_000),
            Uri = AbsoluteRedditUri(ReadString(data, "permalink")),
            Score = ReadInt32(data, "score"),
            CreatedAt = ReadUnixTime(data, "created_utc"),
            MentionedCards = ExtractMentionedCards(combined, query)
        };
    }

    /// <summary>
    /// Adds deterministic card signals from exact discussion references.
    /// </summary>
    private static void AddSignalsFromDiscussions(
        CorpusSignalReport report,
        CorpusSourceStatus status,
        int maxCandidates)
    {
        foreach (IGrouping<string, DiscussionEvidence> group in report.Discussions
            .SelectMany(discussion => discussion.MentionedCards.Select(card => (Card: card, Discussion: discussion)))
            .GroupBy(item => item.Card, item => item.Discussion, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Sum(discussion => discussion.Score ?? 0))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates))
        {
            int evidenceCount = group.Count();
            int scoreTotal = group.Sum(discussion => Math.Max(0, discussion.Score ?? 0));
            report.Signals.Add(new CardCorpusSignal
            {
                CardName = group.Key,
                Source = status.Name,
                SignalType = CorpusSignalTypes.Discussion,
                Score = Math.Clamp(0.35 + (evidenceCount * 0.08) + (Math.Min(scoreTotal, 500) / 500.0 * 0.20), 0, 1),
                DeckCount = evidenceCount,
                Uri = group.First().Uri,
                Rationale = $"{group.Key} was explicitly referenced in {evidenceCount} Reddit discussion evidence row(s)."
            });
        }
    }

    /// <summary>
    /// Extracts explicit card references from discussion text.
    /// </summary>
    private static List<string> ExtractMentionedCards(string text, CorpusSignalQuery query)
    {
        List<string> names = CardReferencePattern.Matches(text)
            .Select(match => match.Groups["name"].Value.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        foreach (string existingCard in query.ExistingCards.Append(query.Commander ?? ""))
        {
            if (!string.IsNullOrWhiteSpace(existingCard)
                && text.Contains(existingCard, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(existingCard);
            }
        }

        return names
            .Select(NormalizeCardReference)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }

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
            ? ["EDH", "CompetitiveEDH", "Magicdeckbuilding"]
            : ["EDH", "CompetitiveEDH"];
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
        return value.Split('#', StringSplitOptions.TrimEntries)[0].Trim();
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
}
