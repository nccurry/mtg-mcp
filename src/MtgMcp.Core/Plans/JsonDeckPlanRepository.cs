namespace MtgMcp.Core;

/// <summary>
/// Persists deck edit plans as JSON files under the local data directory.
/// </summary>
public sealed class JsonDeckPlanRepository : IDeckPlanRepository
{
    /// <summary>
    /// Owns atomic JSON persistence and legacy filename migration for plan files.
    /// </summary>
    private readonly JsonFileStore<DeckEditPlan> store;

    /// <summary>
    /// Creates a repository rooted under the mtg-mcp data directory.
    /// </summary>
    public JsonDeckPlanRepository(string dataDirectory)
    {
        store = new JsonFileStore<DeckEditPlan>(
            Path.Combine(dataDirectory, "plans"),
            "Plan",
            static plan => plan.PlanId);
    }

    /// <summary>
    /// Saves a deck edit plan under its stable plan id.
    /// </summary>
    public async Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return await store.SaveAsync(plan.PlanId, plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a deck edit plan by id from disk.
    /// </summary>
    public async Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        return await store.GetAsync(planId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists saved plans, optionally scoped to one workspace, with the newest plans first.
    /// </summary>
    public async Task<IReadOnlyList<DeckEditPlan>> ListAsync(
        string? workspaceId,
        CancellationToken cancellationToken
    )
    {
        List<DeckEditPlan> plans = [];
        IReadOnlyList<DeckEditPlan> storedPlans = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (DeckEditPlan plan in storedPlans)
        {
            if (!string.IsNullOrWhiteSpace(workspaceId)
                && !string.Equals(plan.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            plans.Add(plan);
        }

        plans.Sort(static (left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
        return plans;
    }

    /// <summary>
    /// Deletes the plan and reports whether one existed.
    /// </summary>
    public Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken)
    {
        return store.DeleteAsync(planId, cancellationToken);
    }
}
