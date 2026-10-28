using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>local_deck_validate</c> with and without the Commander rule set: the plain result is unchanged, the
/// facts come from installed card data, and missing card data is reported (RULE-001, cases R01 and R08).
/// </summary>
public sealed class DeckRuleToolTests
{
    /// <summary>
    /// Holds the real cards recorded from Scryfall, by name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, JsonObject> Recorded = CardJson.Recorded();

    /// <summary>
    /// Verifies a call without rules returns only the structural report, so existing callers see no change.
    /// </summary>
    [Fact]
    public async Task Validate_WithoutRules_ReturnsOnlyTheStructuralReport()
    {
        using CardDataFixture data = await CardDataFixture.CreateAsync([.. Cards()]);
        DeckDocument deck = await CreateDeckAsync(data);

        DeckValidationView view = Success(await Coordinator(data).ValidateAsync(
            deck.DeckId,
            null,
            TestContext.Current.CancellationToken));

        Assert.True(view.IsStructurallyValid);
        Assert.Null(view.Rules);
        JsonNode json = JsonSerializer.SerializeToNode(view, ToolCallPolicy.SerializerOptions)!;
        Assert.Equal(
            ["deckId", "revision", "isStructurallyValid", "issues"],
            json.AsObject().Select(pair => pair.Key));
    }

    /// <summary>
    /// Verifies the Commander rule set adds facts worked out from installed card data, and an entry with no Scryfall
    /// identity is listed as unresolved.
    /// </summary>
    [Fact]
    public async Task Validate_CommanderRules_AddsFactsFromInstalledCardData()
    {
        using CardDataFixture data = await CardDataFixture.CreateAsync([.. Cards()]);
        DeckDocument deck = await CreateDeckAsync(data);

        DeckValidationView view = Success(await Coordinator(data).ValidateAsync(
            deck.DeckId,
            "commander",
            TestContext.Current.CancellationToken));

        CommanderRuleFacts facts = view.Rules!;
        Assert.Equal("commander", facts.Rules);
        Assert.Equal(4, facts.DeckSize.Count);
        Assert.Equal("legendary-creature", Assert.Single(facts.Commanders.Items).Via);
        Assert.Equal(["G"], facts.ColorIdentity.Identity);
        Assert.Equal(
            ["Lightning Bolt", "Rhystic Study"],
            facts.ColorIdentity.OffIdentity.Select(entry => entry.Name));
        Assert.Equal("Home-made proxy", Assert.Single(facts.Unresolved).Name);
        Assert.Equal("Rhystic Study", Assert.Single(facts.GameChangers.Entries).Name);
        JsonNode json = JsonSerializer.SerializeToNode(view, ToolCallPolicy.SerializerOptions)!;
        Assert.NotNull(json["rules"]!["deckSize"]);
        Assert.Null(json["rules"]!["pairing"]);
    }

    /// <summary>
    /// Verifies card data that is not installed gives not-cached and names the sync tool (case R08), and an unknown
    /// rule set, a missing deck, and an empty ID are refused.
    /// </summary>
    [Fact]
    public async Task Validate_WithoutCardDataOrWithBadInput_FailsWithTheReason()
    {
        using CardDataFixture bare = await CardDataFixture.CreateAsync([.. Cards()], install: false);
        DeckDocument deck = await CreateDeckAsync(bare);
        DeckRuleCoordinator coordinator = Coordinator(bare);

        OperationNotCached notCached = Assert.IsType<OperationNotCached>((await coordinator.ValidateAsync(
            deck.DeckId,
            "commander",
            TestContext.Current.CancellationToken)).Value);
        OperationInvalidInput unknown = Assert.IsType<OperationInvalidInput>((await coordinator.ValidateAsync(
            deck.DeckId,
            "modern",
            TestContext.Current.CancellationToken)).Value);
        OperationResult<DeckValidationView> missing = await coordinator.ValidateAsync(
            Guid.NewGuid(),
            "commander",
            TestContext.Current.CancellationToken);

        Assert.Contains("scryfall_card_data_sync", notCached.Message, StringComparison.Ordinal);
        Assert.Equal("rules", unknown.Path);
        Assert.IsType<OperationNotFound>(missing.Value);
        Assert.True(Success(await coordinator.ValidateAsync(
            deck.DeckId,
            null,
            TestContext.Current.CancellationToken)).IsStructurallyValid);
    }

    /// <summary>
    /// Lists the recorded cards the deck uses.
    /// </summary>
    private static IEnumerable<JsonObject> Cards()
    {
        return
        [
            Recorded["Kodama of the East Tree"],
            Recorded["Forest"],
            Recorded["Lightning Bolt"],
            Recorded["Rhystic Study"],
        ];
    }

    /// <summary>
    /// Builds the coordinator under test.
    /// </summary>
    private static DeckRuleCoordinator Coordinator(CardDataFixture data)
    {
        return new DeckRuleCoordinator(data.Store, data.Scryfall);
    }

    /// <summary>
    /// Creates a four-entry deck: a commander, a Forest, two off-identity cards, and an entry with no card.
    /// </summary>
    private static async Task<DeckDocument> CreateDeckAsync(CardDataFixture data)
    {
        static DeckEntryDraft Draft(string name, string zone, int quantity = 1)
        {
            return new DeckEntryDraft(
                quantity,
                name,
                PrintingId: CardJson.PrintingOf(Recorded[name]),
                Zone: zone);
        }

        DeckCreateRequest request = new(
            "Rules",
            Format: "commander",
            Entries:
            [
                Draft("Kodama of the East Tree", "commander"),
                Draft("Lightning Bolt", "main"),
                Draft("Rhystic Study", "main"),
                new DeckEntryDraft(1, "Home-made proxy", Zone: "main"),
            ]);
        return Success(await data.Store.CreateAsync(request, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return result.Value is OperationSuccess<T> success
            ? success.Data
            : throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(result.Value));
    }
}
