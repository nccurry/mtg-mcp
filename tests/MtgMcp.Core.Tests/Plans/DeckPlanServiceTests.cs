using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck edit plan preview, apply, and repository tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies that preview deck plan applies operations only to a clone.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_DoesNotMutateWorkspace()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Swap",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.RemoveCard,
                    CardName = "Mana Crypt",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Before.Cost.IncludedTotal.Should().Be(180);
        preview.After.Cost.IncludedTotal.Should().Be(1);
        workspaces.Workspaces[workspace.Id].Cards.Should().ContainSingle().Which.Name.Should().Be("Mana Crypt");
    }

    /// <summary>
    /// Verifies that preview deck plan degrades gracefully when Game Changer data is unavailable.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_WarnsWhenGameChangersUnavailable()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "No-op preview"
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(
            workspaces,
            new FakeCardCatalog { ThrowOnGameChangerSearch = true },
            archidektGateway: null,
            plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Warnings.Should().Contain(warning =>
            warning.Contains("Game Changer", StringComparison.OrdinalIgnoreCase));
        preview.Before.Bracket.GameChangers.Should().BeEmpty();
        preview.Before.Bracket.Notes.Should().Contain(note =>
            note.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that preview deck plan warns when added-card metadata resolution is unavailable.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_WarnsWhenAddedCardResolutionUnavailable()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview Missing Metadata"
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Add card",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(
            workspaces,
            new FakeCardCatalog { ThrowOnGetCard = true },
            archidektGateway: null,
            plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Warnings.Should().Contain(warning =>
            warning.Contains("Could not resolve added card", StringComparison.OrdinalIgnoreCase));
        preview.After.Analysis.CategoryCounts[DeckRoles.Ramp].Should().Be(1);
        preview.After.Analysis.Notes.Should().Contain(note =>
            note.Contains("not been normalized", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that caller cancellation during added-card resolution is not converted into a preview warning.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_PropagatesCallerCancellationDuringAddedCardResolution()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Cancelled Preview"
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Add card",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(
            workspaces,
            new FakeCardCatalog { CancelGetCard = true },
            archidektGateway: null,
            plans);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> preview = () => service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            cancellation.Token);

        await preview.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that preview metrics can show an overfilled plan without mutating saved state.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_ShowsIncludedOverfillWithoutMutatingWorkspace()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview Overfill",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Existing Package",
                    Quantity = 100,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                },
            ],
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Add ramp",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Before.Analysis.IncludedCards.Should().Be(100);
        preview.After.Analysis.IncludedCards.Should().Be(101);
        preview.After.Validation.Errors.Should().Contain(error =>
            error.Contains("100", StringComparison.OrdinalIgnoreCase));
        workspaces.Workspaces[workspace.Id].Cards.Should().ContainSingle().Which.Name.Should().Be("Existing Package");
    }

    /// <summary>
    /// Verifies that preview deck plan applies card-category operations on the clone.
    /// </summary>
    [Fact]
    public async Task PreviewDeckPlan_AppliesCardCategoryOperations()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Preview Categories",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Move to maybeboard",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCardCategory,
                    CardName = "Mana Crypt",
                    Category = DeckDefaults.Maybeboard
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.SetPrimaryCardCategory,
                    CardName = "Mana Crypt",
                    Category = DeckDefaults.Maybeboard
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.RemoveCardCategory,
                    CardName = "Mana Crypt",
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckPlanPreviewResult preview = await service.PreviewDeckPlanAsync(
            plan.PlanId,
            resolveAddedCards: true,
            TestContext.Current.CancellationToken);

        preview.Warnings.Should().BeEmpty();
        preview.After.Cost.IncludedTotal.Should().Be(0);
        preview.After.Cost.MaybeboardTotal.Should().Be(180);
        DeckCard original = workspaces.Workspaces[workspace.Id].Cards.Single();
        original.PrimaryCategory.Should().Be(DeckRoles.Ramp);
        original.Categories.Should().NotContain(DeckDefaults.Maybeboard);
    }

    /// <summary>
    /// Verifies that apply deck plan applies local mutations.
    /// </summary>
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
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(DeckEditPlanStatus.Applied);
        result.AppliedOperations.Should().Be(1);
        result.AttemptedOperations.Should().Be(1);
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

    /// <summary>
    /// Verifies that applying a local add-card plan continues when card metadata is unavailable.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_LocalPlan_AddsCardWhenMetadataUnavailable()
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
        DeckPlanService service = CreatePlanService(
            workspaces,
            new FakeCardCatalog { ThrowOnGetCard = true },
            archidektGateway: null,
            plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        DeckCard added = result.Workspace.Cards.Single();
        added.Name.Should().Be("Sol Ring");
        added.ScryfallId.Should().BeNull();
        result.Messages.Should().ContainSingle(message => message.Contains("Added 1 Sol Ring", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that add-first Commander swap plans are judged by their final included card count.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_CommanderSwapPlan_AddsBeforeRemovingWithoutTransientOverfill()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        FakeArchidektGateway archidekt = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Commander Swap",
            Format = "commander",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Cards =
            [
                new DeckCard
                {
                    Name = "Existing Package",
                    Quantity = 100,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard]
                }
            ]
        }, TestContext.Current.CancellationToken);
        archidekt.ImportedDeck = workspace;
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Add before cut",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.RemoveCard,
                    CardName = "Existing Package",
                    Quantity = 1,
                    Category = DeckDefaults.Mainboard
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidekt, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: "Before swap",
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(DeckEditPlanStatus.Applied);
        result.AppliedOperations.Should().Be(2);
        result.AttemptedOperations.Should().Be(2);
        result.CheckpointId.Should().Be("checkpoint-1");
        archidekt.PersistedCardRequests.Should().Be(1);
        result.Workspace.Cards.Single(card => card.Name == "Existing Package").Quantity.Should().Be(99);
        result.Workspace.Cards.Should().ContainSingle(card => card.Name == "Arcane Signet");
        result.Workspace.Cards.Sum(card => Math.Max(0, card.Quantity)).Should().Be(100);
    }

    /// <summary>
    /// Verifies that final Commander overfills still fail without mutating saved workspace state.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_CommanderOverfillPlan_FailsWithoutMutatingWorkspace()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Commander Overfill",
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Existing Package",
                    Quantity = 100,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard]
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Overfill",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.AddCard,
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(DeckEditPlanStatus.Failed);
        result.AttemptedOperations.Should().Be(1);
        result.FailedOperationIndex.Should().Be(0);
        result.Error.Should().Contain("101 included cards");
        DeckWorkspace saved = workspaces.Workspaces[workspace.Id];
        saved.Cards.Should().ContainSingle().Which.Name.Should().Be("Existing Package");
        saved.Cards.Single().Quantity.Should().Be(100);
    }

    /// <summary>
    /// Verifies that apply deck plan returns structured failure details instead of surfacing a generic MCP error.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_LocalPlan_ReturnsFailedOperationDetails()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace { Name = "Apply Failure" }, TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Bad remove",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.RemoveCard,
                    CardName = "Missing Card",
                    Quantity = 1,
                    Category = DeckRoles.Ramp
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(DeckEditPlanStatus.Failed);
        result.AppliedOperations.Should().Be(0);
        result.AttemptedOperations.Should().Be(1);
        result.FailedOperationIndex.Should().Be(0);
        result.FailedOperation.Should().NotBeNull();
        result.Error.Should().Contain("Missing Card");
        (await plans.GetAsync(plan.PlanId, TestContext.Current.CancellationToken))!
            .Status
            .Should()
            .Be(DeckEditPlanStatus.Failed);
    }

    /// <summary>
    /// Verifies that explicit plan creation preserves caller-supplied adds and cuts.
    /// </summary>
    [Fact]
    public async Task CreateDeckPlanFromExplicitChanges_PersistsExactOperations()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace { Name = "Explicit" }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlan plan = await service.CreateDeckPlanFromExplicitChangesAsync(
            workspace.Id,
            "Agent-selected edits",
            "The caller decided these exact changes.",
            addCards:
            [
                new ExplicitDeckPlanCardChange
                {
                    CardName = "Arcane Signet",
                    Quantity = 1,
                    Category = DeckRoles.Ramp,
                    Rationale = "Chosen by the caller."
                }
            ],
            removeCards:
            [
                new ExplicitDeckPlanCardChange
                {
                    CardName = "Mind Stone",
                    Quantity = 1,
                    Category = DeckRoles.Ramp,
                    Rationale = "Caller-supplied cut."
                }
            ],
            moveCards:
            [
                new ExplicitDeckPlanMoveCardChange
                {
                    CardName = "Finale of Devastation",
                    FromCategory = DeckDefaults.Mainboard,
                    ToCategory = DeckDefaults.Sideboard,
                    Rationale = "Caller wants this outside the active package."
                }
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        plan.Kind.Should().Be("explicit-changes");
        plan.Rationale.Should().Be("The caller decided these exact changes.");
        plan.Confidence.Should().Be(1);
        plan.Operations.Should().HaveCount(3);
        plan.Operations[0].Should().Match<DeckEditOperation>(operation =>
            operation.Operation == DeckEditOperations.AddCard
            && operation.CardName == "Arcane Signet"
            && operation.Category == DeckRoles.Ramp
            && operation.Rationale == "Chosen by the caller.");
        plan.Operations[1].Should().Match<DeckEditOperation>(operation =>
            operation.Operation == DeckEditOperations.RemoveCard
            && operation.CardName == "Mind Stone"
            && operation.Category == DeckRoles.Ramp
            && operation.Rationale == "Caller-supplied cut.");
        plan.Operations[2].Should().Match<DeckEditOperation>(operation =>
            operation.Operation == DeckEditOperations.MoveCard
            && operation.CardName == "Finale of Devastation"
            && operation.FromCategory == DeckDefaults.Mainboard
            && operation.ToCategory == DeckDefaults.Sideboard
            && operation.Rationale == "Caller wants this outside the active package.");
        (await plans.GetAsync(plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that explicit plan creation does not persist empty plans.
    /// </summary>
    [Fact]
    public async Task CreateDeckPlanFromExplicitChanges_RequiresAtLeastOneOperation()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace { Name = "Empty" }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        Func<Task> act = () => service.CreateDeckPlanFromExplicitChangesAsync(
            workspace.Id,
            name: null,
            rationale: null,
            addCards: [],
            removeCards: [],
            moveCards: [],
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*At least one explicit card add, remove, or move is required*");
        (await plans.ListAsync(workspace.Id, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that package preview uses a transient plan and returns deltas without saving planning state.
    /// </summary>
    [Fact]
    public async Task PreviewCardPackage_ReturnsTransientDeltasWithoutSavingPlan()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateIngaAndEsikaFixtureWorkspace(),
            TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckCardPackagePreviewResult result = await service.PreviewCardPackageAsync(
            workspace.Id,
            "Creature package test",
            "Try a small package without saving it.",
            addCards:
            [
                new ExplicitDeckPlanCardChange
                {
                    CardName = "Arcane Signet",
                    Category = DeckRoles.Ramp,
                    Rationale = "Package add."
                }
            ],
            removeCards:
            [
                new ExplicitDeckPlanCardChange
                {
                    CardName = "Counterspell",
                    Category = DeckRoles.Interaction,
                    Rationale = "Package cut."
                }
            ],
            moveCards:
            [
                new ExplicitDeckPlanMoveCardChange
                {
                    CardName = "Overwhelming Stampede",
                    FromCategory = DeckRoles.Wincons,
                    ToCategory = DeckDefaults.Sideboard,
                    Rationale = "Package sideboard move."
                }
            ],
            resolveAddedCards: false,
            simulationProfile: SimulationProfileIds.Neutral,
            simulations: 100,
            maxTurn: 4,
            seed: 22,
            cancellationToken: TestContext.Current.CancellationToken);

        result.PreviewOnly.Should().BeTrue();
        result.CanApply.Should().BeFalse();
        result.ApplyPlanId.Should().BeNull();
        result.NextAction.Should().Contain("deck_plan_create");
        result.PreviewPlan.Kind.Should().Be("transient-card-package");
        result.PreviewPlan.Operations.Should().HaveCount(3);
        result.Preview.Before.Analysis.IncludedCards.Should().BeGreaterThan(result.Preview.After.Analysis.IncludedCards);
        result.RoleDeltas.Should().Contain(delta => delta.Role == DeckRoles.Interaction && delta.Delta < 0);
        result.ValidationChanges.Should().NotBeNull();
        result.SourceSupport.Should().OnlyContain(row => row.Status == "not-evaluated");
        result.Performance.Deltas.Should().NotBeEmpty();
        (await plans.ListAsync(workspace.Id, TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that plan cloning creates a new draft after compatibility validation.
    /// </summary>
    [Fact]
    public async Task CloneDeckPlanAsync_CreatesDraftForCompatibleWorkspace()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace source = await workspaces.SaveAsync(CreatePlanCloneWorkspace("source", "23097041"), TestContext.Current.CancellationToken);
        DeckWorkspace target = await workspaces.SaveAsync(CreatePlanCloneWorkspace("target", "23097041"), TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = source.Id,
            Name = "Move parked card",
            Kind = "explicit-changes",
            Rationale = "Try the same category move on the writeback import.",
            Confidence = 1,
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.MoveCard,
                    CardName = "Beast Whisperer",
                    FromCategory = DeckRoles.Draw,
                    ToCategory = DeckDefaults.Maybeboard,
                    Rationale = "Caller-approved move."
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlan clone = await service.CloneDeckPlanAsync(
            plan.PlanId,
            target.Id,
            TestContext.Current.CancellationToken);

        clone.PlanId.Should().NotBe(plan.PlanId);
        clone.WorkspaceId.Should().Be(target.Id);
        clone.Status.Should().Be(DeckEditPlanStatus.Draft);
        clone.Persistence.Should().Be(DeckPersistence.LocalOnly);
        clone.Operations.Should().ContainSingle().Which.Should().Match<DeckEditOperation>(operation =>
            operation.Operation == DeckEditOperations.MoveCard
            && operation.CardName == "Beast Whisperer"
            && operation.FromCategory == DeckRoles.Draw
            && operation.ToCategory == DeckDefaults.Maybeboard);
        (await plans.GetAsync(clone.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that plan cloning refuses a workspace imported from a different source deck.
    /// </summary>
    [Fact]
    public async Task CloneDeckPlanAsync_RejectsDifferentSourceReference()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace source = await workspaces.SaveAsync(CreatePlanCloneWorkspace("source", "23097041"), TestContext.Current.CancellationToken);
        DeckWorkspace target = await workspaces.SaveAsync(CreatePlanCloneWorkspace("target", "different"), TestContext.Current.CancellationToken);
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = source.Id,
            Name = "Move parked card",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.MoveCard,
                    CardName = "Beast Whisperer",
                    FromCategory = DeckRoles.Draw,
                    ToCategory = DeckDefaults.Maybeboard
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        Func<Task> act = () => service.CloneDeckPlanAsync(
            plan.PlanId,
            target.Id,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*source references differ*");
    }

    /// <summary>
    /// Verifies that apply deck plan handles local category quantity and metadata operations.
    /// </summary>
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
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidektGateway: null, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: null,
            TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Workspace.Name.Should().Be("Updated");
        result.Workspace.Description.Should().Be("Edited");
        result.Workspace.Cards.Should().ContainSingle(card => card.Name == "Sol Ring" && card.Quantity == 2);
        result.Workspace.Cards.Should().NotContain(card => card.Name == "Lightning Bolt");
        result.Workspace.Categories.Should().NotContain(category => category.Name == "Mana");
    }

    /// <summary>
    /// Verifies that Archidekt writeback plans require and create checkpoints.
    /// </summary>
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
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidekt, plans);

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

        result.Success.Should().BeTrue();
        result.CheckpointId.Should().Be("checkpoint-1");
        archidekt.CreatedCheckpoints.Should().ContainSingle().Which.Should().Be("Before remote edits");
        archidekt.PersistedCardRequests.Should().Be(1);
        result.Workspace.Cards.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that Archidekt writeback timeouts return structured unknown-state details.
    /// </summary>
    [Fact]
    public async Task ApplyDeckPlan_ArchidektWritebackTimeout_ReturnsStructuredUnknownState()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        FakeArchidektGateway archidekt = new()
        {
            PersistCardsException = new TaskCanceledException("Archidekt write timed out.")
        };
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Remote Timeout",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        }, TestContext.Current.CancellationToken);
        archidekt.ImportedDeck = workspace;
        DeckEditPlan plan = await plans.SaveAsync(new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = "Remote timeout edits",
            Operations =
            [
                new DeckEditOperation { Operation = DeckEditOperations.AddCard, CardName = "Sol Ring", Quantity = 1, Category = DeckRoles.Ramp },
                new DeckEditOperation { Operation = DeckEditOperations.AddCard, CardName = "Arcane Signet", Quantity = 1, Category = DeckRoles.Ramp }
            ]
        }, TestContext.Current.CancellationToken);
        DeckPlanService service = CreatePlanService(workspaces, new FakeCardCatalog(), archidekt, plans);

        DeckEditPlanApplyResult result = await service.ApplyDeckPlanAsync(
            plan.PlanId,
            createCheckpoint: true,
            checkpointName: "Before remote timeout",
            TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(DeckEditPlanStatus.ApplyStateUnknown);
        result.AppliedOperations.Should().Be(0);
        result.AttemptedOperations.Should().Be(2);
        result.FailedOperationIndex.Should().BeNull();
        result.Error.Should().Contain("timed out");
        result.Messages.Should().Contain(message => message.Contains("Added 1 Sol Ring", StringComparison.Ordinal));
        result.Messages.Should().Contain(message => message.Contains("Added 1 Arcane Signet", StringComparison.Ordinal));
        result.Workspace.Cards.Should().HaveCount(2);
        archidekt.PersistedCardRequests.Should().Be(1);
        (await plans.GetAsync(plan.PlanId, TestContext.Current.CancellationToken))!
            .Status
            .Should()
            .Be(DeckEditPlanStatus.ApplyStateUnknown);
    }

    /// <summary>
    /// Verifies that json deck plan repository saves lists gets and deletes plans.
    /// </summary>
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

            bool deleted = await repository.DeleteAsync(saved.PlanId, TestContext.Current.CancellationToken);
            bool deletedAgain = await repository.DeleteAsync(saved.PlanId, TestContext.Current.CancellationToken);

            deleted.Should().BeTrue();
            deletedAgain.Should().BeFalse();
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
}
