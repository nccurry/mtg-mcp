namespace MtgMcp.Core;

/// <summary>
/// Refreshes card snapshots and summarizes deck analysis inputs.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Refreshes cached card snapshot metadata for workspace cards.
    /// </summary>
    public async Task<DeckNormalizationResult> RefreshDeckCardSnapshotsAsync(
        string workspaceId,
        string scope,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        string normalizedScope = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();
        DeckNormalizationResult result = await NormalizeWorkspaceCardsAsync(workspace, normalizedScope, cancellationToken)
            .ConfigureAwait(false);

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Summarizes the deck workspace, role map, risks, and suggested next steps.
    /// </summary>
    public async Task<DeckPlanSummary> SummarizeDeckWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        DeckPlanSummary summary = new()
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Format = workspace.Format,
            Intent = intent,
            Persistence = DeckPersistence.For(workspace),
            IncludedCards = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity)),
            MaybeboardCards = workspace.Cards
                .Where(card => string.Equals(card.PrimaryCategory, DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase))
                .Sum(card => Math.Max(0, card.Quantity))
        };

        foreach (DeckCard card in workspace.Cards)
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            AddCount(summary.RoleCounts, assignment.PrimaryRole, card.Quantity);
            foreach (string tag in assignment.Tags)
            {
                AddCount(summary.TagCounts, tag, card.Quantity);
            }

            if (assignment.PrimaryRole == DeckRoles.Commander)
            {
                summary.Commanders.Add(card.Name);
            }
        }

        foreach (DeckCategory category in workspace.Categories)
        {
            string suggestedRole = SuggestRoleForCategory(workspace, category.Name);
            summary.CategoryMap[category.Name] = suggestedRole;
        }

        AddSummaryNotes(summary, intent);
        return summary;
    }

    /// <summary>
    /// Analyzes draw odds for deck targets.
    /// </summary>
    public async Task<DeckOddsAnalysis> AnalyzeDrawOddsAsync(
        string workspaceId,
        string? targets,
        int turn,
        int openingHandSize,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        List<string> requestedTargets = ParseTargets(targets, intent);
        return DeckStatistics.AnalyzeDrawOdds(
            workspace,
            requestedTargets,
            Math.Max(1, turn),
            Math.Clamp(openingHandSize, 1, 20),
            simulations,
            seed);
    }

    /// <summary>
    /// Analyzes turn-by-turn odds of making land drops.
    /// </summary>
    public async Task<LandDropOddsAnalysis> AnalyzeLandDropOddsAsync(
        string workspaceId,
        int turn,
        int openingHandSize,
        bool onThePlay,
        bool includeMulligans,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return DeckStatistics.AnalyzeLandDropOdds(
            workspace,
            turn,
            Math.Clamp(openingHandSize, 1, 20),
            onThePlay,
            includeMulligans,
            simulations,
            seed);
    }
}
