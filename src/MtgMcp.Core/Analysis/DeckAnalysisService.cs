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
    /// Creates an analysis service backed by workspace storage and card-data providers.
    /// </summary>
    public DeckAnalysisService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null
    )
        : base(
            repository,
            cardCatalog,
            currentDateOverride: currentDateOverride)
    {
        this.comboCatalog = comboCatalog;
    }
}
