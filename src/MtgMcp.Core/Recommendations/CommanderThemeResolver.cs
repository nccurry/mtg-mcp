namespace MtgMcp.Core;

/// <summary>
/// Resolves user-provided Commander theme hints into deterministic source theme slugs.
/// </summary>
public sealed class CommanderThemeResolver
{
    /// <summary>
    /// Supplies aggregate Commander tags used to validate or refine requested themes.
    /// </summary>
    private readonly IReadOnlyList<ICorpusSignalProvider> providers;

    /// <summary>
    /// Creates a resolver over the configured corpus signal providers.
    /// </summary>
    public CommanderThemeResolver(IEnumerable<ICorpusSignalProvider>? providers)
    {
        this.providers = providers?.ToList() ?? [];
    }

    /// <summary>
    /// Maps noisy theme or goal text onto a bounded set of deterministic Commander theme slugs.
    /// </summary>
    internal async Task<CommanderThemeResolution> ResolveAsync(
        string? commanderName,
        string? requestedTheme,
        string? goal,
        string? source,
        RecommendationAnalysisBudget budget,
        bool refresh,
        CancellationToken cancellationToken)
    {
        string? normalizedTheme = NormalizeTheme(requestedTheme);
        if (string.IsNullOrWhiteSpace(commanderName)
            || !HasCommanderThemeSource(source))
        {
            return new CommanderThemeResolution(normalizedTheme, [], KnownCommanderThemeSlugs());
        }

        string hintText = NormalizeThemeHintText(normalizedTheme, goal);
        if (string.IsNullOrWhiteSpace(hintText))
        {
            return new CommanderThemeResolution(normalizedTheme, [], KnownCommanderThemeSlugs());
        }

        string? obviousTheme = MatchKnownCommanderTheme(hintText);
        if (!string.IsNullOrWhiteSpace(obviousTheme)
            && !obviousTheme.Equals(normalizedTheme, StringComparison.OrdinalIgnoreCase))
        {
            return new CommanderThemeResolution(
                obviousTheme,
                [$"theme-resolved: commander context matched obvious theme '{obviousTheme}'."],
                KnownCommanderThemeSlugs());
        }

        if (!ShouldInspectCommanderThemeTags(normalizedTheme))
        {
            return new CommanderThemeResolution(normalizedTheme, [], KnownCommanderThemeSlugs());
        }

        List<CommanderThemeCandidate> candidates = await GetCommanderThemeCandidatesAsync(
            commanderName.Trim(),
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        List<string> suggestions = SuggestedCommanderThemes(candidates);
        string? matchedTheme = MatchCommanderThemeCandidate(hintText, candidates);
        if (!string.IsNullOrWhiteSpace(matchedTheme)
            && !matchedTheme.Equals(normalizedTheme, StringComparison.OrdinalIgnoreCase))
        {
            return new CommanderThemeResolution(
                matchedTheme,
                [$"theme-resolved: commander page tags matched source theme '{matchedTheme}'."],
                suggestions);
        }

        return new CommanderThemeResolution(normalizedTheme, [], suggestions);
    }

    /// <summary>
    /// Adds a compact unsupported-theme note with suggested alternatives when available.
    /// </summary>
    internal static void AddUnsupportedThemeNote(
        List<string> notes,
        string? requestedTheme,
        string attemptedTheme,
        IReadOnlyList<string> suggestedThemes)
    {
        string themeText = string.IsNullOrWhiteSpace(requestedTheme)
            || requestedTheme.Equals(attemptedTheme, StringComparison.OrdinalIgnoreCase)
                ? $"theme '{attemptedTheme}'"
                : $"requested theme '{requestedTheme}' resolved to '{attemptedTheme}'";
        if (suggestedThemes.Count == 0)
        {
            notes.Add($"unsupported-theme: no configured source returned deterministic rows for {themeText}.");
            return;
        }

        notes.Add(
            $"unsupported-theme: no configured source returned deterministic rows for {themeText}. " +
            $"Suggested alternatives: {string.Join(", ", suggestedThemes.Take(8))}.");
    }

    /// <summary>
    /// Normalizes optional theme text without inferring unsupported aliases.
    /// </summary>
    internal static string? NormalizeTheme(string? theme)
    {
        return string.IsNullOrWhiteSpace(theme) ? null : theme.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Builds a simple URL slug from source-provided display text.
    /// </summary>
    internal static string SlugifySimple(string value)
    {
        return string.Join(
            '-',
            value.ToLowerInvariant()
                .Split([' ', '_', '/', '\\', ':'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Determines whether a source can answer commander theme lookups without fuzzy inference.
    /// </summary>
    internal static bool SupportsCommanderTheme(CorpusSourceStatus status)
    {
        return status.Kind.Equals("commander-aggregate", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads candidate theme tags from commander-aggregate sources only.
    /// </summary>
    private async Task<List<CommanderThemeCandidate>> GetCommanderThemeCandidatesAsync(
        string commanderName,
        string? source,
        RecommendationAnalysisBudget budget,
        bool refresh,
        CancellationToken cancellationToken)
    {
        CorpusSignalQuery query = new()
        {
            Format = "commander",
            Commander = commanderName,
            Refresh = refresh
        };
        Dictionary<string, CommanderThemeCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (ICorpusSignalProvider provider in providers)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (!CorpusSourceStatusHelpers.MatchesSourceFilter(status, source)
                || !status.Enabled
                || !SupportsCommanderTheme(status))
            {
                continue;
            }

            try
            {
                CorpusSignalReport report = await provider.GetSignalsAsync(query, budget, cancellationToken)
                    .ConfigureAwait(false);
                AddCommanderThemeCandidates(candidates, report.Signals);
            }
            catch (Exception exception) when (!DeckServiceHelpers.IsCancellation(exception))
            {
                continue;
            }
        }

        List<CommanderThemeCandidate> sortedCandidates = candidates.Values.ToList();
        sortedCandidates.Sort(CompareCandidates);
        return sortedCandidates;
    }

    /// <summary>
    /// Adds distinct, nonblank commander themes from one attributed source report.
    /// </summary>
    private static void AddCommanderThemeCandidates(
        Dictionary<string, CommanderThemeCandidate> candidates,
        IEnumerable<CardCorpusSignal> signals)
    {
        foreach (CardCorpusSignal signal in signals)
        {
            string tag = string.IsNullOrWhiteSpace(signal.Section) ? signal.SignalType : signal.Section;
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            string slug = SlugifySimple(tag);
            if (string.IsNullOrWhiteSpace(slug) || candidates.ContainsKey(slug))
            {
                continue;
            }

            candidates.Add(slug, new CommanderThemeCandidate(tag, slug, signal.DeckCount ?? 0));
        }
    }

    /// <summary>
    /// Checks whether any matching source can answer deterministic commander theme lookups.
    /// </summary>
    private bool HasCommanderThemeSource(string? source)
    {
        foreach (ICorpusSignalProvider provider in providers)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (CorpusSourceStatusHelpers.MatchesSourceFilter(status, source)
                && status.Enabled
                && SupportsCommanderTheme(status))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether source-provided commander tags are worth inspecting for a noisy requested theme.
    /// </summary>
    private static bool ShouldInspectCommanderThemeTags(string? normalizedTheme)
    {
        return !string.IsNullOrWhiteSpace(normalizedTheme)
            && normalizedTheme.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>
    /// Matches common commander theme words used in natural-language goals.
    /// </summary>
    private static string? MatchKnownCommanderTheme(string normalizedHintText)
    {
        foreach ((string slug, string[] aliases) in KnownCommanderThemeAliases())
        {
            foreach (string alias in aliases)
            {
                if (ContainsThemePhrase(normalizedHintText, alias))
                {
                    return slug;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Matches a source-provided commander page tag against normalized goal text.
    /// </summary>
    private static string? MatchCommanderThemeCandidate(
        string normalizedHintText,
        IReadOnlyList<CommanderThemeCandidate> candidates)
    {
        foreach (CommanderThemeCandidate candidate in candidates)
        {
            if (ContainsThemePhrase(normalizedHintText, candidate.ThemeSlug)
                || ContainsThemePhrase(normalizedHintText, candidate.TagName))
            {
                return candidate.ThemeSlug;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a prioritized suggestion list from source tags and known commander theme slugs.
    /// </summary>
    private static List<string> SuggestedCommanderThemes(IReadOnlyList<CommanderThemeCandidate> candidates)
    {
        List<string> suggestions = [];
        foreach (CommanderThemeCandidate candidate in candidates)
        {
            AddThemeSuggestion(suggestions, candidate.ThemeSlug);
        }

        foreach (string knownTheme in KnownCommanderThemeSlugs())
        {
            AddThemeSuggestion(suggestions, knownTheme);
        }

        return suggestions;
    }

    /// <summary>
    /// Gets known high-signal commander theme aliases that are safe to retry directly.
    /// </summary>
    private static IReadOnlyList<(string Slug, string[] Aliases)> KnownCommanderThemeAliases()
    {
        return
        [
            ("treasure", ["treasure", "treasures"]),
            ("artifacts", ["artifact", "artifacts"]),
            ("tokens", ["token", "tokens"]),
            ("aristocrats", ["aristocrat", "aristocrats", "sacrifice", "dies", "death triggers"]),
            ("outlaws", ["outlaw", "outlaws"])
        ];
    }

    /// <summary>
    /// Gets known commander theme slugs for unsupported-theme suggestions.
    /// </summary>
    private static List<string> KnownCommanderThemeSlugs()
    {
        List<string> slugs = [];
        foreach ((string slug, _) in KnownCommanderThemeAliases())
        {
            AddThemeSuggestion(slugs, slug);
        }

        return slugs;
    }

    /// <summary>
    /// Adds one theme slug to a suggestion list if it is usable and unique.
    /// </summary>
    private static void AddThemeSuggestion(List<string> suggestions, string theme)
    {
        if (!string.IsNullOrWhiteSpace(theme)
            && !suggestions.Contains(theme, StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add(theme);
        }
    }

    /// <summary>
    /// Normalizes free-form theme and goal text for word-boundary matching.
    /// </summary>
    private static string NormalizeThemeHintText(string? theme, string? goal)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(theme))
        {
            parts.Add(theme);
        }

        if (!string.IsNullOrWhiteSpace(goal))
        {
            parts.Add(goal);
        }

        string text = string.Join(' ', parts);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        char[] characters = text.ToLowerInvariant().ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            if (!char.IsLetterOrDigit(characters[index]))
            {
                characters[index] = ' ';
            }
        }

        return $" {string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries))} ";
    }

    /// <summary>
    /// Checks for a normalized theme phrase with word boundaries.
    /// </summary>
    private static bool ContainsThemePhrase(string normalizedHintText, string phrase)
    {
        string normalizedPhrase = NormalizeThemeHintText(phrase, null);
        return !string.IsNullOrWhiteSpace(normalizedPhrase)
            && normalizedHintText.Contains(normalizedPhrase, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orders source-provided theme candidates by popularity and slug.
    /// </summary>
    private static int CompareCandidates(CommanderThemeCandidate left, CommanderThemeCandidate right)
    {
        int countComparison = right.DeckCount.CompareTo(left.DeckCount);
        return countComparison != 0
            ? countComparison
            : string.Compare(left.ThemeSlug, right.ThemeSlug, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Carries the theme slug chosen for a commander lookup plus user-facing notes.
/// </summary>
internal sealed record CommanderThemeResolution(
    string? Theme,
    List<string> Notes,
    List<string> SuggestedThemes);

/// <summary>
/// Represents one source-provided commander theme or tag candidate.
/// </summary>
internal sealed record CommanderThemeCandidate(
    string TagName,
    string ThemeSlug,
    int DeckCount);
