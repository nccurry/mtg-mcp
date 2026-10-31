using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Builds the deck, card data, and tool that the <c>stats_simulate</c> tests share, and the request records they send.
/// </summary>
internal sealed class SimulateSetup : IDisposable
{
    /// <summary>
    /// Identifies the saved deck.
    /// </summary>
    internal static readonly Guid DeckId = Guid.Parse("0193e97a-ef95-7700-8000-0000000000d1");

    /// <summary>
    /// Identifies the category that holds the land entries.
    /// </summary>
    internal static readonly Guid LandsCategoryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000021");

    /// <summary>
    /// Identifies the land entry, 24 copies in the main zone.
    /// </summary>
    internal static readonly Guid LandEntryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000011");

    /// <summary>
    /// Identifies the ramp entry, 4 copies in the main zone.
    /// </summary>
    internal static readonly Guid RampEntryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000012");

    /// <summary>
    /// Identifies the one-copy spell entry.
    /// </summary>
    internal static readonly Guid SpellEntryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000013");

    /// <summary>
    /// Identifies the filler entry, 31 copies in the main zone.
    /// </summary>
    internal static readonly Guid FillerEntryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000014");

    /// <summary>
    /// Identifies the commander entry, in the commander zone, which the Lands category also contains.
    /// </summary>
    internal static readonly Guid CommanderEntryId = Guid.Parse("0193e97a-ef95-7700-8000-000000000015");

    /// <summary>
    /// Creates a setup around card data.
    /// </summary>
    private SimulateSetup(CardDataFixture data)
    {
        Data = data;
        Coordinator = new StatisticsSimulateCoordinator(new CardListResolver(data.Scryfall, data.Access()));
        Tools = new StatisticsSimulateTools(Coordinator);
    }

    /// <summary>
    /// Gets the coordinator under test.
    /// </summary>
    internal StatisticsSimulateCoordinator Coordinator { get; }

    /// <summary>
    /// Gets the tool under test.
    /// </summary>
    internal StatisticsSimulateTools Tools { get; }

    /// <summary>
    /// Gets the card data fixture.
    /// </summary>
    internal CardDataFixture Data { get; }

    /// <summary>
    /// Creates a setup without card data, for requests that choose groups by entry or category ID.
    /// </summary>
    internal static async Task<SimulateSetup> CreateAsync(IReadOnlyList<JsonObject>? cards = null, bool install = false)
    {
        return new SimulateSetup(
            await CardDataFixture.CreateAsync(cards ?? [CardJson.Make("Placeholder")], install: install));
    }

    /// <summary>
    /// Creates the 60-card deck: lands, ramp, one spell, filler, and a commander the Lands category also holds.
    /// </summary>
    internal async Task<DeckDocument> CreateDeckAsync()
    {
        DeckCreateRequest request = new(
            "Simulated",
            Format: "commander",
            Entries:
            [
                new DeckEntryDraft(24, "Forest", Zone: "main", EntryId: LandEntryId),
                new DeckEntryDraft(4, "Ramp", Zone: "main", EntryId: RampEntryId),
                new DeckEntryDraft(1, "Spell", Zone: "main", EntryId: SpellEntryId),
                new DeckEntryDraft(31, "Filler", Zone: "main", EntryId: FillerEntryId),
                new DeckEntryDraft(1, "Commander", Zone: "commander", EntryId: CommanderEntryId),
            ],
            Categories: [new DeckCategoryDraft("Lands", CategoryId: LandsCategoryId)],
            CategoryAssignments:
            [
                new DeckCategoryAssignment(LandEntryId, LandsCategoryId, true),
                new DeckCategoryAssignment(CommanderEntryId, LandsCategoryId, false),
            ],
            DeckId: DeckId);
        OperationResult<DeckDocument> result = await Data.Store.CreateAsync(
            request,
            TestContext.Current.CancellationToken);
        return Assert.IsType<OperationSuccess<DeckDocument>>(result.Value).Data;
    }

    /// <summary>
    /// Runs the tool with explicit rules.
    /// </summary>
    internal Task<OperationResult<SimulateToolResult>> RunAsync(
        SimulationDeckInput? deck = null,
        IReadOnlyList<SimulationGroupInput>? groups = null,
        SimulationOpeningHandInput? openingHand = null,
        IReadOnlyList<SimulationRuleInput>? eachTurn = null,
        int? turns = 4,
        IReadOnlyList<SimulationConditionInput>? conditions = null,
        SimulationPresetInput? preset = null,
        int samples = 200,
        int sampleHands = 0,
        string? seed = "0000000000000001")
    {
        return Coordinator.SimulateAsync(
            deck ?? new SimulationDeckInput { DeckId = DeckId },
            groups ?? [Lands()],
            openingHand ?? Hand(),
            eachTurn ?? [Play("lands")],
            turns,
            conditions ?? [Condition("mana", new SimulationManaAtLeastInput { Amount = 1, Turn = 1 })],
            preset,
            samples,
            sampleHands,
            seed,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Builds the Lands group chosen by category.
    /// </summary>
    internal static SimulationGroupInput Lands(string id = "lands")
    {
        return new SimulationGroupInput
        {
            Id = id,
            Select = new CastabilityCategoryIdsSelect([LandsCategoryId]),
        };
    }

    /// <summary>
    /// Builds a group chosen by entry IDs.
    /// </summary>
    internal static SimulationGroupInput Entries(string id, params Guid[] entryIds)
    {
        return new SimulationGroupInput { Id = id, Select = new CastabilityEntryIdsSelect(entryIds) };
    }

    /// <summary>
    /// Builds an opening hand.
    /// </summary>
    internal static SimulationOpeningHandInput Hand(
        int size = 7,
        bool onThePlay = true,
        SimulationMulliganInput? mulligan = null)
    {
        return new SimulationOpeningHandInput { Size = size, OnThePlay = onThePlay, Mulligan = mulligan };
    }

    /// <summary>
    /// Builds a <c>play</c> rule.
    /// </summary>
    internal static SimulationPlayInput Play(
        string group,
        string pick = "random",
        int maxPerTurn = 1,
        string manaFrom = "same-turn",
        string? targetCost = null)
    {
        return new SimulationPlayInput
        {
            Group = group,
            Pick = pick,
            MaxPerTurn = maxPerTurn,
            ManaFrom = manaFrom,
            TargetCost = targetCost,
        };
    }

    /// <summary>
    /// Builds a named condition.
    /// </summary>
    internal static SimulationConditionInput Condition(string id, SimulationCheckInput check)
    {
        return new SimulationConditionInput { Id = id, Check = check };
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    internal static T Success<T>(OperationResult<T> result)
    {
        return result.Value is OperationSuccess<T> success
            ? success.Data
            : throw new Xunit.Sdk.XunitException(System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    /// <summary>
    /// Unwraps an invalid-input result.
    /// </summary>
    internal static OperationInvalidInput Invalid<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationInvalidInput>(result.Value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Data.Dispose();
    }
}
