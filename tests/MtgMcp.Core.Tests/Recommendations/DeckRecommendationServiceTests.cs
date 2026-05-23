using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck recommendation and improvement-plan tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies that goal recommendations create a previewable plan.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_CreatesPlanForTableInteraction()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Goal",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckRecommendationService service = CreateRecommendationService(workspaces, catalog, archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add a few cards that interact with the whole table",
            count: 2,
            maxPrice: 5,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation => operation.Operation == DeckEditOperations.AddCard);
        result.Suggestions.Should().Contain(suggestion => suggestion.Tags.Contains(DeckTags.TableInteraction));
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that goal recommendations understand politics and table-wide interaction requests.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_CreatesPlanForPoliticsInteraction()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Politics Goal",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckRecommendationService service = CreateRecommendationService(workspaces, catalog, archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add politics or monarch cards that interact with the whole table",
            count: 1,
            maxPrice: 5,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion =>
            suggestion.CardName == "Court of Ambition"
            && suggestion.Tags.Contains(DeckTags.Politics)
            && suggestion.Tags.Contains(DeckTags.TableInteraction));
    }

    /// <summary>
    /// Verifies that goal recommendations understand commander protection requests.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_CreatesPlanForCommanderProtection()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Protection Goal",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckRecommendationService service = CreateRecommendationService(workspaces, catalog, archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "ensure I have commander protection",
            count: 1,
            maxPrice: 10,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard
            && operation.CardName == "Lightning Greaves"
            && operation.Category == DeckRoles.Protection);
        result.Suggestions.Should().Contain(suggestion =>
            suggestion.CardName == "Lightning Greaves"
            && suggestion.Role == DeckRoles.Protection);
        catalog.SearchQueries.Should().Contain(CardSearchPreset.CommanderProtectionEquipment.ToString());
    }

    /// <summary>
    /// Verifies that query-first ranking keeps accepted and rejected cards explainable.
    /// </summary>
    [Fact]
    public async Task RankCardsForDeckQuery_FiltersAndExplainsDrawDiscardCandidates()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Tinybones Query",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog());

        DeckQueryRecommendationResult result = await service.RankCardsForDeckQueryAsync(
            workspace.Id,
            "Improve Tinybones draw/discard engine",
            "o:\"whenever an opponent discards\" or o:\"each opponent discards\" or o:\"draw a card\"",
            count: 4,
            maxPrice: 10,
            requiredRoles: [DeckRoles.Draw],
            requiredTags: [DeckTags.Discard],
            excludedRoles: [DeckRoles.Wincons],
            excludedTags: [DeckTags.Aristocrats, DeckTags.Drain],
            cancellationToken: TestContext.Current.CancellationToken);

        result.Candidates.Should().Contain(candidate => candidate.CardName == "Geth's Grimoire");
        result.Candidates.Should().Contain(candidate => candidate.CardName == "Syphon Mind");
        result.Candidates.Should().NotContain(candidate => candidate.CardName == "Zulaport Cutthroat");
        result.Rejected.Should().Contain(rejected =>
            rejected.CardName == "Zulaport Cutthroat"
            && rejected.Reasons.Any(reason => reason.Contains("Excluded tag", StringComparison.OrdinalIgnoreCase)));
        result.Rejected.Should().Contain(rejected =>
            rejected.CardName == "Torment of Hailfire"
            && rejected.Reasons.Any(reason => reason.Contains("Excluded role", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Verifies that query-first recommendations can create persisted non-mutating plans.
    /// </summary>
    [Fact]
    public async Task CreateDeckPlanFromQuery_CreatesPersistedPlanFromRankedCandidates()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Query Plan",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckQueryPlanResult result = await service.CreateDeckPlanFromQueryAsync(
            workspace.Id,
            "Improve Tinybones draw/discard engine",
            "o:\"whenever an opponent discards\" or o:\"each opponent discards\" or o:\"draw a card\"",
            DeckRoles.Draw,
            cutsStrategy: "auto",
            count: 2,
            maxPrice: 10,
            requiredRoles: [DeckRoles.Draw],
            requiredTags: [DeckTags.Discard],
            excludedRoles: [],
            excludedTags: [DeckTags.Aristocrats, DeckTags.Drain],
            cancellationToken: TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard
            && operation.CardName == "Geth's Grimoire"
            && operation.Category == DeckRoles.Draw);
        result.Ranking.Rejected.Should().Contain(rejected => rejected.CardName == "Zulaport Cutthroat");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that automatic cuts use deterministic deck statistics in their rationale.
    /// </summary>
    [Fact]
    public async Task CreateDeckPlanFromQuery_RanksCutsFromDeckStatistics()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Cut Statistics",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 37, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands], Snapshot = new CardSnapshot { TypeLine = "Basic Land" } },
                new DeckCard
                {
                    Name = "Clunky Bauble",
                    Quantity = 61,
                    PrimaryCategory = DeckRoles.Utility,
                    Categories = [DeckRoles.Utility],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "" }
                },
                new DeckCard
                {
                    Name = "Arcane Signet",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add one mana of any color." }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckQueryPlanResult result = await service.CreateDeckPlanFromQueryAsync(
            workspace.Id,
            "Add a draw piece",
            "draw",
            DeckRoles.Draw,
            cutsStrategy: "auto",
            count: 1,
            maxPrice: 10,
            requiredRoles: [DeckRoles.Draw],
            requiredTags: [],
            excludedRoles: [],
            excludedTags: [],
            cancellationToken: TestContext.Current.CancellationToken);

        DeckEditOperation cut = result.Plan.Operations.Should().ContainSingle(operation =>
            operation.Operation == DeckEditOperations.RemoveCard).Which;
        cut.CardName.Should().Be("Clunky Bauble");
        cut.Rationale.Should().Contain("Utility count");
        cut.Rationale.Should().Contain("target maximum");
    }

    /// <summary>
    /// Verifies that natural-language draw/discard goals use the query pipeline without drain leakage.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_DrawDiscardDoesNotRecommendAristocratsDrain()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Draw Discard Goal",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add draw/discard cards for Tinybones",
            count: 3,
            maxPrice: 10,
            strategy: "balanced",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Waste Not");
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Zulaport Cutthroat");
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Torment of Hailfire");
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Zulaport Cutthroat");
    }

    /// <summary>
    /// Verifies that new-card radar uses release filters and deck fit.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_UsesCatalogTrendFallback()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "New Cards",
            Description =
                """
                MTG MCP Deck Intent
                Archetype: tokens
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckRecommendationService service = CreateRecommendationService(workspaces, catalog);

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "tst",
            limit: 3,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        catalog.SearchQueries.Should().Contain(query => query.Contains("RecentCards:2026-01-01", StringComparison.OrdinalIgnoreCase));
        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Season of Loss");
    }

    /// <summary>
    /// Verifies that release radar defaults to a recent window when since is omitted.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_DefaultsToRecentReleaseWindow()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Default Release Radar",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            catalog,
            currentDateOverride: new DateOnly(2026, 5, 10));

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: null,
            setCode: null,
            limit: 3,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        catalog.SearchQueries.Should().Contain(query => query.Contains("RecentCards:2025-05-10", StringComparison.OrdinalIgnoreCase));
        result.Notes.Should().Contain(note => note.Contains("2025-05-10", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that budgeted goal packages do not treat unknown prices as free.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_ExcludesUnpricedCardsWhenBudgeted()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Budget Goal",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new GoalBudgetCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "add a few cards that interact with the whole table",
            count: 2,
            maxPrice: 1,
            strategy: "balanced",
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Syphon Mind");
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Mystery Table Spell");
    }

    /// <summary>
    /// Verifies that lower-salt goal packages cut high-pressure cards rather than only adding value.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_LessSaltyAddsCutsForHighPressureCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Salty Deck",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                },
                new DeckCard
                {
                    Name = "Mana Crypt",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add {C}{C}.", ColorIdentity = [] }
                },
                new DeckCard
                {
                    Name = "Demonic Tutor",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Tutors,
                    Categories = [DeckRoles.Tutors],
                    Snapshot = new CardSnapshot { TypeLine = "Sorcery", OracleText = "Search your library for a card.", ColorIdentity = ["B"] }
                },
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        GoalPackagePlanResult result = await service.FindCardsForDeckGoalAsync(
            workspace.Id,
            "make this deck less salty",
            count: 2,
            maxPrice: 5,
            strategy: "casual",
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Phyrexian Arena");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Mana Crypt");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Demonic Tutor");
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Ayara, First of Locthwain");
    }

    /// <summary>
    /// Verifies that new-card fallback keeps searched print metadata.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_PreservesSearchPrintMetadataAndExcludesUnpricedCards()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Release Radar",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new TrendMetadataCatalog());

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "new",
            limit: 5,
            maxPrice: 1,
            TestContext.Current.CancellationToken);

        NewCardSuggestion suggestion = result.Suggestions.Should().ContainSingle(card => card.CardName == "Reprinted Drain").Subject;
        suggestion.Set.Should().Be("new");
        suggestion.ReleasedAt.Should().Be(new DateOnly(2026, 2, 1));
        result.Suggestions.Should().NotContain(card => card.CardName == "Unpriced New Card");
    }

    /// <summary>
    /// Verifies that card trend providers are optional and failure-tolerant.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_FallsBackWhenTrendProviderFails()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Trend Fallback",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            cardTrendProvider: new ThrowingCardTrendProvider());

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "tst",
            limit: 3,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Season of Loss");
        result.Notes.Should().Contain(note => note.Contains("provider failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that Commander meta providers are optional and failure-tolerant.
    /// </summary>
    [Fact]
    public async Task CompareToCommanderMeta_FallsBackWhenProviderFails()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Meta Fallback",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            commanderMetaProvider: new ThrowingCommanderMetaProvider());

        CommanderMetaReport result = await service.CompareToCommanderMetaAsync(
            workspace.Id,
            limit: 5,
            TestContext.Current.CancellationToken);

        result.MissingPopularCards.Should().NotBeEmpty();
        result.Notes.Should().Contain(note => note.Contains("provider failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that provider new-card suggestions are filtered through deck fit rules.
    /// </summary>
    [Fact]
    public async Task FindNewCardsForDeck_FiltersProviderSuggestionsThroughDeckRules()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Provider Filtering",
            Cards =
            [
                new DeckCard
                {
                    Name = "Ayara, First of Locthwain",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { ColorIdentity = ["B"] }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            cardTrendProvider: new FixedCardTrendProvider(
            [
                new NewCardSuggestion { CardName = "Blasphemous Act", Score = 1, Price = 3 },
                new NewCardSuggestion { CardName = "Season of Loss", Score = 0.8, Price = 2, ReleasedAt = new DateOnly(2026, 2, 1), Set = "tst" }
            ]));

        NewCardsForDeckResult result = await service.FindNewCardsForDeckAsync(
            workspace.Id,
            since: null,
            setCode: null,
            limit: 5,
            maxPrice: 5,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().Contain(suggestion => suggestion.CardName == "Season of Loss");
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Blasphemous Act");
    }

    /// <summary>
    /// Verifies that brainstorming explicitly persists one previewable goal plan.
    /// </summary>
    [Fact]
    public async Task BrainstormDeckImprovements_PersistsOnePreviewableGoalPlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Brainstorm",
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 38, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        BrainstormDeckImprovementsResult result = await service.BrainstormDeckImprovementsAsync(
            workspace.Id,
            "add a few cards that interact with the whole table",
            budget: 5,
            targetPower: "balanced",
            TestContext.Current.CancellationToken);

        IReadOnlyList<DeckEditPlan> savedPlans = await plans.ListAsync(workspace.Id, TestContext.Current.CancellationToken);
        savedPlans.Should().ContainSingle();
        result.Notes.Should().Contain(note => note.Contains(savedPlans.Single().PlanId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that find budget replacements creates persisted plan without mutating deck.
    /// </summary>
    [Fact]
    public async Task FindBudgetReplacements_CreatesPersistedPlanWithoutMutatingDeck()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Budget",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle();
        result.Plan.Operations.Should().HaveCount(2);
        result.Plan.Operations[0].Operation.Should().Be(DeckEditOperations.RemoveCard);
        result.Plan.Operations[1].Operation.Should().Be(DeckEditOperations.AddCard);
        workspaces.Workspaces[workspace.Id].Cards.Single().Name.Should().Be("Mana Crypt");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that budget replacements respect protected cards from intent.
    /// </summary>
    [Fact]
    public async Task FindBudgetReplacements_RespectsIntentProtectedCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Protected Budget",
            Description =
                """
                MTG MCP Deck Intent

                Protect
                - Mana Crypt
                End MTG MCP Deck Intent
                """,
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().BeEmpty();
        result.Plan.Warnings.Should().Contain(warning => warning.Contains("intent", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that find card upgrades creates persisted upgrade plan.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_CreatesPersistedUpgradePlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Upgrades",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        EdhrecRank = 20_000,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: new ReplacementWeights { Role = 2, Power = 1, Price = 1 },
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Phyrexian Arena");
        result.Plan.Operations.Should().Contain(operation => operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Phyrexian Arena");
    }

    /// <summary>
    /// Verifies that card upgrade focus supplies default weights.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_FocusSuppliesDefaultWeights()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Speed Upgrades",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        EdhrecRank = 20_000,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            focus: "speed",
            maxPrice: null,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Plan.Rationale.Should().Contain("role=0.35");
        result.Plan.Rationale.Should().Contain("power=0.5");
        result.Plan.Rationale.Should().Contain("price=0.15");
    }

    /// <summary>
    /// Verifies that find card upgrades rejects off color and wrong format candidates.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_RejectsOffColorAndWrongFormatCandidates()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Modern Mono Black",
            Format = "modern",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["B"] }
                },
                new DeckCard
                {
                    Name = "Weak Draw",
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you may draw a card.",
                        ManaValue = 5,
                        ColorIdentity = ["B"]
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Phyrexian Arena");
        result.Suggestions.Select(suggestion => suggestion.WithCard)
            .Should()
            .NotContain(["Rhystic Study", "Necropotence"]);
    }

    /// <summary>
    /// Verifies that budget recommendations never replace the commander.
    /// </summary>
    [Fact]
    public async Task FindBudgetReplacements_SkipsCommanderCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Commander Budget",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        OracleText = "Whenever an opponent discards a card, that player loses 2 life.",
                        ColorIdentity = ["B"],
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "25.00" }
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().BeEmpty();
        result.Plan.Warnings.Should().ContainSingle().Which.Should().Contain("No replacements");
    }

    /// <summary>
    /// Verifies that candidate cards are scored from their own text instead of inheriting the replaced card's category.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_ScoresCandidateRoleFromCandidateText()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Interaction Upgrade",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Weak Edict",
                    PrimaryCategory = DeckRoles.Interaction,
                    Categories = [DeckRoles.Interaction],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        OracleText = "Each opponent sacrifices a creature.",
                        ManaValue = 5,
                        ColorIdentity = ["B"],
                        EdhrecRank = 20_000
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Hero's Downfall");
    }

    /// <summary>
    /// Verifies that generic utility cards do not produce broad staple upgrade suggestions.
    /// </summary>
    [Fact]
    public async Task FindCardUpgrades_SkipsGenericUtilityCards()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Generic Utility",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Unclear Utility",
                    PrimaryCategory = DeckRoles.Utility,
                    Categories = [DeckRoles.Utility],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        OracleText = "This card has no useful role signal.",
                        ManaValue = 4,
                        ColorIdentity = [],
                        EdhrecRank = 25_000
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: null,
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().BeEmpty();
        result.Plan.Warnings.Should().ContainSingle().Which.Should().Contain("No replacements");
    }

    /// <summary>
    /// Verifies that suggest deck categories persists move plan.
    /// </summary>
    [Fact]
    public async Task SuggestDeckCategories_PersistsMovePlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Categories",
            Cards =
            [
                new DeckCard
                {
                    Name = "Arcane Signet",
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add one mana of any color." }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        CategoryPlanResult result = await service.SuggestDeckCategoriesAsync(workspace.Id, TestContext.Current.CancellationToken);

        result.Suggestions.Single().SuggestedPrimaryRole.Should().Be(DeckRoles.Ramp);
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.MoveCard
            && operation.ToCategory == DeckRoles.Ramp);
    }

    /// <summary>
    /// Verifies that bracket reduction creates a persisted non-mutating plan.
    /// </summary>
    [Fact]
    public async Task FindBracketReductionCandidates_CreatesPersistedPlanWithoutMutatingDeck()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Bracket Plan",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindBracketReductionCandidatesAsync(
            workspace.Id,
            targetBracket: 2,
            limit: 5,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Mana Crypt");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Arcane Signet");
        workspaces.Workspaces[workspace.Id].Cards.Single().Name.Should().Be("Mana Crypt");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that consistency improvements create add-card plans.
    /// </summary>
    [Fact]
    public async Task FindConsistencyImprovements_CreatesAddPlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Consistency",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindConsistencyImprovementsAsync(
            workspace.Id,
            focus: "balanced",
            maxPrice: 10,
            limit: 3,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Arcane Signet");
        workspaces.Workspaces[workspace.Id].Cards.Should().ContainSingle().Which.Name.Should().Be("Swamp");
    }

    /// <summary>
    /// Verifies that card-selection consistency does not fall back to unrelated legal cards.
    /// </summary>
    [Fact]
    public async Task FindConsistencyImprovements_CardSelectionAddsMatchingCard()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Selection",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 36,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land — Swamp" }
                }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckRecommendationService service = CreateRecommendationService(workspaces, catalog, archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindConsistencyImprovementsAsync(
            workspace.Id,
            focus: "selection",
            maxPrice: 10,
            limit: 3,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Opt");
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Lightning Greaves");
        catalog.SearchQueries.Should().Contain(query => query.Contains($"Role:{DeckTags.CardSelection}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that mana base improvements can replace tapped lands.
    /// </summary>
    [Fact]
    public async Task FindManaBaseImprovements_ReplacesTappedLands()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Mana Plan",
            Cards =
            [
                new DeckCard
                {
                    Name = "Temple of Deceit",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        OracleText = "Temple of Deceit enters the battlefield tapped.",
                        ProducedMana = ["U", "B"],
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                    }
                },
                new DeckCard
                {
                    Name = "Barren Moor",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        OracleText = "Barren Moor enters the battlefield tapped.",
                        ProducedMana = ["B"],
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindManaBaseImprovementsAsync(
            workspace.Id,
            maxPrice: 10,
            limit: 3,
            TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Command Tower");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Temple of Deceit");
        result.Plan.Operations.Count(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Command Tower").Should().Be(1);
        result.Plan.Operations.Should().NotContain(operation => operation.CardName == "Temple of Silence");
    }
}
