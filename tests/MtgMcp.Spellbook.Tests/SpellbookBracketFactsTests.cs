using System.Net;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Verifies the Commander Spellbook bracket estimate is read as facts only, through the existing transport, cache, and
/// failure mapping, against a response recorded from the real service (BRACKET-001, cases B01 to B04).
/// </summary>
public sealed class SpellbookBracketFactsTests
{
    /// <summary>
    /// Holds the deck sent in the tests: one commander and eight main cards.
    /// </summary>
    private static readonly SpellbookDeckRequest Deck = new(
        [new SpellbookDeckEntry("Kinnan, Bonder Prodigy", 1)],
        [
            new SpellbookDeckEntry("Thassa's Oracle", 1),
            new SpellbookDeckEntry("Demonic Consultation", 1),
            new SpellbookDeckEntry("Rhystic Study", 1),
            new SpellbookDeckEntry("Armageddon", 1),
            new SpellbookDeckEntry("Time Warp", 1),
            new SpellbookDeckEntry("Sol Ring", 1),
            new SpellbookDeckEntry("Basalt Monolith", 1),
            new SpellbookDeckEntry("Rings of Brighthearth", 1),
        ]);

    /// <summary>
    /// Verifies per-card flags and per-combo details are read, and the request is one POST to the estimate endpoint
    /// with the deck as its body (case B01).
    /// </summary>
    [Fact]
    public async Task EstimateBracketFacts_RealResponse_GivesCardFlagsAndComboDetails()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson(await Fixture());
        using TemporarySpellbookDirectory directory = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        using SpellbookService service = SpellbookTestFactory.CreateService(handler, directory, clock);

        SpellbookBracketFacts facts = Success(await service.EstimateBracketFactsAsync(
            Deck,
            TestContext.Current.CancellationToken));

        CapturedSpellbookRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/estimate-bracket", request.Uri.PathAndQuery);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Kinnan, Bonder Prodigy",
            body.RootElement.GetProperty("commanders")[0].GetProperty("card").GetString());
        Assert.Equal(8, body.RootElement.GetProperty("main").GetArrayLength());
        Assert.Equal(9, facts.Cards.Count);
        Assert.Equal(
            ["Thassa's Oracle", "Rhystic Study"],
            facts.Cards.Where(card => card.GameChanger).Select(card => card.Name));
        Assert.Equal("Armageddon", Assert.Single(facts.Cards, card => card.MassLandDenial).Name);
        Assert.Equal("Time Warp", Assert.Single(facts.Cards, card => card.ExtraTurn).Name);
        Assert.DoesNotContain(facts.Cards, card => card.Banned);
        Assert.NotNull(facts.Cards[0].OracleId);
        Assert.Equal(3, facts.Combos.Count);
        SpellbookBracketCombo oracle = Assert.Single(facts.Combos, combo => combo.VariantId == "742-1295");
        Assert.Equal(["Demonic Consultation", "Thassa's Oracle"], oracle.Cards.Order(StringComparer.Ordinal));
        Assert.True(oracle.DefinitelyTwoCard);
        Assert.True(oracle.ArguablyTwoCard);
        Assert.Equal(5, oracle.Speed);
        Assert.Equal("{U}{U}{B}", oracle.ManaNeeded);
        Assert.Equal(3, oracle.ManaValueNeeded);
        Assert.Equal("estimate-bracket", facts.Evidence.Operation);
        Assert.Equal("network", facts.Evidence.CacheStatus);
    }

    /// <summary>
    /// Verifies the provider's bracket label, at the top level and inside each combo, never reaches a result (case
    /// B02).
    /// </summary>
    [Fact]
    public async Task EstimateBracketFacts_ProviderLabel_NeverReachesTheResult()
    {
        string raw = await Fixture();
        Assert.Contains("\"bracketTag\"", raw, StringComparison.Ordinal);
        SpellbookTestHttpHandler handler = new();
        handler.AddJson(raw);
        using TemporarySpellbookDirectory directory = new();
        using SpellbookService service = SpellbookTestFactory.CreateService(
            handler,
            directory,
            new MutableTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)));

        SpellbookBracketFacts facts = Success(await service.EstimateBracketFactsAsync(
            Deck,
            TestContext.Current.CancellationToken));

        string json = JsonSerializer.Serialize(facts);
        Assert.DoesNotContain("bracketTag", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tag", json, StringComparison.Ordinal);
        foreach (Type type in new[]
        {
            typeof(SpellbookBracketFacts), typeof(SpellbookBracketCard), typeof(SpellbookBracketCombo),
            typeof(SpellbookFactsEvidence),
        })
        {
            Assert.DoesNotContain(type.GetProperties(), property => property.Name.Contains("Bracket",
                StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Verifies the same request twice within the cache window reports cached and sends one HTTP request (case B03).
    /// </summary>
    [Fact]
    public async Task EstimateBracketFacts_SameRequestTwice_UsesTheCache()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson(await Fixture());
        using TemporarySpellbookDirectory directory = new();
        using SpellbookService service = SpellbookTestFactory.CreateService(
            handler,
            directory,
            new MutableTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)));

        SpellbookBracketFacts first = Success(await service.EstimateBracketFactsAsync(Deck,
            TestContext.Current.CancellationToken));
        SpellbookBracketFacts second = Success(await service.EstimateBracketFactsAsync(Deck,
            TestContext.Current.CancellationToken));

        Assert.Equal("network", first.Evidence.CacheStatus);
        Assert.Equal("cached", second.Evidence.CacheStatus);
        Assert.Single(handler.Requests);
        Assert.Equal(first.Cards, second.Cards);
    }

    /// <summary>
    /// Verifies a 503 maps to the existing unavailable failure (case B04), a bad deck fails before any request, and a
    /// response without its lists is unavailable.
    /// </summary>
    [Fact]
    public async Task EstimateBracketFacts_FailuresAndBadInput_UseTheExistingFailureKinds()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddStatus(HttpStatusCode.ServiceUnavailable);
        handler.AddJson("{\"bracketTag\":\"R\"}");
        using TemporarySpellbookDirectory directory = new();
        using SpellbookService service = SpellbookTestFactory.CreateService(
            handler,
            directory,
            new MutableTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)));

        OperationResult<SpellbookBracketFacts> outage = await service.EstimateBracketFactsAsync(
            Deck,
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookBracketFacts> invalidBody = await service.EstimateBracketFactsAsync(
            new SpellbookDeckRequest([new SpellbookDeckEntry("Other", 1)], [new SpellbookDeckEntry("Sol Ring", 1)]),
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookBracketFacts> empty = await service.EstimateBracketFactsAsync(
            new SpellbookDeckRequest([], []),
            TestContext.Current.CancellationToken);

        Assert.Equal("source-unavailable", Assert.IsType<OperationUnavailable>(outage.Value).ReasonCode);
        Assert.Equal("source-invalid-response", Assert.IsType<OperationUnavailable>(invalidBody.Value).ReasonCode);
        Assert.IsType<OperationInvalidInput>(empty.Value);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies a sparse response maps with defaults instead of failing, so a field the provider adds or drops later
    /// cannot break the tool.
    /// </summary>
    [Fact]
    public async Task EstimateBracketFacts_SparseResponse_UsesDefaults()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson(
            "{\"bracketTag\":\"S\",\"cards\":[{\"card\":{\"name\":\"X\"}},{}],\"combos\":[{\"speed\":\"fast\"}]}");
        using TemporarySpellbookDirectory directory = new();
        using SpellbookService service = SpellbookTestFactory.CreateService(
            handler,
            directory,
            new MutableTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)));

        SpellbookBracketFacts facts = Success(await service.EstimateBracketFactsAsync(Deck,
            TestContext.Current.CancellationToken));

        Assert.Equal(["X", string.Empty], facts.Cards.Select(card => card.Name));
        Assert.All(facts.Cards, card => Assert.Equal(1, card.Quantity));
        SpellbookBracketCombo combo = Assert.Single(facts.Combos);
        Assert.Equal((string.Empty, 0), (combo.VariantId, combo.Speed));
    }

    /// <summary>
    /// Reads the recorded response from the fixture file.
    /// </summary>
    private static Task<string> Fixture()
    {
        return File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "estimate-bracket-response.json"),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
