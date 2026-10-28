using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.CardLists;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Exposes the castability tool.
/// </summary>
internal sealed class StatisticsCastabilityTools
{
    /// <summary>
    /// Stands for a group placeholder in the examples.
    /// </summary>
    internal const string ExampleCategoryId = "0193e97a-ef95-7700-8000-000000000021";

    /// <summary>
    /// Stands for a second entry ID in the examples.
    /// </summary>
    internal const string ExampleEntryId2 = "0193e97a-ef95-7700-8000-000000000012";

    /// <summary>
    /// A minimal request: one group chosen by category, each spell checked on its mana value.
    /// </summary>
    internal const string ExampleMinimal =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"," +
        "\"zones\":[\"main\"]},\"sources\":[{\"id\":\"lands\",\"select\":{\"kind\":\"category-ids\",\"ids\":[\"" +
        ExampleCategoryId + "\"]},\"produces\":{\"default\":[\"G\"]}}],\"turn\":{\"kind\":\"mana-value\"}," +
        "\"onThePlay\":true,\"requireLandDrops\":true}";

    /// <summary>
    /// A full request: two groups, a color override, a fixed turn, and a backup source.
    /// </summary>
    internal const string ExampleFull =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId +
        "\",\"expectedRevision\":" + DeckToolExamples.PlaceholderRevision + ",\"zones\":[\"main\"]}," +
        "\"sources\":[{\"id\":\"lands\",\"select\":{\"kind\":\"category-ids\",\"ids\":[\"" + ExampleCategoryId +
        "\"]},\"produces\":{\"default\":[\"G\"],\"byEntry\":[{\"entryIds\":[\"" + DeckToolExamples.PlaceholderEntryId +
        "\"],\"colors\":[\"B\",\"G\"]}]}},{\"id\":\"rocks\",\"select\":{\"kind\":\"entry-ids\",\"ids\":[\"" +
        ExampleEntryId2 + "\"]},\"produces\":{\"default\":[\"C\"]}}]," +
        "\"turn\":{\"kind\":\"fixed\",\"turn\":4},\"onThePlay\":false,\"requireLandDrops\":false}";

    /// <summary>
    /// Runs the calculation.
    /// </summary>
    private readonly StatisticsCastabilityCoordinator coordinator;

    /// <summary>
    /// Creates the tool around one coordinator.
    /// </summary>
    internal StatisticsCastabilityTools(StatisticsCastabilityCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Calculates the exact chance that each spell of a deck can be cast on curve.
    /// </summary>
    [McpServerTool(
        Name = "stats_castability",
        Title = "Calculate Spell Castability",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Exact math: for each distinct mana cost of a deck's nonland cards, the exact probability that the cards " +
        "seen by its turn hold sources that can pay it, using at most one source per turn. You declare the source " +
        "groups (by entry or category ID) and the colors each makes, and choose the turn rule (mana-value, or " +
        "fixed), play or draw, and requireLandDrops. With requireLandDrops true, only draws that have seen at least " +
        "as many cards from your declared source groups as the turn number count (Frank Karsten's method), so the " +
        "probability is conditional on that; false counts every draw and equals stats_mana_availability. " +
        "Rows are sorted by probability, lowest first, then name. It states no target and no pass; what counts as a " +
        "source and what probability is good enough are your judgments. Cards with no cost (no-mana-cost), X (x-cost), hybrid or " +
        "Phyrexian symbols, other symbols, or two costs (split-card-has-two-costs), a mana value past turn 99, and " +
        "entries with no card data are listed in skipped with that reason. source is a deck or deck-backup " +
        "list source and needs installed card data (run scryfall_card_data_sync to install it)." +
        UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "minimal): " + ExampleMinimal +
        DeckToolExamples.ExamplePrefix + "full): " + ExampleFull)]
    internal Task<OperationResult<CastabilityToolResult>> CalculateAsync(
        [Description("The deck to read: a deck or deck-backup list source. Choose zones to leave out the commander.")]
        CardListSourceInput source,
        [Description("Mana source groups. Groups must not overlap.")]
        IReadOnlyList<CastabilitySourceGroupInput> sources,
        [Description("When each spell is checked: mana-value (its mana value, at least 1) or fixed with a turn.")]
        CastabilityTurnInput turn,
        [Description("True if the player goes first (7 + turn - 1 cards seen), false on the draw (7 + turn).")]
        bool onThePlay,
        [Description(
            "Required. True counts only draws that have seen at least as many cards from the declared source " +
            "groups as the turn number, so the probability is conditional on that.")]
        bool requireLandDrops,
        CancellationToken cancellationToken = default)
    {
        return coordinator.CalculateAsync(source, sources, turn, onThePlay, requireLandDrops, cancellationToken);
    }
}
