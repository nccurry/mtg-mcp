using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes deterministic deck data lookup MCP tools.
/// </summary>
[McpServerToolType]
public sealed class RecommendationTools
{
    /// <summary>
    /// Indicates candidate cards are supplied directly by the caller.
    /// </summary>
    private const string ExplicitCardsCandidateSource = "explicit-cards";

    /// <summary>
    /// Indicates candidates should be read from excluded workspace categories.
    /// </summary>
    private const string ExcludedWorkspaceCardsCandidateSource = "excluded-workspace-cards";

    /// <summary>
    /// Creates recommendation reports from source data.
    /// </summary>
    private readonly DeckRecommendationService recommendations;

    /// <summary>
    /// Creates recommendation tools for the MCP surface.
    /// </summary>
    public RecommendationTools(DeckRecommendationService recommendations)
    {
        this.recommendations = recommendations;
    }

    /// <summary>
    /// Gets source-backed aggregate cards for a commander.
    /// </summary>
    [McpServerTool(Name = "commander_get_aggregate_cards", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Return source-backed aggregate card rows for a commander. If source is omitted, rows are grouped by source and counts are not merged across unlike source populations. theme is normalized deterministically; unsupported themes return notes instead of fuzzy inference.")]
    public Task<CommanderAggregateCardsResult> GetCommanderAggregateCardsAsync(
        string commanderName,
        string? theme = null,
        [Description("Recommendation source key such as edhrec, edhtop16, topdeck, spicerack, or reddit-discussions.")]
        string? source = null,
        int limit = 50,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.GetCommanderAggregateCardsAsync(
            commanderName,
            theme,
            source,
            limit,
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Gets source-backed commander tags or theme sections.
    /// </summary>
    [McpServerTool(Name = "commander_get_tags", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Return source-backed commander tags or theme sections only. Tags are not inferred unless backed by source fields or configured deterministic rules.")]
    public Task<CommanderTagsResult> GetCommanderTagsAsync(
        string commanderName,
        [Description("Recommendation source key such as edhrec, edhtop16, topdeck, spicerack, or reddit-discussions.")]
        string? source = null,
        int limit = 50,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.GetCommanderTagsAsync(
            commanderName,
            source,
            limit,
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Bundles commander win-condition evidence without conclusions.
    /// </summary>
    [McpServerTool(Name = "commander_get_win_condition_evidence", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Return structured commander win-condition evidence only: aggregate cards, tags, commander-containing combos, route classifications, and payoff candidates for non-terminal routes. The tool does not synthesize conclusions or recommendations.")]
    public Task<CommanderWinConditionEvidenceResult> GetCommanderWinConditionEvidenceAsync(
        string commanderName,
        string? theme = null,
        bool strictColorIdentity = true,
        [Description("Optional recommendation source keys. When multiple are supplied, source populations remain separate.")]
        string[]? sources = null,
        int limit = 50,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.GetCommanderWinConditionEvidenceAsync(
            commanderName,
            theme,
            strictColorIdentity,
            sources,
            limit,
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Finds commander candidates within bounded EDHREC popularity ranges.
    /// </summary>
    [McpServerTool(Name = "commander_search_candidates", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find commander candidates with bounded catalog search and bounded EDHREC eligible deck count checks. Defaults inspect up to 80 catalog candidates and fetch up to 24 EDHREC aggregates; caps are 200 and 50. Partial source failures return notes.")]
    public Task<CommanderCandidateSearchResult> SearchCommanderCandidatesAsync(
        [Description("Optional color identity such as WUBRG, UB, or G. Omit for any colors; use C, colorless, or an explicit empty string for colorless.")]
        string? colorIdentity = null,
        bool exactColorIdentity = false,
        int minEligibleDecks = 1_500,
        int? maxEligibleDecks = 3_500,
        int limit = 10,
        int scryfallCandidateCap = 80,
        int edhrecFetchCap = 24,
        bool refreshSources = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.SearchCommanderCandidatesAsync(
            colorIdentity,
            exactColorIdentity,
            minEligibleDecks,
            maxEligibleDecks,
            limit,
            scryfallCandidateCap,
            edhrecFetchCap,
            refreshSources,
            cancellationToken);
    }

    /// <summary>
    /// Finds payoff candidates for a route using Scryfall query templates.
    /// </summary>
    [McpServerTool(Name = "wincon_find_payoffs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find Scryfall-query-derived payoff candidates for an approved route label and color identity. Results are payoff candidates, not popularity evidence unless joined with aggregate source rows.")]
    public Task<WinconPayoffSearchResult> FindWinconPayoffsAsync(
        [Description("Approved route label: combat, tokens, storm, infinite-mana, self-mill, opponent-mill, extra-turns, aristocrats, alternate-win, value-combat, etb, or draw-deck.")]
        string route,
        [Description("Commander color identity such as WUBRG, UB, G, or empty/colorless.")]
        string colorIdentity,
        string format = "commander",
        decimal? maxPrice = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindWinconPayoffsAsync(
            route,
            colorIdentity,
            format,
            maxPrice,
            limit,
            cancellationToken);
    }

    /// <summary>
    /// Reviews new-card candidates and deterministic cuts for a deck.
    /// </summary>
    [McpServerTool(Name = "deck_review_new_card_swaps", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Review newly released card candidates and deterministic cut evidence: role overlap, mana curve slot, duplicate effect density, theme mismatch, price delta, and protected-card warnings. Read-only; use deck plan tools after approval.")]
    public Task<NewCardSwapReviewResult> ReviewNewCardSwapsAsync(
        string workspaceId,
        string? since = null,
        string? setCode = null,
        decimal? maxPrice = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        return recommendations.ReviewNewCardSwapsAsync(
            workspaceId,
            since,
            setCode,
            maxPrice,
            limit,
            cancellationToken);
    }

    /// <summary>
    /// Gets cards from an agent-supplied Scryfall query using deterministic deck-aware filters.
    /// </summary>
    [McpServerTool(Name = "deck_query_cards", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Return source-order cards from a caller-supplied Scryfall query after deck legality, color identity, budget, required role/tag, excluded role/tag, and duplicate filters. No fit scores or plan is created.")]
    public Task<DeckQueryDataResult> QueryCardsForDeckAsync(
        string workspaceId,
        string goal,
        string scryfallQuery,
        int limit = 10,
        decimal? maxPrice = null,
        string[]? requiredRoles = null,
        string[]? requiredTags = null,
        string[]? excludedRoles = null,
        string[]? excludedTags = null,
        CancellationToken cancellationToken = default)
    {
        return recommendations.QueryCardsForDeckAsync(
            workspaceId,
            goal,
            scryfallQuery,
            limit,
            maxPrice,
            requiredRoles,
            requiredTags,
            excludedRoles,
            excludedTags,
            cancellationToken);
    }

    /// <summary>
    /// Evaluates one card's deterministic operational facts in deck context.
    /// </summary>
    [McpServerTool(Name = "deck_evaluate_card", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Read-only ramp-fit evaluation for one card in deck context. Non-ramp cards return applicable=false with a not-applicable status; detailLevel=full includes operational facts, sub-scores, evidence, and warnings.")]
    public async Task<object> EvaluateCardAsync(
        string workspaceId,
        string cardName,
        [Description("Optional explicit candidate card names to compare. mtg-mcp does not maintain a hidden replacement list.")]
        string[]? candidateCards = null,
        int candidateLimit = 8,
        [Description("Output detail level: compact or full.")]
        string detailLevel = "compact",
        CancellationToken cancellationToken = default)
    {
        RampContextEvaluation evaluation = await recommendations
            .EvaluateCardAsync(
                workspaceId,
                cardName,
                candidateCards,
                candidateLimit,
                cancellationToken)
            .ConfigureAwait(false);
        return detailLevel.Equals("full", StringComparison.OrdinalIgnoreCase)
            ? evaluation
            : ToCompactEvaluation(evaluation);
    }

    /// <summary>
    /// Builds a read-only tuning report across several workspaces.
    /// </summary>
    [McpServerTool(Name = "deck_batch_tuning_report", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Build a bounded read-only tuning report for 1-8 local workspaces with validation, cost, bracket, mana, consistency, best practices, and goldfish results. Per-workspace failures are returned without aborting the batch.")]
    public async Task<object> BuildBatchTuningReportAsync(
        string[] workspaceIds,
        decimal? maxBudget = null,
        [Description("Output detail level: summary, normal, or full.")]
        string detailLevel = "summary",
        [Description("Simulation profile: auto, neutral, aggro, combo, control, value, big-mana, stax, or configured profile id.")]
        string simulationProfile = "auto",
        int targetTurn = 7,
        int simulations = 1_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        DeckBatchTuningReport report = await recommendations
            .BuildBatchTuningReportAsync(
                workspaceIds,
                maxBudget,
                simulationProfile,
                targetTurn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
        return GoldfishOutputPresenter.Present(report, detailLevel);
    }

    /// <summary>
    /// Scores candidate cards against Playgroup-derived local-meta pressure.
    /// </summary>
    [McpServerTool(Name = "deck_score_cards_for_playgroup_meta", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Score candidate cards against ranked Playgroup.gg meta pressure with deterministic plan fit, performance delta, meta coverage, self-harm, price/bracket, and evidence-confidence factors.")]
    public Task<PlaygroupMetaScoringResult> ScoreCardsForPlaygroupMetaAsync(
        string workspaceId,
        string playgroupIdOrUrl,
        [Description("Candidate source: explicit-cards or excluded-workspace-cards.")]
        string candidateSource,
        string[]? candidateCards = null,
        int maxGames = 200,
        int metaDeckLimit = 6,
        int simulations = 500,
        int maxTurn = 6,
        int seed = 1903,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidateSource))
        {
            throw new ArgumentException(
                "candidateSource must be explicit-cards or excluded-workspace-cards.",
                nameof(candidateSource)
            );
        }

        string normalizedCandidateSource = candidateSource.Trim().ToLowerInvariant();
        if (normalizedCandidateSource == ExplicitCardsCandidateSource)
        {
            if (candidateCards is null || candidateCards.Length == 0)
            {
                throw new ArgumentException(
                    "candidateCards must be provided when candidateSource is explicit-cards.",
                    nameof(candidateCards)
                );
            }
        }
        else if (normalizedCandidateSource == ExcludedWorkspaceCardsCandidateSource)
        {
            if (candidateCards is { Length: > 0 })
            {
                throw new ArgumentException(
                    "candidateCards must be omitted when candidateSource is excluded-workspace-cards.",
                    nameof(candidateCards)
                );
            }
        }
        else
        {
            throw new ArgumentException(
                "candidateSource must be explicit-cards or excluded-workspace-cards.",
                nameof(candidateSource)
            );
        }

        return recommendations.ScoreCardsForPlaygroupMetaAsync(
            workspaceId,
            playgroupIdOrUrl,
            normalizedCandidateSource == ExplicitCardsCandidateSource ? candidateCards : null,
            maxGames,
            metaDeckLimit,
            simulations,
            maxTurn,
            seed,
            maxPrice,
            cancellationToken);
    }

    /// <summary>
    /// Creates compact card-evaluation output that omits evidence lists by default.
    /// </summary>
    private static object ToCompactEvaluation(RampContextEvaluation evaluation)
    {
        return new
        {
            evaluation.Evaluator,
            evaluation.Applicable,
            evaluation.EvaluationStatus,
            evaluation.WorkspaceId,
            evaluation.CardName,
            evaluation.Role,
            evaluation.Score,
            evaluation.RampKind,
            evaluation.TopIssues,
            evaluation.TopStrengths,
            TopCandidates = evaluation.CandidateEvaluations
                .Select(candidate => new
                {
                    candidate.CardName,
                    candidate.Role,
                    candidate.Score,
                    candidate.RampKind,
                    candidate.TopIssues,
                    candidate.TopStrengths,
                })
                .ToList(),
            evaluation.Warnings,
            ReadOnly = true
        };
    }
}
