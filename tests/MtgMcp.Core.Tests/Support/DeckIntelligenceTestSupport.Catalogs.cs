
namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains small provider and catalog fixtures for deck intelligence tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Provides card data for budget filtering tests.
    /// </summary>
    private sealed class GoalBudgetCatalog : ICardCatalog
    {
        /// <summary>
        /// Searches budget goal candidates.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results =
            [
                new CardSearchResult { Name = "Mystery Table Spell" },
                new CardSearchResult { Name = "Syphon Mind" }
            ];
            return Task.FromResult(results);
        }

        /// <summary>
        /// Searches budget goal candidates from a semantic request.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return SearchCardsAsync(request.Preset.ToString(), limit, cancellationToken);
        }

        /// <summary>
        /// Gets a budget goal card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets budget goal cards by name.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, CreateCard, StringComparer.OrdinalIgnoreCase));
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
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a budget test card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            return name.Equals("Syphon Mind", StringComparison.OrdinalIgnoreCase)
                ? new CardInfo
                {
                    Name = "Syphon Mind",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Sorcery",
                    OracleText = "Each opponent discards a card. You draw a card for each card discarded this way.",
                    ColorIdentity = ["B"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                }
                : new CardInfo
                {
                    Name = "Mystery Table Spell",
                    ManaCost = "{2}{B}",
                    ManaValue = 3,
                    TypeLine = "Sorcery",
                    OracleText = "Each opponent loses 2 life. Each opponent sacrifices a creature.",
                    ColorIdentity = ["B"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" }
                };
        }
    }

    /// <summary>
    /// Provides card data for release metadata tests.
    /// </summary>
    private sealed class TrendMetadataCatalog : ICardCatalog
    {
        /// <summary>
        /// Searches recent cards with explicit print metadata.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results =
            [
                new CardSearchResult { Name = "Reprinted Drain", Set = "new", ReleasedAt = new DateOnly(2026, 2, 1) },
                new CardSearchResult { Name = "Unpriced New Card", Set = "new", ReleasedAt = new DateOnly(2026, 2, 1) }
            ];
            return Task.FromResult(results);
        }

        /// <summary>
        /// Searches recent cards from a semantic request.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return SearchCardsAsync(request.Preset.ToString(), limit, cancellationToken);
        }

        /// <summary>
        /// Gets a recent card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets recent cards by name.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, CreateCard, StringComparer.OrdinalIgnoreCase));
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
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a release metadata test card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            Dictionary<string, string> prices = name.Equals("Reprinted Drain", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new CardInfo
            {
                Name = name,
                ManaCost = "{1}{B}",
                ManaValue = 2,
                TypeLine = "Sorcery",
                OracleText = "Each opponent loses life and you create a token.",
                Set = "old",
                ReleasedAt = new DateOnly(2020, 1, 1),
                ColorIdentity = ["B"],
                Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                Prices = prices
            };
        }
    }

    /// <summary>
    /// Provides a failing trend provider.
    /// </summary>
    private sealed class ThrowingCardTrendProvider : ICardTrendProvider
    {
        /// <summary>
        /// Throws for trend lookup.
        /// </summary>
        public Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
            CardTrendQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("trend unavailable");
        }
    }

    /// <summary>
    /// Provides a failing Commander meta provider.
    /// </summary>
    private sealed class ThrowingCommanderMetaProvider : ICommanderMetaProvider
    {
        /// <summary>
        /// Throws for Commander meta lookup.
        /// </summary>
        public Task<CommanderMetaReport> GetCommanderMetaAsync(
            CommanderMetaQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("meta unavailable");
        }
    }

    /// <summary>
    /// Provides fixed Commander meta rows.
    /// </summary>
    private sealed class FixedCommanderMetaProvider : ICommanderMetaProvider
    {
        /// <summary>
        /// Stores fixed Commander meta data.
        /// </summary>
        private readonly CommanderMetaReport report;

        /// <summary>
        /// Creates a fixed Commander meta provider.
        /// </summary>
        public FixedCommanderMetaProvider(CommanderMetaReport report)
        {
            this.report = report;
        }

        /// <summary>
        /// Returns fixed Commander meta data.
        /// </summary>
        public Task<CommanderMetaReport> GetCommanderMetaAsync(
            CommanderMetaQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(report);
        }
    }

    /// <summary>
    /// Provides fixed trend suggestions.
    /// </summary>
    private sealed class FixedCardTrendProvider : ICardTrendProvider
    {
        /// <summary>
        /// Stores fixed trend suggestions.
        /// </summary>
        private readonly IReadOnlyList<NewCardSuggestion> suggestions;

        /// <summary>
        /// Creates a fixed trend provider.
        /// </summary>
        public FixedCardTrendProvider(IReadOnlyList<NewCardSuggestion> suggestions)
        {
            this.suggestions = suggestions;
        }

        /// <summary>
        /// Returns fixed trend suggestions.
        /// </summary>
        public Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
            CardTrendQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(suggestions);
        }
    }

    /// <summary>
    /// Provides a failing combo catalog.
    /// </summary>
    private sealed class ThrowingComboCatalog : IComboCatalog
    {
        /// <summary>
        /// Throws for combo lookup.
        /// </summary>
        public Task<DeckComboReport> FindCombosAsync(
            ComboCatalogQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("combo unavailable");
        }

        /// <summary>
        /// Throws for combo card search.
        /// </summary>
        public Task<IReadOnlyList<ComboEvidence>> SearchCombosByCardAsync(
            ComboCardSearchQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("combo unavailable");
        }

        /// <summary>
        /// Throws for combo detail lookup.
        /// </summary>
        public Task<ComboEvidence?> GetComboDetailsAsync(
            ComboDetailsQuery query,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("combo unavailable");
        }
    }

}
