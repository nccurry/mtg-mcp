using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Contains tests for Scryfall-backed deckbuilding context providers.
/// </summary>
public sealed class ScryfallProviderTests
{
    /// <summary>
    /// Verifies that recent-card provider searches, filters, scores, and caches results.
    /// </summary>
    [Fact]
    public async Task CardTrendProvider_FindsFilteredRecentCardsAndCachesClones()
    {
        FakeCardCatalog catalog = new()
        {
            SearchResults =
            [
                new CardSearchResult
                {
                    Name = "Fresh Token Maker",
                    ReleasedAt = new DateOnly(2026, 3, 1),
                    Set = "abc"
                },
                new CardSearchResult
                {
                    Name = "Pricey Token Maker",
                    ReleasedAt = new DateOnly(2026, 3, 1),
                    Set = "abc"
                },
                new CardSearchResult
                {
                    Name = "Old Token Maker",
                    ReleasedAt = new DateOnly(2025, 1, 1),
                    Set = "old"
                }
            ],
            CardsByName = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["Fresh Token Maker"] = Card("Fresh Token Maker", "Enchantment", "Create two 1/1 creature tokens.", "2.50", 500, "abc", new DateOnly(2026, 3, 1)),
                ["Pricey Token Maker"] = Card("Pricey Token Maker", "Enchantment", "Create a creature token.", "12.00", 800, "abc", new DateOnly(2026, 3, 1)),
                ["Old Token Maker"] = Card("Old Token Maker", "Enchantment", "Create a creature token.", "1.00", 900, "old", new DateOnly(2025, 1, 1))
            }
        };
        ScryfallCardTrendProvider provider = new(catalog);
        CardTrendQuery query = new()
        {
            Format = "commander",
            Theme = "tokens-provider-test",
            Since = new DateOnly(2026, 1, 1),
            SetCode = "abc",
            MaxPrice = 5,
            Limit = 2
        };

        IReadOnlyList<NewCardSuggestion> first = await provider.FindNewCardsAsync(
            query,
            TestContext.Current.CancellationToken);
        first[0].Tags.Add("mutated");
        IReadOnlyList<NewCardSuggestion> second = await provider.FindNewCardsAsync(
            query,
            TestContext.Current.CancellationToken);

        first.Should().ContainSingle();
        first[0].CardName.Should().Be("Fresh Token Maker");
        first[0].Tags.Should().Contain(DeckTags.Tokens);
        second.Should().ContainSingle();
        second[0].Tags.Should().NotContain("mutated");
        catalog.SearchCalls.Should().Be(1);
        catalog.LastSearchQuery.Should().Contain("legal:commander");
        catalog.LastSearchQuery.Should().Contain("date>=2026-01-01");
        catalog.LastSearchQuery.Should().Contain("set:abc");
        catalog.LastSearchQuery.Should().Contain("usd<=5");
    }

    /// <summary>
    /// Verifies that Commander meta provider builds popularity rows and caches cloned reports.
    /// </summary>
    [Fact]
    public async Task CommanderMetaProvider_BuildsPopularityContextAndCachesClones()
    {
        FakeCardCatalog catalog = new()
        {
            SearchResults =
            [
                new CardSearchResult { Name = "Blood Artist" },
                new CardSearchResult { Name = "Village Rites" }
            ],
            CardsByName = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["Blood Artist"] = Card("Blood Artist", "Creature", "Whenever a creature dies, target player loses 1 life and you gain 1 life.", "3.00", 250, "clu", null),
                ["Village Rites"] = Card("Village Rites", "Instant", "As an additional cost, sacrifice a creature. Draw two cards.", "0.10", 2_000, "m21", null)
            }
        };
        ScryfallCommanderMetaProvider provider = new(catalog);
        CommanderMetaQuery query = new()
        {
            Commander = "Teysa Karlov",
            Theme = "aristocrats-provider-test",
            Format = "commander",
            Limit = 5
        };

        CommanderMetaReport first = await provider.GetCommanderMetaAsync(
            query,
            TestContext.Current.CancellationToken);
        first.PopularCards[0].Name = "mutated";
        CommanderMetaReport second = await provider.GetCommanderMetaAsync(
            query,
            TestContext.Current.CancellationToken);

        first.Source.Should().Be("Scryfall global EDHREC-rank search");
        second.PopularCards.Should().HaveCount(2);
        second.PopularCards[0].Name.Should().Be("Blood Artist");
        second.PopularCards[0].InclusionRate.Should().BeGreaterThan(second.PopularCards[1].InclusionRate);
        second.PopularCards.Should().Contain(card => card.Category == DeckRoles.Draw);
        second.Notes.Should().Contain(note => note.Contains("global EDHREC rank", StringComparison.OrdinalIgnoreCase));
        catalog.SearchCalls.Should().Be(1);
        catalog.LastSearchQuery.Should().Contain("legal:commander");
        catalog.LastSearchQuery.Should().Contain("-t:basic");
    }

    /// <summary>
    /// Creates a card info fixture.
    /// </summary>
    private static CardInfo Card(
        string name,
        string typeLine,
        string oracleText,
        string usd,
        int edhrecRank,
        string set,
        DateOnly? releasedAt)
    {
        return new CardInfo
        {
            Name = name,
            TypeLine = typeLine,
            OracleText = oracleText,
            EdhrecRank = edhrecRank,
            Set = set,
            ReleasedAt = releasedAt,
            Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["usd"] = usd
            },
            Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["commander"] = "legal"
            },
            ScryfallUri = $"https://scryfall.test/card/{set}/{Uri.EscapeDataString(name)}"
        };
    }

    /// <summary>
    /// Provides fake card catalog behavior.
    /// </summary>
    private sealed class FakeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Gets or sets fake search results.
        /// </summary>
        public IReadOnlyList<CardSearchResult> SearchResults { get; set; } = [];

        /// <summary>
        /// Gets or sets fake cards by name.
        /// </summary>
        public Dictionary<string, CardInfo> CardsByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets search call count.
        /// </summary>
        public int SearchCalls { get; private set; }

        /// <summary>
        /// Gets the last search query.
        /// </summary>
        public string LastSearchQuery { get; private set; } = "";

        /// <summary>
        /// Searches fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            SearchCalls++;
            LastSearchQuery = query;
            return Task.FromResult<IReadOnlyList<CardSearchResult>>(SearchResults.Take(limit).ToList());
        }

        /// <summary>
        /// Gets a fake card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            CardsByName.TryGetValue(nameOrId, out CardInfo? card);
            return Task.FromResult(card);
        }

        /// <summary>
        /// Gets fake cards by names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, CardInfo> cards = names
                .Where(CardsByName.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, name => CardsByName[name], StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(cards);
        }

        /// <summary>
        /// Gets no fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets no fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests no fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }
}
