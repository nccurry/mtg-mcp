using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Produces bounded raw discussion evidence from Reddit JSON endpoints.
/// </summary>
public sealed partial class RedditDiscussionCorpusSignalProvider : ICorpusSignalProvider, IDisposable
{
    /// <summary>
    /// Stores the default Reddit base address.
    /// </summary>
    private static readonly Uri DefaultBaseAddress = new("https://www.reddit.com/");

    /// <summary>
    /// Limits discussion evidence to recent Commander discourse.
    /// </summary>
    private const int DiscussionLookbackYears = 4;

    /// <summary>
    /// Caps exact-name validation requests built from plain discussion text.
    /// </summary>
    private const int MaximumPlainTextCandidates = 120;

    /// <summary>
    /// Finds explicit MTGCardFetcher-style card references.
    /// </summary>
    private static readonly Regex DoubleBracketCardReferencePattern = new(
        @"\[\[(?<name>[^\]\|\r\n]{2,120})(?:\|[^\]\r\n]*)?\]\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Finds single-bracket card references for exact Scryfall name validation.
    /// </summary>
    private static readonly Regex SingleBracketCardCandidatePattern = new(
        @"(?<!\[)\[(?<name>[^\]\|\r\n]{2,120})(?:\|[^\]\r\n]*)?\](?!\])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Finds card-like title-case phrases for exact Scryfall name validation.
    /// </summary>
    private static readonly Regex PlainTextCardCandidatePattern = new(
        @"\b(?:[A-Z0-9][A-Za-z0-9'\-]*(?:,)?|of|the|and|to|for|in|on|a|an|"
            + @"from|at|by|with|without|into|over|under|up|down|not|or|as)"
            + @"(?:\s+(?:[A-Z0-9][A-Za-z0-9'\-]*(?:,)?|of|the|and|to|for|"
            + @"in|on|a|an|from|at|by|with|without|into|over|under|up|down|not|or|as)){0,5}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Finds decklist URLs commonly shared in Commander discussions.
    /// </summary>
    private static readonly Regex LinkedDeckUriPattern = new(
        @"https?://(?:www\.)?(?:archidekt\.com/decks/[^\s\]\)<>]+|moxfield\.com/decks/[^\s\]\)<>]+|mtggoldfish\.com/deck/[^\s\]\)<>]+|tappedout\.net/mtg-decks/[^\s\]\)<>]+|deckstats\.net/decks/[^\s\]\)<>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Sends requests to Reddit.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Resolves exact card names found in discussion text.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Stores source facts for reuse between prompts.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores source and cache configuration.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Stores the latest live Reddit source health observation.
    /// </summary>
    private readonly RedditSourceHealth sourceHealth;

    /// <summary>
    /// Stores Reddit OAuth configuration.
    /// </summary>
    private readonly RedditOptions redditOptions;

    /// <summary>
    /// Creates a Reddit discussion corpus provider with shared source health storage.
    /// </summary>
    public RedditDiscussionCorpusSignalProvider(
        HttpClient httpClient,
        ICardCatalog cardCatalog,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options,
        RedditSourceHealth sourceHealth,
        IOptions<RedditOptions>? redditOptions = null)
    {
        this.httpClient = httpClient;
        this.cardCatalog = cardCatalog;
        this.cache = cache;
        this.options = options.Value;
        this.sourceHealth = sourceHealth;
        this.redditOptions = redditOptions?.Value ?? new RedditOptions();
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        RedditCredentials loaded = LoadCredentials();
        bool hasOAuthCredential = HasOAuthCredential(loaded);
        this.httpClient.BaseAddress ??= sourceOptions.BaseAddress
            ?? (hasOAuthCredential ? this.redditOptions.OAuthBaseAddress : DefaultBaseAddress);
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (this.httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(EffectiveUserAgent(loaded));
        }
    }

    /// <summary>
    /// Gets Reddit source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        RedditCredentials loaded = LoadCredentials();
        bool hasOAuthCredential = HasOAuthCredential(loaded);
        bool allowPublicJson = DecklistCorpusProviderSupport.AllowsUnofficialApi(sourceOptions, defaultAllowed: true);
        bool enabled = sourceOptions.Enabled
            && (hasOAuthCredential || allowPublicJson);
        string statusLabel = sourceOptions.Enabled
            ? enabled
                ? CorpusSourceStatuses.Available
                : CorpusSourceStatuses.NeedsOAuth
            : CorpusSourceStatuses.Disabled;
        bool usesPublicJson = !hasOAuthCredential && allowPublicJson;
        CorpusSourceStatus status = new()
        {
            Key = "reddit-discussions",
            Name = "Reddit discussion search",
            Kind = "discussion-api",
            Enabled = enabled,
            StableApi = hasOAuthCredential,
            ApiType = hasOAuthCredential || !allowPublicJson ? CorpusSourceApiTypes.Official : CorpusSourceApiTypes.UnofficialApi,
            UnofficialApi = usesPublicJson,
            RequiresKey = !hasOAuthCredential && !allowPublicJson,
            PermissionSensitive = true,
            AttributionRequired = true,
            Status = statusLabel,
            Uri = "https://www.reddit.com/dev/api/",
            Notes =
            [
                "Queries bounded Reddit post and comment JSON for exact card-reference evidence.",
                "Searches a fixed EDH/Commander subreddit allowlist for popular commander discussions.",
                "Reports linked decklist URLs from discussion text without fetching those sites.",
                hasOAuthCredential
                    ? "Uses Reddit OAuth bearer credentials and the oauth.reddit.com API path."
                    : "No Reddit OAuth credentials configured; bounded public JSON fallback is enabled only while AllowUnofficialApi permits it."
            ]
        };
        if (hasOAuthCredential)
        {
            sourceHealth.Clear();
            return status;
        }

        if (status.Status == CorpusSourceStatuses.NeedsOAuth)
        {
            status.Notes.Add(
                "Reddit public JSON is disabled; configure MtgMcp:Reddit OAuth credentials or set AllowUnofficialApi=true.");
            return status;
        }

        ApplyObservedPublicJsonHealth(status);
        return status;
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
            AdapterVersion = "3",
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
        DateTimeOffset earliestCreatedAt = DateTimeOffset.UtcNow.AddYears(-DiscussionLookbackYears);
        try
        {
            foreach (string subreddit in subreddits)
            {
                Dictionary<string, RedditPost> postsById = new(StringComparer.OrdinalIgnoreCase);
                foreach (RedditSearchRequest searchRequest in BuildSearchRequests(budget))
                {
                    using JsonDocument searchDocument = await GetJsonAsync(
                        BuildSearchPath(subreddit, searchText, searchRequest, postsPerSubreddit),
                        cancellationToken).ConfigureAwait(false);
                    foreach (RedditPost post in ReadPosts(searchDocument.RootElement, status, query, searchText))
                    {
                        if (!string.IsNullOrWhiteSpace(post.Id))
                        {
                            postsById.TryAdd(post.Id, post);
                        }
                    }
                }

                List<RedditPost> selectedPosts = postsById.Values
                    .Where(post => IsRecentEnough(post.Evidence.CreatedAt, earliestCreatedAt))
                    .OrderByDescending(post => post.Evidence.Score ?? 0)
                    .ThenByDescending(post => post.Evidence.CreatedAt ?? DateTimeOffset.MinValue)
                    .Take(postsPerSubreddit)
                    .ToList();
                foreach (RedditPost post in selectedPosts)
                {
                    report.Discussions.Add(post.Evidence);
                    report.Discussions.AddRange(await GetCommentsAsync(post.Id, post.Title, searchText, status, query, commentsPerPost, cancellationToken)
                        .ConfigureAwait(false));
                }
            }
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            status.Status = CorpusSourceStatuses.AccessBlocked;
            status.Enabled = false;
            status.LastCheckedAt = checkedAt;
            status.Notes.Add(status.StableApi
                ? "Reddit returned HTTP 403 for the OAuth API path."
                : "Reddit returned HTTP 403; public JSON access may be blocked or require OAuth credentials.");
            if (status.StableApi)
            {
                report.Notes.Add("Reddit discussion evidence was skipped because Reddit returned HTTP 403 for configured OAuth credentials. Continuing without Reddit source evidence.");
                return report;
            }

            status.Notes.Add(
                "Configure MtgMcp:Reddit OAuth credentials, or leave the source disabled for Reddit-backed evidence.");
            sourceHealth.Remember(
                CorpusSourceStatuses.AccessBlocked,
                checkedAt,
                [
                    "Last public JSON check returned HTTP 403.",
                    "Configure MtgMcp:Reddit OAuth credentials for the official API path."
                ]);
            report.Notes.Add("Reddit discussion evidence was skipped because Reddit returned HTTP 403. Continuing without Reddit source evidence.");
            return report;
        }
        catch (RedditOAuthException exception)
        {
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            status.Status = exception.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? CorpusSourceStatuses.AccessBlocked
                : CorpusSourceStatuses.Failed;
            status.Enabled = false;
            status.LastCheckedAt = checkedAt;
            status.Notes.Add(exception.Message);
            report.Notes.Add("Reddit discussion evidence was skipped because Reddit OAuth token acquisition failed. Continuing without Reddit source evidence.");
            return report;
        }

        await AnnotateMentionedCardsAsync(report.Discussions, query, report.Notes, cancellationToken)
            .ConfigureAwait(false);
        AddSignalsFromDiscussions(report, status, budget.MaxCandidates);
        report.Notes.Add(
            $"Reddit evidence is raw bounded discussion data from posts within the last {DiscussionLookbackYears} years; " +
            "mtg-mcp does not infer sentiment or card quality from comment text.");
        DateTimeOffset successCheckedAt = DateTimeOffset.UtcNow;
        status.LastCheckedAt = successCheckedAt;
        if (!status.StableApi)
        {
            sourceHealth.Remember(
                CorpusSourceStatuses.Available,
                successCheckedAt,
                ["Last public JSON check completed successfully."]);
        }

        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Applies the last live public JSON health observation to source_list status.
    /// </summary>
    private void ApplyObservedPublicJsonHealth(CorpusSourceStatus status)
    {
        if (!sourceHealth.TryGetLastObservation(
            out string observedStatus,
            out DateTimeOffset checkedAt,
            out IReadOnlyList<string> observedNotes))
        {
            return;
        }

        status.LastCheckedAt = checkedAt;
        foreach (string note in observedNotes)
        {
            status.Notes.Add(note);
        }

        if (observedStatus.Equals(CorpusSourceStatuses.AccessBlocked, StringComparison.OrdinalIgnoreCase))
        {
            status.Status = CorpusSourceStatuses.AccessBlocked;
            status.Enabled = false;
        }
    }

    /// <summary>
    /// Releases local synchronization resources.
    /// </summary>
    public void Dispose()
    {
        authLock.Dispose();
    }

}
