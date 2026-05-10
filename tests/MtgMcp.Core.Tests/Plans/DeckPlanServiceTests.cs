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

        result.CheckpointId.Should().Be("checkpoint-1");
        archidekt.CreatedCheckpoints.Should().ContainSingle().Which.Should().Be("Before remote edits");
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
}
