namespace MtgMcp.Core;

/// <summary>
/// Provides evidence-first Commander, payoff, and new-card swap workflows.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Gets source-backed aggregate cards for a commander.
    /// </summary>
    public async Task<CommanderAggregateCardsResult> GetCommanderAggregateCardsAsync(
        string commanderName,
        string? theme,
        string? source,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        string normalizedCommander = commanderName.Trim();
        string? normalizedTheme = NormalizeTheme(theme);
        int boundedLimit = Math.Clamp(limit, 1, 100);
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(AnalysisDepths.Balanced);
        budget.MaxCandidates = boundedLimit;
        budget.MaxRecommendations = boundedLimit;
        CommanderThemeResolution themeResolution = await ResolveCommanderThemeAsync(
            normalizedCommander,
            normalizedTheme,
            goal: null,
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        CorpusSignalReport report = await CollectCommanderSignalsAsync(
            normalizedCommander,
            themeResolution.Theme,
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        List<CardCorpusSignal> aggregateSignals = report.Signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.CardName))
            .OrderBy(signal => signal.Source, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(signal => signal.Score)
            .ThenByDescending(signal => signal.DeckCount ?? 0)
            .ThenBy(signal => signal.CardName, StringComparer.OrdinalIgnoreCase)
            .Take(boundedLimit)
            .ToList();
        IReadOnlyDictionary<string, string?> scryfallUris = await ResolveScryfallUrisAsync(
            aggregateSignals.Select(signal => signal.CardName),
            cancellationToken).ConfigureAwait(false);

        CommanderAggregateCardsResult result = new()
        {
            CommanderName = normalizedCommander,
            Theme = themeResolution.Theme,
            Sources = MergeSourceStatuses(report.Sources),
            Cards = aggregateSignals.Select(signal => BuildAggregateRow(signal, scryfallUris)).ToList()
        };
        result.Notes.AddRange(themeResolution.Notes);
        result.Notes.AddRange(report.Notes);
        if (!string.IsNullOrWhiteSpace(themeResolution.Theme) && result.Cards.Count == 0)
        {
            AddUnsupportedThemeNote(result.Notes, normalizedTheme, themeResolution.Theme, themeResolution.SuggestedThemes);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            result.Notes.Add("source was omitted; rows are grouped by source and counts are not merged across unlike populations.");
        }

        return result;
    }

    /// <summary>
    /// Gets source-backed tags and theme sections for a commander.
    /// </summary>
    public async Task<CommanderTagsResult> GetCommanderTagsAsync(
        string commanderName,
        string? source,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        int boundedLimit = Math.Clamp(limit, 1, 100);
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(AnalysisDepths.Balanced);
        budget.MaxCandidates = boundedLimit * 2;
        CorpusSignalReport report = await CollectCommanderSignalsAsync(
            commanderName.Trim(),
            theme: null,
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        CommanderTagsResult result = new()
        {
            CommanderName = commanderName.Trim(),
            Sources = MergeSourceStatuses(report.Sources),
            Tags = report.Signals
                .Select(signal => new
                {
                    Tag = string.IsNullOrWhiteSpace(signal.Section) ? signal.SignalType : signal.Section,
                    signal.Source,
                    signal.DeckCount,
                    Uri = signal.Uri
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.Tag))
                .GroupBy(row => $"{row.Source}|{row.Tag}", StringComparer.OrdinalIgnoreCase)
                .Select(group => new CommanderTagRow
                {
                    Source = group.First().Source,
                    TagName = group.First().Tag,
                    ThemeSlug = SlugifySimple(group.First().Tag),
                    DeckCount = group.Any(row => row.DeckCount.HasValue)
                        ? group.Sum(row => row.DeckCount ?? 0)
                        : null,
                    Metadata = BuildMetadata(
                        group.First().Source,
                        "commander-aggregate-tag",
                        group.Select(row => row.Uri).FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
                        confidence: 0.75)
                })
                .OrderBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(row => row.DeckCount ?? 0)
                .ThenBy(row => row.TagName, StringComparer.OrdinalIgnoreCase)
                .Take(boundedLimit)
                .ToList()
        };
        result.Notes.AddRange(report.Notes);
        if (result.Tags.Count == 0)
        {
            result.Notes.Add("No configured source returned deterministic commander tag or theme rows.");
        }

        return result;
    }

    /// <summary>
    /// Finds payoff candidates for a route using deterministic Scryfall queries.
    /// </summary>
    public async Task<WinconPayoffSearchResult> FindWinconPayoffsAsync(
        string route,
        string colorIdentity,
        string format,
        decimal? maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        string normalizedRoute = NormalizeRoute(route);
        if (!WinRouteLabels.All.Contains(normalizedRoute, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("route must be one of the approved win-route labels.", nameof(route));
        }

        string normalizedFormat = NormalizeFormat(format);
        HashSet<string> colors = NormalizeColorIdentity(colorIdentity);
        string query = BuildPayoffQuery(normalizedRoute, colors, normalizedFormat, maxPrice);
        int boundedLimit = Math.Clamp(limit, 1, 50);
        IReadOnlyList<CardSearchResult> searchResults = await CardCatalog.SearchCardsAsync(
            query,
            Math.Clamp(boundedLimit * 3, 10, 100),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> details = await CardCatalog.GetCardsByNamesAsync(
            searchResults.Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            cancellationToken).ConfigureAwait(false);

        WinconPayoffSearchResult result = new()
        {
            Route = normalizedRoute,
            ColorIdentity = colors.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            Format = normalizedFormat,
            ScryfallQuery = query
        };
        foreach (CardSearchResult searchResult in searchResults)
        {
            if (!details.TryGetValue(searchResult.Name, out CardInfo? card))
            {
                continue;
            }

            bool legal = IsLegalInFormat(card, normalizedFormat);
            bool colorOk = IsInDeckColorIdentity(card, colorIdentityKnown: true, colors);
            if (!legal || !colorOk || (maxPrice.HasValue && ReadUsdPrice(card).GetValueOrDefault(decimal.MaxValue) > maxPrice.Value))
            {
                continue;
            }

            result.Candidates.Add(new WinconPayoffCandidate
            {
                CardName = card.Name,
                WhyItMatches = $"{card.Name} matched the fixed {normalizedRoute} Scryfall payoff query.",
                LegalInFormat = legal,
                ColorIdentityOk = colorOk,
                Price = ReadUsdPrice(card),
                EdhrecRank = card.EdhrecRank,
                ScryfallUri = card.ScryfallUri,
                Metadata = BuildMetadata("scryfall", "payoff-candidate-search", card.ScryfallUri, confidence: 0.70)
            });
            if (result.Candidates.Count >= boundedLimit)
            {
                break;
            }
        }

        result.Notes.Add("Payoff rows are Scryfall-query-derived candidates, not popularity evidence unless joined with aggregate source rows.");
        return result;
    }

    /// <summary>
    /// Bundles structured win-condition evidence for one commander.
    /// </summary>
    public async Task<CommanderWinConditionEvidenceResult> GetCommanderWinConditionEvidenceAsync(
        string commanderName,
        string? theme,
        bool strictColorIdentity,
        IReadOnlyList<string>? sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        List<string> requestedSources = NormalizeSources(sources);
        CommanderAggregateCardsResult aggregate = await GetAggregateForSourcesAsync(
            commanderName,
            theme,
            requestedSources,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
        CommanderTagsResult tags = await GetTagsForSourcesAsync(
            commanderName,
            requestedSources,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
        ComboEvidenceSearchResult combos = await analysis.SearchCombosByCardAsync(
            commanderName,
            "commander",
            commanderName,
            strictColorIdentity,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
        List<WinRouteClassification> routes = combos.Combos
            .SelectMany(combo => combo.RouteClassifications)
            .ToList();
        HashSet<string> payoffRoutes = routes
            .Where(route => route.NeedsPayoff)
            .SelectMany(route => route.RouteTypes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        CardInfo? commander = await CardCatalog.GetCardAsync(commanderName, cancellationToken).ConfigureAwait(false);
        string commanderColorIdentity = string.Join("", commander?.ColorIdentity ?? []);
        List<WinconPayoffSearchResult> payoffSearches = [];
        List<string> bundleNotes = [];
        if (commander is null && payoffRoutes.Count > 0)
        {
            bundleNotes.Add("Could not determine commander color identity from Scryfall, so payoff searches were not run.");
        }
        else
        {
            foreach (string payoffRoute in payoffRoutes.Take(5))
            {
                payoffSearches.Add(await FindWinconPayoffsAsync(
                    payoffRoute,
                    commanderColorIdentity,
                    "commander",
                    maxPrice: null,
                    limit: Math.Min(limit, 10),
                    cancellationToken).ConfigureAwait(false));
            }
        }

        CommanderWinConditionEvidenceResult result = new()
        {
            CommanderName = commander?.Name ?? commanderName.Trim(),
            Theme = NormalizeTheme(theme),
            AggregateCards = aggregate,
            Tags = tags,
            Combos = combos,
            RouteClassifications = routes,
            PayoffSearches = payoffSearches
        };
        result.Notes.Add("This bundle returns structured evidence only; the LLM should synthesize conclusions separately.");
        result.Notes.AddRange(bundleNotes);
        result.Notes.AddRange(aggregate.Notes);
        result.Notes.AddRange(combos.Notes);
        return result;
    }

    /// <summary>
    /// Reviews newly released card candidates and deterministic cuts.
    /// </summary>
    public async Task<NewCardSwapReviewResult> ReviewNewCardSwapsAsync(
        string workspaceId,
        string? since,
        string? setCode,
        decimal? maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        NewCardsForDeckResult newCards = await FindNewCardsForDeckAsync(
            workspaceId,
            since,
            setCode,
            limit,
            maxPrice,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> candidateCards = await CardCatalog.GetCardsByNamesAsync(
            newCards.Suggestions.Select(suggestion => suggestion.CardName).ToList(),
            cancellationToken).ConfigureAwait(false);
        NewCardSwapReviewResult result = new()
        {
            WorkspaceId = workspace.Id
        };
        foreach (NewCardSuggestion suggestion in newCards.Suggestions)
        {
            candidateCards.TryGetValue(suggestion.CardName, out CardInfo? candidateInfo);
            DeckCard candidateCard = candidateInfo is null
                ? new DeckCard { Name = suggestion.CardName, PrimaryCategory = suggestion.Role }
                : CreateCandidateCard(candidateInfo);
            CardRoleAssignment candidateRole = DeckRoleClassifier.Classify(candidateCard);
            result.Candidates.Add(new NewCardSwapCandidate
            {
                CardName = suggestion.CardName,
                Role = suggestion.Role,
                Tags = suggestion.Tags,
                ReleasedAt = suggestion.ReleasedAt,
                Set = suggestion.Set,
                Price = suggestion.Price,
                ScryfallUri = candidateInfo?.ScryfallUri ?? suggestion.ScryfallUri,
                Score = suggestion.Score,
                Rationale = suggestion.Rationale,
                CutCandidates = BuildCutEvidence(workspace, intent, candidateRole, candidateInfo, suggestion.Price)
                    .Take(5)
                    .ToList(),
                Metadata = BuildMetadata("scryfall", "recent-card-swap-review", candidateInfo?.ScryfallUri ?? suggestion.ScryfallUri, confidence: 0.70)
            });
        }

        result.Notes.AddRange(newCards.Notes);
        result.Notes.Add("Cut evidence is deterministic: role overlap, mana curve slot, duplicate effect density, theme mismatch, price delta, and protected-card warnings.");
        return result;
    }

    /// <summary>
    /// Collects commander signals directly from source providers without a workspace.
    /// </summary>
    private async Task<CorpusSignalReport> CollectCommanderSignalsAsync(
        string commanderName,
        string? theme,
        string? source,
        RecommendationAnalysisBudget budget,
        bool refresh,
        CancellationToken cancellationToken)
    {
        CorpusSignalQuery query = new()
        {
            Format = "commander",
            Commander = commanderName,
            Theme = theme,
            Refresh = refresh
        };
        CorpusSignalReport combined = new();
        bool sourceFilterActive = !string.IsNullOrWhiteSpace(source);
        bool matchedSource = false;
        int queriedSources = 0;
        foreach (ICorpusSignalProvider provider in corpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (sourceFilterActive && !MatchesSourceFilter(status, source))
            {
                continue;
            }

            matchedSource = true;
            combined.Sources.Add(status);
            if (!status.Enabled || queriedSources >= budget.MaxSources)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(theme) && !SupportsCommanderTheme(status))
            {
                combined.Notes.Add(
                    $"unsupported-theme: {status.Name} does not expose deterministic commander theme slugs; skipped theme lookup.");
                continue;
            }

            queriedSources++;
            try
            {
                CorpusSignalReport report = await provider.GetSignalsAsync(query, budget, cancellationToken)
                    .ConfigureAwait(false);
                combined.Signals.AddRange(report.Signals);
                combined.Sources.AddRange(report.Sources);
                combined.Notes.AddRange(report.Notes);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                status.Status = CorpusSourceStatuses.Failed;
                status.Notes.Add($"{exception.GetType().Name}: {exception.Message}");
                combined.Notes.Add($"{status.Name} failed; continuing with remaining recommendation sources.");
            }
        }

        if (sourceFilterActive && !matchedSource)
        {
            combined.Notes.Add($"No configured recommendation source matched '{source}'.");
        }

        combined.Sources = MergeSourceStatuses(combined.Sources);
        return combined;
    }

    /// <summary>
    /// Maps noisy goal text onto a bounded set of deterministic commander theme slugs.
    /// </summary>
    private async Task<CommanderThemeResolution> ResolveCommanderThemeAsync(
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
        foreach (ICorpusSignalProvider provider in corpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (!MatchesSourceFilter(status, source)
                || !status.Enabled
                || !SupportsCommanderTheme(status))
            {
                continue;
            }

            try
            {
                CorpusSignalReport report = await provider.GetSignalsAsync(query, budget, cancellationToken)
                    .ConfigureAwait(false);
                foreach (CardCorpusSignal signal in report.Signals)
                {
                    string tag = string.IsNullOrWhiteSpace(signal.Section) ? signal.SignalType : signal.Section;
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        continue;
                    }

                    string slug = SlugifySimple(tag);
                    if (string.IsNullOrWhiteSpace(slug)
                        || candidates.ContainsKey(slug))
                    {
                        continue;
                    }

                    candidates.Add(slug, new CommanderThemeCandidate(tag, slug, signal.DeckCount ?? 0));
                }
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                continue;
            }
        }

        List<CommanderThemeCandidate> sortedCandidates = candidates.Values.ToList();
        sortedCandidates.Sort((left, right) =>
        {
            int countComparison = right.DeckCount.CompareTo(left.DeckCount);
            return countComparison != 0
                ? countComparison
                : string.Compare(left.ThemeSlug, right.ThemeSlug, StringComparison.OrdinalIgnoreCase);
        });
        return sortedCandidates;
    }

    /// <summary>
    /// Checks whether any matching source can answer deterministic commander theme lookups.
    /// </summary>
    private bool HasCommanderThemeSource(string? source)
    {
        foreach (ICorpusSignalProvider provider in corpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (MatchesSourceFilter(status, source)
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
    /// Adds a compact unsupported-theme note with suggested alternatives when available.
    /// </summary>
    private static void AddUnsupportedThemeNote(
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
        return KnownCommanderThemeAliases()
            .Select(theme => theme.Slug)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        string text = string.Join(' ', new[] { theme, goal }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        char[] characters = text.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            if (!char.IsLetterOrDigit(characters[i]))
            {
                characters[i] = ' ';
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
    /// Determines whether a source can answer commander theme lookups without fuzzy inference.
    /// </summary>
    private static bool SupportsCommanderTheme(CorpusSourceStatus status)
    {
        return status.Kind.Equals("commander-aggregate", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Carries the theme slug chosen for a commander lookup plus user-facing notes.
    /// </summary>
    private sealed record CommanderThemeResolution(
        string? Theme,
        List<string> Notes,
        List<string> SuggestedThemes);

    /// <summary>
    /// Represents one source-provided commander theme or tag candidate.
    /// </summary>
    private sealed record CommanderThemeCandidate(
        string TagName,
        string ThemeSlug,
        int DeckCount);

    /// <summary>
    /// Gets aggregate rows from the requested source set without merging source populations.
    /// </summary>
    private async Task<CommanderAggregateCardsResult> GetAggregateForSourcesAsync(
        string commanderName,
        string? theme,
        IReadOnlyList<string> sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return await GetCommanderAggregateCardsAsync(
                commanderName,
                theme,
                source: null,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        if (sources.Count == 1)
        {
            return await GetCommanderAggregateCardsAsync(
                commanderName,
                theme,
                sources[0],
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        int boundedLimit = Math.Clamp(limit, 1, 100);
        CommanderAggregateCardsResult combined = new()
        {
            CommanderName = commanderName.Trim(),
            Theme = NormalizeTheme(theme)
        };
        foreach (string source in sources)
        {
            CommanderAggregateCardsResult sourceResult = await GetCommanderAggregateCardsAsync(
                commanderName,
                theme,
                source,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
            combined.Cards.AddRange(sourceResult.Cards);
            combined.Sources.AddRange(sourceResult.Sources);
            combined.Notes.AddRange(sourceResult.Notes);
        }

        combined.Sources = MergeSourceStatuses(combined.Sources);
        combined.Cards.Sort(CompareAggregateRows);
        if (combined.Cards.Count > boundedLimit)
        {
            combined.Cards.RemoveRange(boundedLimit, combined.Cards.Count - boundedLimit);
        }

        combined.Notes.Add("sources were queried separately; counts are not merged across unlike populations.");
        return combined;
    }

    /// <summary>
    /// Gets tag rows from the requested source set without merging unlike populations.
    /// </summary>
    private async Task<CommanderTagsResult> GetTagsForSourcesAsync(
        string commanderName,
        IReadOnlyList<string> sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return await GetCommanderTagsAsync(
                commanderName,
                source: null,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        if (sources.Count == 1)
        {
            return await GetCommanderTagsAsync(
                commanderName,
                sources[0],
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        int boundedLimit = Math.Clamp(limit, 1, 100);
        CommanderTagsResult combined = new()
        {
            CommanderName = commanderName.Trim()
        };
        foreach (string source in sources)
        {
            CommanderTagsResult sourceResult = await GetCommanderTagsAsync(
                commanderName,
                source,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
            combined.Tags.AddRange(sourceResult.Tags);
            combined.Sources.AddRange(sourceResult.Sources);
            combined.Notes.AddRange(sourceResult.Notes);
        }

        combined.Sources = MergeSourceStatuses(combined.Sources);
        combined.Tags.Sort(CompareTagRows);
        if (combined.Tags.Count > boundedLimit)
        {
            combined.Tags.RemoveRange(boundedLimit, combined.Tags.Count - boundedLimit);
        }

        combined.Notes.Add("sources were queried separately; tag counts are not merged across unlike populations.");
        return combined;
    }

    /// <summary>
    /// Converts one corpus signal into a public commander aggregate row.
    /// </summary>
    private static CommanderAggregateCardRow BuildAggregateRow(
        CardCorpusSignal signal,
        IReadOnlyDictionary<string, string?> scryfallUris)
    {
        return new CommanderAggregateCardRow
        {
            CardName = signal.CardName,
            Source = signal.Source,
            Section = string.IsNullOrWhiteSpace(signal.Section) ? signal.SignalType : signal.Section,
            DeckCount = signal.DeckCount,
            EligibleDeckCount = signal.EligibleDeckCount,
            InclusionRate = signal.InclusionRate,
            SynergyScore = signal.SynergyScore,
            Score = signal.Score,
            ScryfallUri = ResolveScryfallUri(signal.CardName, signal.ScryfallUri, scryfallUris),
            Metadata = BuildMetadata(signal.Source, "commander-aggregate-card", signal.Uri, signal.Score)
        };
    }

    /// <summary>
    /// Builds deterministic cut evidence for one candidate.
    /// </summary>
    private static List<NewCardCutEvidence> BuildCutEvidence(
        DeckWorkspace workspace,
        DeckIntent? intent,
        CardRoleAssignment candidateRole,
        CardInfo? candidateInfo,
        decimal? candidatePrice)
    {
        List<DeckCard> included = IncludedCards(workspace).Where(card => !IsCommanderCard(card)).ToList();
        Dictionary<string, int> roleCounts = included
            .Select(card => DeckRoleClassifier.Classify(card).PrimaryRole)
            .GroupBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        List<NewCardCutEvidence> cuts = [];
        foreach (DeckCard card in included)
        {
            CardRoleAssignment currentRole = DeckRoleClassifier.Classify(card);
            bool roleOverlap = currentRole.PrimaryRole.Equals(candidateRole.PrimaryRole, StringComparison.OrdinalIgnoreCase)
                || currentRole.Tags.Intersect(candidateRole.Tags, StringComparer.OrdinalIgnoreCase).Any();
            bool curveSlot = IsSameCurveSlot(GetSnapshot(card).ManaValue, candidateInfo?.ManaValue);
            double duplicateDensity = roleCounts.TryGetValue(currentRole.PrimaryRole, out int count)
                ? Math.Clamp(count / 10.0, 0, 1)
                : 0;
            bool themeMismatch = candidateRole.Tags.Count > 0
                && !currentRole.Tags.Intersect(candidateRole.Tags, StringComparer.OrdinalIgnoreCase).Any()
                && !currentRole.PrimaryRole.Equals(candidateRole.PrimaryRole, StringComparison.OrdinalIgnoreCase);
            decimal? currentPrice = ReadUsdPrice(GetSnapshot(card));
            decimal? priceDelta = currentPrice.HasValue && candidatePrice.HasValue
                ? currentPrice.Value - candidatePrice.Value
                : null;
            List<string> protectedWarnings = [];
            if (DeckIntentProtection.IsProtectedCard(card, intent))
            {
                protectedWarnings.Add("Card is protected by deck intent.");
            }

            double score = 0;
            score += roleOverlap ? 0.45 : 0;
            score += curveSlot ? 0.20 : 0;
            score += duplicateDensity * 0.20;
            score += themeMismatch ? 0.10 : 0;
            score += priceDelta is > 0 ? 0.05 : 0;
            if (protectedWarnings.Count > 0)
            {
                score *= 0.25;
            }

            NewCardCutEvidence evidence = new()
            {
                CardName = card.Name,
                Role = currentRole.PrimaryRole,
                RoleOverlap = roleOverlap,
                ManaCurveSlot = curveSlot,
                DuplicateEffectDensity = duplicateDensity,
                ThemeMismatch = themeMismatch,
                PriceDelta = priceDelta,
                ScryfallUri = GetSnapshot(card).ScryfallUri,
                ProtectedCardWarnings = protectedWarnings,
                Score = Math.Clamp(score, 0, 1)
            };
            AddCutReasons(evidence);
            cuts.Add(evidence);
        }

        return cuts
            .OrderByDescending(cut => cut.Score)
            .ThenBy(cut => cut.CardName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds exact scoring reasons to a cut row.
    /// </summary>
    private static void AddCutReasons(NewCardCutEvidence evidence)
    {
        if (evidence.RoleOverlap)
        {
            evidence.Reasons.Add("Role or tag overlaps the new card.");
        }

        if (evidence.ManaCurveSlot)
        {
            evidence.Reasons.Add("Mana value is in the same curve slot.");
        }

        if (evidence.DuplicateEffectDensity > 0)
        {
            evidence.Reasons.Add($"Duplicate effect density for role is {evidence.DuplicateEffectDensity:0.00}.");
        }

        if (evidence.ThemeMismatch)
        {
            evidence.Reasons.Add("Existing card has weaker tag overlap with the new card's route/theme.");
        }

        if (evidence.PriceDelta is > 0)
        {
            evidence.Reasons.Add("Candidate is cheaper than the existing card.");
        }
    }

    /// <summary>
    /// Checks whether two mana values share a curve slot.
    /// </summary>
    private static bool IsSameCurveSlot(double? current, double? candidate)
    {
        return current.HasValue && candidate.HasValue && Math.Abs(current.Value - candidate.Value) <= 1;
    }

    /// <summary>
    /// Normalizes optional source filters for bundle tools.
    /// </summary>
    private static List<string> NormalizeSources(IReadOnlyList<string>? sources)
    {
        if (sources is null)
        {
            return [];
        }

        List<string> normalizedSources = [];
        foreach (string source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)
                || normalizedSources.Contains(source.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedSources.Add(source.Trim());
        }

        return normalizedSources;
    }

    /// <summary>
    /// Sorts aggregate rows by source population and source-provided scores.
    /// </summary>
    private static int CompareAggregateRows(CommanderAggregateCardRow left, CommanderAggregateCardRow right)
    {
        int source = string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
        if (source != 0)
        {
            return source;
        }

        int score = right.Score.CompareTo(left.Score);
        if (score != 0)
        {
            return score;
        }

        int deckCount = (right.DeckCount ?? 0).CompareTo(left.DeckCount ?? 0);
        return deckCount != 0
            ? deckCount
            : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts tag rows by source and source-provided deck counts.
    /// </summary>
    private static int CompareTagRows(CommanderTagRow left, CommanderTagRow right)
    {
        int source = string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
        if (source != 0)
        {
            return source;
        }

        int deckCount = (right.DeckCount ?? 0).CompareTo(left.DeckCount ?? 0);
        return deckCount != 0
            ? deckCount
            : string.Compare(left.TagName, right.TagName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a route label.
    /// </summary>
    private static string NormalizeRoute(string route)
    {
        return route.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes color identity text such as WUBRG, U,B, or colorless.
    /// </summary>
    private static HashSet<string> NormalizeColorIdentity(string colorIdentity)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        foreach (char character in colorIdentity.ToUpperInvariant())
        {
            string color = character.ToString();
            if ("WUBRG".Contains(color, StringComparison.Ordinal))
            {
                colors.Add(color);
            }
        }

        return colors;
    }

    /// <summary>
    /// Builds a Scryfall query for payoff candidates.
    /// </summary>
    private static string BuildPayoffQuery(string route, HashSet<string> colors, string format, decimal? maxPrice)
    {
        string expression = route switch
        {
            WinRouteLabels.InfiniteMana => "(o:\"{X}\" or o:\"x damage\" or o:\"draw x\" or o:\"each opponent loses x\")",
            WinRouteLabels.Storm => "(o:storm or o:\"copy target instant\" or o:\"copy target sorcery\" or o:\"whenever you cast\")",
            WinRouteLabels.DrawDeck => "(o:\"win the game\" or o:\"if you would draw\" or o:\"no cards in your library\")",
            WinRouteLabels.SelfMill => "(o:\"win the game\" o:graveyard or o:\"no cards in your library\")",
            WinRouteLabels.Etb => "(o:\"whenever\" o:\"enters the battlefield\" o:\"each opponent\")",
            WinRouteLabels.Tokens => "(o:\"tokens you control\" or o:\"creatures you control get\" or o:\"whenever you create\")",
            WinRouteLabels.Aristocrats => "(o:\"whenever\" o:dies o:\"each opponent loses\" or o:sacrifice o:\"each opponent loses\")",
            WinRouteLabels.Combat or WinRouteLabels.ValueCombat => "(o:\"creatures you control get\" or o:\"extra combat\" or o:trample)",
            WinRouteLabels.OpponentMill => "(o:\"each opponent mills\" or o:\"target opponent mills\")",
            WinRouteLabels.ExtraTurns => "(o:\"extra turn\" or o:\"additional turn\")",
            WinRouteLabels.AlternateWin => "o:\"win the game\"",
            _ => ""
        };
        List<string> parts = [expression, $"legal:{format}"];
        if (colors.Count > 0)
        {
            parts.Add($"id<={string.Concat(colors.Order(StringComparer.OrdinalIgnoreCase)).ToLowerInvariant()}");
        }
        else
        {
            parts.Add("id<=c");
        }

        if (maxPrice.HasValue)
        {
            parts.Add($"usd<={maxPrice.Value:0.##}");
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// Normalizes optional theme text without inferring unsupported aliases.
    /// </summary>
    private static string? NormalizeTheme(string? theme)
    {
        return string.IsNullOrWhiteSpace(theme) ? null : theme.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Builds a simple URL slug from source-provided display text.
    /// </summary>
    private static string SlugifySimple(string value)
    {
        return string.Join(
            '-',
            value.ToLowerInvariant()
                .Split([' ', '_', '/', '\\', ':'], StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Builds source metadata for deterministic evidence rows.
    /// </summary>
    private static SourceEvidenceMetadata BuildMetadata(
        string source,
        string sourceKind,
        string? sourceUri,
        double confidence)
    {
        return new SourceEvidenceMetadata
        {
            Source = source,
            SourceKind = sourceKind,
            SourceUri = sourceUri,
            CacheStatus = "live-or-cache",
            Confidence = Math.Clamp(confidence, 0, 1),
            Deterministic = true
        };
    }
}
