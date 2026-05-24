using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Provides shared fixtures for deck intelligence service tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Creates a workspace service for workspace and intent tests.
    /// </summary>
    private static DeckWorkspaceService CreateWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        return new DeckWorkspaceService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders);
    }

    /// <summary>
    /// Creates an analysis service using the same dependency order as workspace fixtures.
    /// </summary>
    private static DeckAnalysisService CreateAnalysisService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        return new DeckAnalysisService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders);
    }

    /// <summary>
    /// Creates a simulation service using the same dependency order as workspace fixtures.
    /// </summary>
    private static DeckSimulationService CreateSimulationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        return new DeckSimulationService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders);
    }

    /// <summary>
    /// Creates a recommendation service with explicit analysis and simulation collaborators.
    /// </summary>
    private static DeckRecommendationService CreateRecommendationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        DeckAnalysisService analysis = CreateAnalysisService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders);
        DeckSimulationService simulation = CreateSimulationService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders);

        return new DeckRecommendationService(
            repository,
            cardCatalog,
            analysis,
            simulation,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders);
    }

    /// <summary>
    /// Creates a plan service with an explicit workspace mutation collaborator.
    /// </summary>
    private static DeckPlanService CreatePlanService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null)
    {
        DeckWorkspaceService workspaceService = CreateWorkspaceService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride);

        return new DeckPlanService(
            repository,
            cardCatalog,
            workspaceService,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride);
    }

    /// <summary>
    /// Creates a deck card fixture.
    /// </summary>
    private static DeckCard Card(string name, string typeLine, string oracleText)
    {
        return new DeckCard
        {
            Name = name,
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                OracleText = oracleText
            }
        };
    }

    /// <summary>
    /// Reads a file from the repository root.
    /// </summary>
    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }

    /// <summary>
    /// Creates an expensive ramp fixture.
    /// </summary>
    private static DeckCard ExpensiveRamp()
    {
        return new DeckCard
        {
            Name = "Mana Crypt",
            Quantity = 1,
            PrimaryCategory = DeckRoles.Ramp,
            Categories = [DeckRoles.Ramp],
            Snapshot = new CardSnapshot
            {
                TypeLine = "Artifact",
                OracleText = "{T}: Add two colorless mana.",
                ManaValue = 0,
                EdhrecRank = 20,
                Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["usd"] = "180"
                }
            }
        };
    }

    /// <summary>
    /// Verifies that a Quill description still contains rich content.
    /// </summary>
    private static void AssertRichQuillContent(string description)
    {
        using JsonDocument document = JsonDocument.Parse(description);
        JsonElement ops = document.RootElement.GetProperty("ops");

        ops.GetArrayLength().Should().BeGreaterThan(3);
        ops.EnumerateArray().Any(HasBoldAttribute).Should().BeTrue();
        ops.EnumerateArray().Any(HasItalicAttribute).Should().BeTrue();
        ops.EnumerateArray().Any(HasImageInsert).Should().BeTrue();
    }

    /// <summary>
    /// Checks whether an op has a bold attribute.
    /// </summary>
    private static bool HasBoldAttribute(JsonElement op)
    {
        return op.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("bold", out JsonElement bold)
            && bold.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Checks whether an op has an italic attribute.
    /// </summary>
    private static bool HasItalicAttribute(JsonElement op)
    {
        return op.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("italic", out JsonElement italic)
            && italic.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Checks whether an op has an image insert.
    /// </summary>
    private static bool HasImageInsert(JsonElement op)
    {
        return op.TryGetProperty("insert", out JsonElement insert)
            && insert.ValueKind == JsonValueKind.Object
            && insert.TryGetProperty("image", out JsonElement image)
            && image.GetString() == "https://example.test/card.jpg";
    }

    /// <summary>
    /// Provides fake card catalog behavior.
    /// </summary>
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
    }

    /// <summary>
    /// Provides card data for deck intelligence tests.
    /// </summary>
    private sealed class FakeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Gets search queries sent to the fake catalog.
        /// </summary>
        public List<string> SearchQueries { get; } = [];

        /// <summary>
        /// Gets or sets whether Game Changer search throws.
        /// </summary>
        public bool ThrowOnGameChangerSearch { get; init; }

        /// <summary>
        /// Gets or sets whether single-card lookup throws.
        /// </summary>
        public bool ThrowOnGetCard { get; init; }

        /// <summary>
        /// Searches fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            SearchQueries.Add(query);
            return Task.FromResult(SearchFakeCards(query));
        }

        /// <summary>
        /// Searches fake cards from a semantic request.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            SearchQueries.Add(DescribeSearchRequest(request));
            return Task.FromResult(SearchFakeCards(BuildFakeQuery(request)));
        }

        /// <summary>
        /// Returns fake cards for a query-like test fixture string.
        /// </summary>
        private IReadOnlyList<CardSearchResult> SearchFakeCards(string query)
        {
            IReadOnlyList<CardSearchResult> results;
            if (query.Contains("is:game-changer", StringComparison.OrdinalIgnoreCase))
            {
                if (ThrowOnGameChangerSearch)
                {
                    throw new HttpRequestException("Scryfall unavailable.");
                }

                results = [new CardSearchResult { Name = "Mana Crypt" }];
            }
            else if (query.Contains("t:land", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Temple of Silence" },
                    new CardSearchResult { Name = "Command Tower" }
                ];
            }
            else if (query.Contains("discard", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Geth's Grimoire" },
                    new CardSearchResult { Name = "Waste Not" },
                    new CardSearchResult { Name = "Syphon Mind" },
                    new CardSearchResult { Name = "Torment of Hailfire" },
                    new CardSearchResult { Name = "Zulaport Cutthroat" },
                    new CardSearchResult { Name = "Mirkwood Bats" }
                ];
            }
            else if (query.Contains("hexproof", StringComparison.OrdinalIgnoreCase)
                || query.Contains("shroud", StringComparison.OrdinalIgnoreCase)
                || query.Contains("phase out", StringComparison.OrdinalIgnoreCase)
                || query.Contains("indestructible", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Lightning Greaves" }];
            }
            else if (query.Contains("add", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Arcane Signet" }];
            }
            else if (query.Contains("scry", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Lightning Greaves" },
                    new CardSearchResult { Name = "Opt" }
                ];
            }
            else if (query.Contains("draw", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Rhystic Study" },
                    new CardSearchResult { Name = "Necropotence" },
                    new CardSearchResult { Name = "Phyrexian Arena" }
                ];
            }
            else if (query.Contains("destroy target", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Lightning Greaves" },
                    new CardSearchResult { Name = "Hero's Downfall" }
                ];
            }
            else if (query.Contains("each opponent", StringComparison.OrdinalIgnoreCase)
                || query.Contains("each player", StringComparison.OrdinalIgnoreCase)
                || query.Contains("each creature", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Syphon Mind" },
                    new CardSearchResult { Name = "Blasphemous Act" }
                ];
            }
            else if (query.Contains("goad", StringComparison.OrdinalIgnoreCase)
                || query.Contains("monarch", StringComparison.OrdinalIgnoreCase)
                || query.Contains("vote", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Court of Ambition" }];
            }
            else if (query.Contains("destroy all tokens", StringComparison.OrdinalIgnoreCase)
                || query.Contains("creatures can't attack", StringComparison.OrdinalIgnoreCase))
            {
                results =
                [
                    new CardSearchResult { Name = "Illness in the Ranks" },
                    new CardSearchResult { Name = "Crawlspace" }
                ];
            }
            else if (query.Contains("date>=", StringComparison.OrdinalIgnoreCase)
                || query.Contains("set:", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Season of Loss" }];
            }
            else if (query.Contains("legal:commander", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Lightning Greaves" }];
            }
            else
            {
                results = [];
            }

            return results;
        }

        /// <summary>
        /// Converts semantic test requests into fixture selectors.
        /// </summary>
        private static string BuildFakeQuery(CardSearchRequest request)
        {
            return request.Preset switch
            {
                CardSearchPreset.RawQuery => request.RawQuery ?? "",
                CardSearchPreset.CommanderGameChangers => "is:game-changer",
                CardSearchPreset.Role => RoleFixtureQuery(request.Role),
                CardSearchPreset.CommanderProtectionEquipment => "hexproof shroud",
                CardSearchPreset.CommanderProtectionSpell => "indestructible phase out",
                CardSearchPreset.DrawDiscard => "discard",
                CardSearchPreset.CardDraw => "draw",
                CardSearchPreset.DiscardSynergy => "discard",
                CardSearchPreset.PoliticalChoices => "goad monarch vote",
                CardSearchPreset.PoliticalTableEffects => "each opponent",
                CardSearchPreset.WholeTablePolitics => "goad monarch vote each opponent",
                CardSearchPreset.WholeTableEffects => "each player each creature",
                CardSearchPreset.TableWideInteraction => "each opponent each player each creature",
                CardSearchPreset.TokenDefenseSweepers => "destroy all tokens",
                CardSearchPreset.TokenDefensePillowfort => "creatures can't attack",
                CardSearchPreset.GraveyardHate => "graveyard",
                CardSearchPreset.Finishers => "each opponent loses",
                CardSearchPreset.LessSaltyValue => "draw",
                CardSearchPreset.BroadUseful => "legal:commander",
                CardSearchPreset.BroadUsefulFallback => "draw destroy target add",
                CardSearchPreset.RecentCards => $"date>={request.Since:yyyy-MM-dd} set:{request.SetCode}",
                _ => ""
            };
        }

        /// <summary>
        /// Converts a role request into a fixture selector.
        /// </summary>
        private static string RoleFixtureQuery(string? role)
        {
            return (role ?? "").ToLowerInvariant() switch
            {
                "lands" => "t:land",
                "ramp" => "add",
                "draw" => "draw",
                "interaction" => "destroy target",
                "board wipes" => "each creature",
                "protection" => "hexproof",
                "card selection" => "scry",
                _ => "legal:commander"
            };
        }

        /// <summary>
        /// Describes a semantic request without adapter query syntax.
        /// </summary>
        private static string DescribeSearchRequest(CardSearchRequest request)
        {
            return request.Preset switch
            {
                CardSearchPreset.RawQuery => request.RawQuery ?? "",
                CardSearchPreset.Role => $"Role:{request.Role}",
                CardSearchPreset.RecentCards => $"RecentCards:{request.Since:yyyy-MM-dd}",
                _ => request.Preset.ToString()
            };
        }

        /// <summary>
        /// Gets a fake card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            if (ThrowOnGetCard)
            {
                throw new HttpRequestException("Scryfall unavailable.");
            }

            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets fake cards by names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            Dictionary<string, CardInfo> cards = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                cards[name] = CreateCard(name);
            }

            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(cards);
        }

        /// <summary>
        /// Gets fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Gets fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Suggests fake cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a fake card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            return name switch
            {
                "Arcane Signet" => new CardInfo
                {
                    Id = "arcane-signet",
                    OracleId = "oracle-arcane-signet",
                    Name = "Arcane Signet",
                    ManaCost = "{2}",
                    ManaValue = 2,
                    TypeLine = "Artifact",
                    OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "U", "B", "R", "G"],
                    EdhrecRank = 5,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Phyrexian Arena" => new CardInfo
                {
                    Id = "phyrexian-arena",
                    OracleId = "oracle-phyrexian-arena",
                    Name = "Phyrexian Arena",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "At the beginning of your upkeep, you draw a card and you lose 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 250,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                        ["modern"] = "legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                },
                "Rhystic Study" => new CardInfo
                {
                    Id = "rhystic-study",
                    OracleId = "oracle-rhystic-study",
                    Name = "Rhystic Study",
                    ManaCost = "{2}{U}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.",
                    ColorIdentity = ["U"],
                    EdhrecRank = 20,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                        ["modern"] = "not_legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "40.00" }
                },
                "Necropotence" => new CardInfo
                {
                    Id = "necropotence",
                    OracleId = "oracle-necropotence",
                    Name = "Necropotence",
                    ManaCost = "{B}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "Skip your draw step. Pay 1 life: Exile the top card of your library face down.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 30,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "not_legal",
                        ["modern"] = "not_legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "25.00" }
                },
                "Lightning Greaves" => new CardInfo
                {
                    Id = "lightning-greaves",
                    OracleId = "oracle-lightning-greaves",
                    Name = "Lightning Greaves",
                    ManaCost = "{2}",
                    ManaValue = 2,
                    TypeLine = "Artifact — Equipment",
                    OracleText = "Equipped creature has haste and shroud. Equip {0}.",
                    ColorIdentity = [],
                    EdhrecRank = 40,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "6.00" }
                },
                "Hero's Downfall" => new CardInfo
                {
                    Id = "heros-downfall",
                    OracleId = "oracle-heros-downfall",
                    Name = "Hero's Downfall",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Instant",
                    OracleText = "Destroy target creature or planeswalker.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 3_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                },
                "Command Tower" => new CardInfo
                {
                    Id = "command-tower",
                    OracleId = "oracle-command-tower",
                    Name = "Command Tower",
                    TypeLine = "Land",
                    OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "U", "B", "R", "G"],
                    EdhrecRank = 10,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.50" }
                },
                "Temple of Silence" => new CardInfo
                {
                    Id = "temple-of-silence",
                    OracleId = "oracle-temple-of-silence",
                    Name = "Temple of Silence",
                    TypeLine = "Land",
                    OracleText = "Temple of Silence enters the battlefield tapped. When it enters, scry 1. {T}: Add {W} or {B}.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "B"],
                    EdhrecRank = 1,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.20" }
                },
                "Opt" => new CardInfo
                {
                    Id = "opt",
                    OracleId = "oracle-opt",
                    Name = "Opt",
                    ManaCost = "{U}",
                    ManaValue = 1,
                    TypeLine = "Instant",
                    OracleText = "Scry 1. Draw a card.",
                    ColorIdentity = ["U"],
                    EdhrecRank = 100,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.10" }
                },
                "Syphon Mind" => new CardInfo
                {
                    Id = "syphon-mind",
                    OracleId = "oracle-syphon-mind",
                    Name = "Syphon Mind",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Sorcery",
                    OracleText = "Each other player discards a card. You draw a card for each card discarded this way.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 2_500,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                },
                "Waste Not" => new CardInfo
                {
                    Id = "waste-not",
                    OracleId = "oracle-waste-not",
                    Name = "Waste Not",
                    ManaCost = "{1}{B}",
                    ManaValue = 2,
                    TypeLine = "Enchantment",
                    OracleText = "Whenever an opponent discards a creature card, create a 2/2 black Zombie creature token. Whenever an opponent discards a land card, add {B}{B}. Whenever an opponent discards a noncreature, nonland card, draw a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 1_400,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "2.00" }
                },
                "Geth's Grimoire" => new CardInfo
                {
                    Id = "geths-grimoire",
                    OracleId = "oracle-geths-grimoire",
                    Name = "Geth's Grimoire",
                    ManaCost = "{4}",
                    ManaValue = 4,
                    TypeLine = "Artifact",
                    OracleText = "Whenever an opponent discards a card, you may draw a card.",
                    ColorIdentity = [],
                    EdhrecRank = 1_800,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.00" }
                },
                "Torment of Hailfire" => new CardInfo
                {
                    Id = "torment-of-hailfire",
                    OracleId = "oracle-torment-of-hailfire",
                    Name = "Torment of Hailfire",
                    ManaCost = "{X}{B}{B}",
                    ManaValue = 2,
                    TypeLine = "Sorcery",
                    OracleText = "Repeat the following process X times. Each opponent loses 3 life unless they sacrifice a nonland permanent or discard a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 400,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "8.00" }
                },
                "Zulaport Cutthroat" => new CardInfo
                {
                    Id = "zulaport-cutthroat",
                    OracleId = "oracle-zulaport-cutthroat",
                    Name = "Zulaport Cutthroat",
                    ManaCost = "{1}{B}",
                    ManaValue = 2,
                    TypeLine = "Creature — Human Rogue Ally",
                    OracleText = "Whenever Zulaport Cutthroat or another creature you control dies, each opponent loses 1 life and you gain 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 800,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Mirkwood Bats" => new CardInfo
                {
                    Id = "mirkwood-bats",
                    OracleId = "oracle-mirkwood-bats",
                    Name = "Mirkwood Bats",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Creature — Bat",
                    OracleText = "Whenever you create or sacrifice a token, each opponent loses 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 900,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.75" }
                },
                "Blasphemous Act" => new CardInfo
                {
                    Id = "blasphemous-act",
                    OracleId = "oracle-blasphemous-act",
                    Name = "Blasphemous Act",
                    ManaCost = "{8}{R}",
                    ManaValue = 9,
                    TypeLine = "Sorcery",
                    OracleText = "Blasphemous Act deals 13 damage to each creature.",
                    ColorIdentity = ["R"],
                    EdhrecRank = 300,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                },
                "Court of Ambition" => new CardInfo
                {
                    Id = "court-of-ambition",
                    OracleId = "oracle-court-of-ambition",
                    Name = "Court of Ambition",
                    ManaCost = "{2}{B}{B}",
                    ManaValue = 4,
                    TypeLine = "Enchantment",
                    OracleText = "When Court of Ambition enters the battlefield, you become the monarch. At the beginning of your upkeep, each opponent loses 3 life unless they discard a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 2_200,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.50" }
                },
                "Illness in the Ranks" => new CardInfo
                {
                    Id = "illness-in-the-ranks",
                    OracleId = "oracle-illness-in-the-ranks",
                    Name = "Illness in the Ranks",
                    ManaCost = "{B}",
                    ManaValue = 1,
                    TypeLine = "Enchantment",
                    OracleText = "Creature tokens get -1/-1.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 8_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Crawlspace" => new CardInfo
                {
                    Id = "crawlspace",
                    OracleId = "oracle-crawlspace",
                    Name = "Crawlspace",
                    ManaCost = "{3}",
                    ManaValue = 3,
                    TypeLine = "Artifact",
                    OracleText = "No more than two creatures can attack you each combat.",
                    ColorIdentity = [],
                    EdhrecRank = 2_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.00" }
                },
                "Season of Loss" => new CardInfo
                {
                    Id = "season-of-loss",
                    OracleId = "oracle-season-of-loss",
                    Name = "Season of Loss",
                    ManaCost = "{3}{B}{B}",
                    ManaValue = 5,
                    TypeLine = "Sorcery",
                    OracleText = "Choose modes. Each opponent sacrifices a creature. Create two tapped creature tokens. You draw two cards.",
                    Set = "tst",
                    ReleasedAt = new DateOnly(2026, 2, 1),
                    ColorIdentity = ["B"],
                    EdhrecRank = 1_500,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "2.00" }
                },
                _ => new CardInfo
                {
                    Id = name.ToLowerInvariant().Replace(' ', '-'),
                    OracleId = $"oracle-{name}",
                    Name = name,
                    ManaCost = "{1}",
                    ManaValue = name.Equals("Sol Ring", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    TypeLine = "Artifact",
                    OracleText = "{T}: Add {C}{C}.",
                    ColorIdentity = [],
                    ProducedMana = ["C"],
                    EdhrecRank = 1,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.25" }
                }
            };
        }
    }

    /// <summary>
    /// Provides fake Archidekt gateway behavior.
    /// </summary>
    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        /// <summary>
        /// Gets or sets the imported deck.
        /// </summary>
        public DeckWorkspace ImportedDeck { get; set; } = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        };

        /// <summary>
        /// Gets fake imported decks keyed by the caller-supplied Archidekt input.
        /// </summary>
        public Dictionary<string, DeckWorkspace> ImportedDecksByInput { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets Archidekt import requests in caller order.
        /// </summary>
        public List<(string DeckIdOrUrl, bool WriteBack)> ImportRequests { get; } = [];

        /// <summary>
        /// Gets created checkpoints.
        /// </summary>
        public List<string> CreatedCheckpoints { get; } = [];

        /// <summary>
        /// Gets persisted metadata count.
        /// </summary>
        public int PersistedMetadataRequests { get; private set; }

        /// <summary>
        /// Gets persisted card mutation request count.
        /// </summary>
        public int PersistedCardRequests { get; private set; }

        /// <summary>
        /// Gets or sets the fake card persistence exception.
        /// </summary>
        public Exception? PersistCardsException { get; set; }

        /// <summary>
        /// Gets fake auth status.
        /// </summary>
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasJwt = true });
        }

        /// <summary>
        /// Lists fake decks.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>([]);
        }

        /// <summary>
        /// Imports a fake deck.
        /// </summary>
        public Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken)
        {
            ImportRequests.Add((deckIdOrUrl, writeBack));
            if (ImportedDecksByInput.TryGetValue(deckIdOrUrl, out DeckWorkspace? importedDeck))
            {
                DeckWorkspace cloned = CloneWorkspace(importedDeck);
                cloned.Mode = WorkspaceMode.Archidekt;
                cloned.WriteBack = writeBack;
                return Task.FromResult(cloned);
            }

            ImportedDeck.Mode = WorkspaceMode.Archidekt;
            ImportedDeck.WriteBack = writeBack;
            ImportedDeck.ArchidektDeckId = "123";
            return Task.FromResult(ImportedDeck);
        }

        /// <summary>
        /// Copies a workspace so tests can mutate returned imports without changing fixtures.
        /// </summary>
        private static DeckWorkspace CloneWorkspace(DeckWorkspace workspace)
        {
            string json = JsonSerializer.Serialize(workspace);
            return JsonSerializer.Deserialize<DeckWorkspace>(json) ?? new DeckWorkspace();
        }

        /// <summary>
        /// Persists fake card changes.
        /// </summary>
        public Task PersistCardsAsync(
            DeckWorkspace workspace,
            IReadOnlyList<DeckCard> upsertedCards,
            IReadOnlyList<DeckCard> removedCards,
            CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            PersistedCardRequests++;
            if (PersistCardsException is not null)
            {
                throw PersistCardsException;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Persists a fake category.
        /// </summary>
        public Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes a fake category.
        /// </summary>
        public Task DeleteCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Persists fake metadata.
        /// </summary>
        public Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            PersistedMetadataRequests++;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> CreateCheckpointAsync(
            DeckWorkspace workspace,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            CreatedCheckpoints.Add(name);
            return Task.FromResult(new DeckCheckpoint
            {
                Id = "checkpoint-1",
                DeckId = workspace.ArchidektDeckId ?? "",
                Name = name,
                Description = description
            });
        }

        /// <summary>
        /// Lists fake checkpoints.
        /// </summary>
        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([]);
        }

        /// <summary>
        /// Gets a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> GetCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = "Checkpoint" });
        }

        /// <summary>
        /// Renames a fake checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> RenameCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = name, Description = description });
        }

        /// <summary>
        /// Deletes a fake checkpoint.
        /// </summary>
        public Task DeleteCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Provides in-memory workspace repository behavior.
    /// </summary>
    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Gets workspaces.
        /// </summary>
        public Dictionary<string, DeckWorkspace> Workspaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a workspace.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            Workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets a workspace.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            Workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Lists workspaces.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(Workspaces.Values.ToList());
        }
    }

    /// <summary>
    /// Provides in-memory plan repository behavior.
    /// </summary>
    private sealed class InMemoryPlanRepository : IDeckPlanRepository
    {
        /// <summary>
        /// Stores plans.
        /// </summary>
        private readonly Dictionary<string, DeckEditPlan> plans = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a plan.
        /// </summary>
        public Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
        {
            plans[plan.PlanId] = plan;
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Gets a plan.
        /// </summary>
        public Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
        {
            plans.TryGetValue(planId, out DeckEditPlan? plan);
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Lists plans.
        /// </summary>
        public Task<IReadOnlyList<DeckEditPlan>> ListAsync(string? workspaceId, CancellationToken cancellationToken)
        {
            IReadOnlyList<DeckEditPlan> result = plans.Values
                .Where(plan => string.IsNullOrWhiteSpace(workspaceId)
                    || plan.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(result);
        }

        /// <summary>
        /// Deletes a plan.
        /// </summary>
        public Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken)
        {
            return Task.FromResult(plans.Remove(planId));
        }
    }
}
