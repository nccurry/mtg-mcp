using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes one read-only sampled deck mana estimate in every operation mode.
/// </summary>
internal sealed class DeckOnCurveEstimateTools
{
    /// <summary>
    /// Joins saved-deck facts with the bounded pure calculation.
    /// </summary>
    private readonly DeckOnCurveEstimateCoordinator coordinator;

    /// <summary>
    /// Creates the public read-only tool around one App coordinator.
    /// </summary>
    internal DeckOnCurveEstimateTools(DeckOnCurveEstimateCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Estimates whether one target can be cast by a stated turn under caller-provided land rules.
    /// </summary>
    [McpServerTool(
        Name = "deck_on_curve_estimate",
        Title = "Estimate Target Card On Curve",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Samples a stated land-only model for one saved deck and target card; returns source facts, " +
        "replay details, uncertainty, and limits without a deckbuilding recommendation.")]
    internal Task<OperationResult<DeckOnCurveEstimateResult>> EstimateAsync(
        [Description("Stable local deck UUID returned by deck_get.")] Guid deckId,
        [Description("The exact deck revision returned by deck_get; a changed deck returns a conflict.")]
        long expectedRevision,
        [Description("Stable ID of one target entry in the deck mainboard.")] Guid targetEntryId,
        [Description("Last turn to model, from 1 through 12.")] int turnLimit,
        [Description("One stated rule for every eligible single-faced land in the mainboard.")]
        IReadOnlyList<DeckOnCurveLandRuleToolInput>? landRules,
        [Description("Number of shuffled hands to sample, from 100 through 100000. Default: 10000.")]
        int sampleCount = 10_000,
        [Description("True skips the normal draw on turn one. Default: true.")] bool onThePlay = true,
        [Description("Optional 16-character hexadecimal replay seed. A missing seed is generated and returned.")]
        string? seed = null,
        CancellationToken cancellationToken = default)
    {
        return coordinator.EstimateAsync(
            new DeckOnCurveEstimateToolRequest(
                deckId,
                expectedRevision,
                targetEntryId,
                turnLimit,
                landRules,
                sampleCount,
                onThePlay,
                seed),
            cancellationToken);
    }
}
