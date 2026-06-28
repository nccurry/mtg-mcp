using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck recommendation and improvement-plan tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies read-only card evaluation explains weak ramp against explicit alternatives.
    /// </summary>
    [Fact]
    public async Task EvaluateCard_RanksExplicitRampCandidatesWithoutMutatingPlans()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateRampEvaluationWorkspace(),
            TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            archidektGateway: null,
            plans);

        RampContextEvaluation result = await service.EvaluateCardAsync(
            workspace.Id,
            "Wayfarer's Bauble",
            ["Nature's Lore", "Three Visits", "Rampant Growth", "Arcane Signet"],
            candidateLimit: 8,
            TestContext.Current.CancellationToken);

        result.Role.Should().Be(DeckRoles.Ramp);
        result.RampKind.Should().Be("activatedLandRamp");
        result.TopIssues.Should().Contain(issue => issue.Contains("future activation mana", StringComparison.OrdinalIgnoreCase));
        result.TopIssues.Should().Contain(issue => issue.Contains("enters tapped", StringComparison.OrdinalIgnoreCase));
        result.TopIssues.Should().Contain(issue => issue.Contains("commander", StringComparison.OrdinalIgnoreCase));
        result.CandidateEvaluations.Should().OnlyContain(candidate => candidate.Score > result.Score);
        result.CandidateEvaluations.Select(candidate => candidate.CardName)
            .Should()
            .Equal("Nature's Lore", "Three Visits", "Arcane Signet", "Rampant Growth");
        result.CandidateEvaluations.First().TopStrengths.Should().Contain(strength =>
            strength.Contains("commander", StringComparison.OrdinalIgnoreCase));
        (await plans.ListAsync(workspace.Id, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies explicit ramp candidate rankings are stable for the same inputs.
    /// </summary>
    [Fact]
    public async Task EvaluateCard_ReturnsStableCandidateRankingsForSameInputs()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateRampEvaluationWorkspace(),
            TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog());

        RampContextEvaluation first = await service.EvaluateCardAsync(
            workspace.Id,
            "Wayfarer's Bauble",
            ["Nature's Lore", "Three Visits", "Rampant Growth", "Arcane Signet"],
            candidateLimit: 8,
            TestContext.Current.CancellationToken);
        RampContextEvaluation second = await service.EvaluateCardAsync(
            workspace.Id,
            "Wayfarer's Bauble",
            ["Nature's Lore", "Three Visits", "Rampant Growth", "Arcane Signet"],
            candidateLimit: 8,
            TestContext.Current.CancellationToken);

        first.Score.Should().Be(second.Score);
        first.CandidateEvaluations.Select(candidate => (candidate.CardName, candidate.Score))
            .Should()
            .Equal(second.CandidateEvaluations.Select(candidate => (candidate.CardName, candidate.Score)));
    }

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
    /// Verifies that query-first data lookups keep accepted and rejected cards explainable.
    /// </summary>
    [Fact]
    public async Task QueryService_FiltersAndExplainsDrawDiscardCandidates()
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
        DeckQueryService service = CreateQueryService(workspaces, new FakeCardCatalog());

        DeckQueryDataResult result = await service.QueryCardsForDeckAsync(
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

        result.Cards.Should().Contain(candidate => candidate.CardName == "Geth's Grimoire");
        result.Cards.Should().Contain(candidate => candidate.CardName == "Syphon Mind");
        result.Cards.Should().Contain(candidate =>
            candidate.CardName == "Geth's Grimoire"
            && candidate.ScryfallUri!.EndsWith(Uri.EscapeDataString("Geth's Grimoire"), StringComparison.Ordinal));
        result.Cards.Should().NotContain(candidate => candidate.CardName == "Zulaport Cutthroat");
        result.Rejected.Should().Contain(rejected =>
            rejected.CardName == "Zulaport Cutthroat"
            && rejected.Reasons.Any(reason => reason.Contains("Excluded tag", StringComparison.OrdinalIgnoreCase))
            && rejected.ScryfallUri!.EndsWith(Uri.EscapeDataString("Zulaport Cutthroat"), StringComparison.Ordinal));
        result.Rejected.Should().Contain(rejected =>
            rejected.CardName == "Torment of Hailfire"
            && rejected.Reasons.Any(reason => reason.Contains("Excluded role", StringComparison.OrdinalIgnoreCase)));
        result.Cards.Should().Contain(candidate =>
            candidate.CardName == "Geth's Grimoire"
            && candidate.PriceKnown
            && !string.IsNullOrWhiteSpace(candidate.PrintingStatus)
            && candidate.Legality == "legal");
    }

    /// <summary>
    /// Verifies that query role filters use broad interaction and removal matching.
    /// </summary>
    [Fact]
    public async Task QueryCardsForDeck_MatchesRequiredRoleAliases()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace redWorkspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Red Interaction",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Torbran, Thane of Red Fell",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ColorIdentity = ["R"] }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspace blackWorkspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Black Removal",
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
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog());

        DeckQueryDataResult interaction = await service.QueryCardsForDeckAsync(
            redWorkspace.Id,
            "Find interaction",
            "each creature",
            count: 2,
            maxPrice: 10,
            requiredRoles: [DeckRoles.Interaction],
            requiredTags: null,
            excludedRoles: null,
            excludedTags: null,
            cancellationToken: TestContext.Current.CancellationToken);
        DeckQueryDataResult boardWipeRemoval = await service.QueryCardsForDeckAsync(
            redWorkspace.Id,
            "Find removal",
            "each creature",
            count: 2,
            maxPrice: 10,
            requiredRoles: ["Removal"],
            requiredTags: null,
            excludedRoles: null,
            excludedTags: null,
            cancellationToken: TestContext.Current.CancellationToken);
        DeckQueryDataResult targetedRemoval = await service.QueryCardsForDeckAsync(
            blackWorkspace.Id,
            "Find removal",
            "destroy target",
            count: 2,
            maxPrice: 10,
            requiredRoles: ["Removal"],
            requiredTags: null,
            excludedRoles: null,
            excludedTags: null,
            cancellationToken: TestContext.Current.CancellationToken);

        interaction.Cards.Should().Contain(card => card.CardName == "Blasphemous Act");
        boardWipeRemoval.Cards.Should().Contain(card => card.CardName == "Blasphemous Act");
        targetedRemoval.Cards.Should().Contain(card => card.CardName == "Hero's Downfall");
    }

    /// <summary>
    /// Verifies that Scryfall query failures return structured errors instead of invocation failures.
    /// </summary>
    [Fact]
    public async Task QueryCardsForDeck_ReturnsStructuredErrorsWhenCatalogRejectsQuery()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Query Error",
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
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new ThrowingSearchCatalog());

        DeckQueryDataResult result = await service.QueryCardsForDeckAsync(
            workspace.Id,
            "bad syntax",
            "not a valid provider query",
            count: 4,
            maxPrice: null,
            requiredRoles: null,
            requiredTags: null,
            excludedRoles: null,
            excludedTags: null,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Cards.Should().BeEmpty();
        result.Errors.Should().ContainSingle(error =>
            error.Contains("Scryfall query", StringComparison.OrdinalIgnoreCase)
            && error.Contains("400", StringComparison.OrdinalIgnoreCase));
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
        result.Suggestions.Should().Contain(suggestion =>
            suggestion.CardName == "Waste Not"
            && suggestion.ScryfallUri!.EndsWith(Uri.EscapeDataString("Waste Not"), StringComparison.Ordinal));
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
        result.Suggestions.Should().Contain(suggestion =>
            suggestion.CardName == "Season of Loss"
            && suggestion.ScryfallUri!.EndsWith(Uri.EscapeDataString("Season of Loss"), StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that new-card swap review returns deterministic cut evidence.
    /// </summary>
    [Fact]
    public async Task ReviewNewCardSwaps_ReturnsDeterministicCutEvidence()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "New Card Swaps",
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
                    Name = "Syphon Mind",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        OracleText = "Each other player discards a card. You draw a card for each card discarded this way.",
                        ManaValue = 4,
                        ColorIdentity = ["B"],
                        ScryfallUri = "https://scryfall.test/card/Syphon%20Mind",
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                    }
                },
                new DeckCard { Name = "Swamp", Quantity = 37, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog());

        NewCardSwapReviewResult result = await service.ReviewNewCardSwapsAsync(
            workspace.Id,
            since: "2026-01-01",
            setCode: "tst",
            maxPrice: 5,
            limit: 3,
            TestContext.Current.CancellationToken);

        NewCardSwapCandidate candidate = result.Candidates.Should().ContainSingle(card => card.CardName == "Season of Loss").Subject;
        candidate.ScryfallUri.Should().EndWith(Uri.EscapeDataString("Season of Loss"));
        candidate.CutCandidates.Should().Contain(cut =>
            cut.CardName == "Syphon Mind"
            && cut.ScryfallUri == "https://scryfall.test/card/Syphon%20Mind"
            && cut.Reasons.Count > 0);
        result.Notes.Should().Contain(note => note.Contains("role overlap", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that goal packages do not invent cuts.
    /// </summary>
    [Fact]
    public async Task FindCardsForDeckGoal_DoesNotGenerateCuts()
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
        result.Plan.Operations.Should().NotContain(operation => operation.Operation == DeckEditOperations.RemoveCard);
        result.Plan.Warnings.Should().Contain(warning => warning.Contains("No cuts were generated", StringComparison.OrdinalIgnoreCase));
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
    public async Task CompareToCommanderMeta_DoesNotInferRowsWhenProviderFails()
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

        result.Source.Should().Be("provider-error");
        result.MissingPopularCards.Should().BeEmpty();
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
                new NewCardSuggestion { CardName = "Season of Loss", Score = 0.8, Price = 2, ReleasedAt = new DateOnly(2026, 2, 1), Set = "tst" },
                new NewCardSuggestion { CardName = "Future Bargain", Score = 0.7, Price = 0.25m, ReleasedAt = new DateOnly(2027, 1, 1), Set = "fut" }
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
        result.Suggestions.Should().NotContain(suggestion => suggestion.CardName == "Future Bargain");
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

        ReplacementSuggestion suggestion = result.Suggestions.Should().ContainSingle().Subject;
        suggestion.WithCardScryfallUri.Should().EndWith(Uri.EscapeDataString("Arcane Signet"));
        suggestion.ReplaceCardScryfallUri.Should().EndWith(Uri.EscapeDataString("Mana Crypt"));
        suggestion.FeatureVector.RoleFit.Should().BeGreaterThan(0.9);
        suggestion.FeatureVector.CommanderCurve.Should().BeGreaterThan(0.5);
        suggestion.FeatureVector.Fixing.Should().BeGreaterThan(0.8);
        suggestion.FeatureVector.Price.Should().BeGreaterThan(0);
        suggestion.FeatureVector.EvidenceQuality.Should().BeGreaterThan(0.6);
        suggestion.Rationale.Should().Contain("feature vector");
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

        ReplacementSuggestion suggestion = result.Suggestions.Should().ContainSingle().Subject;
        suggestion.WithCard.Should().Be("Phyrexian Arena");
        suggestion.WithCardScryfallUri.Should().EndWith(Uri.EscapeDataString("Phyrexian Arena"));
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

    /// <summary>
    /// Verifies that the recommendation facade still exposes batch tuning reports.
    /// </summary>
    [Fact]
    public async Task BuildBatchTuningReport_DelegatesToBatchTuningService()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateIngaAndEsikaFixtureWorkspace(),
            TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog());

        DeckBatchTuningReport report = await service.BuildBatchTuningReportAsync(
            [workspace.Id, "missing-workspace"],
            maxBudget: 5,
            targetTurn: 4,
            simulations: 10,
            seed: 2026,
            TestContext.Current.CancellationToken);

        report.Simulations.Should().Be(100);
        report.Decks.Should().ContainSingle().Which.WorkspaceId.Should().Be(workspace.Id);
        report.Failures.Should().ContainSingle().Which.WorkspaceId.Should().Be("missing-workspace");
    }

    /// <summary>
    /// Verifies that the extracted batch tuning collaborator preserves batch row behavior.
    /// </summary>
    [Fact]
    public async Task BatchTuningService_ReturnsRowsAndPartialFailures()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateIngaAndEsikaFixtureWorkspace(),
            TestContext.Current.CancellationToken);
        DeckBatchTuningService service = CreateBatchTuningService(workspaces, new FakeCardCatalog());

        DeckBatchTuningReport report = await service.BuildBatchTuningReportAsync(
            [workspace.Id, "missing-workspace"],
            maxBudget: 5,
            targetTurn: 4,
            simulations: 10,
            seed: 2026,
            TestContext.Current.CancellationToken);

        report.Simulations.Should().Be(100);
        DeckBatchTuningDeckReport deck = report.Decks.Should().ContainSingle().Subject;
        deck.WorkspaceId.Should().Be(workspace.Id);
        deck.Cost.IncludedTotal.Should().BeGreaterThan(5);
        deck.Goldfish.TargetTurn.Should().Be(4);
        deck.Goldfish.Simulations.Should().Be(100);
        deck.Risks.Should().Contain(risk => risk.Contains("max budget", StringComparison.OrdinalIgnoreCase));
        DeckBatchTuningFailure failure = report.Failures.Should().ContainSingle().Subject;
        failure.WorkspaceId.Should().Be("missing-workspace");
        failure.Reason.Should().Contain("Workspace");
    }

    /// <summary>
    /// Simulates a catalog that rejects search syntax.
    /// </summary>
    private sealed class ThrowingSearchCatalog : ICardCatalog
    {
        /// <summary>
        /// Throws for raw query search.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Scryfall returned HTTP 400 for query syntax.", null, HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// Throws for structured query search.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Scryfall returned HTTP 400 for query syntax.", null, HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// Returns no single-card metadata.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(null);
        }

        /// <summary>
        /// Returns no card metadata.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(
                new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns no rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Returns no prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Returns no suggestions.
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
