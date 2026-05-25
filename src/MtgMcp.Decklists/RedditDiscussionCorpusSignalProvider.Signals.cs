using System.Text.RegularExpressions;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Contains discussion-to-signal extraction helpers.
/// </summary>
public sealed partial class RedditDiscussionCorpusSignalProvider
{
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
    /// Resolves bracketed, known, and plain-text card references in all sampled discussions.
    /// </summary>
    private async Task AnnotateMentionedCardsAsync(
        IReadOnlyList<DiscussionEvidence> discussions,
        CorpusSignalQuery query,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        Dictionary<DiscussionEvidence, List<string>> candidatesByDiscussion = new();
        List<string> allCandidates = [];
        foreach (DiscussionEvidence discussion in discussions)
        {
            string text = $"{discussion.Title}\n{discussion.Body}";
            List<string> candidates = ExtractPlainTextCardCandidates(text);
            candidatesByDiscussion[discussion] = candidates;
            allCandidates.AddRange(candidates);
        }

        IReadOnlyDictionary<string, CardInfo> validatedCards = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        List<string> candidateNames = allCandidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPlainTextCandidates)
            .ToList();
        if (candidateNames.Count > 0)
        {
            try
            {
                validatedCards = await cardCatalog.GetCardsByNamesAsync(candidateNames, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                notes.Add($"{exception.GetType().Name}: Reddit plain-text card-name validation failed; bracketed card references are still included.");
            }
        }

        foreach (DiscussionEvidence discussion in discussions)
        {
            List<string> names = [.. discussion.MentionedCards];
            foreach (string candidate in candidatesByDiscussion[discussion])
            {
                if (validatedCards.TryGetValue(candidate, out CardInfo? card))
                {
                    names.Add(card.Name);
                }
            }

            discussion.MentionedCards = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();
        }
    }

    /// <summary>
    /// Extracts card references that do not need external validation.
    /// </summary>
    private static List<string> ExtractTrustedCardReferences(string text, CorpusSignalQuery query)
    {
        List<string> names = DoubleBracketCardReferencePattern.Matches(text)
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
    /// Extracts linked decklist URLs from discussion text.
    /// </summary>
    private static List<string> ExtractLinkedDeckUris(string text)
    {
        return LinkedDeckUriPattern.Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Extracts title-case phrases that could be exact Magic card names.
    /// </summary>
    private static List<string> ExtractPlainTextCardCandidates(string text)
    {
        List<string> candidates = SingleBracketCardCandidatePattern.Matches(text)
            .Select(match => NormalizeCardReference(match.Groups["name"].Value))
            .Where(IsPlausiblePlainTextCardCandidate)
            .ToList();
        foreach (Match match in PlainTextCardCandidatePattern.Matches(text))
        {
            string candidate = NormalizePlainTextCardCandidate(match.Value);
            AddPlainTextCandidate(candidates, candidate);
            string[] words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int start = 0; start < words.Length; start++)
            {
                for (int length = 1; length <= Math.Min(6, words.Length - start); length++)
                {
                    AddPlainTextCandidate(candidates, string.Join(' ', words.Skip(start).Take(length)));
                }
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    /// <summary>
    /// Adds one plausible plain-text card-name candidate.
    /// </summary>
    private static void AddPlainTextCandidate(List<string> candidates, string candidate)
    {
        if (IsPlausiblePlainTextCardCandidate(candidate))
        {
            candidates.Add(candidate);
        }
    }

    /// <summary>
    /// Checks whether a title-case phrase is worth exact card-name validation.
    /// </summary>
    private static bool IsPlausiblePlainTextCardCandidate(string candidate)
    {
        string value = candidate.Trim(',', '.', ';', ':', '!', '?', ')', ']', '}');
        if (value.Length < 4 || value.Length > 120)
        {
            return false;
        }

        if (!value.Any(char.IsLetter) || value.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 6)
        {
            return false;
        }

        return words.Any(word => char.IsUpper(word[0]) || char.IsDigit(word[0]));
    }

}
