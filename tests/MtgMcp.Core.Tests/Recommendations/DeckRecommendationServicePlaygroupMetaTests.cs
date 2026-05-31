using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains Playgroup-aware recommendation scoring tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies that local-meta scoring uses Playgroup rankings, Archidekt decklists, and candidate factors.
    /// </summary>
    [Fact]
    public async Task ScoreCardsForPlaygroupMeta_UsesPlaygroupAndArchidektPressureEvidence()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Abdel Test",
            Format = "commander",
            Description =
                """
                MTG MCP Deck Intent
                Version: 2
                Commander: Abdel Adrian, Gorion's Ward
                Goal: Turbo dungeon blink
                Budget: max card price $10; avoid Game Changers
                Simulation Profile: combo
                Archetype Tags: blink, dungeon
                End MTG MCP Deck Intent
                """,
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Abdel Adrian, Gorion's Ward",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        ManaValue = 5,
                        ColorIdentity = ["W"],
                    },
                },
                new DeckCard
                {
                    Name = "Plains",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land — Plains",
                        ProducedMana = ["W"],
                    },
                },
                new DeckCard { Name = "Swords to Plowshares", PrimaryCategory = DeckDefaults.Sideboard, Categories = [DeckDefaults.Sideboard] },
                new DeckCard { Name = "Rest in Peace", PrimaryCategory = DeckDefaults.Sideboard, Categories = [DeckDefaults.Sideboard] },
                new DeckCard { Name = "Cloudshift", PrimaryCategory = DeckDefaults.Sideboard, Categories = [DeckDefaults.Sideboard] },
            ],
        }, TestContext.Current.CancellationToken);
        MetaScoringCatalog catalog = new();
        FakeArchidektGateway archidekt = new();
        archidekt.ImportedDecksByInput["https://archidekt.com/decks/999/raggadragga"] = RaggadraggaDeck();
        PlaygroupService playgroups = new(new MetaPlaygroupGateway());
        DeckAnalysisService analysis = CreateAnalysisService(workspaces, catalog, archidekt);
        DeckSimulationService simulation = CreateSimulationService(workspaces, catalog, archidekt);
        DeckRecommendationService service = new(
            workspaces,
            catalog,
            analysis,
            simulation,
            archidektGateway: archidekt,
            simulationProfiles: SimulationProfileCatalog.CreateDefault(),
            playgroups: playgroups);

        PlaygroupMetaScoringResult result = await service.ScoreCardsForPlaygroupMetaAsync(
            workspace.Id,
            "https://playgroup.gg/playgroups/49295-heaters",
            candidateCards: null,
            maxGames: 20,
            metaDeckLimit: 3,
            simulations: 100,
            maxTurn: 5,
            seed: 7,
            maxPrice: null,
            TestContext.Current.CancellationToken);

        result.MetaDecks.Should().ContainSingle(deck => deck.ImportedDecklist);
        result.MetaPressures.Should().Contain(pressure => pressure.Pressure == "fast-combo");
        result.CandidateSource.Should().Be("excluded-workspace-categories");
        result.CandidateScores.Should().Contain(score => score.CardName == "Swords to Plowshares");
        PlaygroupMetaCandidateScore swords = result.CandidateScores.Single(score => score.CardName == "Swords to Plowshares");
        swords.MetaCoverageScore.Should().BeGreaterThan(0.5);
        swords.PriceBracketScore.Should().BeGreaterThan(0.7);
        swords.ScryfallUri.Should().EndWith(Uri.EscapeDataString("Swords to Plowshares"));
        swords.Evidence.Should().Contain(line => line.Contains("meta pressure", StringComparison.OrdinalIgnoreCase));
        archidekt.ImportRequests.Should().Contain(request =>
            request.DeckIdOrUrl == "https://archidekt.com/decks/999/raggadragga"
            && !request.WriteBack);
    }

    /// <summary>
    /// Verifies that large candidate batches reduce per-card simulation work and report that tradeoff.
    /// </summary>
    [Fact]
    public async Task ScoreCardsForPlaygroupMeta_BudgetsCandidatePerformanceSimulationsForLargeBatches()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Batch Scoring Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "White Commander",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        ManaValue = 3,
                        ColorIdentity = ["W"],
                    },
                },
                new DeckCard
                {
                    Name = "Plains",
                    Quantity = 99,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Plains",
                        ProducedMana = ["W"],
                    },
                },
            ],
        }, TestContext.Current.CancellationToken);
        MetaScoringCatalog catalog = new();
        FakeArchidektGateway archidekt = new();
        archidekt.ImportedDecksByInput["https://archidekt.com/decks/999/raggadragga"] = RaggadraggaDeck();
        PlaygroupService playgroups = new(new MetaPlaygroupGateway());
        DeckAnalysisService analysis = CreateAnalysisService(workspaces, catalog, archidekt);
        DeckSimulationService simulation = CreateSimulationService(workspaces, catalog, archidekt);
        DeckRecommendationService service = new(
            workspaces,
            catalog,
            analysis,
            simulation,
            archidektGateway: archidekt,
            simulationProfiles: SimulationProfileCatalog.CreateDefault(),
            playgroups: playgroups);
        string[] candidates = Enumerable.Range(1, 20)
            .Select(index => $"Candidate {index}")
            .ToArray();

        PlaygroupMetaScoringResult result = await service.ScoreCardsForPlaygroupMetaAsync(
            workspace.Id,
            "https://playgroup.gg/playgroups/49295-heaters",
            candidates,
            maxGames: 20,
            metaDeckLimit: 1,
            simulations: 1_000,
            maxTurn: 1,
            seed: 11,
            maxPrice: null,
            TestContext.Current.CancellationToken);

        result.CandidateScores.Should().HaveCount(20);
        result.Warnings.Should().Contain(warning =>
            warning.Contains("capped at 200 per card", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("requested 1000", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("20 candidates", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that meta-deck imports overlap and repeated Archidekt URLs share the same import task.
    /// </summary>
    [Fact]
    public async Task ScoreCardsForPlaygroupMeta_ParallelizesAndCachesArchidektMetaDeckImports()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Parallel Import Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "White Commander",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        ManaValue = 3,
                        ColorIdentity = ["W"],
                    },
                },
                new DeckCard
                {
                    Name = "Plains",
                    Quantity = 99,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Plains",
                        ProducedMana = ["W"],
                    },
                },
            ],
        }, TestContext.Current.CancellationToken);
        string firstUrl = "https://archidekt.com/decks/900/raggadragga-900";
        string secondUrl = "https://archidekt.com/decks/901/raggadragga-901";
        MetaScoringCatalog catalog = new();
        FakeArchidektGateway archidekt = new()
        {
            ReleaseImportsAfterStartedCount = 2,
        };
        archidekt.ImportedDecksByInput[firstUrl] = RaggadraggaDeck();
        archidekt.ImportedDecksByInput[secondUrl] = RaggadraggaDeck();
        PlaygroupService playgroups = new(new MetaPlaygroupGateway
        {
            DeckCount = 4,
            DistinctDecklistUrls = 2,
        });
        DeckAnalysisService analysis = CreateAnalysisService(workspaces, catalog, archidekt);
        DeckSimulationService simulation = CreateSimulationService(workspaces, catalog, archidekt);
        DeckRecommendationService service = new(
            workspaces,
            catalog,
            analysis,
            simulation,
            archidektGateway: archidekt,
            simulationProfiles: SimulationProfileCatalog.CreateDefault(),
            playgroups: playgroups);

        PlaygroupMetaScoringResult result = await service.ScoreCardsForPlaygroupMetaAsync(
            workspace.Id,
            "https://playgroup.gg/playgroups/49295-heaters",
            ["Swords to Plowshares"],
            maxGames: 20,
            metaDeckLimit: 4,
            simulations: 100,
            maxTurn: 1,
            seed: 13,
            maxPrice: null,
            TestContext.Current.CancellationToken);

        result.MetaDecks.Should().HaveCount(4);
        result.MetaDecks.Should().OnlyContain(deck => deck.ImportedDecklist);
        archidekt.ImportRequests.Select(request => request.DeckIdOrUrl).Should().BeEquivalentTo([firstUrl, secondUrl]);
        archidekt.MaxConcurrentImports.Should().BeGreaterThan(1);
    }

    /// <summary>
    /// Creates an imported fast creature-combo deck fixture.
    /// </summary>
    private static DeckWorkspace RaggadraggaDeck()
    {
        return new DeckWorkspace
        {
            Name = "Raggadragga Dork Combo",
            Format = "commander",
            Categories = [new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true }],
            Cards =
            [
                new DeckCard
                {
                    Name = "Mana Dork Package",
                    Quantity = 14,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature — Elf Druid",
                        OracleText = "{T}: Add {G}. Combo mana dork.",
                        ProducedMana = ["G"],
                    },
                },
                new DeckCard
                {
                    Name = "Creature Payoff Package",
                    Quantity = 16,
                    PrimaryCategory = DeckRoles.Payoffs,
                    Categories = [DeckRoles.Payoffs],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature",
                        OracleText = "Whenever you tap a creature for mana, it gets bigger.",
                    },
                },
            ],
        };
    }

    /// <summary>
    /// Provides candidate card facts for local-meta scoring tests.
    /// </summary>
    private sealed class MetaScoringCatalog : ICardCatalog
    {
        /// <summary>
        /// Returns no search results for local-meta tests.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no preset search results for local-meta tests.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Gets one configured candidate card.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

        /// <summary>
        /// Gets configured candidate cards by name.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(names
                .Select(CreateCard)
                .Where(card => card is not null)
                .Cast<CardInfo>()
                .ToDictionary(card => card.Name, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns no fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Returns no fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Returns no fake suggestions.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Creates a configured candidate card.
        /// </summary>
        private static CardInfo? CreateCard(string name)
        {
            CardInfo? card = name switch
            {
                "Swords to Plowshares" => new CardInfo
                {
                    Name = "Swords to Plowshares",
                    ManaCost = "{W}",
                    ManaValue = 1,
                    TypeLine = "Instant",
                    OracleText = "Exile target creature.",
                    ColorIdentity = ["W"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" },
                },
                "Rest in Peace" => new CardInfo
                {
                    Name = "Rest in Peace",
                    ManaCost = "{1}{W}",
                    ManaValue = 2,
                    TypeLine = "Enchantment",
                    OracleText = "When Rest in Peace enters, exile all graveyards. If a card would be put into a graveyard, exile it instead.",
                    ColorIdentity = ["W"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "2.00" },
                },
                "Cloudshift" => new CardInfo
                {
                    Name = "Cloudshift",
                    ManaCost = "{W}",
                    ManaValue = 1,
                    TypeLine = "Instant",
                    OracleText = "Exile target creature you control, then return that card to the battlefield under your control.",
                    ColorIdentity = ["W"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" },
                },
                string candidate when candidate.StartsWith("Candidate ", StringComparison.OrdinalIgnoreCase) => new CardInfo
                {
                    Name = candidate,
                    ManaCost = "{W}",
                    ManaValue = 1,
                    TypeLine = "Instant",
                    OracleText = "Exile target creature.",
                    ColorIdentity = ["W"],
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" },
                },
                _ => null,
            };

            if (card is not null)
            {
                card.ScryfallUri ??= $"https://scryfall.test/card/{Uri.EscapeDataString(card.Name)}";
            }

            return card;
        }
    }

    /// <summary>
    /// Provides Playgroup data for local-meta scoring tests.
    /// </summary>
    private sealed class MetaPlaygroupGateway : IPlaygroupGateway
    {
        /// <summary>
        /// Gets or sets the number of fake decks observed in the playgroup games.
        /// </summary>
        public int DeckCount { get; set; } = 1;

        /// <summary>
        /// Gets or sets how many distinct Archidekt URLs are spread across fake decks.
        /// </summary>
        public int DistinctDecklistUrls { get; set; } = 1;

        /// <summary>
        /// Gets fake auth status.
        /// </summary>
        public Task<PlaygroupAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupAuthStatus());
        }

        /// <summary>
        /// Gets a fake current user.
        /// </summary>
        public Task<PlaygroupUser> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupUser { Id = 1, Username = "tester" });
        }

        /// <summary>
        /// Gets a fake playgroup summary.
        /// </summary>
        public Task<PlaygroupSummary> GetUserPlaygroupAsync(
            long userId,
            long playgroupId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupSummary { Id = playgroupId, Name = "Heaters" });
        }

        /// <summary>
        /// Lists fake games with one observed deck.
        /// </summary>
        public Task<IReadOnlyList<PlaygroupGame>> ListPlaygroupGamesAsync(
            long playgroupId,
            int page,
            int limit,
            bool includeEvents,
            CancellationToken cancellationToken)
        {
            List<PlaygroupParticipation> participations = Enumerable.Range(0, Math.Max(1, DeckCount))
                .Select(index => new PlaygroupParticipation
                {
                    DeckId = 100 + index,
                    DeckName = DeckName(100 + index),
                    UserId = 10 + index,
                    UserName = $"Player {index + 1}",
                    Winner = index == 0,
                })
                .ToList();
            IReadOnlyList<PlaygroupGame> games =
            [
                new PlaygroupGame
                {
                    Id = 1,
                    PlaygroupId = playgroupId,
                    Participations = participations,
                },
            ];
            return Task.FromResult(games);
        }

        /// <summary>
        /// Gets fake Playgroup deck details.
        /// </summary>
        public Task<PlaygroupDeck> GetDeckAsync(long deckId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupDeck
            {
                Id = deckId,
                Name = DeckName(deckId),
                DecklistUrl = DecklistUrl(deckId),
                PowerLevel = 8.5,
                ConfidenceFactor = 0.9,
                AverageWinsByRound = 5.5,
                Commander = new PlaygroupCommander { Name = "Raggadragga, Goreguts Boss" },
            });
        }

        /// <summary>
        /// Lists no fake user decks.
        /// </summary>
        public Task<IReadOnlyList<PlaygroupDeck>> ListUserDecksAsync(long userId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PlaygroupDeck>>([]);
        }

        /// <summary>
        /// Gets fake Elo history.
        /// </summary>
        public Task<PlaygroupEloHistory> GetDeckEloHistoryAsync(
            long deckId,
            long? playgroupId,
            long? leagueId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupEloHistory { DeckId = deckId, CurrentRating = 1600 });
        }

        /// <summary>
        /// Creates a stable fake deck name.
        /// </summary>
        private static string DeckName(long deckId)
        {
            return deckId == 100
                ? "Raggadragga Dork Combo"
                : $"Raggadragga Dork Combo {deckId}";
        }

        /// <summary>
        /// Creates stable Archidekt URLs, optionally repeating them across fake decks.
        /// </summary>
        private string DecklistUrl(long deckId)
        {
            if (DeckCount <= 1)
            {
                return "https://archidekt.com/decks/999/raggadragga";
            }

            int distinctUrls = Math.Clamp(DistinctDecklistUrls, 1, Math.Max(1, DeckCount));
            long archidektDeckId = 900 + ((deckId - 100) % distinctUrls);
            return $"https://archidekt.com/decks/{archidektDeckId}/raggadragga-{archidektDeckId}";
        }
    }
}
