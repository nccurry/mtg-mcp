namespace MtgMcp.Core;

/// <summary>
/// Analyzes deck composition, card snapshots, draw odds, costs, mana, brackets, combos, and heuristic health.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Supplies optional combo lookups before analysis falls back to local heuristics.
    /// </summary>
    private readonly IComboCatalog? comboCatalog;

    /// <summary>
    /// Builds reusable cost, mana, consistency, bracket, and preview metric snapshots.
    /// </summary>
    private readonly DeckAnalysisMetrics metrics;

    /// <summary>
    /// Creates an analysis service backed by workspace storage and card-data providers.
    /// </summary>
    public DeckAnalysisService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        DeckAnalysisMetrics? metrics = null
    )
        : base(
            repository,
            cardCatalog,
            currentDateOverride: currentDateOverride)
    {
        this.comboCatalog = comboCatalog;
        this.metrics = metrics ?? new DeckAnalysisMetrics(cardCatalog, CurrentDate);
    }
}
