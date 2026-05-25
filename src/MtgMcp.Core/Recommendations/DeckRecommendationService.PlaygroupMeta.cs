using System.Collections.Concurrent;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Scores candidate cards against Playgroup-derived local-meta pressure.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Identifies meta pressure from decks that can assemble early wins.
    /// </summary>
    private const string FastComboPressure = "fast-combo";

    /// <summary>
    /// Identifies meta pressure from creature-forward combat decks.
    /// </summary>
    private const string CreatureCombatPressure = "creature-combat";

    /// <summary>
    /// Identifies meta pressure from token-heavy boards.
    /// </summary>
    private const string GoWideTokensPressure = "go-wide-tokens";

    /// <summary>
    /// Identifies meta pressure from graveyard recursion or sacrifice loops.
    /// </summary>
    private const string GraveyardRecursionPressure = "graveyard-recursion";

    /// <summary>
    /// Identifies meta pressure from stack interaction and draw-control decks.
    /// </summary>
    private const string StackControlPressure = "stack-control";

    /// <summary>
    /// Identifies meta pressure from artifact engines.
    /// </summary>
    private const string ArtifactEnginePressure = "artifact-engine";

    /// <summary>
    /// Identifies meta pressure from enchantment engines.
    /// </summary>
    private const string EnchantmentEnginePressure = "enchantment-engine";

    /// <summary>
    /// Identifies meta pressure from burn, slug, and other life-total attack decks.
    /// </summary>
    private const string LifePressure = "life-total-pressure";

    /// <summary>
    /// Identifies meta pressure from stax, prison, and tax effects.
    /// </summary>
    private const string StaxPressure = "stax-control";

    /// <summary>
    /// Caps aggregate candidate performance simulations so large batches stay responsive.
    /// </summary>
    private const int CandidatePerformanceSimulationBudget = 4_000;

    /// <summary>
    /// Keeps each candidate performance sample above the analyzer's minimum useful size.
    /// </summary>
    private const int CandidatePerformanceMinimumSimulations = 100;

    /// <summary>
    /// Limits concurrent read-only Archidekt imports while collecting local-meta deck evidence.
    /// </summary>
    private const int MetaDeckEvidenceParallelism = 4;

    /// <summary>
    /// Scores candidate cards using deterministic deck-plan, performance, meta, budget, and confidence factors.
    /// </summary>
    public async Task<PlaygroupMetaScoringResult> ScoreCardsForPlaygroupMetaAsync(
        string workspaceId,
        string playgroupIdOrUrl,
        IReadOnlyList<string>? candidateCards,
        int maxGames,
        int metaDeckLimit,
        int simulations,
        int maxTurn,
        int seed,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        if (playgroups is null)
        {
            throw new InvalidOperationException("Playgroup service is not configured, so local-meta scoring cannot run.");
        }

        int safeMetaDeckLimit = Math.Clamp(metaDeckLimit, 1, 10);
        int safeSimulations = Math.Clamp(simulations, 100, 2_000);
        int safeMaxTurn = Math.Clamp(maxTurn, 1, 10);
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntentResult intentResult = DeckIntentText.Extract(workspace.Description, workspace.Id);
        DeckIntent? intent = intentResult.Intent;
        decimal? effectiveMaxPrice = maxPrice ?? intent?.Budget.MaxCardPrice;
        PlaygroupDeckRankingResult rankings = await playgroups
            .RankDecksAsync(
                playgroupIdOrUrl,
                PlaygroupDeckRankingMetrics.EstimatedPower,
                minGames: 0,
                includeLowConfidence: true,
                maxGames,
                safeMetaDeckLimit,
                cancellationToken)
            .ConfigureAwait(false);

        ResolvedSimulationProfile profileResolution = simulationProfiles.Resolve(workspace, SimulationProfileIds.Auto, intent);
        List<string> candidateNames = CandidateNames(workspace, candidateCards);
        IReadOnlyDictionary<string, CardInfo> candidateDetails = await CardCatalog
            .GetCardsByNamesAsync(candidateNames, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlySet<string> gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        List<string> warnings = [.. rankings.Warnings, .. intentResult.Warnings, .. profileResolution.Warnings];
        List<PlaygroupDeckRanking> rankedMetaDecks = rankings.Rankings.Take(safeMetaDeckLimit).ToList();
        List<PlaygroupMetaDeckEvidence> metaDecks = await BuildMetaDeckEvidenceBatchAsync(
                rankedMetaDecks,
                rankings.Rankings.Count,
                cancellationToken)
            .ConfigureAwait(false);

        List<PlaygroupMetaPressureEvidence> pressures = AggregatePressures(metaDecks);
        int candidatePerformanceSimulations = BudgetCandidatePerformanceSimulations(
            candidateNames.Count,
            safeSimulations);
        if (candidatePerformanceSimulations < safeSimulations)
        {
            warnings.Add(
                $"Candidate performance simulations were capped at {candidatePerformanceSimulations} per card from requested {safeSimulations} to keep local-meta scoring responsive for {candidateNames.Count} candidates; baseline analysis still used {safeSimulations} simulations.");
        }

        if (candidateNames.Count == 0)
        {
            warnings.Add("No candidate cards were supplied and no cards were found in excluded workspace categories.");
            return new PlaygroupMetaScoringResult
            {
                WorkspaceId = workspace.Id,
                PlaygroupId = rankings.PlaygroupId,
                CandidateSource = "none",
                ProfileResolution = profileResolution,
                MetaDecks = metaDecks,
                MetaPressures = pressures,
                Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Notes =
                [
                    "Playgroup deck selection is derived from fetched game participations, not a direct full playgroup deck list.",
                    "Scores are deterministic heuristics; no candidates were available to score.",
                ],
            };
        }

        DeckPerformanceAnalysis baseline = DeckPerformanceAnalyzer.Analyze(
            workspace,
            SimulationProfileIds.Auto,
            safeSimulations,
            safeMaxTurn,
            seed,
            includeMulligans: true,
            cancellationToken,
            simulationProfiles);
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        List<PlaygroupMetaCandidateScore> scores = [];

        foreach (string candidateName in candidateNames)
        {
            CardInfo? card = candidateDetails.Values.FirstOrDefault(value =>
                value.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
            if (card is null)
            {
                DeckCard? workspaceCard = workspace.Cards.FirstOrDefault(value =>
                    value.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
                if (workspaceCard?.Snapshot is null)
                {
                    warnings.Add($"Candidate '{candidateName}' could not be resolved from the card catalog.");
                    continue;
                }

                card = CardInfoFromWorkspaceCard(workspaceCard);
            }

            scores.Add(ScoreMetaCandidate(
                workspace,
                card,
                baseline,
                pressures,
                profileResolution,
                intent,
                effectiveMaxPrice,
                gameChangers,
                colorKnown,
                colors,
                candidatePerformanceSimulations,
                safeMaxTurn,
                seed,
                cancellationToken));
        }

        PlaygroupMetaScoringResult result = new()
        {
            WorkspaceId = workspace.Id,
            PlaygroupId = rankings.PlaygroupId,
            CandidateSource = candidateCards is { Count: > 0 }
                ? "explicit-card-list"
                : "excluded-workspace-categories",
            ProfileResolution = profileResolution,
            MetaDecks = metaDecks,
            MetaPressures = pressures,
            CandidateScores = scores
                .OrderByDescending(score => score.OverallScore)
                .ThenBy(score => score.CardName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
        result.Notes.Add("Playgroup deck selection is derived from fetched game participations, not a direct full playgroup deck list.");
        result.Notes.Add("Archidekt decklists are imported read-only when a Playgroup deck exposes an Archidekt URL.");
        result.Notes.Add("Scores are deterministic heuristics; meta coverage and self-harm are evidence factors, not full matchup simulations.");
        return result;
    }

    /// <summary>
    /// Builds pressure evidence for ranked local-meta decks using bounded import parallelism.
    /// </summary>
    private async Task<List<PlaygroupMetaDeckEvidence>> BuildMetaDeckEvidenceBatchAsync(
        IReadOnlyList<PlaygroupDeckRanking> rankings,
        int rankingCount,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<string, Lazy<Task<DeckWorkspace>>> importCache = new(StringComparer.OrdinalIgnoreCase);
        using SemaphoreSlim gate = new(MetaDeckEvidenceParallelism);

        async Task<PlaygroupMetaDeckEvidence> BuildWithGateAsync(PlaygroupDeckRanking ranking)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await BuildMetaDeckEvidenceAsync(
                        ranking,
                        rankingCount,
                        importCache,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        Task<PlaygroupMetaDeckEvidence>[] tasks = rankings.Select(BuildWithGateAsync).ToArray();
        PlaygroupMetaDeckEvidence[] evidence = await Task.WhenAll(tasks).ConfigureAwait(false);
        return evidence.ToList();
    }

    /// <summary>
    /// Builds pressure evidence for one ranked local-meta deck.
    /// </summary>
    private async Task<PlaygroupMetaDeckEvidence> BuildMetaDeckEvidenceAsync(
        PlaygroupDeckRanking ranking,
        int rankingCount,
        ConcurrentDictionary<string, Lazy<Task<DeckWorkspace>>> importCache,
        CancellationToken cancellationToken)
    {
        PlaygroupDeckSummary deck = ranking.Deck;
        List<string> warnings = [.. deck.Warnings];
        DeckWorkspace? imported = null;
        bool importedDecklist = false;
        if (IsArchidektDecklistUrl(deck.DecklistUrl))
        {
            if (archidektGateway is null)
            {
                warnings.Add("Archidekt decklist URL was present, but no Archidekt gateway is configured.");
            }
            else
            {
                try
                {
                    imported = await ImportArchidektDecklistAsync(
                            deck.DecklistUrl!,
                            importCache,
                            cancellationToken)
                        .ConfigureAwait(false);
                    importedDecklist = true;
                }
                catch (Exception exception) when (!IsCancellation(exception))
                {
                    warnings.Add($"Archidekt decklist import failed: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        double confidence = DeckEvidenceConfidence(deck, importedDecklist);
        double rankWeight = rankingCount <= 1
            ? 1
            : 1 - ((ranking.Rank - 1) / (double)Math.Max(1, rankingCount)) * 0.35;
        List<PlaygroupMetaPressureEvidence> pressures = InferDeckPressures(deck, imported);
        return new PlaygroupMetaDeckEvidence
        {
            DeckId = deck.DeckId,
            Name = deck.Name,
            OwnerName = deck.OwnerName,
            CommanderNames = deck.CommanderNames.ToList(),
            RankingScore = ranking.Score,
            Weight = Math.Clamp(rankWeight * confidence, 0.2, 1),
            ImportedDecklist = importedDecklist,
            DecklistUrl = deck.DecklistUrl,
            Confidence = confidence,
            Pressures = pressures,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Imports an Archidekt decklist once per scoring request, sharing duplicate URL lookups.
    /// </summary>
    private Task<DeckWorkspace> ImportArchidektDecklistAsync(
        string decklistUrl,
        ConcurrentDictionary<string, Lazy<Task<DeckWorkspace>>> importCache,
        CancellationToken cancellationToken)
    {
        IArchidektGateway gateway = archidektGateway
            ?? throw new InvalidOperationException("Archidekt gateway is not configured.");
        Lazy<Task<DeckWorkspace>> importTask = importCache.GetOrAdd(
            decklistUrl,
            url => new Lazy<Task<DeckWorkspace>>(
                () => gateway.ImportDeckAsync(url, writeBack: false, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return importTask.Value;
    }

    /// <summary>
    /// Scores one card from the deterministic scoring factors.
    /// </summary>
    private PlaygroupMetaCandidateScore ScoreMetaCandidate(
        DeckWorkspace workspace,
        CardInfo card,
        DeckPerformanceAnalysis baseline,
        IReadOnlyList<PlaygroupMetaPressureEvidence> pressures,
        ResolvedSimulationProfile profileResolution,
        DeckIntent? intent,
        decimal? maxPrice,
        IReadOnlySet<string> gameChangers,
        bool colorKnown,
        HashSet<string> colors,
        int simulations,
        int maxTurn,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckCard candidate = CreateCandidateCard(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
        DeckPerformanceAnalysis after = DeckPerformanceAnalyzer.Analyze(
            WorkspaceWithAddedCandidate(workspace, card),
            SimulationProfileIds.Auto,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken,
            simulationProfiles);
        bool isGameChanger = gameChangers.Contains(card.Name);
        double planFit = ScorePlanFit(role, candidate, profileResolution.Profile, intent);
        double performanceDelta = ScorePerformanceDelta(baseline, after, profileResolution.Profile);
        double metaCoverage = ScoreMetaCoverage(candidate, role, pressures);
        double selfHarmPenalty = ScoreSelfHarm(candidate, role, profileResolution.Profile, intent);
        double priceBracket = ScorePriceBracket(card, maxPrice, isGameChanger, colorKnown, colors, workspace.Format);
        double confidence = ScoreEvidenceConfidence(card, pressures, simulations);
        double overall = Math.Clamp(
            (planFit * 0.25)
            + (performanceDelta * 0.20)
            + (metaCoverage * 0.30)
            + (priceBracket * 0.15)
            + (confidence * 0.10)
            - (selfHarmPenalty * 0.25),
            0,
            1);
        List<string> evidence =
        [
            $"plan fit {planFit:0.00}",
            $"performance delta score {performanceDelta:0.00}",
            $"meta coverage {metaCoverage:0.00}",
            $"self-harm penalty {selfHarmPenalty:0.00}",
            $"price/bracket score {priceBracket:0.00}",
        ];
        foreach (PlaygroupMetaPressureEvidence pressure in pressures.Take(3))
        {
            evidence.Add($"meta pressure {pressure.Pressure} at {pressure.Score:0.00}");
        }

        return new PlaygroupMetaCandidateScore
        {
            CardName = card.Name,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            OverallScore = overall,
            PlanFitScore = planFit,
            PerformanceDeltaScore = performanceDelta,
            MetaCoverageScore = metaCoverage,
            SelfHarmPenalty = selfHarmPenalty,
            PriceBracketScore = priceBracket,
            EvidenceConfidence = confidence,
            Price = ReadUsdPrice(card),
            IsGameChanger = isGameChanger,
            Rationale = BuildMetaCandidateRationale(card.Name, role.PrimaryRole, metaCoverage, selfHarmPenalty),
            Evidence = evidence,
        };
    }

    /// <summary>
    /// Gets candidate names from explicit input or non-included workspace categories.
    /// </summary>
    private static List<string> CandidateNames(DeckWorkspace workspace, IReadOnlyList<string>? candidateCards)
    {
        if (candidateCards is { Count: > 0 })
        {
            return candidateCards
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }

        HashSet<string> excludedCategories = workspace.Categories
            .Where(category => !category.IncludedInDeck)
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return workspace.Cards
            .Where(card => DeckCategoryOrdering.OrderedDistinct(
                DeckCategoryOrdering.PrimaryCategory(card),
                card.Categories).Any(excludedCategories.Contains))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    /// <summary>
    /// Chooses the per-candidate simulation count for local-meta scoring batches.
    /// </summary>
    private static int BudgetCandidatePerformanceSimulations(int candidateCount, int requestedSimulations)
    {
        if (candidateCount <= 0)
        {
            return requestedSimulations;
        }

        int budgetedSimulations = CandidatePerformanceSimulationBudget / candidateCount;
        return Math.Clamp(
            Math.Min(requestedSimulations, budgetedSimulations),
            CandidatePerformanceMinimumSimulations,
            requestedSimulations);
    }

    /// <summary>
    /// Infers pressure categories from Playgroup deck metadata and optional imported deck cards.
    /// </summary>
    private static List<PlaygroupMetaPressureEvidence> InferDeckPressures(
        PlaygroupDeckSummary deck,
        DeckWorkspace? imported)
    {
        List<PlaygroupMetaPressureEvidence> pressures = [];
        string text = string.Join(' ', deck.Name, string.Join(' ', deck.CommanderNames));
        AddPressure(pressures, FastComboPressure, 0.75, "playgroup-summary", text, "combo", "turbo", "storm", "dork", "raggadragga");
        AddPressure(pressures, StackControlPressure, 0.7, "playgroup-summary", text, "control", "talion", "faerie", "counter", "permission");
        AddPressure(pressures, GoWideTokensPressure, 0.65, "playgroup-summary", text, "tokens", "saproling", "sap attack", "go-wide");
        AddPressure(pressures, GraveyardRecursionPressure, 0.65, "playgroup-summary", text, "graveyard", "reanimator", "dredge", "sac", "aristocrat");
        AddPressure(pressures, LifePressure, 0.55, "playgroup-summary", text, "slug", "burn", "norin", "ashling", "purphoros");
        if (deck.AverageWinsByRound is <= 6)
        {
            pressures.Add(new PlaygroupMetaPressureEvidence
            {
                Pressure = FastComboPressure,
                Score = 0.70,
                Source = "playgroup-stats",
                Evidence = [$"average winning round {deck.AverageWinsByRound:0.0} suggests early kill pressure"],
            });
        }

        if (imported is not null)
        {
            pressures.AddRange(InferImportedDeckPressures(imported));
        }

        return pressures
            .GroupBy(pressure => pressure.Pressure, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PlaygroupMetaPressureEvidence
            {
                Pressure = group.Key,
                Score = Math.Clamp(group.Max(item => item.Score), 0, 1),
                Source = string.Join(", ", group.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase)),
                Evidence = group.SelectMany(item => item.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList(),
            })
            .OrderByDescending(pressure => pressure.Score)
            .ThenBy(pressure => pressure.Pressure, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Infers pressure from an imported Archidekt decklist.
    /// </summary>
    private static List<PlaygroupMetaPressureEvidence> InferImportedDeckPressures(DeckWorkspace imported)
    {
        List<DeckCard> cards = DeckCategoryInclusion.IncludedCards(imported).ToList();
        int creatures = cards
            .Where(card => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"))
            .Sum(card => Math.Max(1, card.Quantity));
        int ramp = CountCards(cards, role: DeckRoles.Ramp);
        int tutors = CountCards(cards, role: DeckRoles.Tutors);
        int interaction = CountCards(cards, role: DeckRoles.Interaction) + CountCards(cards, role: DeckRoles.BoardWipes);
        int stax = CountTaggedCards(cards, DeckTags.Stax);
        int tokens = CountTaggedCards(cards, DeckTags.Tokens) + CountTaggedCards(cards, DeckTags.SacrificeFodder);
        int graveyard = CountTaggedCards(cards, DeckTags.GraveyardHate) + CountTaggedCards(cards, DeckTags.Reanimation);
        int combo = CountTaggedCards(cards, DeckTags.ComboPiece) + CountTaggedCards(cards, DeckTags.ComboEnabler);
        int artifacts = cards
            .Where(card => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Artifact"))
            .Sum(card => Math.Max(1, card.Quantity));
        int enchantments = cards
            .Where(card => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Enchantment"))
            .Sum(card => Math.Max(1, card.Quantity));
        List<PlaygroupMetaPressureEvidence> pressures = [];
        AddImportedPressure(pressures, FastComboPressure, ramp >= 12 || tutors + combo >= 5, $"ramp {ramp}, tutors {tutors}, combo tags {combo}");
        AddImportedPressure(pressures, CreatureCombatPressure, creatures >= 24, $"creatures {creatures}");
        AddImportedPressure(pressures, GoWideTokensPressure, tokens >= 5, $"token tags {tokens}");
        AddImportedPressure(pressures, GraveyardRecursionPressure, graveyard >= 4, $"graveyard/reanimation tags {graveyard}");
        AddImportedPressure(pressures, StackControlPressure, interaction >= 14, $"interaction and wipes {interaction}");
        AddImportedPressure(pressures, StaxPressure, stax >= 3, $"stax tags {stax}");
        AddImportedPressure(pressures, ArtifactEnginePressure, artifacts >= 14, $"artifacts {artifacts}");
        AddImportedPressure(pressures, EnchantmentEnginePressure, enchantments >= 12, $"enchantments {enchantments}");
        return pressures;
    }

    /// <summary>
    /// Aggregates deck-level pressure evidence into weighted local-meta pressure.
    /// </summary>
    private static List<PlaygroupMetaPressureEvidence> AggregatePressures(IReadOnlyList<PlaygroupMetaDeckEvidence> decks)
    {
        Dictionary<string, (double Score, List<string> Evidence)> aggregate = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlaygroupMetaDeckEvidence deck in decks)
        {
            foreach (PlaygroupMetaPressureEvidence pressure in deck.Pressures)
            {
                double weighted = pressure.Score * deck.Weight;
                if (!aggregate.TryGetValue(pressure.Pressure, out (double Score, List<string> Evidence) current))
                {
                    current = (0, []);
                }

                current.Score += weighted;
                current.Evidence.AddRange(pressure.Evidence.Select(evidence => $"{deck.Name}: {evidence}"));
                aggregate[pressure.Pressure] = current;
            }
        }

        double totalDeckWeight = Math.Max(1, decks.Sum(deck => deck.Weight));
        return aggregate
            .Select(item => new PlaygroupMetaPressureEvidence
            {
                Pressure = item.Key,
                Score = Math.Clamp(item.Value.Score / totalDeckWeight, 0, 1),
                Source = "playgroup-aggregate",
                Evidence = item.Value.Evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList(),
            })
            .Where(pressure => pressure.Score > 0)
            .OrderByDescending(pressure => pressure.Score)
            .ThenBy(pressure => pressure.Pressure, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Scores how well a candidate aligns with the deck plan.
    /// </summary>
    private static double ScorePlanFit(
        CardRoleAssignment role,
        DeckCard candidate,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        double score = role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase) ? 0.25 : 0.45;
        IEnumerable<string> buildTargets = intent?.BuildTargets.Keys ?? Enumerable.Empty<string>();
        IEnumerable<string> legacyTargets = intent?.Targets.Keys ?? Enumerable.Empty<string>();
        IEnumerable<string> targetNames = buildTargets.Concat(legacyTargets);
        if (targetNames.Any(target => DeckRoleClassifier.MatchesTarget(candidate, target)))
        {
            score += 0.25;
        }

        if (role.Tags.Any(tag => profile.ThemeTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            || role.Tags.Any(tag => intent?.ArchetypeTags.Contains(tag, StringComparer.OrdinalIgnoreCase) == true))
        {
            score += 0.20;
        }

        score += profile.Id switch
        {
            SimulationProfileIds.Combo when role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Combo when role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler) => 0.25,
            SimulationProfileIds.Control when role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Control when role.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Aggro when role.Tags.Any(tag => tag is DeckTags.Tokens or DeckTags.Voltron or DeckTags.Finishers) => 0.25,
            SimulationProfileIds.BigMana when role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Stax when role.Tags.Contains(DeckTags.Stax, StringComparer.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Value when role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase) => 0.20,
            _ => 0,
        };
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Scores a candidate's performance impact from before and after deterministic simulation snapshots.
    /// </summary>
    private static double ScorePerformanceDelta(
        DeckPerformanceAnalysis before,
        DeckPerformanceAnalysis after,
        SimulationProfile profile)
    {
        double interaction = ScenarioRate(after, "hold-up-interaction-by-turn-4") - ScenarioRate(before, "hold-up-interaction-by-turn-4");
        double protection = ScenarioRate(after, "commander-with-protection-by-turn-5") - ScenarioRate(before, "commander-with-protection-by-turn-5");
        double combo = ScenarioRate(after, "combo-or-tutor-assembly-by-turn-5") - ScenarioRate(before, "combo-or-tutor-assembly-by-turn-5");
        double graveyard = ScenarioRate(after, "graveyard-hate-by-turn-3") - ScenarioRate(before, "graveyard-hate-by-turn-3");
        double strandedRiskReduction = ScenarioRate(before, "stranded-high-mana-risk-by-max-turn") - ScenarioRate(after, "stranded-high-mana-risk-by-max-turn");
        double weighted = profile.Id switch
        {
            SimulationProfileIds.Combo => (combo * 0.35) + (protection * 0.25) + (interaction * 0.25) + (graveyard * 0.10) + (strandedRiskReduction * 0.05),
            SimulationProfileIds.Control => (interaction * 0.40) + (graveyard * 0.20) + (protection * 0.15) + (combo * 0.10) + (strandedRiskReduction * 0.15),
            _ => (interaction * 0.30) + (protection * 0.20) + (combo * 0.20) + (graveyard * 0.15) + (strandedRiskReduction * 0.15),
        };
        return Math.Clamp(0.5 + (weighted * 2.0), 0, 1);
    }

    /// <summary>
    /// Scores a candidate's matchup coverage against aggregate pressures.
    /// </summary>
    private static double ScoreMetaCoverage(
        DeckCard candidate,
        CardRoleAssignment role,
        IReadOnlyList<PlaygroupMetaPressureEvidence> pressures)
    {
        double weighted = 0;
        double total = 0;
        foreach (PlaygroupMetaPressureEvidence pressure in pressures)
        {
            weighted += pressure.Score * CoverageForPressure(candidate, role, pressure.Pressure);
            total += pressure.Score;
        }

        return total <= 0 ? 0.45 : Math.Clamp(weighted / total, 0, 1);
    }

    /// <summary>
    /// Scores likely conflict between a candidate and the deck's own plan.
    /// </summary>
    private static double ScoreSelfHarm(
        DeckCard candidate,
        CardRoleAssignment role,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        string text = $"{candidate.Name} {GetSnapshot(candidate).OracleText}";
        double penalty = 0;
        bool blinkDeck = profile.ThemeTags.Contains("blink", StringComparer.OrdinalIgnoreCase)
            || intent?.ArchetypeTags.Contains("blink", StringComparer.OrdinalIgnoreCase) == true;
        if (blinkDeck && ContainsAny(text, "entering the battlefield don't cause", "entering the battlefield doesn't cause"))
        {
            penalty = Math.Max(penalty, 0.90);
        }

        if (profile.Id.Equals(SimulationProfileIds.Combo, StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "each player can't cast more than one spell", "players can't cast more than one spell"))
        {
            penalty = Math.Max(penalty, 0.35);
        }

        if (role.Tags.Contains(DeckTags.Stax, StringComparer.OrdinalIgnoreCase)
            && intent?.Avoid.Any(avoid => text.Contains(avoid, StringComparison.OrdinalIgnoreCase)) == true)
        {
            penalty = Math.Max(penalty, 0.50);
        }

        return Math.Clamp(penalty, 0, 1);
    }

    /// <summary>
    /// Scores price, legality, color identity, and Game Changer constraints.
    /// </summary>
    private static double ScorePriceBracket(
        CardInfo card,
        decimal? maxPrice,
        bool isGameChanger,
        bool colorKnown,
        HashSet<string> colors,
        string format)
    {
        double score = 1;
        decimal? price = ReadUsdPrice(card);
        if (maxPrice.HasValue)
        {
            score = price.HasValue && price.Value <= maxPrice.Value
                ? 1 - Math.Clamp((double)(price.Value / maxPrice.Value) * 0.20, 0, 0.20)
                : 0.15;
        }
        else if (!price.HasValue)
        {
            score = 0.70;
        }

        if (isGameChanger)
        {
            score = Math.Min(score, 0.10);
        }

        if (!IsLegalInFormat(card, NormalizeFormat(format))
            || !IsInDeckColorIdentity(card, colorKnown, colors))
        {
            score = 0;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Scores the confidence of candidate card facts and meta pressure data.
    /// </summary>
    private static double ScoreEvidenceConfidence(
        CardInfo card,
        IReadOnlyList<PlaygroupMetaPressureEvidence> pressures,
        int simulations)
    {
        double score = 0.35;
        score += !string.IsNullOrWhiteSpace(card.OracleText) ? 0.15 : 0;
        score += ReadUsdPrice(card).HasValue ? 0.05 : 0;
        score += card.EdhrecRank.HasValue ? 0.05 : 0;
        score += pressures.Count > 0 ? 0.15 : 0;
        score += Math.Min(0.15, simulations / 2000.0 * 0.15);
        return Math.Clamp(score, 0, 0.95);
    }

    /// <summary>
    /// Scores one card against one local-meta pressure.
    /// </summary>
    private static double CoverageForPressure(DeckCard candidate, CardRoleAssignment role, string pressure)
    {
        string text = $"{candidate.Name} {GetSnapshot(candidate).TypeLine} {GetSnapshot(candidate).OracleText}";
        return pressure switch
        {
            FastComboPressure => Max(
                RoleScore(role, DeckRoles.Interaction, 0.75),
                RoleScore(role, DeckRoles.BoardWipes, 0.55),
                TagScore(role, DeckTags.Stax, 0.90),
                TextScore(text, 0.85, "counter target", "exile target", "destroy target", "can't activate")),
            CreatureCombatPressure => Max(
                RoleScore(role, DeckRoles.BoardWipes, 0.95),
                RoleScore(role, DeckRoles.Interaction, 0.70),
                TagScore(role, DeckTags.Pillowfort, 0.80),
                TagScore(role, DeckTags.GoWideProtection, 0.75)),
            GoWideTokensPressure => Max(
                RoleScore(role, DeckRoles.BoardWipes, 0.95),
                TagScore(role, DeckTags.TokenHate, 0.95),
                TextScore(text, 0.85, "all creatures", "each creature", "creature tokens get")),
            GraveyardRecursionPressure => Max(
                TagScore(role, DeckTags.GraveyardHate, 0.95),
                TextScore(text, 0.95, "exile all graveyards", "exile target card from a graveyard", "cards in graveyards"),
                RoleScore(role, DeckRoles.Interaction, 0.35)),
            StackControlPressure => Max(
                RoleScore(role, DeckRoles.Protection, 0.85),
                TextScore(text, 0.90, "can't be countered", "hexproof", "phase out"),
                RoleScore(role, DeckRoles.Draw, 0.45)),
            ArtifactEnginePressure => Max(
                TagScore(role, DeckTags.ArtifactEnchantmentHate, 0.95),
                TextScore(text, 0.95, "destroy target artifact", "exile target artifact", "destroy all artifacts"),
                RoleScore(role, DeckRoles.Interaction, 0.55)),
            EnchantmentEnginePressure => Max(
                TagScore(role, DeckTags.ArtifactEnchantmentHate, 0.95),
                TextScore(text, 0.95, "destroy target enchantment", "exile target enchantment", "destroy all enchantments"),
                RoleScore(role, DeckRoles.Interaction, 0.55)),
            LifePressure => Max(
                TagScore(role, DeckTags.Lifegain, 0.70),
                RoleScore(role, DeckRoles.Protection, 0.45),
                RoleScore(role, DeckRoles.Interaction, 0.40)),
            StaxPressure => Max(
                TagScore(role, DeckTags.ArtifactEnchantmentHate, 0.80),
                RoleScore(role, DeckRoles.Interaction, 0.70),
                RoleScore(role, DeckRoles.BoardWipes, 0.65)),
            _ => 0.35,
        };
    }

    /// <summary>
    /// Creates a preview workspace with one candidate added to the included deck.
    /// </summary>
    private static DeckWorkspace WorkspaceWithAddedCandidate(DeckWorkspace workspace, CardInfo card)
    {
        DeckWorkspace clone = CloneWorkspace(workspace);
        EnsureCategory(clone, DeckDefaults.Mainboard);
        DeckCard candidate = CreateCandidateCard(card);
        candidate.PrimaryCategory = DeckDefaults.Mainboard;
        candidate.Categories = [DeckDefaults.Mainboard];
        clone.Cards.Add(candidate);
        return clone;
    }

    /// <summary>
    /// Converts an existing workspace card snapshot into catalog-like card facts.
    /// </summary>
    private static CardInfo CardInfoFromWorkspaceCard(DeckCard card)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        return new CardInfo
        {
            Name = card.Name,
            ManaCost = snapshot.ManaCost,
            ManaValue = snapshot.ManaValue,
            TypeLine = snapshot.TypeLine,
            OracleText = snapshot.OracleText,
            Set = snapshot.Set,
            CollectorNumber = snapshot.CollectorNumber,
            Rarity = snapshot.Rarity,
            ReleasedAt = snapshot.ReleasedAt,
            ScryfallUri = snapshot.ScryfallUri,
            EdhrecRank = snapshot.EdhrecRank,
            ColorIdentity = snapshot.ColorIdentity.ToList(),
            Keywords = snapshot.Keywords.ToList(),
            ProducedMana = snapshot.ProducedMana.ToList(),
            Legalities = new Dictionary<string, string>(snapshot.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(snapshot.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(snapshot.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Clones a workspace for read-only preview scoring.
    /// </summary>
    private static DeckWorkspace CloneWorkspace(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        return JsonSerializer.Deserialize<DeckWorkspace>(json)
            ?? throw new InvalidOperationException("Unable to clone deck workspace for local-meta scoring.");
    }

    /// <summary>
    /// Adds one text-derived pressure row when a keyword matches.
    /// </summary>
    private static void AddPressure(
        List<PlaygroupMetaPressureEvidence> pressures,
        string pressure,
        double score,
        string source,
        string text,
        params string[] needles)
    {
        List<string> matched = needles.Where(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0)
        {
            return;
        }

        pressures.Add(new PlaygroupMetaPressureEvidence
        {
            Pressure = pressure,
            Score = score,
            Source = source,
            Evidence = [$"matched {string.Join(", ", matched)}"],
        });
    }

    /// <summary>
    /// Adds one imported-deck pressure row when a count threshold matches.
    /// </summary>
    private static void AddImportedPressure(
        List<PlaygroupMetaPressureEvidence> pressures,
        string pressure,
        bool matched,
        string evidence)
    {
        if (!matched)
        {
            return;
        }

        pressures.Add(new PlaygroupMetaPressureEvidence
        {
            Pressure = pressure,
            Score = 0.80,
            Source = "archidekt-decklist",
            Evidence = [evidence],
        });
    }

    /// <summary>
    /// Computes confidence for one Playgroup deck evidence row.
    /// </summary>
    private static double DeckEvidenceConfidence(PlaygroupDeckSummary deck, bool importedDecklist)
    {
        double confidence = 0.45;
        confidence += importedDecklist ? 0.25 : 0;
        confidence += deck.FetchedPlaygroupGames > 0 ? 0.10 : 0;
        confidence += deck.ConfidenceFactor.HasValue ? Math.Clamp(deck.ConfidenceFactor.Value, 0, 1) * 0.15 : 0;
        confidence += deck.AverageWinsByRound.HasValue ? 0.05 : 0;
        return Math.Clamp(confidence, 0.20, 0.95);
    }

    /// <summary>
    /// Builds concise candidate rationale.
    /// </summary>
    private static string BuildMetaCandidateRationale(
        string cardName,
        string role,
        double metaCoverage,
        double selfHarmPenalty)
    {
        string tradeoff = selfHarmPenalty > 0.35
            ? " with a notable self-harm tradeoff"
            : "";
        return $"{cardName} is a {role} candidate with {metaCoverage:0.00} local-meta coverage{tradeoff}.";
    }

    /// <summary>
    /// Gets a scenario rate by name.
    /// </summary>
    private static double ScenarioRate(DeckPerformanceAnalysis analysis, string scenarioName)
    {
        return analysis.Scenarios
            .FirstOrDefault(scenario => scenario.Name.Equals(scenarioName, StringComparison.OrdinalIgnoreCase))
            ?.SuccessRate
            ?? 0;
    }

    /// <summary>
    /// Counts cards with a requested primary role.
    /// </summary>
    private static int CountCards(IEnumerable<DeckCard> cards, string role)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(role, StringComparison.OrdinalIgnoreCase))
            .Sum(card => Math.Max(1, card.Quantity));
    }

    /// <summary>
    /// Counts cards with a requested secondary tag.
    /// </summary>
    private static int CountTaggedCards(IEnumerable<DeckCard> cards, string tag)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Sum(card => Math.Max(1, card.Quantity));
    }

    /// <summary>
    /// Checks whether a URL points at Archidekt.
    /// </summary>
    private static bool IsArchidektDecklistUrl(string? decklistUrl)
    {
        return Uri.TryCreate(decklistUrl, UriKind.Absolute, out Uri? uri)
            && uri.Host.Contains("archidekt.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scores a role match when a pressure mapping wants one role.
    /// </summary>
    private static double RoleScore(CardRoleAssignment role, string roleName, double score)
    {
        return role.PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase) ? score : 0;
    }

    /// <summary>
    /// Scores a tag match when a pressure mapping wants one tag.
    /// </summary>
    private static double TagScore(CardRoleAssignment role, string tag, double score)
    {
        return role.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? score : 0;
    }

    /// <summary>
    /// Scores an oracle-text match when a pressure mapping wants one phrase.
    /// </summary>
    private static double TextScore(string text, double score, params string[] phrases)
    {
        return ContainsAny(text, phrases) ? score : 0;
    }

    /// <summary>
    /// Returns the largest score from a pressure mapping.
    /// </summary>
    private static double Max(params double[] values)
    {
        return values.Max();
    }

}
