using FluentAssertions;

namespace MtgMcp.Core.Tests;

public sealed class DeckIntelligenceTests
{
    [Fact]
    public async Task NormalizeDeckCards_PopulatesExtendedSnapshot()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Normalize",
            Cards = [new DeckCard { Name = "Sol Ring" }]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckNormalizationResult result = await service.NormalizeDeckCardsAsync(
            workspace.Id,
            "all",
            TestContext.Current.CancellationToken);

        result.UpdatedCards.Should().Be(1);
        DeckCard card = result.Workspace.Cards.Single();
        card.Snapshot.OracleText.Should().Contain("Add");
        card.Snapshot.EdhrecRank.Should().Be(1);
        card.Snapshot.ProducedMana.Should().Contain("C");
        card.Snapshot.Prices["usd"].Should().Be("1.25");
        card.Snapshot.Legalities["commander"].Should().Be("legal");
    }

    [Fact]
    public async Task NormalizeDeckCards_HandlesLegacyNullSnapshots()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Legacy",
            Cards = [new DeckCard { Name = "Sol Ring", Snapshot = null! }]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckNormalizationResult result = await service.NormalizeDeckCardsAsync(
            workspace.Id,
            "missing",
            TestContext.Current.CancellationToken);

        result.UpdatedCards.Should().Be(1);
        result.Workspace.Cards.Single().Snapshot.Should().NotBeNull();
    }

    [Fact]
    public void RoleClassifier_ClassifiesCommonDeckRolesAndTags()
    {
        DeckRoleClassifier.Classify(Card("Arcane Signet", "Artifact", "{T}: Add one mana of any color."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.Ramp);
        DeckRoleClassifier.Classify(Card("Toxic Deluge", "Sorcery", "All creatures get -X/-X until end of turn."))
            .PrimaryRole
            .Should()
            .Be(DeckRoles.BoardWipes);

        CardRoleAssignment tinybones = DeckRoleClassifier.Classify(Card(
            "Tinybones, Trinket Thief",
            "Legendary Creature",
            "Whenever an opponent discards a card, you draw a card."));
        tinybones.PrimaryRole.Should().Be(DeckRoles.Draw);
        tinybones.Tags.Should().Contain(DeckTags.Discard);
    }

    [Fact]
    public void AnalyzeDrawOdds_UsesHypergeometricOdds()
    {
        DeckWorkspace workspace = new()
        {
            Cards =
            [
                new DeckCard { Name = "Ramp A", Quantity = 2, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Spell A", Quantity = 8, PrimaryCategory = DeckRoles.Utility, Categories = [DeckRoles.Utility] }
            ]
        };

        DeckOddsAnalysis analysis = DeckStatistics.AnalyzeDrawOdds(
            workspace,
            [DeckRoles.Ramp],
            turn: 1,
            openingHandSize: 5,
            simulations: 1_000,
            seed: 42);

        analysis.Rows.Single().HypergeometricAtLeastOne.Should().BeApproximately(0.777777, 0.0005);
        analysis.Rows.Single().MonteCarloAtLeastOne.Should().BeApproximately(0.809, 0.001);
    }

    [Fact]
    public async Task SummarizeDeckPlan_ReturnsRoleCountsAndRisks()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Summary",
            Cards =
            [
                Card("Tinybones, Trinket Thief", "Legendary Creature", "Whenever an opponent discards a card, you draw a card."),
                Card("Arcane Signet", "Artifact", "{T}: Add one mana of any color."),
                Card("Toxic Deluge", "Sorcery", "All creatures get -X/-X until end of turn.")
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckPlanSummary summary = await service.SummarizeDeckPlanAsync(workspace.Id, TestContext.Current.CancellationToken);

        summary.RoleCounts[DeckRoles.Ramp].Should().Be(1);
        summary.RoleCounts[DeckRoles.Draw].Should().Be(1);
        summary.RoleCounts[DeckRoles.BoardWipes].Should().Be(1);
        summary.Risks.Should().Contain(note => note.Contains("Land count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeDrawOddsAsync_UsesDefaultTargets()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Cards =
            [
                new DeckCard { Name = "Swamp", Quantity = 36, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Signet", Quantity = 8, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp] },
                new DeckCard { Name = "Spell", Quantity = 56, PrimaryCategory = DeckRoles.Utility, Categories = [DeckRoles.Utility] }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog());

        DeckOddsAnalysis analysis = await service.AnalyzeDrawOddsAsync(
            workspace.Id,
            targets: null,
            turn: 3,
            openingHandSize: 7,
            simulations: 500,
            seed: 7,
            TestContext.Current.CancellationToken);

        analysis.Rows.Select(row => row.Target).Should().Contain([DeckRoles.Lands, DeckRoles.Ramp, DeckRoles.Draw]);
        analysis.Rows.Single(row => row.Target == DeckRoles.Lands).SuccessesInDeck.Should().Be(36);
    }

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        RecommendationPlanResult result = await service.FindCardUpgradesAsync(
            workspace.Id,
            limit: 3,
            weights: new ReplacementWeights { Role = 2, Power = 1, Price = 1 },
            TestContext.Current.CancellationToken);

        result.Suggestions.Should().ContainSingle().Which.WithCard.Should().Be("Phyrexian Arena");
        result.Plan.Operations.Should().Contain(operation => operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Phyrexian Arena");
    }

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

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
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        CategoryPlanResult result = await service.SuggestDeckCategoriesAsync(workspace.Id, TestContext.Current.CancellationToken);

        result.Suggestions.Single().SuggestedPrimaryRole.Should().Be(DeckRoles.Ramp);
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.MoveCard
            && operation.ToCategory == DeckRoles.Ramp);
    }

    [Fact]
    public async Task ApplyDeckPlan_LocalPlan_AppliesThroughMutationPath()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace { Name = "Apply" }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Add card",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Sol Ring",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.AppliedOperations.Should().Be(1);
        result.Workspace.Cards.Single().Name.Should().Be("Sol Ring");
        result.Persistence.Should().Be(DeckPersistence.LocalOnly);

        Func<Task> reapply = () => service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);
        await reapply.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been applied*");
    }

    [Fact]
    public async Task ApplyDeckPlan_LocalPlan_AppliesCategoryQuantityAndMetadataOperations()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Apply Many",
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", OracleText = "{T}: Add {C}{C}." }
                },
                new DeckCard
                {
                    Name = "Lightning Bolt",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Instant", OracleText = "Deal 3 damage." }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Many edits",
            Operations =
            [
                new DeckEditOperation { Operation = DeckEditOperations.CreateCategory, Category = DeckRoles.Ramp, IncludedInDeck = true, IncludedInPrice = true },
                new DeckEditOperation { Operation = DeckEditOperations.MoveCard, CardName = "Sol Ring", FromCategory = DeckDefaults.Mainboard, ToCategory = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.AddCardCategory, CardName = "Sol Ring", Category = "Testing" },
                new DeckEditOperation { Operation = DeckEditOperations.SetPrimaryCardCategory, CardName = "Sol Ring", Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.RemoveCardCategory, CardName = "Sol Ring", Category = "Testing" },
                new DeckEditOperation { Operation = DeckEditOperations.SetCardQuantity, CardName = "Sol Ring", Quantity = 2, Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.UpdateDeckMetadata, Name = "Updated", Format = "commander", Description = "Edited" },
                new DeckEditOperation { Operation = DeckEditOperations.RenameCategory, FromCategory = DeckRoles.Ramp, ToCategory = "Mana" },
                new DeckEditOperation { Operation = DeckEditOperations.DeleteCategory, Category = "Mana", ToCategory = DeckDefaults.Mainboard },
                new DeckEditOperation { Operation = DeckEditOperations.RemoveCard, CardName = "Lightning Bolt", Quantity = 1, Category = DeckDefaults.Mainboard }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Workspace.Name.Should().Be("Updated");
        result.Workspace.Description.Should().Be("Edited");
        result.Workspace.Cards.Should().ContainSingle(card => card.Name == "Sol Ring" && card.Quantity == 2);
        result.Workspace.Cards.Should().NotContain(card => card.Name == "Lightning Bolt");
        result.Workspace.Categories.Should().NotContain(category => category.Name == "Mana");
    }

    [Fact]
    public async Task ApplyDeckPlan_ArchidektWritebackRequiresAndCreatesCheckpointForMultiEditPlans()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        FakeArchidektGateway archidekt = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Remote",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        }, TestContext.Current.CancellationToken);
        archidekt.ImportedDeck = workspace;
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Remote edits",
            Operations =
            [
                new DeckEditOperation { Operation = DeckEditOperations.AddCard, CardName = "Sol Ring", Quantity = 1, Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.AddCard, CardName = "Arcane Signet", Quantity = 1, Category = DeckRoles.Ramp }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(workspaces, new FakeCardCatalog(), archidekt, plans);

        Func<Task> withoutCheckpoint = () => service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: false,
            checkpointName: null,
            TestContext.Current.CancellationToken);
        await withoutCheckpoint.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires a checkpoint*");

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: "Before remote edits",
            TestContext.Current.CancellationToken);

        result.CheckpointId.Should().Be("checkpoint-1");
        archidekt.CreatedCheckpoints.Should().ContainSingle().Which.Should().Be("Before remote edits");
    }

    [Fact]
    public async Task JsonDeckPlanRepository_SavesListsGetsAndDeletesPlans()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), $"mtg-mcp-plans-{Guid.NewGuid():N}");
        try
        {
            JsonDeckPlanRepository repository = new(dataDirectory);
            DeckEditPlan saved = await repository.SaveAsync(new DeckEditPlan
            {
                WorkspaceId = "workspace-1",
                Name = "Plan"
            }, TestContext.Current.CancellationToken);

            (await repository.GetAsync(saved.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
            IReadOnlyList<DeckEditPlan> listed = await repository.ListAsync("workspace-1", TestContext.Current.CancellationToken);
            listed.Should().ContainSingle(plan => plan.PlanId == saved.PlanId);

            await repository.DeleteAsync(saved.PlanId, TestContext.Current.CancellationToken);
            (await repository.GetAsync(saved.PlanId, TestContext.Current.CancellationToken)).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

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

    private sealed class FakeCardCatalog : ICardCatalog
    {
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<CardSearchResult> results;
            if (query.Contains("add", StringComparison.OrdinalIgnoreCase))
            {
                results = [new CardSearchResult { Name = "Arcane Signet" }];
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
            else
            {
                results = [];
            }

            return Task.FromResult(results);
        }

        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(CreateCard(nameOrId));
        }

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

        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

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

    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        public DeckWorkspace ImportedDeck { get; set; } = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        };

        public List<string> CreatedCheckpoints { get; } = [];

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasJwt = true });
        }

        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>([]);
        }

        public Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken)
        {
            ImportedDeck.Mode = WorkspaceMode.Archidekt;
            ImportedDeck.WriteBack = writeBack;
            ImportedDeck.ArchidektDeckId = "123";
            return Task.FromResult(ImportedDeck);
        }

        public Task PersistCardsAsync(
            DeckWorkspace workspace,
            IReadOnlyList<DeckCard> upsertedCards,
            IReadOnlyList<DeckCard> removedCards,
            CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        public Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        public Task DeleteCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

        public Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            ImportedDeck = workspace;
            return Task.CompletedTask;
        }

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

        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([]);
        }

        public Task<DeckCheckpoint> GetCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = "Checkpoint" });
        }

        public Task<DeckCheckpoint> RenameCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            string name,
            string? description,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = name, Description = description });
        }

        public Task DeleteCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        public Dictionary<string, DeckWorkspace> Workspaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            Workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            Workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(Workspaces.Values.ToList());
        }
    }

    private sealed class InMemoryPlanRepository : IDeckPlanRepository
    {
        private readonly Dictionary<string, DeckEditPlan> plans = new(StringComparer.OrdinalIgnoreCase);

        public Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
        {
            plans[plan.PlanId] = plan;
            return Task.FromResult(plan);
        }

        public Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
        {
            plans.TryGetValue(planId, out DeckEditPlan? plan);
            return Task.FromResult(plan);
        }

        public Task<IReadOnlyList<DeckEditPlan>> ListAsync(string? workspaceId, CancellationToken cancellationToken)
        {
            IReadOnlyList<DeckEditPlan> result = plans.Values
                .Where(plan => string.IsNullOrWhiteSpace(workspaceId)
                    || plan.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(result);
        }

        public Task DeleteAsync(string planId, CancellationToken cancellationToken)
        {
            plans.Remove(planId);
            return Task.CompletedTask;
        }
    }
}
