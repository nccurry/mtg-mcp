using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Contains Reddit JSON request and response parsing helpers.
/// </summary>
public sealed partial class RedditDiscussionCorpusSignalProvider
{
    /// <summary>
    /// Reads JSON from Reddit and rejects HTML payloads.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateRedditRequestAsync(path, cancellationToken)
            .ConfigureAwait(false);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
            MentionedCards = ExtractTrustedCardReferences(combined, query),
            LinkedDeckUris = ExtractLinkedDeckUris(combined)
        };
    }

}
