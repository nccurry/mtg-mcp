using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Carries a complete local deck together with private synchronization baselines for lossless interchange.
/// </summary>
internal sealed record DeckInterchangeSnapshot(
    DeckDocument Deck,
    IReadOnlyList<DeckSyncBaseline> SyncBaselines)
{
    /// <summary>
    /// Gets an immutable snapshot of provider synchronization baselines.
    /// </summary>
    internal IReadOnlyList<DeckSyncBaseline> SyncBaselines { get; init; } =
        Array.AsReadOnly(SyncBaselines.ToArray());
}
