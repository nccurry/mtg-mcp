using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Exposes the seeded simulation tool.
/// </summary>
internal sealed class StatisticsSimulateTools
{
    /// <summary>
    /// A minimal request: land cards played one a turn, and one condition on the mana of turn 4.
    /// </summary>
    internal const string ExampleMinimal =
        "{\"deck\":{\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"},\"groups\":[{\"id\":\"lands\"," +
        "\"select\":{\"kind\":\"category-ids\",\"ids\":[\"" + StatisticsCastabilityTools.ExampleCategoryId +
        "\"]}}],\"openingHand\":{\"size\":7,\"onThePlay\":true},\"eachTurn\":[{\"kind\":\"play\",\"group\":\"lands\"," +
        "\"pick\":\"random\",\"maxPerTurn\":1,\"manaFrom\":\"same-turn\"}],\"turns\":4,\"conditions\":[{\"id\":" +
        "\"four-mana\",\"check\":{\"kind\":\"mana-at-least\",\"amount\":4,\"turn\":4}}],\"samples\":5000," +
        "\"sampleHands\":0}";

    /// <summary>
    /// A full request: colored lands, a free-first mulligan, ramp, a card-draw spell, and three kinds of check.
    /// </summary>
    internal const string ExampleFull =
        "{\"deck\":{\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\",\"expectedRevision\":" +
        DeckToolExamples.PlaceholderRevision + "},\"groups\":[{\"id\":\"lands\"," +
        "\"select\":{\"kind\":\"category-ids\"," +
        "\"ids\":[\"" + StatisticsCastabilityTools.ExampleCategoryId + "\"]},\"produces\":{\"default\":[\"G\"]," +
        "\"byEntry\":[{\"entryIds\":[\"" + DeckToolExamples.PlaceholderEntryId + "\"],\"colors\":[\"B\",\"G\"]}]}}," +
        "{\"id\":\"ramp\",\"select\":{\"kind\":\"entry-ids\",\"ids\":[\"" + StatisticsCastabilityTools.ExampleEntryId2 +
        "\"]},\"produces\":{\"default\":[\"any\"]}}],\"openingHand\":{\"size\":7,\"onThePlay\":false,\"mulligan\":" +
        "{\"rule\":\"london\",\"freeFirstMulligan\":true,\"maxMulligans\":2,\"keepWhen\":{\"kind\":\"count-between\"," +
        "\"group\":\"lands\",\"min\":2,\"max\":5},\"bottom\":{\"preferGroups\":[],\"otherwise\":\"random\"}}}," +
        "\"eachTurn\":[{\"kind\":\"play\",\"group\":\"lands\",\"pick\":\"random\",\"maxPerTurn\":1,\"manaFrom\":" +
        "\"same-turn\"},{\"kind\":\"cast-if-able\",\"group\":\"ramp\",\"pick\":\"listed-order\",\"maxPerTurn\":1," +
        "\"cost\":2,\"manaFrom\":\"next-turn\"}],\"turns\":5,\"conditions\":[{\"id\":\"cast-turn-4\",\"check\":" +
        "{\"kind\":\"can-cast\",\"manaCost\":\"{2}{B}{G}\",\"turn\":4}},{\"id\":\"ramp-seen\",\"check\":" +
        "{\"kind\":\"drawn-at-least\",\"group\":\"ramp\",\"count\":1,\"byTurn\":2}}],\"samples\":20000," +
        "\"sampleHands\":3,\"seed\":\"9f3c1a7e5b2d4c60\"}";

    /// <summary>
    /// Runs the simulation.
    /// </summary>
    private readonly StatisticsSimulateCoordinator coordinator;

    /// <summary>
    /// Creates the tool around one coordinator.
    /// </summary>
    internal StatisticsSimulateTools(StatisticsSimulateCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Samples seeded games of a local deck under rules the caller states and reports how often each condition held.
    /// </summary>
    [McpServerTool(
        Name = "stats_simulate",
        Title = "Simulate Deck Games",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Seeded sampling: an estimate, not exact math. Shuffles the main-zone cards of one local deck and plays " +
        "your rules over many random games, then reports how often each condition you name held (hits, rate, 95% " +
        "Wilson interval) with the seed that replays it. You state every choice; only these rules are fixed. " +
        "1) Groups pick main-zone entries by entry or category ID and must not overlap; cards in no group are " +
        "drawn and stay in hand. 2) Each turn: draw one card (none on turn 1 on the play), apply each eachTurn " +
        "rule in listed order up to maxPerTurn times, then check conditions. 3) pick random is uniform and " +
        "seeded; listed-order takes the entry listed first in select.ids (entry-ids groups only); target-first " +
        "(play only) takes the card whose mana lets the mana in play pay the most colored symbols of targetCost, " +
        "then the most symbols, then mana usable this turn, then the entry ID that sorts first. 4) same-turn mana " +
        "is usable the turn the card enters play, next-turn mana the turn after; unused mana is lost at turn " +
        "end. 5) London mulligan: draw a new hand while keepWhen fails, up to maxMulligans (then keep the last " +
        "hand), then bottom cards from preferGroups, else at random; freeFirstMulligan bottoms none the first " +
        "time. 6) play is free; cast-if-able and draw-cards need cost generic mana unspent and spend it. 7) " +
        "draw-cards draws at once, so later rules that turn can use the cards; each-turn cards draw again at " +
        "every later draw step; drawing from an empty library stops and counts the game in libraryEmpty. 8) The " +
        "first sampleHands games are returned in full and are counted; moved[].rule is the eachTurn index; " +
        "mulligans.keptByRule counts hands that passed keepWhen, keptAtLimit hands kept at the limit; skipped " +
        "lists category cards outside the main zone. 9) produces gives the colors each card adds (W, U, B, R, G, " +
        "C, or any of W/U/B/R/G); without it a card adds colorless mana for generic costs only. can-cast pays its " +
        "cost plus all mana spent that turn with one assignment; hybrid takes either color; Phyrexian needs its " +
        "color (life is not modeled). Limits: samples x turns x deck cards <= 50,000,000 and a cost estimate " +
        "<= 400,000,000 (can-cast checks and target-first rules are costly), 1,000 units of mana in play. " +
        "preset on-curve replaces groups, openingHand, eachTurn, turns, and conditions: one land a turn toward a " +
        "target card, from installed card data." +
        UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "minimal): " + ExampleMinimal +
        DeckToolExamples.ExamplePrefix + "full): " + ExampleFull)]
    internal Task<OperationResult<SimulateToolResult>> SimulateAsync(
        [Description("The saved deck whose main zone is shuffled: at most 150 entries and 500 cards.")]
        SimulationDeckInput deck,
        [Description("Number of sampled games, from 1 to 100000.")]
        int samples,
        [Description("How many of the first samples to return in full, from 0 to 10, and no more than samples.")]
        int sampleHands,
        [Description(
            "Groups of main-zone cards that rules and checks name. Optional. At most 20 groups, at most 150 IDs in " +
            "one select and 300 in the request.")]
        IReadOnlyList<SimulationGroupInput>? groups = null,
        [Description("Opening hand size, play or draw, and the optional mulligan. Required unless preset is given.")]
        SimulationOpeningHandInput? openingHand = null,
        [Description("Rules applied each turn, in order, at most 20. Required unless preset is given.")]
        IReadOnlyList<SimulationRuleInput>? eachTurn = null,
        [Description("Last turn to model, from 1 to 20. Required unless preset is given.")]
        int? turns = null,
        [Description(
            "1 to 20 conditions whose success rate is reported; each holds at most 20 checks nested 4 deep. " +
            "Required unless preset is given.")]
        IReadOnlyList<SimulationConditionInput>? conditions = null,
        [Description("A ready-made rule set; only on-curve. Not allowed together with groups, openingHand, eachTurn, " +
            "turns, or conditions.")]
        SimulationPresetInput? preset = null,
        [Description("Optional 16-character hexadecimal seed. A missing seed is generated and returned.")]
        string? seed = null,
        CancellationToken cancellationToken = default)
    {
        return coordinator.SimulateAsync(
            deck,
            groups,
            openingHand,
            eachTurn,
            turns,
            conditions,
            preset,
            samples,
            sampleHands,
            seed,
            cancellationToken);
    }
}
