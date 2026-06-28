using System.Text.Json;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains tests for deck workspace service.
/// </summary>
public sealed class DeckWorkspaceServiceTests
{
    /// <summary>
    /// Verifies that parse decklist groups cards by headings.
    /// </summary>
    [Fact]
    public void ParseDecklist_GroupsCardsByHeadings()
    {
        const string decklist = """
            Commander
            1 Atraxa, Praetors' Voice

            Maybeboard
            2 Brainstorm

            Sideboard
            1 Swords to Plowshares
            """;

        ParsedDecklist parsed = DeckParser.Parse(decklist);

        parsed.Warnings.Should().BeEmpty();
        parsed.Cards.Should().HaveCount(3);
        parsed.Cards[0].Name.Should().Be("Atraxa, Praetors' Voice");
        parsed.Cards[0].Category.Should().Be(DeckDefaults.Mainboard);
        parsed.Cards[1].Category.Should().Be(DeckDefaults.Maybeboard);
        parsed.Cards[2].Category.Should().Be(DeckDefaults.Sideboard);
    }

    /// <summary>
    /// Verifies that candidate-card headings normalize to considering.
    /// </summary>
    [Fact]
    public void ParseDecklist_NormalizesConsideringHeadings()
    {
        const string decklist = """
            Consider
            1 Lightning Bolt
            """;

        ParsedDecklist parsed = DeckParser.Parse(decklist);

        parsed.Warnings.Should().BeEmpty();
        parsed.Cards.Should().ContainSingle(card =>
            card.Name == "Lightning Bolt"
            && card.Category == DeckDefaults.Considering);
    }

    /// <summary>
    /// Verifies that local mutations move cards and preserve categories.
    /// </summary>
    [Fact]
    public async Task LocalMutations_MoveCardsReordersPrimaryCategory()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );

        await service.AddCardAsync(
            deck.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        await service.AddCardCategoryAsync(
            deck.Id,
            "Lightning Bolt",
            "Testing",
            TestContext.Current.CancellationToken
        );
        DeckChangeResult result = await service.MoveCardAsync(
            deck.Id,
            "Lightning Bolt",
            DeckDefaults.Maybeboard,
            null,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        );

        result.Persistence.Should().Be(DeckPersistence.LocalOnly);
        opened.Cards.Should().ContainSingle();
        opened.Cards[0].PrimaryCategory.Should().Be(DeckDefaults.Maybeboard);
        opened.Cards[0].Categories.Should().Equal(DeckDefaults.Maybeboard, DeckDefaults.Mainboard, "Testing");
        opened.Cards[0].Snapshot.TypeLine.Should().Be("Instant");
        opened.Cards[0].Snapshot.ColorIdentity.Should().BeEquivalentTo(["R"]);
        opened.Cards[0].Snapshot.Set.Should().Be("tst");
        opened.Cards[0].Snapshot.CollectorNumber.Should().Be("1");
        opened.Cards[0].Snapshot.ScryfallUri.Should().Contain("Lightning%20Bolt");
    }

    /// <summary>
    /// Verifies that local checkpoints restore workspace cards without recursive snapshot payloads.
    /// </summary>
    [Fact]
    public async Task WorkspaceCheckpoints_CreateRestoreAndDeleteLocalSnapshots()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken);
        await service.AddCardAsync(
            deck.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken);
        WorkspaceCheckpointSummary summary = await service.CreateWorkspaceCheckpointAsync(
            deck.Id,
            "Before testing",
            "safe point",
            TestContext.Current.CancellationToken);

        await service.AddCardAsync(
            deck.Id,
            "Sol Ring",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken);
        WorkspaceCheckpoint checkpoint = await service.GetWorkspaceCheckpointAsync(
            deck.Id,
            summary.Id,
            TestContext.Current.CancellationToken);
        WorkspaceCheckpointRestoreResult restore = await service.RestoreWorkspaceCheckpointAsync(
            deck.Id,
            summary.Id,
            TestContext.Current.CancellationToken);
        DeckWorkspace restored = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);

        summary.Name.Should().Be("Before testing");
        checkpoint.Snapshot.Cards.Should().ContainSingle(card => card.Name == "Lightning Bolt");
        checkpoint.Snapshot.LocalCheckpoints.Should().BeEmpty();
        checkpoint.Snapshot.ImportHistory.Should().BeEmpty();
        restore.Status.Should().Be("restored");
        restored.Cards.Should().ContainSingle(card => card.Name == "Lightning Bolt");
        restored.Cards.Should().NotContain(card => card.Name == "Sol Ring");
        restored.LocalCheckpoints.Should().ContainSingle(saved => saved.Id == summary.Id);

        await service.DeleteWorkspaceCheckpointAsync(
            deck.Id,
            summary.Id,
            TestContext.Current.CancellationToken);
        IReadOnlyList<WorkspaceCheckpointSummary> afterDelete = await service.ListWorkspaceCheckpointsAsync(
            deck.Id,
            TestContext.Current.CancellationToken);
        afterDelete.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that local checkpoint retention keeps only the newest ten snapshots.
    /// </summary>
    [Fact]
    public async Task WorkspaceCheckpoints_TrimToNewestTen()
    {
        DeckWorkspaceService service = new(new InMemoryRepository(), new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken);

        for (int index = 0; index < 11; index++)
        {
            await service.CreateWorkspaceCheckpointAsync(
                deck.Id,
                $"Checkpoint {index}",
                null,
                TestContext.Current.CancellationToken);
        }

        IReadOnlyList<WorkspaceCheckpointSummary> checkpoints = await service.ListWorkspaceCheckpointsAsync(
            deck.Id,
            TestContext.Current.CancellationToken);

        checkpoints.Should().HaveCount(10);
        checkpoints.Should().NotContain(checkpoint => checkpoint.Name == "Checkpoint 0");
    }

    /// <summary>
    /// Verifies that local checkpoints are allowed for read-only imported provider workspaces.
    /// </summary>
    [Fact]
    public async Task WorkspaceCheckpoints_AllowReadOnlyImportedProviderWorkspaces()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = new()
        {
            Id = "remote-local",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = false,
            ArchidektDeckId = "123"
        };
        await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        WorkspaceCheckpointSummary checkpoint = await service.CreateWorkspaceCheckpointAsync(
            workspace.Id,
            "Read-only import",
            null,
            TestContext.Current.CancellationToken);

        checkpoint.WorkspaceId.Should().Be(workspace.Id);
    }

    /// <summary>
    /// Verifies that local checkpoints reject Archidekt writeback workspaces.
    /// </summary>
    [Fact]
    public async Task WorkspaceCheckpoints_RejectArchidektWritebackRestore()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = new()
        {
            Id = "writeback",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            LocalCheckpoints =
            [
                new WorkspaceCheckpoint
                {
                    Id = "checkpoint",
                    WorkspaceId = "writeback",
                    Name = "Remote",
                    Snapshot = new DeckWorkspace { Id = "writeback" }
                }
            ]
        };
        await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        Func<Task> act = () => service.RestoreWorkspaceCheckpointAsync(
            workspace.Id,
            "checkpoint",
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Archidekt writeback*archidekt_checkpoint_*");
    }

    /// <summary>
    /// Verifies that category mutations preserve ordered Archidekt categories.
    /// </summary>
    [Fact]
    public async Task CategoryMutations_ReorderAppendDeduplicateAndPromoteCategories()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Sol Ring",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );

        await service.AddCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Testing",
            TestContext.Current.CancellationToken
        );
        await service.AddCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Utility",
            TestContext.Current.CancellationToken
        );
        await service.AddCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "testing",
            TestContext.Current.CancellationToken
        );
        await service.SetPrimaryCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Testing",
            TestContext.Current.CancellationToken
        );

        DeckCard reordered = (await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        )).Cards.Single();
        reordered.PrimaryCategory.Should().Be("Testing");
        reordered.Categories.Should().Equal("Testing", DeckDefaults.Mainboard, "Utility");

        await service.RemoveCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Testing",
            TestContext.Current.CancellationToken
        );
        DeckCard promoted = (await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        )).Cards.Single();
        promoted.PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        promoted.Categories.Should().Equal(DeckDefaults.Mainboard, "Utility");

        await service.RemoveCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        DeckCard secondaryPromoted = (await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        )).Cards.Single();
        secondaryPromoted.PrimaryCategory.Should().Be("Utility");
        secondaryPromoted.Categories.Should().Equal("Utility");

        await service.RemoveCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Utility",
            TestContext.Current.CancellationToken
        );
        DeckCard fallback = (await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        )).Cards.Single();
        fallback.PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        fallback.Categories.Should().Equal(DeckDefaults.Mainboard);
    }

    /// <summary>
    /// Verifies that bulk add resolves cards in one batch and preserves secondary categories.
    /// </summary>
    [Fact]
    public async Task AddCardsBulk_ResolvesOnceAndPreservesSecondaryCategories()
    {
        InMemoryRepository repository = new();
        FakeCardCatalog catalog = new();
        DeckWorkspaceService service = new(repository, catalog);
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );

        DeckChangeResult result = await service.AddCardsBulkAsync(
            deck.Id,
            [
                new BulkDeckCardAdd
                {
                    CardName = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    SecondaryCategories = ["Artifact", DeckRoles.Ramp]
                },
                new BulkDeckCardAdd
                {
                    CardName = "Lightning Bolt",
                    Quantity = 2,
                    PrimaryCategory = DeckDefaults.Sideboard,
                    SecondaryCategories = [DeckRoles.Interaction]
                },
            ],
            force: false,
            TestContext.Current.CancellationToken);

        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);

        result.Kind.Should().Be(DeckMutationKind.CardAdded);
        catalog.BatchLookupRequests.Should().Be(1);
        catalog.SingleLookupRequests.Should().Be(0);
        DeckCard solRing = opened.Cards.Should().ContainSingle(card =>
            card.Name == "Sol Ring"
            && card.Quantity == 1).Subject;
        solRing.Categories.Should().Equal(DeckRoles.Ramp, "Artifact");
        DeckCard lightningBolt = opened.Cards.Should().ContainSingle(card =>
            card.Name == "Lightning Bolt"
            && card.Quantity == 2).Subject;
        lightningBolt.Categories.Should().Equal(DeckDefaults.Sideboard, DeckRoles.Interaction);
        opened.Categories.Single(category => category.Name == DeckDefaults.Sideboard)
            .IncludedInPrice.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that bulk add cancellation after card resolution does not persist rows.
    /// </summary>
    [Fact]
    public async Task AddCardsBulk_PropagatesCancellationAfterResolutionWithoutPersisting()
    {
        InMemoryRepository repository = new();
        using CancellationTokenSource cancellation = new();
        FakeCardCatalog catalog = new() { CancelAfterBatchLookup = cancellation };
        DeckWorkspaceService service = new(repository, catalog);
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Cancelled Bulk",
            "commander",
            null,
            TestContext.Current.CancellationToken);
        int saveRequests = repository.SaveRequests;

        Func<Task> add = () => service.AddCardsBulkAsync(
            deck.Id,
            [
                new BulkDeckCardAdd
                {
                    CardName = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp
                },
                new BulkDeckCardAdd
                {
                    CardName = "Lightning Bolt",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Maybeboard
                },
            ],
            force: false,
            cancellation.Token);

        await add.Should().ThrowAsync<OperationCanceledException>();
        repository.SaveRequests.Should().Be(saveRequests);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);
        opened.Cards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that bulk category changes validate the full batch before persistence.
    /// </summary>
    [Fact]
    public async Task UpdateCardCategoriesBulk_ValidatesBeforePersisting()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Sol Ring",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );

        Func<Task> update = () => service.UpdateCardCategoriesBulkAsync(
            deck.Id,
            [
                new BulkCardCategoryChange
                {
                    CardName = "Sol Ring",
                    Action = BulkCardCategoryActions.AddSecondary,
                    Category = "Keep"
                },
                new BulkCardCategoryChange
                {
                    CardName = "Missing Card",
                    Action = BulkCardCategoryActions.SetPrimary,
                    Category = "Should Not Persist"
                },
            ],
            TestContext.Current.CancellationToken);

        await update.Should().ThrowAsync<InvalidOperationException>();
        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);
        opened.Cards.Single().Categories.Should().Equal(DeckDefaults.Mainboard);
        opened.Categories.Should().NotContain(category => category.Name == "Should Not Persist");
    }

    /// <summary>
    /// Verifies that compact category listing reads local cached rows.
    /// </summary>
    [Fact]
    public async Task ListCardsByCategory_ReturnsCompactRowsForPrimaryAndSecondaryMatches()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await service.AddCardsBulkAsync(
            deck.Id,
            [
                new BulkDeckCardAdd
                {
                    CardName = "Sol Ring",
                    PrimaryCategory = DeckRoles.Ramp,
                    SecondaryCategories = ["Artifacts"]
                },
                new BulkDeckCardAdd
                {
                    CardName = "Lightning Bolt",
                    PrimaryCategory = DeckDefaults.Sideboard,
                    SecondaryCategories = [DeckRoles.Interaction]
                },
            ],
            force: false,
            TestContext.Current.CancellationToken);

        DeckCategoryCardListResult result = await service.ListCardsByCategoryAsync(
            deck.Id,
            DeckRoles.Interaction,
            includeSecondary: true,
            limit: 25,
            TestContext.Current.CancellationToken);

        result.Count.Should().Be(1);
        result.TotalQuantity.Should().Be(1);
        result.Cards.Should().ContainSingle(card =>
            card.CardName == "Lightning Bolt"
            && card.PrimaryCategory == DeckDefaults.Sideboard
            && !card.IncludedInDeck
            && !card.IncludedInPrice);
    }

    /// <summary>
    /// Verifies that list local workspaces excludes cached archidekt decks.
    /// </summary>
    [Fact]
    public async Task ListLocalWorkspaces_ExcludesCachedArchidektDecks()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace local = await service.CreateLocalDeckAsync(
            "Local",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await repository.SaveAsync(
            new DeckWorkspace
            {
                Id = "remote-cache",
                Name = "Remote Cache",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
            },
            TestContext.Current.CancellationToken
        );

        IReadOnlyList<DeckWorkspace> workspaces = await service.ListLocalWorkspacesAsync(
            TestContext.Current.CancellationToken
        );

        workspaces.Should().ContainSingle();
        workspaces[0].Id.Should().Be(local.Id);
    }

    /// <summary>
    /// Verifies that start deck workspace rejects ambiguous mode.
    /// </summary>
    [Fact]
    public async Task StartDeckWorkspace_RejectsAmbiguousMode()
    {
        DeckWorkspaceService service = new(new InMemoryRepository(), new FakeCardCatalog());

        Func<Task> act = () =>
            service.StartDeckWorkspaceAsync(
                mode: null,
                name: "Brew",
                format: "commander",
                description: null,
                archidektDeckIdOrUrl: null,
                moxfieldDeckIdOrUrl: null,
                writeBack: null,
                decklist: null,
                TestContext.Current.CancellationToken
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Ask the user*local*Archidekt*");
    }

    /// <summary>
    /// Verifies that start deck workspace creates local or imports decklist.
    /// </summary>
    [Fact]
    public async Task StartDeckWorkspace_CreatesLocalOrImportsDecklist()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckWorkspace created = await service.StartDeckWorkspaceAsync(
            "local",
            "Scratch",
            "commander",
            "notes",
            archidektDeckIdOrUrl: null,
            moxfieldDeckIdOrUrl: null,
            writeBack: null,
            decklist: null,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace imported = await service.StartDeckWorkspaceAsync(
            "local",
            "Import",
            "modern",
            description: null,
            archidektDeckIdOrUrl: null,
            moxfieldDeckIdOrUrl: null,
            writeBack: null,
            decklist: "1 Lightning Bolt",
            TestContext.Current.CancellationToken
        );

        created.Mode.Should().Be(WorkspaceMode.Local);
        created.Description.Should().Be("notes");
        imported.Cards.Should().ContainSingle(card => card.Name == "Lightning Bolt");
    }

    /// <summary>
    /// Verifies that start deck workspace requires explicit archidekt write back choice.
    /// </summary>
    [Fact]
    public async Task StartDeckWorkspace_RequiresExplicitArchidektWriteBackChoice()
    {
        DeckWorkspaceService service = new(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            new FakeArchidektGateway()
        );

        Func<Task> act = () =>
            service.StartDeckWorkspaceAsync(
                "archidekt",
                name: null,
                format: "commander",
                description: null,
                archidektDeckIdOrUrl: "123",
                moxfieldDeckIdOrUrl: null,
                writeBack: null,
                decklist: null,
                TestContext.Current.CancellationToken
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*writeback intent is ambiguous*Ask*");
    }

    /// <summary>
    /// Verifies that start deck workspace opens archidekt when mode and write back are explicit.
    /// </summary>
    [Fact]
    public async Task StartDeckWorkspace_OpensArchidektWhenModeAndWriteBackAreExplicit()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway gateway = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote",
                Name = "Remote",
                Mode = WorkspaceMode.Archidekt,
                ArchidektDeckId = "123",
            },
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), gateway);

        DeckWorkspace workspace = await service.StartDeckWorkspaceAsync(
            "archidekt",
            name: null,
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: "123",
            moxfieldDeckIdOrUrl: null,
            writeBack: false,
            decklist: null,
            TestContext.Current.CancellationToken
        );

        workspace.Mode.Should().Be(WorkspaceMode.Archidekt);
        workspace.WriteBack.Should().BeFalse();
        gateway.ImportedDeckRequests.Should().Be(1);
    }

    /// <summary>
    /// Verifies that an Archidekt-sourced cached workspace can be reopened with writeback enabled.
    /// </summary>
    [Fact]
    public async Task ReopenWorkspaceWithWriteback_UsesArchidektSourceReference()
    {
        InMemoryRepository repository = new();
        DeckWorkspace cached = await repository.SaveAsync(new DeckWorkspace
        {
            Id = "cached",
            Name = "Cached Import",
            Mode = WorkspaceMode.Local,
            WriteBack = false,
            SourceReferences =
            [
                new DeckSourceReference
                {
                    Provider = DeckImportProviders.Archidekt,
                    ExternalId = "23097041",
                    Url = "https://archidekt.com/decks/23097041/inga_and_esika"
                }
            ]
        }, TestContext.Current.CancellationToken);
        FakeArchidektGateway gateway = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote-writeback",
                Name = "Remote Writeback",
                Mode = WorkspaceMode.Archidekt,
                ArchidektDeckId = "23097041",
                Categories = [new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true }]
            }
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), gateway);

        DeckWorkspace reopened = await service.ReopenWorkspaceWithWritebackAsync(
            cached.Id,
            TestContext.Current.CancellationToken);

        reopened.Mode.Should().Be(WorkspaceMode.Archidekt);
        reopened.WriteBack.Should().BeTrue();
        reopened.ArchidektDeckId.Should().Be("23097041");
        gateway.ImportedDeckRequests.Should().Be(1);
    }

    /// <summary>
    /// Verifies that start deck workspace imports Moxfield decks as local workspaces.
    /// </summary>
    [Fact]
    public async Task StartDeckWorkspace_ImportsMoxfieldAsLocalWorkspace()
    {
        FakeMoxfieldGateway moxfield = new()
        {
            ImportedDeck = CreateImportedMoxfieldWorkspace(),
        };
        DeckWorkspaceService service = new(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            moxfieldGateway: moxfield);

        DeckWorkspace workspace = await service.StartDeckWorkspaceAsync(
            "moxfield",
            name: null,
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: null,
            moxfieldDeckIdOrUrl: "mox-1",
            writeBack: null,
            decklist: null,
            TestContext.Current.CancellationToken);

        workspace.Mode.Should().Be(WorkspaceMode.Local);
        workspace.WriteBack.Should().BeFalse();
        workspace.Cards.Single(card => card.Name == "Sol Ring").Categories
            .Should().Equal(DeckDefaults.Mainboard, "Ramp");
        workspace.SourceReferences.Should().ContainSingle(source =>
            source.Provider == DeckImportProviders.Moxfield
            && source.ExternalId == "mox-1");
        moxfield.ImportRequests.Should().ContainSingle().Which.Should().Be("mox-1");
    }

    /// <summary>
    /// Verifies that Moxfield mode refuses unsupported writeback.
    /// </summary>
    [Fact]
    public async Task StartDeckWorkspace_RejectsMoxfieldWriteback()
    {
        DeckWorkspaceService service = new(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            moxfieldGateway: new FakeMoxfieldGateway());

        Func<Task> act = () => service.StartDeckWorkspaceAsync(
            "moxfield",
            name: null,
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: null,
            moxfieldDeckIdOrUrl: "mox-1",
            writeBack: true,
            decklist: null,
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Moxfield writeback is not supported*");
    }

    /// <summary>
    /// Verifies that copy to Archidekt dry run reports a safe migration plan.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_DryRunReportsCardsCategoriesAndWarnings()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(
            repository,
            new FakeCardCatalog(),
            new FakeArchidektGateway());

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: true,
            createNew: true,
            destinationDeckIdOrUrl: null,
            name: "Migrated",
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        result.DryRun.Should().BeTrue();
        result.CreatedNewDeck.Should().BeTrue();
        result.DestinationName.Should().Be("Migrated");
        result.TotalCards.Should().Be(3);
        result.IncludedCards.Should().Be(2);
        result.ExpectedCardRows.Should().Be(source.Cards.Count);
        result.Categories.Should().Contain(DeckDefaults.Mainboard);
        result.Categories.Should().Contain(DeckDefaults.Maybeboard);
        result.Categories.Should().Contain("Ramp");
        result.CopyPhase.Should().Be("dry-run");
        result.EstimatedArchidektRequests.Should().BeGreaterThan(0);
        result.MissingArchidektCardIds.Should().BeGreaterThan(0);
        result.CardIdDiagnostics.Should().Contain("apply mode will resolve");
        result.NextAction.Should().Contain("dryRun=false");
        result.Commanders.Should().ContainSingle().Which.Should().Be("Atraxa, Praetors' Voice");
        result.Warnings.Should().Contain(warning => warning.Contains("no Scryfall id", StringComparison.OrdinalIgnoreCase));
        result.Warnings.Should().Contain(warning => warning.Contains("do not by themselves mean the copy will fail", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that copy to Archidekt creates a deck and preserves secondary tags.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_CreateNewPreservesTags()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: true,
            destinationDeckIdOrUrl: null,
            name: "Migrated",
            format: "commander",
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        result.DryRun.Should().BeFalse();
        result.DestinationArchidektDeckId.Should().Be("created");
        result.CopyPhase.Should().Be("complete");
        result.CardIdsResolved.Should().Be(source.Cards.Count);
        result.MissingArchidektCardIds.Should().Be(0);
        archidekt.UpsertedCards.Should().Contain(card =>
            card.Name == "Sol Ring"
            && card.Categories.SequenceEqual(new[] { DeckDefaults.Mainboard, "Ramp" }));
        archidekt.UpsertedCards.Should().Contain(card =>
            card.Name == "Brainstorm"
            && card.PrimaryCategory == DeckDefaults.Maybeboard
            && card.Categories.Contains("Card Draw"));
        archidekt.PersistedCategories.Should().Contain(category =>
            category.Name == "Ramp" && !category.IncludedInDeck);
        archidekt.PersistedCategories.Should().Contain(category =>
            category.Name == "Card Draw" && !category.IncludedInDeck);
        source.Mode.Should().Be(WorkspaceMode.Local);
    }

    /// <summary>
    /// Verifies that retrying a completed create-new migration returns the existing deck.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_CreateNewReturnsCompletedMigration()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            DeckSummaries =
            [
                new ArchidektDeckSummary { Id = "existing", Name = "Migrated" },
            ],
            ImportedDeck = CreateMigrationDestination(source, "existing", "Migrated", includeCards: true),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: true,
            destinationDeckIdOrUrl: null,
            name: "Migrated",
            format: "commander",
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        result.CreatedNewDeck.Should().BeFalse();
        result.DestinationArchidektDeckId.Should().Be("existing");
        result.Warnings.Should().Contain(warning =>
            warning.Contains("instead of creating a duplicate", StringComparison.OrdinalIgnoreCase));
        archidekt.CreatedDeckRequests.Should().Be(0);
        archidekt.UpsertedCards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that retrying after deck creation reuses the empty shell.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_CreateNewReusesEmptyMigrationShell()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            DeckSummaries =
            [
                new ArchidektDeckSummary { Id = "existing", Name = "Migrated" },
            ],
            ImportedDeck = CreateMigrationDestination(source, "existing", "Migrated", includeCards: false),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: true,
            destinationDeckIdOrUrl: null,
            name: "Migrated",
            format: "commander",
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        result.CreatedNewDeck.Should().BeFalse();
        result.DestinationArchidektDeckId.Should().Be("existing");
        result.Warnings.Should().Contain(warning =>
            warning.Contains("reusing it", StringComparison.OrdinalIgnoreCase));
        archidekt.CreatedDeckRequests.Should().Be(0);
        archidekt.UpsertedCards.Should().HaveCount(source.Cards.Count);
    }

    /// <summary>
    /// Verifies that retrying a mismatched migration deck fails without creating another deck.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_CreateNewRejectsMismatchedMigration()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        DeckWorkspace mismatchedDestination = CreateMigrationDestination(
            source,
            "existing",
            "Migrated",
            includeCards: true);
        mismatchedDestination.Cards.Add(new DeckCard
        {
            Name = "Unexpected Card",
            Quantity = 1,
            PrimaryCategory = DeckDefaults.Mainboard,
            Categories = [DeckDefaults.Mainboard],
        });
        FakeArchidektGateway archidekt = new()
        {
            DeckSummaries =
            [
                new ArchidektDeckSummary { Id = "existing", Name = "Migrated" },
            ],
            ImportedDeck = mismatchedDestination,
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        Func<Task> act = () => service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: true,
            destinationDeckIdOrUrl: null,
            name: "Migrated",
            format: "commander",
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*existing*replaceExistingDestination=true*");
        archidekt.CreatedDeckRequests.Should().Be(0);
        archidekt.UpsertedCards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that copying into a non-empty Archidekt deck requires an explicit override.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_RejectsNonEmptyExistingDestination()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = CreateExistingArchidektDestination(),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult dryRun = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: true,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);
        Func<Task> apply = () => service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        dryRun.Warnings.Should().Contain(warning =>
            warning.Contains("not empty", StringComparison.OrdinalIgnoreCase));
        await apply.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*not empty*allowNonEmptyDestination=true*replaceExistingDestination=true*");
    }

    /// <summary>
    /// Verifies that append and replace modes cannot be requested together.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_RejectsConflictingNonEmptyPolicies()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        Func<Task> act = () => service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: true,
            replaceExistingDestination: true,
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Choose either allowNonEmptyDestination=true*replaceExistingDestination=true*not both*");
        archidekt.ImportedDeckRequests.Should().Be(0);
    }

    /// <summary>
    /// Verifies that replace mode removes destination cards before copying source tags.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_ReplaceExistingDestination()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = CreateExistingArchidektDestination(),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: true,
            TestContext.Current.CancellationToken);

        result.DestinationArchidektDeckId.Should().Be("123");
        result.CheckpointId.Should().Be("7");
        result.VerificationStatus.Should().Be("verified");
        result.ExpectedCardRows.Should().Be(source.Cards.Count);
        result.DetectedCardRows.Should().Be(source.Cards.Count);
        archidekt.RemovedCards.Should().Contain(card => card.Name == "Existing Card");
        archidekt.RemovedCards.Should().Contain(card => card.Name == "Sol Ring");
        archidekt.UpsertedCards.Should().Contain(card =>
            card.Name == "Sol Ring"
            && card.ArchidektCardId == "500"
            && card.Categories.SequenceEqual(new[] { DeckDefaults.Mainboard, "Ramp" }));
        archidekt.UpsertedCards.Should().Contain(card =>
            card.Name == "Brainstorm"
            && card.Categories.Contains("Card Draw"));
    }

    /// <summary>
    /// Verifies that replace mode stops before writes when checkpoint creation fails.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_ReplaceCheckpointFailureStopsBeforeMutation()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            FailCheckpointCreation = true,
            ImportedDeck = CreateExistingArchidektDestination(),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: true,
            TestContext.Current.CancellationToken);

        result.FailedPhase.Should().Be("checkpoint");
        result.VerificationStatus.Should().Be("blocked");
        result.CanResume.Should().BeFalse();
        result.RecoveryInstructions.Should().Contain(instruction =>
            instruction.Contains("No destination card mutation", StringComparison.OrdinalIgnoreCase));
        archidekt.PersistedMetadataRequests.Should().Be(0);
        archidekt.RemovedCards.Should().BeEmpty();
        archidekt.UpsertedCards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that replace removal failures return restore-first recovery guidance.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_ReplaceRemovalFailureRequiresCheckpointRestore()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            ThrowAfterCardRemoval = true,
            ImportedDeck = CreateExistingArchidektDestination(),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: true,
            TestContext.Current.CancellationToken);

        result.FailedPhase.Should().Be("remove-cards");
        result.CheckpointId.Should().Be("7");
        result.CanResume.Should().BeFalse();
        result.DetectedCardRows.Should().Be(0);
        result.NextAction.Should().Contain("Restore Archidekt checkpoint 7");
        result.RecoveryInstructions.Should().Contain(instruction =>
            instruction.Contains("Restore Archidekt checkpoint 7", StringComparison.OrdinalIgnoreCase));
        archidekt.UpsertedCards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that final verification mismatches are explicit and do not claim success.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_ReplaceVerificationMismatchRequiresCheckpointRestore()
    {
        InMemoryRepository repository = new();
        DeckWorkspace source = await repository.SaveAsync(
            CreateImportedMoxfieldWorkspace(),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            SkipRemoteCardMutation = true,
            ImportedDeck = CreateExistingArchidektDestination(),
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        ArchidektCopyResult result = await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: true,
            TestContext.Current.CancellationToken);

        result.CopyPhase.Should().Be("verify");
        result.FailedPhase.Should().Be("verify");
        result.VerificationStatus.Should().Be("mismatch");
        result.ExpectedCardRows.Should().Be(source.Cards.Count);
        result.DetectedCardRows.Should().Be(2);
        result.CanResume.Should().BeFalse();
        result.NextAction.Should().Contain("Restore Archidekt checkpoint 7");
    }

    /// <summary>
    /// Verifies that copying into an existing Archidekt deck preserves source deck intent by default.
    /// </summary>
    [Fact]
    public async Task CopyWorkspaceToArchidekt_ExistingDestinationPreservesSourceIntent()
    {
        InMemoryRepository repository = new();
        DeckWorkspace imported = CreateImportedMoxfieldWorkspace();
        imported.Description = DeckIntentText.UpsertDescription(
            "Source primer.",
            """
            Commander: Atraxa, Praetors' Voice
            Archetype: counters
            """);
        DeckWorkspace source = await repository.SaveAsync(imported, TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote",
                Name = "Existing",
                Description = "Existing destination notes.",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
                Categories =
                [
                    new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                ],
            },
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        await service.CopyWorkspaceToArchidektAsync(
            source.Id,
            dryRun: false,
            createNew: false,
            destinationDeckIdOrUrl: "123",
            name: null,
            format: null,
            description: null,
            visibility: "private",
            allowNonEmptyDestination: false,
            replaceExistingDestination: false,
            TestContext.Current.CancellationToken);

        string persistedDescription = archidekt.PersistedDescriptions.Should().ContainSingle().Subject ?? "";
        persistedDescription.Should().Contain("Existing destination notes.");
        persistedDescription.Should().Contain("MTG MCP Deck Intent");
        persistedDescription.Should().Contain("Commander: Atraxa, Praetors' Voice");
    }

    /// <summary>
    /// Verifies that set quantity to zero removes card.
    /// </summary>
    [Fact]
    public async Task SetQuantityToZero_RemovesCard()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );

        await service.AddCardAsync(
            deck.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        await service.SetCardQuantityAsync(
            deck.Id,
            "Lightning Bolt",
            0,
            null,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        );

        opened.Cards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that included Commander additions refuse accidental overfills.
    /// </summary>
    [Fact]
    public async Task AddCard_RefusesIncludedCommanderOverfillUnlessForced()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace workspace = await repository.SaveAsync(
            new DeckWorkspace
            {
                Name = "Full Commander",
                Format = "commander",
                Categories =
                [
                    new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                    new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = true },
                ],
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
            },
            TestContext.Current.CancellationToken);

        Func<Task> blocked = () => service.AddCardAsync(
            workspace.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Sideboard,
            TestContext.Current.CancellationToken);
        await blocked.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Commander deck from 100 to 101*force=true*");

        DeckChangeResult forced = await service.AddCardAsync(
            workspace.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Sideboard,
            force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        forced.Workspace.Cards.Should().Contain(card =>
            card.Name == "Lightning Bolt"
            && card.PrimaryCategory == DeckDefaults.Sideboard);
    }

    /// <summary>
    /// Verifies that excluded categories remain safe places for Commander maybes.
    /// </summary>
    [Fact]
    public async Task AddCard_AllowsExcludedCommanderSideboardOverfill()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace workspace = await repository.SaveAsync(
            new DeckWorkspace
            {
                Name = "Full Commander",
                Format = "commander",
                Categories =
                [
                    new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                    new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
                ],
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
            },
            TestContext.Current.CancellationToken);

        DeckChangeResult result = await service.AddCardAsync(
            workspace.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Sideboard,
            TestContext.Current.CancellationToken);

        result.Workspace.Cards.Should().Contain(card =>
            card.Name == "Lightning Bolt"
            && card.PrimaryCategory == DeckDefaults.Sideboard);
    }

    /// <summary>
    /// Verifies that considering is created as an excluded candidate category.
    /// </summary>
    [Fact]
    public async Task AddCard_CreatesConsideringAsExcludedCategory()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace workspace = await repository.SaveAsync(
            new DeckWorkspace
            {
                Name = "Full Commander",
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
            },
            TestContext.Current.CancellationToken);

        DeckChangeResult result = await service.AddCardAsync(
            workspace.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Considering,
            TestContext.Current.CancellationToken);

        result.Workspace.Categories.Should().Contain(category =>
            category.Name == DeckDefaults.Considering
            && !category.IncludedInDeck);
        result.Workspace.Cards.Should().Contain(card =>
            card.Name == "Lightning Bolt"
            && card.PrimaryCategory == DeckDefaults.Considering);
    }

    /// <summary>
    /// Verifies that caller cancellation is not treated as an optional metadata outage.
    /// </summary>
    [Fact]
    public async Task AddCard_PropagatesCallerCancellationDuringMetadataLookup()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog { CancelGetCard = true });
        DeckWorkspace workspace = await repository.SaveAsync(
            new DeckWorkspace { Name = "Cancelled Add" },
            TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> add = () => service.AddCardAsync(
            workspace.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            cancellation.Token);

        await add.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that remove card decrements then removes card.
    /// </summary>
    [Fact]
    public async Task RemoveCard_DecrementsThenRemovesCard()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "modern",
            null,
            TestContext.Current.CancellationToken
        );

        await service.AddCardAsync(
            deck.Id,
            "Lightning Bolt",
            3,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        DeckChangeResult decremented = await service.RemoveCardAsync(
            deck.Id,
            "Lightning Bolt",
            1,
            null,
            TestContext.Current.CancellationToken
        );
        DeckChangeResult removed = await service.RemoveCardAsync(
            deck.Id,
            "Lightning Bolt",
            2,
            null,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        );

        decremented.Kind.Should().Be(DeckMutationKind.CardRemoved);
        removed.Kind.Should().Be(DeckMutationKind.CardRemoved);
        opened.Cards.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that imported decklists export grouped deck text.
    /// </summary>
    [Fact]
    public async Task ImportDecklist_ExportsGroupedDeckText()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckWorkspace imported = await service.ImportDecklistAsync(
            CreateDeckTextComponentDecklist(),
            "Imported",
            "modern",
            TestContext.Current.CancellationToken
        );
        string exported = await service.ExportDeckAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        DeckAnalysis analysis = await service.AnalyzeDeckAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        DeckValidationResult validation = await service.ValidateDeckAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        object summary = await service.GetDeckSummaryAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace resource = await service.GetDeckResourceAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );

        exported.Should().Contain("Mainboard");
        exported.Should().Contain("2 Lightning Bolt");
        exported.Should().Contain("1 Sol Ring");
        exported.Should().Contain("Sideboard");
        exported.Should().Contain("Maybeboard");
        exported.Should().Contain("1 Missing Card");
    }

    /// <summary>
    /// Verifies that export options add markdown formats without changing the grouped text default.
    /// </summary>
    [Fact]
    public async Task ExportDeckAsync_SupportsMarkdownOptionsWithoutChangingDefault()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckWorkspace imported = await service.ImportDecklistAsync(
            CreateDeckTextComponentDecklist(),
            "Imported",
            "modern",
            TestContext.Current.CancellationToken
        );

        string defaultText = await service.ExportDeckAsync(
            imported.Id,
            TestContext.Current.CancellationToken);
        string markdownLinks = await service.ExportDeckAsync(
            imported.Id,
            "markdown-links",
            includedOnly: true,
            includeCategories: true,
            cancellationToken: TestContext.Current.CancellationToken);
        string ungroupedMarkdown = await service.ExportDeckAsync(
            imported.Id,
            "markdown",
            includedOnly: true,
            includeCategories: false,
            cancellationToken: TestContext.Current.CancellationToken);

        defaultText.Should().Contain("Mainboard");
        defaultText.Should().Contain("2 Lightning Bolt");
        markdownLinks.Should().Contain("## Mainboard");
        markdownLinks.Should().Contain("- 2 [Lightning Bolt](https://scryfall.test/Lightning%20Bolt)");
        markdownLinks.Should().NotContain("Maybeboard");
        ungroupedMarkdown.Should().NotContain("## Mainboard");
        ungroupedMarkdown.Should().Contain("- 1 Sol Ring");
        ungroupedMarkdown.Should().NotContain($"{Environment.NewLine}{Environment.NewLine}");
    }

    /// <summary>
    /// Verifies that workspace diff uses an explicit baseline and reports card/category changes.
    /// </summary>
    [Fact]
    public async Task DiffWorkspacesAsync_ReportsExplicitBaselineAndCardChanges()
    {
        InMemoryRepository repository = new();
        DeckWorkspace previous = await repository.SaveAsync(CreateDiffWorkspace("baseline"), TestContext.Current.CancellationToken);
        DeckWorkspace current = CreateDiffWorkspace("current");
        current.Cards.RemoveAll(card => card.Name == "Brainstorm");
        DeckCategoryOrdering.SetPrimary(current.Cards.Single(card => card.Name == "Counterspell"), DeckDefaults.Maybeboard);
        DeckCategoryOrdering.SetPrimary(current.Cards.Single(card => card.Name == "Finale of Devastation"), DeckDefaults.Mainboard);
        DeckCategoryOrdering.AddSecondary(current.Cards.Single(card => card.Name == "Sol Ring"), "Protected");
        current.Cards.Add(new DeckCard
        {
            Name = "Beast Whisperer",
            Quantity = 1,
            PrimaryCategory = DeckRoles.Draw,
            Categories = [DeckRoles.Draw],
            ScryfallOracleId = "oracle-beast-whisperer",
            Snapshot = new CardSnapshot
            {
                TypeLine = "Creature - Elf",
                OracleText = "Whenever you cast a creature spell, draw a card.",
                ScryfallUri = "https://scryfall.test/beast-whisperer"
            }
        });
        current = await repository.SaveAsync(current, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        WorkspaceDiffResult diff = await service.DiffWorkspacesAsync(
            current.Id,
            previous.Id,
            TestContext.Current.CancellationToken);

        diff.Baseline.WorkspaceId.Should().Be(previous.Id);
        diff.Baseline.Source.Should().Contain("archidekt:23097041");
        diff.Notes.Should().Contain(note => note.Contains(previous.Id, StringComparison.OrdinalIgnoreCase));
        diff.AddedCards.Should().ContainSingle(card => card.CardName == "Beast Whisperer");
        diff.RemovedCards.Should().ContainSingle(card => card.CardName == "Brainstorm");
        diff.PrimaryMoves.Should().Contain(card =>
            card.CardName == "Counterspell"
            && card.PrimaryCategoryBefore == DeckDefaults.Mainboard
            && card.PrimaryCategoryAfter == DeckDefaults.Maybeboard);
        diff.PrimaryMoves.Should().Contain(card =>
            card.CardName == "Finale of Devastation"
            && card.PrimaryCategoryBefore == DeckDefaults.Sideboard
            && card.PrimaryCategoryAfter == DeckDefaults.Mainboard);
        diff.SecondaryTagChanges.Should().ContainSingle(card =>
            card.CardName == "Sol Ring"
            && card.SecondaryCategoriesAfter.Contains("Protected"));
    }

    /// <summary>
    /// Verifies that compact zone listings separate active and excluded card rows.
    /// </summary>
    [Fact]
    public async Task ListCardsByZoneAsync_FiltersAndCollapsesDuplicateRows()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = CreateDiffWorkspace("zones");
        workspace.Cards.Add(new DeckCard
        {
            Name = "Sol Ring",
            Quantity = 2,
            PrimaryCategory = DeckDefaults.Maybeboard,
            Categories = [DeckDefaults.Maybeboard],
            ScryfallOracleId = "oracle-sol-ring",
            Snapshot = new CardSnapshot { TypeLine = "Artifact" }
        });
        workspace.Cards.Single(card => card.Name == "Sol Ring" && card.PrimaryCategory == DeckDefaults.Mainboard)
            .Categories.Add(DeckDefaults.Sideboard);
        workspace = await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckCardsByZoneResult active = await service.ListCardsByZoneAsync(
            workspace.Id,
            DeckCardZones.Active,
            collapseDuplicates: true,
            TestContext.Current.CancellationToken);
        DeckCardsByZoneResult excluded = await service.ListCardsByZoneAsync(
            workspace.Id,
            DeckCardZones.Excluded,
            collapseDuplicates: true,
            TestContext.Current.CancellationToken);
        DeckCardsByZoneResult all = await service.ListCardsByZoneAsync(
            workspace.Id,
            DeckCardZones.All,
            collapseDuplicates: true,
            TestContext.Current.CancellationToken);

        active.Cards.Single(row => row.CardName == "Sol Ring").Quantity.Should().Be(1);
        active.Cards.Should().NotContain(row => row.CardName == "Finale of Devastation");
        excluded.Cards.Single(row => row.CardName == "Sol Ring").Quantity.Should().Be(2);
        excluded.Cards.Should().Contain(row => row.CardName == "Finale of Devastation");
        DeckCardZoneRow collapsedSolRing = all.Cards.Single(row => row.CardName == "Sol Ring");
        collapsedSolRing.Quantity.Should().Be(3);
        collapsedSolRing.PrimaryCategory.Should().BeNull();
        collapsedSolRing.Categories.Should().Contain([DeckDefaults.Mainboard, DeckDefaults.Sideboard, DeckDefaults.Maybeboard]);
        collapsedSolRing.Locations.Should().Contain(location =>
            location.Category == DeckDefaults.Mainboard
            && location.Primary
            && location.IncludedInDeck
            && location.Quantity == 1);
        collapsedSolRing.Locations.Should().NotContain(location =>
            location.Category == DeckDefaults.Sideboard
            && !location.Primary);
        collapsedSolRing.Locations.Should().Contain(location =>
            location.Category == DeckDefaults.Maybeboard
            && location.Primary
            && !location.IncludedInDeck
            && location.Quantity == 2);
    }

    /// <summary>
    /// Verifies that bulk moves preserve card metadata and support local partial splits.
    /// </summary>
    [Fact]
    public async Task MoveCardsBulkAsync_MovesWholeRowsAndSplitsLocalPartialRows()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreateBulkMoveWorkspace(), TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckChangeResult result = await service.MoveCardsBulkAsync(
            workspace.Id,
            [
                new BulkDeckCardMove
                {
                    CardName = "Ramp Stone",
                    Quantity = 1,
                    FromCategory = DeckRoles.Ramp,
                    ToCategory = DeckDefaults.Sideboard
                },
                new BulkDeckCardMove
                {
                    CardName = "Sol Ring",
                    FromCategory = DeckRoles.Ramp,
                    ToCategory = DeckDefaults.Maybeboard
                }
            ],
            TestContext.Current.CancellationToken);

        DeckWorkspace changed = result.Workspace;
        changed.Cards.Single(card => card.Name == "Ramp Stone" && card.PrimaryCategory == DeckRoles.Ramp)
            .Quantity.Should().Be(2);
        DeckCard split = changed.Cards.Single(card =>
            card.Name == "Ramp Stone"
            && card.PrimaryCategory == DeckDefaults.Sideboard);
        split.Quantity.Should().Be(1);
        split.ScryfallId.Should().Be("ramp-stone-print");
        split.Snapshot.ScryfallUri.Should().Be("https://scryfall.test/ramp-stone");
        split.ArchidektDeckRelationId.Should().BeNull();
        changed.Cards.Single(card => card.Name == "Sol Ring").PrimaryCategory.Should().Be(DeckDefaults.Maybeboard);
    }

    /// <summary>
    /// Verifies that partial bulk moves are rejected for Archidekt writeback workspaces.
    /// </summary>
    [Fact]
    public async Task MoveCardsBulkAsync_RejectsPartialArchidektWritebackMoves()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = CreateBulkMoveWorkspace();
        workspace.Mode = WorkspaceMode.Archidekt;
        workspace.WriteBack = true;
        workspace.ArchidektDeckId = "123";
        workspace = await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        FakeArchidektGateway gateway = new() { ImportedDeck = workspace };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), gateway);

        Func<Task> act = () => service.MoveCardsBulkAsync(
            workspace.Id,
            [
                new BulkDeckCardMove
                {
                    CardName = "Ramp Stone",
                    Quantity = 1,
                    FromCategory = DeckRoles.Ramp,
                    ToCategory = DeckDefaults.Sideboard
                }
            ],
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Partial bulk moves are not writeback-safe*");
    }

    /// <summary>
    /// Verifies that explicit non-positive bulk move quantities are rejected instead of treated as whole-row moves.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MoveCardsBulkAsync_RejectsNonPositiveQuantities(int quantity)
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreateBulkMoveWorkspace(), TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        Func<Task> act = () => service.MoveCardsBulkAsync(
            workspace.Id,
            [
                new BulkDeckCardMove
                {
                    CardName = "Ramp Stone",
                    Quantity = quantity,
                    FromCategory = DeckRoles.Ramp,
                    ToCategory = DeckDefaults.Sideboard
                }
            ],
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*quantity must be greater than zero*");
    }

    /// <summary>
    /// Verifies that markdown-link exports use exact-name Scryfall fallbacks.
    /// </summary>
    [Fact]
    public async Task ExportDeckAsync_MarkdownLinksUsesSnapshotUrisAndExactNameFallbacks()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreateDiffWorkspace("export-links"), TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        string markdown = await service.ExportDeckAsync(
            workspace.Id,
            format: "markdown-links",
            includedOnly: false,
            includeCategories: true,
            TestContext.Current.CancellationToken);

        markdown.Should().Contain("[Sol Ring](https://scryfall.test/sol-ring)");
        markdown.Should().Contain("[Brainstorm](https://scryfall.test/brainstorm)");

        workspace.Cards.Single(card => card.Name == "Brainstorm").Snapshot.ScryfallUri = null;
        await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        markdown = await service.ExportDeckAsync(
            workspace.Id,
            format: "markdown-links",
            includedOnly: false,
            includeCategories: true,
            TestContext.Current.CancellationToken);

        markdown.Should().Contain("[Brainstorm](https://scryfall.com/search?as=grid&order=name&q=%21%22Brainstorm%22)");
    }

    /// <summary>
    /// Verifies last-import diff status values and baseline diffing.
    /// </summary>
    [Fact]
    public async Task DiffLastImportAsync_ReturnsExplicitStatusesAndBaselineDiff()
    {
        InMemoryRepository repository = new();
        DeckWorkspace noSource = await repository.SaveAsync(new DeckWorkspace { Id = "no-source" }, TestContext.Current.CancellationToken);
        DeckWorkspace noBaseline = await repository.SaveAsync(CreateDiffWorkspace("no-baseline"), TestContext.Current.CancellationToken);
        DeckWorkspace unsupported = await repository.SaveAsync(new DeckWorkspace
        {
            Id = "unsupported",
            SourceReferences =
            [
                new DeckSourceReference { Provider = "other", ExternalId = "deck-1" }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspace unavailable = await repository.SaveAsync(CreateDiffWorkspace("unavailable"), TestContext.Current.CancellationToken);
        unavailable.ImportHistory.Add(new DeckImportHistoryEntry
        {
            Provider = DeckImportProviders.Archidekt,
            ExternalId = "23097041",
            LocalWorkspaceId = unavailable.Id,
            BaselineWorkspace = null
        });
        await repository.SaveAsync(unavailable, TestContext.Current.CancellationToken);
        DeckWorkspace baseline = await repository.SaveAsync(CreateDiffWorkspace("imported"), TestContext.Current.CancellationToken);
        DeckWorkspace remote = CreateDiffWorkspace("remote");
        remote.ArchidektDeckId = "23097041";
        remote.Mode = WorkspaceMode.Archidekt;
        remote.Cards.Add(new DeckCard
        {
            Name = "Beast Whisperer",
            Quantity = 1,
            PrimaryCategory = DeckRoles.Draw,
            Categories = [DeckRoles.Draw],
            ScryfallOracleId = "oracle-beast-whisperer"
        });
        FakeArchidektGateway gateway = new() { ImportedDeck = remote };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), gateway);

        DeckWorkspace imported = await service.ReopenWorkspaceWithWritebackAsync(
            baseline.Id,
            TestContext.Current.CancellationToken);
        WorkspaceDiffLastImportResult sourceMissing = await service.DiffLastImportAsync(
            noSource.Id,
            TestContext.Current.CancellationToken);
        WorkspaceDiffLastImportResult baselineMissing = await service.DiffLastImportAsync(
            noBaseline.Id,
            TestContext.Current.CancellationToken);
        WorkspaceDiffLastImportResult unsupportedSource = await service.DiffLastImportAsync(
            unsupported.Id,
            TestContext.Current.CancellationToken);
        WorkspaceDiffLastImportResult unavailableHistory = await service.DiffLastImportAsync(
            unavailable.Id,
            TestContext.Current.CancellationToken);
        WorkspaceDiffLastImportResult diff = await service.DiffLastImportAsync(
            imported.Id,
            TestContext.Current.CancellationToken);

        sourceMissing.Status.Should().Be(WorkspaceDiffLastImportStatus.WorkspaceHasNoSource);
        baselineMissing.Status.Should().Be(WorkspaceDiffLastImportStatus.NoPriorBaseline);
        unsupportedSource.Status.Should().Be(WorkspaceDiffLastImportStatus.SourceUnsupported);
        unavailableHistory.Status.Should().Be(WorkspaceDiffLastImportStatus.HistoryUnavailable);
        diff.Status.Should().Be(WorkspaceDiffLastImportStatus.BaselineFound);
        diff.Diff.Should().NotBeNull();
        diff.Diff!.AddedCards.Should().ContainSingle(card => card.CardName == "Beast Whisperer");
        imported.ImportHistory.Should().ContainSingle();
        imported.ImportHistory[0].LocalWorkspaceId.Should().Be(baseline.Id);
        imported.ImportHistory[0].BaselineWorkspace!.ImportHistory.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that Archidekt refresh preserves workspace identity and captures a baseline diff.
    /// </summary>
    [Fact]
    public async Task RefreshWorkspaceFromSourceAsync_RefreshesArchidektInPlace()
    {
        InMemoryRepository repository = new();
        DeckWorkspace current = CreateDiffWorkspace("refresh-archidekt");
        current.Mode = WorkspaceMode.Archidekt;
        current.WriteBack = true;
        current.ArchidektDeckId = "23097041";
        current.LocalCheckpoints.Add(new WorkspaceCheckpoint
        {
            Id = "checkpoint",
            WorkspaceId = current.Id,
            Name = "Before refresh",
            Snapshot = new DeckWorkspace { Id = current.Id }
        });
        await repository.SaveAsync(current, TestContext.Current.CancellationToken);
        DeckWorkspace remote = CreateDiffWorkspace("remote-archidekt");
        remote.Mode = WorkspaceMode.Archidekt;
        remote.ArchidektDeckId = "23097041";
        remote.Cards.Add(new DeckCard
        {
            Name = "Beast Whisperer",
            Quantity = 1,
            PrimaryCategory = DeckRoles.Draw,
            Categories = [DeckRoles.Draw],
            ScryfallOracleId = "oracle-beast-whisperer"
        });
        DeckWorkspaceService service = new(
            repository,
            new FakeCardCatalog(),
            new FakeArchidektGateway { ImportedDeck = remote });

        WorkspaceRefreshFromSourceResult result = await service.RefreshWorkspaceFromSourceAsync(
            current.Id,
            writeBack: null,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(WorkspaceRefreshFromSourceStatus.Refreshed);
        result.WorkspaceId.Should().Be(current.Id);
        result.Workspace.Should().NotBeNull();
        result.Workspace!.Id.Should().Be(current.Id);
        result.Workspace.WriteBack.Should().BeTrue();
        result.Workspace.LocalCheckpoints.Should().ContainSingle(checkpoint => checkpoint.Id == "checkpoint");
        result.DiffLastImport!.Status.Should().Be(WorkspaceDiffLastImportStatus.BaselineFound);
        result.DiffLastImport.Diff!.AddedCards.Should().ContainSingle(card => card.CardName == "Beast Whisperer");
        result.Workspace.ImportHistory.Should().ContainSingle();
        result.Workspace.ImportHistory[0].BaselineWorkspace!.LocalCheckpoints.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that Moxfield refresh preserves workspace identity while staying local-only.
    /// </summary>
    [Fact]
    public async Task RefreshWorkspaceFromSourceAsync_RefreshesMoxfieldInPlaceAsLocalOnly()
    {
        InMemoryRepository repository = new();
        FakeMoxfieldGateway moxfield = new()
        {
            ImportedDeck = CreateImportedMoxfieldWorkspace(),
        };
        DeckWorkspaceService service = new(
            repository,
            new FakeCardCatalog(),
            archidektGateway: null,
            moxfieldGateway: moxfield);
        DeckWorkspace current = await service.StartDeckWorkspaceAsync(
            "moxfield",
            name: null,
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: null,
            moxfieldDeckIdOrUrl: "mox-1",
            writeBack: null,
            decklist: null,
            TestContext.Current.CancellationToken);
        DeckWorkspace remote = CreateImportedMoxfieldWorkspace();
        remote.Cards.Add(new DeckCard
        {
            Name = "Cultivate",
            Quantity = 1,
            PrimaryCategory = DeckDefaults.Mainboard,
            Categories = [DeckDefaults.Mainboard],
            Snapshot = new CardSnapshot { TypeLine = "Sorcery" }
        });
        moxfield.ImportedDeck = remote;

        WorkspaceRefreshFromSourceResult result = await service.RefreshWorkspaceFromSourceAsync(
            current.Id,
            writeBack: true,
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(WorkspaceRefreshFromSourceStatus.Refreshed);
        result.Workspace!.Id.Should().Be(current.Id);
        result.Workspace.Mode.Should().Be(WorkspaceMode.Local);
        result.Workspace.WriteBack.Should().BeFalse();
        result.Workspace.Cards.Should().Contain(card => card.Name == "Cultivate");
        result.DiffLastImport!.Status.Should().Be(WorkspaceDiffLastImportStatus.BaselineFound);
        moxfield.ImportRequests.Should().Equal("mox-1", "mox-1");
    }

    /// <summary>
    /// Verifies that refresh reports explicit statuses for missing, unsupported, and unavailable sources.
    /// </summary>
    [Fact]
    public async Task RefreshWorkspaceFromSourceAsync_ReturnsExplicitFailureStatuses()
    {
        InMemoryRepository repository = new();
        DeckWorkspace noSource = await repository.SaveAsync(new DeckWorkspace { Id = "no-source" }, TestContext.Current.CancellationToken);
        DeckWorkspace unsupported = await repository.SaveAsync(new DeckWorkspace
        {
            Id = "unsupported",
            SourceReferences =
            [
                new DeckSourceReference { Provider = "other", ExternalId = "deck-1" }
            ]
        }, TestContext.Current.CancellationToken);
        DeckWorkspace unavailable = CreateDiffWorkspace("unavailable-refresh");
        unavailable.Mode = WorkspaceMode.Archidekt;
        unavailable.ArchidektDeckId = "23097041";
        unavailable = await repository.SaveAsync(unavailable, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(
            repository,
            new FakeCardCatalog(),
            new FakeArchidektGateway { ThrowOnImport = true });

        WorkspaceRefreshFromSourceResult sourceMissing = await service.RefreshWorkspaceFromSourceAsync(
            noSource.Id,
            null,
            TestContext.Current.CancellationToken);
        WorkspaceRefreshFromSourceResult unsupportedSource = await service.RefreshWorkspaceFromSourceAsync(
            unsupported.Id,
            null,
            TestContext.Current.CancellationToken);
        WorkspaceRefreshFromSourceResult unavailableSource = await service.RefreshWorkspaceFromSourceAsync(
            unavailable.Id,
            null,
            TestContext.Current.CancellationToken);

        sourceMissing.Status.Should().Be(WorkspaceRefreshFromSourceStatus.WorkspaceHasNoSource);
        unsupportedSource.Status.Should().Be(WorkspaceRefreshFromSourceStatus.SourceUnsupported);
        unavailableSource.Status.Should().Be(WorkspaceRefreshFromSourceStatus.SourceUnavailable);
    }

    /// <summary>
    /// Verifies that imported card snapshots feed analysis and validation.
    /// </summary>
    [Fact]
    public async Task ImportedDecklist_AnalyzesAndValidatesCardSnapshots()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckWorkspace imported = await service.ImportDecklistAsync(
            CreateDeckTextComponentDecklist(),
            "Imported",
            "modern",
            TestContext.Current.CancellationToken
        );
        DeckAnalysis analysis = await service.AnalyzeDeckAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        DeckValidationResult validation = await service.ValidateDeckAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );

        analysis.TotalCards.Should().Be(5);
        analysis.IncludedCards.Should().Be(3);
        analysis.TypeCounts["Instant"].Should().Be(2);
        analysis.TypeCounts["Artifact"].Should().Be(1);
        analysis.TypeCounts.Should().NotContainKey("Land");
        analysis.ManaCurve["1"].Should().Be(3);
        analysis.ColorIdentityCounts["R"].Should().Be(2);
        analysis
            .Notes.Should()
            .NotContain(note => note.Contains("Missing Card", StringComparison.OrdinalIgnoreCase));
        validation
            .Errors.Should()
            .Contain(error => error.Contains("60", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that legality audits report Commander legality, color, copy, sideboard, and metadata issues.
    /// </summary>
    [Fact]
    public async Task ValidateLegalityAsync_ReportsStructuredCommanderFindings()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = CreateLegalityAuditWorkspace();
        await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckLegalityAudit audit = await service.ValidateLegalityAsync(
            workspace.Id,
            includeExcluded: false,
            TestContext.Current.CancellationToken);

        audit.IsLegal.Should().BeFalse();
        audit.CommandZone.DisplayName.Should().Be("Partner One // Partner Two");
        audit.CommandZone.HasPartnerPair.Should().BeTrue();
        audit.CommandZone.ColorIdentity.Should().Equal("W", "B");
        audit.Warnings.Should().Contain(warning => warning.Contains("exactly 100", StringComparison.OrdinalIgnoreCase));
        audit.CardLegalityIssues.Should().ContainSingle(issue => issue.CardName == "Banned Spell");
        audit.ColorIdentityIssues.Should().ContainSingle(issue => issue.CardName == "Blue Spell");
        audit.CopyLimitIssues.Should().ContainSingle(issue => issue.CardName == "Duplicate Spell");
        audit.SideboardIssues.Should().Contain(issue => issue.Severity == "error" && issue.Quantity == 16);
        audit.MetadataGaps.Should().Contain(issue =>
            issue.CardName == "Mystery Card"
            && issue.Message.Contains("type line", StringComparison.OrdinalIgnoreCase));
        audit.MetadataGaps.Should().Contain(issue =>
            issue.CardName == "Mystery Card"
            && issue.Message.Contains("legality", StringComparison.OrdinalIgnoreCase));
        audit.CardLegalityIssues.Should().NotContain(issue => issue.CardName == "Illegal Sideboard Card");
    }

    /// <summary>
    /// Verifies that excluded cards are audited only when requested.
    /// </summary>
    [Fact]
    public async Task ValidateLegalityAsync_IncludeExcludedControlsCardLevelAudit()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = CreateLegalityAuditWorkspace();
        await repository.SaveAsync(workspace, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckLegalityAudit includedOnly = await service.ValidateLegalityAsync(
            workspace.Id,
            includeExcluded: false,
            TestContext.Current.CancellationToken);
        DeckLegalityAudit withExcluded = await service.ValidateLegalityAsync(
            workspace.Id,
            includeExcluded: true,
            TestContext.Current.CancellationToken);

        includedOnly.CardLegalityIssues.Should().NotContain(issue => issue.CardName == "Illegal Sideboard Card");
        withExcluded.CardLegalityIssues.Should().Contain(issue => issue.CardName == "Illegal Sideboard Card");
        withExcluded.AuditedCardRows.Should().BeGreaterThan(includedOnly.AuditedCardRows);
    }

    /// <summary>
    /// Verifies that full decklist imports use one batched metadata refresh instead of per-card mutation lookups.
    /// </summary>
    [Fact]
    public async Task ImportDecklist_UsesBatchedMetadataRefresh()
    {
        InMemoryRepository repository = new();
        FakeCardCatalog catalog = new();
        DeckWorkspaceService service = new(repository, catalog);

        DeckWorkspace imported = await service.ImportDecklistAsync(
            CreateDeckTextComponentDecklist(),
            "Imported",
            "modern",
            TestContext.Current.CancellationToken);

        catalog.SingleLookupRequests.Should().Be(0);
        catalog.BatchLookupRequests.Should().Be(1);
        imported.Cards.Single(card => card.Name == "Lightning Bolt").Snapshot.ManaValue.Should().Be(1);
        imported.Cards.Single(card => card.Name == "Missing Card").Snapshot.TypeLine.Should().BeNull();
    }

    /// <summary>
    /// Verifies that deck summaries and resources expose imported workspace details.
    /// </summary>
    [Fact]
    public async Task ImportedDecklist_SummaryAndResourceExposeWorkspaceDetails()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckWorkspace imported = await service.ImportDecklistAsync(
            CreateDeckTextComponentDecklist(),
            "Imported",
            "modern",
            TestContext.Current.CancellationToken
        );
        object summary = await service.GetDeckSummaryAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace resource = await service.GetDeckResourceAsync(
            imported.Id,
            TestContext.Current.CancellationToken
        );
        using JsonDocument summaryJson = JsonSerializer.SerializeToDocument(summary);

        summary.Should().NotBeNull();
        summaryJson.RootElement.GetProperty("Name").GetString().Should().Be("Imported");
        summaryJson.RootElement.GetProperty("Format").GetString().Should().Be("modern");
        summaryJson.RootElement.GetProperty("Persistence").GetString().Should().Be("local-only");
        summaryJson.RootElement.GetProperty("TotalCards").GetInt32().Should().Be(5);
        resource.Name.Should().Be("Imported");
        resource
            .Cards.Single(card => card.Name == "Lightning Bolt")
            .Snapshot.ManaValue.Should()
            .Be(1);
    }

    /// <summary>
    /// Verifies that analyzer uses legacy metadata fallback when snapshot is null.
    /// </summary>
    [Fact]
    public void Analyzer_UsesLegacyMetadataFallbackWhenSnapshotIsNull()
    {
        DeckWorkspace deck = new()
        {
            Format = "modern",
            Cards =
            [
                new DeckCard
                {
                    Name = "Legacy Bolt",
                    Quantity = 2,
                    Snapshot = null!,
                    Metadata =
                    {
                        ["typeLine"] = "Instant",
                        ["manaValue"] = "1",
                        ["colorIdentity"] = "R",
                    },
                },
            ],
        };

        DeckAnalysis analysis = DeckAnalyzer.Analyze(deck);

        analysis.TypeCounts["Instant"].Should().Be(2);
        analysis.ManaCurve["1"].Should().Be(2);
        analysis.ColorIdentityCounts["R"].Should().Be(2);
        analysis.Notes.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that functional analysis ignores cards whose primary category is excluded.
    /// </summary>
    [Fact]
    public void Analyzer_UsesIncludedCardsForFunctionalCounts()
    {
        DeckWorkspace deck = new()
        {
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Active Removal",
                    Quantity = 2,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Instant",
                        ManaValue = 2,
                        OracleText = "Destroy target creature.",
                        ColorIdentity = ["B"],
                    },
                },
                new DeckCard
                {
                    Name = "Maybeboard Finisher",
                    Quantity = 3,
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckDefaults.Maybeboard, DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        ManaValue = 7,
                        OracleText = "Each opponent loses X life.",
                        ColorIdentity = ["B"],
                    },
                },
            ],
        };

        DeckAnalysis analysis = DeckAnalyzer.Analyze(deck);

        analysis.TotalCards.Should().Be(5);
        analysis.IncludedCards.Should().Be(2);
        analysis.CategoryCounts[DeckDefaults.Mainboard].Should().Be(2);
        analysis.CategoryCounts[DeckDefaults.Maybeboard].Should().Be(3);
        analysis.IncludedCategoryCounts.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, int>(DeckDefaults.Mainboard, 2));
        analysis.RoleCounts.Should().ContainKey(DeckRoles.Interaction);
        analysis.RoleCounts.Should().NotContainKey(DeckRoles.Wincons);
        analysis.TagCounts.Should().NotContainKey(DeckTags.Finishers);
        analysis.TypeCounts.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, int>("Instant", 2));
        analysis.ManaCurve.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, int>("2", 2));
        analysis.ColorIdentityCounts.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, int>("B", 2));
    }

    /// <summary>
    /// Verifies that analyzer and validator share inclusion rules for unknown categories.
    /// </summary>
    [Fact]
    public void AnalyzerAndValidator_TreatUnknownPrimaryCategoriesAsIncluded()
    {
        DeckWorkspace deck = new()
        {
            Format = "modern",
            Cards =
            [
                new DeckCard
                {
                    Name = "Custom Card",
                    Quantity = 60,
                    PrimaryCategory = "Custom",
                    Categories = ["Custom"],
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 1 },
                },
            ],
        };

        DeckAnalysis analysis = DeckAnalyzer.Analyze(deck);
        DeckValidationResult validation = DeckValidator.Validate(deck);

        analysis.IncludedCards.Should().Be(60);
        validation.Errors.Should().NotContain(error =>
            error.Contains("60", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that category mutations update cards and catalog.
    /// </summary>
    [Fact]
    public async Task CategoryMutations_UpdateCardsAndCatalog()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Sol Ring",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );

        await service.CreateCategoryAsync(
            deck.Id,
            " Ramp ",
            includedInDeck: true,
            includedInPrice: false,
            TestContext.Current.CancellationToken
        );
        await service.AddCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Ramp",
            TestContext.Current.CancellationToken
        );
        await service.SetPrimaryCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Ramp",
            TestContext.Current.CancellationToken
        );
        await service.RenameCategoryAsync(
            deck.Id,
            "Ramp",
            "Acceleration",
            TestContext.Current.CancellationToken
        );
        await service.RemoveCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Acceleration",
            TestContext.Current.CancellationToken
        );
        await service.DeleteCategoryAsync(
            deck.Id,
            "Acceleration",
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        );

        opened.Categories.Should().NotContain(category => category.Name == "Acceleration");
        opened.Cards.Should().ContainSingle();
        opened.Cards[0].PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        opened.Cards[0].Categories.Should().Contain(DeckDefaults.Mainboard);
    }

    /// <summary>
    /// Verifies that deleting a category only retags cards that used that category.
    /// </summary>
    [Fact]
    public async Task DeleteCategory_OnlyAppliesReplacementToAffectedCards()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Sol Ring",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        await service.CreateCategoryAsync(
            deck.Id,
            "Ramp",
            includedInDeck: true,
            includedInPrice: true,
            TestContext.Current.CancellationToken
        );
        await service.SetPrimaryCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Ramp",
            TestContext.Current.CancellationToken
        );

        await service.DeleteCategoryAsync(
            deck.Id,
            "Ramp",
            DeckDefaults.Sideboard,
            TestContext.Current.CancellationToken
        );

        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        );
        DeckCard affected = opened.Cards.Single(card => card.Name == "Sol Ring");
        DeckCard unaffected = opened.Cards.Single(card => card.Name == "Lightning Bolt");

        affected.PrimaryCategory.Should().Be(DeckDefaults.Sideboard);
        affected.Categories.Should().NotContain("Ramp");
        affected.Categories.Should().Contain(DeckDefaults.Sideboard);
        unaffected.PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        unaffected.Categories.Should().NotContain(DeckDefaults.Sideboard);
    }

    /// <summary>
    /// Verifies that deleting a secondary category keeps affected cards grouped under the replacement.
    /// </summary>
    [Fact]
    public async Task DeleteCategory_ReplacesSecondaryCategoryTags()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Brew",
            "commander",
            null,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Sol Ring",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        await service.AddCardAsync(
            deck.Id,
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );
        await service.AddCardCategoryAsync(
            deck.Id,
            "Sol Ring",
            "Testing",
            TestContext.Current.CancellationToken
        );

        await service.DeleteCategoryAsync(
            deck.Id,
            "Testing",
            DeckDefaults.Sideboard,
            TestContext.Current.CancellationToken
        );

        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken
        );
        DeckCard affected = opened.Cards.Single(card => card.Name == "Sol Ring");
        DeckCard unaffected = opened.Cards.Single(card => card.Name == "Lightning Bolt");

        affected.PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        affected.Categories.Should().Equal(DeckDefaults.Mainboard, DeckDefaults.Sideboard);
        unaffected.Categories.Should().Equal(DeckDefaults.Mainboard);
    }

    /// <summary>
    /// Verifies that commander validation flags non basic duplicates.
    /// </summary>
    [Fact]
    public void CommanderValidation_FlagsNonBasicDuplicates()
    {
        DeckWorkspace deck = new()
        {
            Format = "commander",
            Cards =
            [
                new DeckCard { Name = "Lightning Bolt", Quantity = 2 },
                new DeckCard { Name = "Island", Quantity = 20 },
            ],
        };

        DeckValidationResult result = DeckValidator.Validate(deck);

        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .Contain(error => error.Contains("Lightning Bolt", StringComparison.OrdinalIgnoreCase));
        result
            .Warnings.Should()
            .Contain(warning => warning.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that Commander validation allows repeated cards whose type line marks them as basic lands.
    /// </summary>
    [Fact]
    public void CommanderValidation_AllowsRepeatedBasicsByTypeLine()
    {
        DeckWorkspace deck = new()
        {
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Swamp",
                    Quantity = 5,
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land - Swamp" },
                },
                new DeckCard
                {
                    Name = "Mountain",
                    Quantity = 4,
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land - Mountain" },
                },
                new DeckCard
                {
                    Name = "Plains",
                    Quantity = 3,
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land - Plains" },
                },
                new DeckCard
                {
                    Name = "Foggy Bottom Swamp",
                    Quantity = 2,
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land - Swamp" },
                },
            ],
        };

        DeckValidationResult result = DeckValidator.Validate(deck);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain(warning => warning.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a requested plain basic land keeps its canonical display name.
    /// </summary>
    [Fact]
    public async Task AddCard_PreservesRequestedBasicNameWhenCatalogReturnsNoveltyName()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(
            repository,
            new FakeCardCatalog { ReturnNoveltySwampName = true });
        DeckWorkspace deck = await service.CreateLocalDeckAsync(
            "Basics",
            "commander",
            null,
            TestContext.Current.CancellationToken);

        DeckChangeResult result = await service.AddCardAsync(
            deck.Id,
            "Swamp",
            5,
            DeckRoles.Lands,
            TestContext.Current.CancellationToken);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(
            deck.Id,
            TestContext.Current.CancellationToken);

        DeckCard swamp = opened.Cards.Should().ContainSingle().Which;
        swamp.Name.Should().Be("Swamp");
        swamp.Quantity.Should().Be(5);
        swamp.Snapshot.TypeLine.Should().Be("Basic Land - Swamp");
        result.Message.Should().Contain("Added 5 Swamp");
        DeckValidator.Validate(opened).Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that commander singleton validation ignores non-included cards.
    /// </summary>
    [Fact]
    public void CommanderValidation_IgnoresMaybeboardDuplicates()
    {
        DeckWorkspace deck = new()
        {
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                },
                new DeckCard
                {
                    Name = "Arwen, Mortal Queen",
                    Quantity = 2,
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckDefaults.Maybeboard],
                },
            ],
        };

        DeckValidationResult result = DeckValidator.Validate(deck);

        result.Errors.Should().BeEmpty();
        result
            .Warnings.Should()
            .Contain(warning => warning.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that parser reports invalid lines and normalizes aliases.
    /// </summary>
    [Fact]
    public void Parser_ReportsInvalidLinesAndNormalizesAliases()
    {
        const string decklist = """
            Maybeboard
            1 Brainstorm (STA) 13
            this is not a card line

            Deck:
            0 Bad Quantity
            1 Counterspell
            """;

        ParsedDecklist parsed = DeckWorkspaceService.ParseDecklist(decklist);

        parsed.Cards.Should().HaveCount(2);
        parsed.Cards[0].Name.Should().Be("Brainstorm");
        parsed.Cards[0].Category.Should().Be(DeckDefaults.Maybeboard);
        parsed.Cards[1].Category.Should().Be(DeckDefaults.Mainboard);
        parsed
            .Warnings.Should()
            .Contain(warning => warning.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that archidekt bound mutation rebases then persists writeback.
    /// </summary>
    [Fact]
    public async Task ArchidektBoundMutation_RebasesThenPersistsWriteback()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new();
        DeckWorkspace remoteDeck = new()
        {
            Id = "remote",
            Name = "Remote",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    ArchidektCardId = "10",
                    ArchidektDeckRelationId = 99,
                },
            ],
        };
        archidekt.ImportedDeck = remoteDeck;

        DeckWorkspace cached = new()
        {
            Id = "workspace",
            Name = "Cached",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        await repository.SaveAsync(cached, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        DeckChangeResult result = await service.AddCardAsync(
            "workspace",
            "Lightning Bolt",
            1,
            DeckDefaults.Mainboard,
            TestContext.Current.CancellationToken
        );

        result.Persistence.Should().Be(DeckPersistence.ArchidektWriteBack);
        archidekt.ImportedDeckRequests.Should().Be(1);
        archidekt.UpsertedCards.Should().ContainSingle(card => card.Name == "Lightning Bolt");
        DeckWorkspace saved = await service.OpenLocalDeckAsync(
            "workspace",
            TestContext.Current.CancellationToken
        );
        saved.Name.Should().Be("Remote");
        saved.Cards.Should().Contain(card => card.Name == "Sol Ring");
        saved.Cards.Should().Contain(card => card.Name == "Lightning Bolt");
    }

    /// <summary>
    /// Verifies that Archidekt mutation rebases preserve rich Scryfall snapshots for exact printing matches.
    /// </summary>
    [Fact]
    public async Task ArchidektBoundMutation_PreservesEnrichedSnapshotForExactPrintingMatch()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new();
        DeckWorkspace remoteDeck = new()
        {
            Id = "remote",
            Name = "Remote",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Cards =
            [
                new DeckCard
                {
                    Name = "Command Tower",
                    Quantity = 1,
                    ScryfallId = "command-tower-print",
                    ScryfallOracleId = "oracle-command-tower",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    ArchidektCardId = "10",
                    ArchidektDeckRelationId = 99,
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        Provenance = new CardSnapshotProvenance
                        {
                            Provider = DeckImportProviders.Archidekt,
                            ProviderCardId = "10",
                            SchemaVersion = 1,
                            RefreshedAtUtc = DateTimeOffset.UtcNow,
                        },
                    },
                },
            ],
        };
        archidekt.ImportedDeck = remoteDeck;
        DeckWorkspace cached = new()
        {
            Id = "workspace",
            Name = "Cached",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Cards =
            [
                new DeckCard
                {
                    Name = "Command Tower",
                    Quantity = 1,
                    ScryfallId = "command-tower-print",
                    ScryfallOracleId = "oracle-command-tower",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = RichCommandTowerSnapshot("command-tower-print", "sld", "1"),
                },
            ],
        };
        await repository.SaveAsync(cached, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        DeckChangeResult result = await service.AddCardCategoryAsync(
            "workspace",
            "Command Tower",
            "Fixing",
            TestContext.Current.CancellationToken);

        DeckCard card = result.Workspace.Cards.Single(card => card.Name == "Command Tower");
        card.Snapshot.Provenance.Provider.Should().Be("scryfall");
        card.Snapshot.ProducedMana.Should().BeEquivalentTo(["W", "U", "B", "R", "G"]);
        card.Snapshot.ScryfallUri.Should().Be("https://scryfall.test/card/command-tower-print");
        card.Snapshot.Set.Should().Be("sld");
        card.Snapshot.Prices["usd"].Should().Be("1.00");
    }

    /// <summary>
    /// Verifies that oracle fallback preservation does not copy printing-specific fields.
    /// </summary>
    [Fact]
    public async Task ArchidektBoundMutation_OracleFallbackPreservesOnlyOracleLevelSnapshotFields()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new();
        DeckWorkspace remoteDeck = new()
        {
            Id = "remote",
            Name = "Remote",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Cards =
            [
                new DeckCard
                {
                    Name = "Command Tower",
                    Quantity = 1,
                    ScryfallId = "new-command-tower-print",
                    ScryfallOracleId = "oracle-command-tower",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    ArchidektCardId = "10",
                    ArchidektDeckRelationId = 99,
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Land",
                        Set = "otc",
                        CollectorNumber = "2",
                        Language = "ja",
                        ReleasedAt = new DateOnly(2023, 5, 5),
                        ScryfallUri = "https://scryfall.test/card/new-command-tower-print",
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["usd"] = "9.99",
                        },
                        Provenance = new CardSnapshotProvenance
                        {
                            Provider = DeckImportProviders.Archidekt,
                            ProviderCardId = "10",
                            SchemaVersion = 1,
                            RefreshedAtUtc = DateTimeOffset.UtcNow,
                        },
                    },
                },
            ],
        };
        archidekt.ImportedDeck = remoteDeck;
        DeckWorkspace cached = new()
        {
            Id = "workspace",
            Name = "Cached",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Cards =
            [
                new DeckCard
                {
                    Name = "Command Tower",
                    Quantity = 1,
                    ScryfallId = "old-command-tower-print",
                    ScryfallOracleId = "oracle-command-tower",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = RichCommandTowerSnapshot("old-command-tower-print", "sld", "1"),
                },
            ],
        };
        await repository.SaveAsync(cached, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        DeckChangeResult result = await service.AddCardCategoryAsync(
            "workspace",
            "Command Tower",
            "Fixing",
            TestContext.Current.CancellationToken);

        DeckCard card = result.Workspace.Cards.Single(card => card.Name == "Command Tower");
        card.Snapshot.ProducedMana.Should().BeEquivalentTo(["W", "U", "B", "R", "G"]);
        card.Snapshot.OracleText.Should().Contain("Add one mana");
        card.Snapshot.Set.Should().Be("otc");
        card.Snapshot.CollectorNumber.Should().Be("2");
        card.Snapshot.Language.Should().Be("ja");
        card.Snapshot.ScryfallUri.Should().Be("https://scryfall.test/card/new-command-tower-print");
        card.Snapshot.Prices["usd"].Should().Be("9.99");
        card.Snapshot.Provenance.Provider.Should().Be(DeckImportProviders.Archidekt);
    }

    /// <summary>
    /// Verifies that Archidekt deck listing and opening delegate to the gateway.
    /// </summary>
    [Fact]
    public async Task ArchidektDeckOperations_ListAndOpenDecks()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote",
                Name = "Remote",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
            },
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        IReadOnlyList<ArchidektDeckSummary> deckSummaries = await service.ListArchidektDecksAsync(
            TestContext.Current.CancellationToken
        );
        DeckWorkspace opened = await service.OpenArchidektDeckAsync(
            "https://archidekt.com/decks/123/deck",
            writeBack: true,
            TestContext.Current.CancellationToken
        );

        deckSummaries.Should().ContainSingle(summary => summary.Id == "123");
        opened.Mode.Should().Be(WorkspaceMode.Archidekt);
        opened.WriteBack.Should().BeTrue();
        opened.ArchidektDeckId.Should().Be("123");
    }

    /// <summary>
    /// Verifies that folder-name deck listing resolves through folder ids before filtering decks.
    /// </summary>
    [Fact]
    public async Task ArchidektDeckOperations_ListDecksResolvesFolderNameToFolderId()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new()
        {
            Folders =
            [
                new ArchidektFolder { Id = "folder-llm", Name = "LLM" },
            ],
            DeckSummaries =
            [
                new ArchidektDeckSummary { Id = "1", Name = "Aurelia", FolderId = "folder-llm" },
                new ArchidektDeckSummary { Id = "2", Name = "Other", FolderId = "folder-other" },
            ],
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        IReadOnlyList<ArchidektDeckSummary> deckSummaries = await service.ListArchidektDecksAsync(
            new ArchidektDeckListRequest { FolderName = "LLM" },
            TestContext.Current.CancellationToken);

        deckSummaries.Should().ContainSingle().Which.Name.Should().Be("Aurelia");
    }

    /// <summary>
    /// Verifies that Archidekt metadata updates persist through the gateway.
    /// </summary>
    [Fact]
    public async Task ArchidektMetadataOperations_PersistUpdatedDeckMetadata()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote",
                Name = "Remote",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
            },
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        DeckWorkspace opened = await service.OpenArchidektDeckAsync(
            "https://archidekt.com/decks/123/deck",
            writeBack: true,
            TestContext.Current.CancellationToken
        );
        DeckChangeResult metadata = await service.UpdateDeckMetadataAsync(
            opened.Id,
            "Renamed",
            "legacy",
            "Updated",
            TestContext.Current.CancellationToken
        );

        metadata.Workspace.Name.Should().Be("Renamed");
        metadata.Workspace.Format.Should().Be("legacy");
        metadata.Workspace.Description.Should().Be("Updated");
        archidekt.PersistedMetadataRequests.Should().Be(1);
    }

    /// <summary>
    /// Verifies that Archidekt checkpoint operations delegate to the gateway.
    /// </summary>
    [Fact]
    public async Task ArchidektCheckpointOperations_DelegateToGateway()
    {
        InMemoryRepository repository = new();
        FakeArchidektGateway archidekt = new()
        {
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote",
                Name = "Remote",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
            },
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        DeckWorkspace opened = await service.OpenArchidektDeckAsync(
            "https://archidekt.com/decks/123/deck",
            writeBack: true,
            TestContext.Current.CancellationToken
        );
        DeckCheckpoint created = await service.CheckpointDeckAsync(
            opened.Id,
            "Before",
            "baseline",
            TestContext.Current.CancellationToken
        );
        IReadOnlyList<DeckCheckpoint> listed = await service.ListDeckCheckpointsAsync(
            opened.Id,
            TestContext.Current.CancellationToken
        );
        DeckCheckpoint fetched = await service.GetDeckCheckpointAsync(
            opened.Id,
            "7",
            TestContext.Current.CancellationToken
        );
        DeckCheckpoint renamed = await service.RenameDeckCheckpointAsync(
            opened.Id,
            "7",
            "After",
            null,
            TestContext.Current.CancellationToken
        );
        await service.DeleteDeckCheckpointAsync(
            opened.Id,
            "7",
            TestContext.Current.CancellationToken
        );

        created.Name.Should().Be("Before");
        listed.Should().ContainSingle(checkpoint => checkpoint.Id == "7");
        fetched.Id.Should().Be("7");
        renamed.Name.Should().Be("After");
        archidekt.DeletedCheckpointIds.Should().ContainSingle().Which.Should().Be("7");
    }

    /// <summary>
    /// Verifies that json repository cancelled save keeps existing workspace.
    /// </summary>
    [Fact]
    public async Task JsonRepository_CancelledSaveKeepsExistingWorkspace()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            JsonDeckWorkspaceRepository repository = new(dataDirectory);
            DeckWorkspace original = new() { Id = "workspace", Name = "Original" };
            await repository.SaveAsync(original, TestContext.Current.CancellationToken);

            DeckWorkspace changed = new() { Id = original.Id, Name = "Changed" };
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();

            Func<Task> act = () => repository.SaveAsync(changed, cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            DeckWorkspace? loaded = await repository.GetAsync(
                original.Id,
                TestContext.Current.CancellationToken
            );
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Original");
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that json repository lists saved workspaces and rejects invalid ids.
    /// </summary>
    [Fact]
    public async Task JsonRepository_ListsSavedWorkspacesAndRejectsInvalidIds()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            JsonDeckWorkspaceRepository repository = new(dataDirectory);
            await repository.SaveAsync(
                new DeckWorkspace { Id = "one", Name = "One" },
                TestContext.Current.CancellationToken
            );
            await repository.SaveAsync(
                new DeckWorkspace { Id = "two", Name = "Two" },
                TestContext.Current.CancellationToken
            );

            IReadOnlyList<DeckWorkspace> workspaces = await repository.ListAsync(
                TestContext.Current.CancellationToken
            );
            Func<Task> invalidSave = () =>
                repository.SaveAsync(
                    new DeckWorkspace { Id = "!!!" },
                    TestContext.Current.CancellationToken
                );

            workspaces.Select(workspace => workspace.Name).Should().BeEquivalentTo(["One", "Two"]);
            await invalidSave.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that secret redactor redacts string and json values.
    /// </summary>
    [Fact]
    public void SecretRedactor_RedactsStringAndJsonValues()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        const string longToken = "abcDEF1234567890abcDEF1234567890abcDEF12";

        SecretRedactor.Redact("authorization: Bearer short-secret")
            .Should()
            .Be("authorization: Bearer ***REDACTED***");
        SecretRedactor.Redact("authorization: JWT login-jwt")
            .Should()
            .Be("authorization: JWT ***REDACTED***");
        SecretRedactor.Redact($"raw jwt {jwt}")
            .Should()
            .Be("raw jwt ***REDACTED***");
        SecretRedactor.Redact($"api token {longToken}")
            .Should()
            .Be("api token ***REDACTED***");
        SecretRedactor.Redact("https://user:password@example.test/decks")
            .Should()
            .Be("https://***REDACTED***@example.test/decks");
        SecretRedactor.Redact("challenge token expired").Should().Be("challenge token expired");
        SecretRedactor.Redact("ordinary text").Should().Be("ordinary text");

        using JsonDocument document = JsonDocument.Parse(
            """{ "jwt": "secret", "nested": { "name": "deck" }, "count": 3 }"""
        );
        using JsonDocument redacted = SecretRedactor.Redact(document);

        redacted.RootElement.GetProperty("jwt").GetString().Should().Be("***REDACTED***");
        redacted
            .RootElement.GetProperty("nested")
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("deck");
        redacted.RootElement.GetProperty("count").GetInt64().Should().Be(3);

        string redactedBody = SecretRedactor.Redact(
            """{ "token": "secret-token", "message": "token expired", "authorization": "Bearer short-secret" }"""
        );
        using JsonDocument redactedBodyDocument = JsonDocument.Parse(redactedBody);
        redactedBodyDocument.RootElement.GetProperty("token").GetString().Should().Be("***REDACTED***");
        redactedBodyDocument.RootElement.GetProperty("message").GetString().Should().Be("token expired");
        redactedBodyDocument.RootElement.GetProperty("authorization").GetString().Should().Be("***REDACTED***");
    }

    /// <summary>
    /// Creates a decklist that exercises mainboard, sideboard, and maybeboard parsing.
    /// </summary>
    private static string CreateDeckTextComponentDecklist()
    {
        return """
            Mainboard
            2 Lightning Bolt
            1 Sol Ring

            Sideboard
            1 Island

            // Maybeboard
            1 Missing Card
            """;
    }

    /// <summary>
    /// Creates an enriched Scryfall-style Command Tower snapshot for metadata preservation tests.
    /// </summary>
    private static CardSnapshot RichCommandTowerSnapshot(
        string providerCardId,
        string setCode,
        string collectorNumber)
    {
        return new CardSnapshot
        {
            TypeLine = "Land",
            OracleText = "{T}: Add one mana of any color in your commander's color identity.",
            ProducedMana = ["W", "U", "B", "R", "G"],
            Set = setCode,
            CollectorNumber = collectorNumber,
            Language = "en",
            ReleasedAt = new DateOnly(2022, 1, 1),
            ScryfallUri = $"https://scryfall.test/card/{providerCardId}",
            SelectedPrintingReason = "test fixture",
            PricingMode = nameof(PricingMode.CheapestReleasedPaper),
            Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["commander"] = "legal",
            },
            Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["usd"] = "1.00",
            },
            Provenance = new CardSnapshotProvenance
            {
                Provider = "scryfall",
                ProviderCardId = providerCardId,
                SchemaVersion = 1,
                RefreshedAtUtc = DateTimeOffset.UtcNow,
            },
        };
    }

    /// <summary>
    /// Creates a provider-neutral workspace shaped like a Moxfield import.
    /// </summary>
    private static DeckWorkspace CreateImportedMoxfieldWorkspace()
    {
        return new DeckWorkspace
        {
            Id = $"moxfield-{Guid.NewGuid():N}",
            Name = "Imported Moxfield",
            Format = "commander",
            Mode = WorkspaceMode.Local,
            WriteBack = false,
            SourceReferences =
            [
                new DeckSourceReference
                {
                    Provider = DeckImportProviders.Moxfield,
                    ExternalId = "mox-1",
                    Url = "https://www.moxfield.com/decks/mox-1",
                },
            ],
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
                new DeckCategory { Name = "Ramp", IncludedInDeck = false },
                new DeckCategory { Name = "Card Draw", IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Atraxa, Praetors' Voice",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    ScryfallId = "scryfall-atraxa",
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        ColorIdentity = ["W", "U", "B", "G"],
                    },
                },
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard, "Ramp"],
                    ScryfallId = "scryfall-sol-ring",
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        Set = "cmm",
                        CollectorNumber = "400",
                    },
                },
                new DeckCard
                {
                    Name = "Brainstorm",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckDefaults.Maybeboard, "Card Draw"],
                    Snapshot = new CardSnapshot { TypeLine = "Instant" },
                },
            ],
        };
    }

    /// <summary>
    /// Creates a non-empty Archidekt destination used by replace safety tests.
    /// </summary>
    private static DeckWorkspace CreateExistingArchidektDestination()
    {
        return new DeckWorkspace
        {
            Id = "remote",
            Name = "Existing",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    ScryfallId = "scryfall-sol-ring",
                    ArchidektCardId = "500",
                    ArchidektDeckRelationId = 101,
                },
                new DeckCard
                {
                    Name = "Existing Card",
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    ArchidektCardId = "99",
                    ArchidektDeckRelationId = 100,
                },
            ],
        };
    }

    /// <summary>
    /// Creates a small pairable workspace for diff tests.
    /// </summary>
    private static DeckWorkspace CreateDiffWorkspace(string id)
    {
        return new DeckWorkspace
        {
            Id = id,
            Name = $"Diff {id}",
            Format = "commander",
            UpdatedAt = DateTimeOffset.Parse("2026-06-07T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            SourceReferences =
            [
                new DeckSourceReference
                {
                    Provider = DeckImportProviders.Archidekt,
                    ExternalId = "23097041",
                    Url = "https://archidekt.com/decks/23097041/inga_and_esika"
                }
            ],
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    ScryfallOracleId = "oracle-sol-ring",
                    Snapshot = new CardSnapshot { TypeLine = "Artifact", ScryfallUri = "https://scryfall.test/sol-ring" }
                },
                new DeckCard
                {
                    Name = "Counterspell",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    ScryfallOracleId = "oracle-counterspell",
                    Snapshot = new CardSnapshot { TypeLine = "Instant", ScryfallUri = "https://scryfall.test/counterspell" }
                },
                new DeckCard
                {
                    Name = "Brainstorm",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    ScryfallOracleId = "oracle-brainstorm",
                    Snapshot = new CardSnapshot { TypeLine = "Instant", ScryfallUri = "https://scryfall.test/brainstorm" }
                },
                new DeckCard
                {
                    Name = "Finale of Devastation",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Sideboard,
                    Categories = [DeckDefaults.Sideboard],
                    ScryfallOracleId = "oracle-finale",
                    Snapshot = new CardSnapshot { TypeLine = "Sorcery", ScryfallUri = "https://scryfall.test/finale" }
                }
            ]
        };
    }

    /// <summary>
    /// Creates a compact Commander deck with intentional legality audit findings.
    /// </summary>
    private static DeckWorkspace CreateLegalityAuditWorkspace()
    {
        return new DeckWorkspace
        {
            Id = "legality-audit",
            Name = "Legality Audit",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
            ],
            Cards =
            [
                LegalityCard("Partner One", 1, DeckRoles.Commander, "Legendary Creature", ["W"], "legal", "oracle-partner-one"),
                LegalityCard("Partner Two", 1, DeckRoles.Commander, "Legendary Creature", ["B"], "legal", "oracle-partner-two"),
                LegalityCard("Blue Spell", 1, DeckDefaults.Mainboard, "Instant", ["U"], "legal", "oracle-blue"),
                LegalityCard("Banned Spell", 1, DeckDefaults.Mainboard, "Sorcery", ["B"], "banned", "oracle-banned"),
                LegalityCard("Duplicate Spell", 1, DeckDefaults.Mainboard, "Instant", ["B"], "legal", "oracle-duplicate"),
                LegalityCard("Duplicate Spell", 1, DeckDefaults.Mainboard, "Instant", ["B"], "legal", "oracle-duplicate"),
                new DeckCard
                {
                    Name = "Mystery Card",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    ScryfallOracleId = "oracle-mystery",
                    Snapshot = new CardSnapshot()
                },
                LegalityCard("Illegal Sideboard Card", 16, DeckDefaults.Sideboard, "Instant", ["B"], "not_legal", "oracle-sideboard"),
            ]
        };
    }

    /// <summary>
    /// Creates one card row with cached Commander legality metadata.
    /// </summary>
    private static DeckCard LegalityCard(
        string name,
        int quantity,
        string category,
        string typeLine,
        List<string> colorIdentity,
        string commanderLegality,
        string oracleId)
    {
        return new DeckCard
        {
            Name = name,
            Quantity = quantity,
            PrimaryCategory = category,
            Categories = [category],
            ScryfallOracleId = oracleId,
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                ColorIdentity = colorIdentity,
                ScryfallUri = $"https://scryfall.test/{Uri.EscapeDataString(name)}",
                Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["commander"] = commanderLegality
                }
            }
        };
    }

    /// <summary>
    /// Creates a workspace with duplicate movable rows.
    /// </summary>
    private static DeckWorkspace CreateBulkMoveWorkspace()
    {
        return new DeckWorkspace
        {
            Id = $"bulk-{Guid.NewGuid():N}",
            Name = "Bulk Move",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Ramp Stone",
                    Quantity = 3,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    ScryfallId = "ramp-stone-print",
                    ScryfallOracleId = "ramp-stone-oracle",
                    ArchidektCardId = "500",
                    ArchidektDeckRelationId = 9001,
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        ScryfallUri = "https://scryfall.test/ramp-stone"
                    }
                },
                new DeckCard
                {
                    Name = "Sol Ring",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    ScryfallId = "sol-ring-print",
                    Snapshot = new CardSnapshot { TypeLine = "Artifact" }
                }
            ]
        };
    }

    /// <summary>
    /// Creates an Archidekt workspace with the migration marker used for retry detection.
    /// </summary>
    private static DeckWorkspace CreateMigrationDestination(
        DeckWorkspace source,
        string deckId,
        string name,
        bool includeCards
    )
    {
        return new DeckWorkspace
        {
            Id = $"archidekt-{Guid.NewGuid():N}",
            Name = name,
            Format = source.Format,
            Description = $"MTG MCP Migration Source: moxfield:mox-1; Workspace: {source.Id}",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = deckId,
            Categories = source.Categories.Select(CloneCategory).ToList(),
            Cards = includeCards
                ? source.Cards.Select(CloneCard).ToList()
                : [],
        };
    }

    /// <summary>
    /// Clones category fields needed by migration retry tests.
    /// </summary>
    private static DeckCategory CloneCategory(DeckCategory category)
    {
        return new DeckCategory
        {
            Name = category.Name,
            IncludedInDeck = category.IncludedInDeck,
            IncludedInPrice = category.IncludedInPrice,
            IsPremier = category.IsPremier,
        };
    }

    /// <summary>
    /// Clones card fields needed by migration retry tests.
    /// </summary>
    private static DeckCard CloneCard(DeckCard card)
    {
        return new DeckCard
        {
            Name = card.Name,
            Quantity = card.Quantity,
            PrimaryCategory = card.PrimaryCategory,
            Categories = card.Categories.ToList(),
            ScryfallId = card.ScryfallId,
            ScryfallOracleId = card.ScryfallOracleId,
            ArchidektCardId = card.ArchidektCardId,
            ArchidektDeckRelationId = card.ArchidektDeckRelationId,
            Modifier = card.Modifier,
            Companion = card.Companion,
            FlippedDefault = card.FlippedDefault,
            Snapshot = card.Snapshot,
            Metadata = new Dictionary<string, string>(card.Metadata, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Provides fake card catalog behavior.
    /// </summary>
    private sealed class FakeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Gets the single-card lookup request count.
        /// </summary>
        public int SingleLookupRequests { get; private set; }

        /// <summary>
        /// Gets the batched card lookup request count.
        /// </summary>
        public int BatchLookupRequests { get; private set; }

        /// <summary>
        /// Gets or sets whether single-card lookup should simulate caller cancellation.
        /// </summary>
        public bool CancelGetCard { get; init; }

        /// <summary>
        /// Gets or sets a token source cancelled after a batched card lookup succeeds.
        /// </summary>
        public CancellationTokenSource? CancelAfterBatchLookup { get; init; }

        /// <summary>
        /// Gets or sets whether Swamp lookup returns a novelty print display name.
        /// </summary>
        public bool ReturnNoveltySwampName { get; init; }

        /// <summary>
        /// Returns no fake search results.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no fake semantic search results.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns deterministic fake card metadata for workspace mutations.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            SingleLookupRequests++;
            return Task.FromResult(BuildCard(nameOrId));
        }

        /// <summary>
        /// Builds deterministic fake card metadata for a name.
        /// </summary>
        private CardInfo? BuildCard(string nameOrId)
        {
            if (CancelGetCard)
            {
                throw new TaskCanceledException("Caller cancelled card lookup.");
            }

            if (nameOrId.Contains("Missing", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            CardInfo card = new()
            {
                Id = $"scryfall-{nameOrId}",
                OracleId = $"oracle-{nameOrId}",
                Name = ReturnNoveltySwampName && nameOrId.Equals("Swamp", StringComparison.OrdinalIgnoreCase)
                    ? "Foggy Bottom Swamp"
                    : nameOrId,
                ManaValue = nameOrId.Contains("Island", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                TypeLine = GetTypeLine(nameOrId),
                ColorIdentity = nameOrId.Contains("Lightning", StringComparison.OrdinalIgnoreCase)
                    ? ["R"]
                    : [],
                Set = "tst",
                CollectorNumber = "1",
                ScryfallUri = $"https://scryfall.test/{Uri.EscapeDataString(nameOrId)}",
            };
            return card;
        }

        /// <summary>
        /// Returns fake metadata for each requested name that resolves.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken
        )
        {
            BatchLookupRequests++;
            Dictionary<string, CardInfo> cards = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                CardInfo? card = BuildCard(name);
                if (card is not null)
                {
                    cards[name] = card;
                }
            }

            CancelAfterBatchLookup?.Cancel();
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(cards);
        }

        /// <summary>
        /// Chooses a type line from the fixture card name.
        /// </summary>
        private static string GetTypeLine(string name)
        {
            if (name.Equals("Swamp", StringComparison.OrdinalIgnoreCase))
            {
                return "Basic Land - Swamp";
            }

            if (name.Contains("Island", StringComparison.OrdinalIgnoreCase))
            {
                return "Basic Land";
            }

            if (name.Contains("Sol Ring", StringComparison.OrdinalIgnoreCase))
            {
                return "Artifact";
            }

            return "Instant";
        }

        /// <summary>
        /// Returns no fake rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Returns no fake prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken
        )
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
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }

    /// <summary>
    /// Provides fake Moxfield import behavior.
    /// </summary>
    private sealed class FakeMoxfieldGateway : IMoxfieldGateway
    {
        /// <summary>
        /// Gets or sets the imported deck.
        /// </summary>
        public DeckWorkspace ImportedDeck { get; set; } = CreateImportedMoxfieldWorkspace();

        /// <summary>
        /// Gets or sets whether imports should fail.
        /// </summary>
        public bool ThrowOnImport { get; set; }

        /// <summary>
        /// Gets import requests in caller order.
        /// </summary>
        public List<string> ImportRequests { get; } = [];

        /// <summary>
        /// Imports a fake Moxfield deck.
        /// </summary>
        public Task<DeckWorkspace> ImportDeckAsync(
            string deckIdOrUrl,
            CancellationToken cancellationToken
        )
        {
            ImportRequests.Add(deckIdOrUrl);
            if (ThrowOnImport)
            {
                throw new InvalidOperationException("Moxfield source unavailable.");
            }

            return Task.FromResult(ImportedDeck);
        }
    }

    /// <summary>
    /// Provides in memory repository behavior.
    /// </summary>
    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Verifies that workspaces.
        /// </summary>
        private readonly Dictionary<string, DeckWorkspace> workspaces = new(
            StringComparer.OrdinalIgnoreCase
        );

        /// <summary>
        /// Gets the number of save requests made to the repository.
        /// </summary>
        public int SaveRequests { get; private set; }

        /// <summary>
        /// Saves a workspace in memory.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
            SaveRequests++;
            workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets a workspace by id from memory.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(
            string workspaceId,
            CancellationToken cancellationToken
        )
        {
            workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Verifies that list.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(workspaces.Values.ToList());
        }
    }

    /// <summary>
    /// Coordinates fake archidekt gateway HTTP operations.
    /// </summary>
    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        /// <summary>
        /// Gets or sets the imported deck.
        /// </summary>
        public DeckWorkspace ImportedDeck { get; set; } = new();

        /// <summary>
        /// Gets or sets whether imports should fail.
        /// </summary>
        public bool ThrowOnImport { get; set; }

        /// <summary>
        /// Gets or sets deck summaries returned by list requests.
        /// </summary>
        public IReadOnlyList<ArchidektDeckSummary> DeckSummaries { get; set; } =
        [
            new ArchidektDeckSummary { Id = "123", Name = "Remote" },
        ];

        /// <summary>
        /// Gets or sets folder summaries returned by folder list requests.
        /// </summary>
        public IReadOnlyList<ArchidektFolder> Folders { get; set; } = [];

        /// <summary>
        /// Gets or sets the created deck requests.
        /// </summary>
        public int CreatedDeckRequests { get; private set; }

        /// <summary>
        /// Gets or sets the imported deck requests.
        /// </summary>
        public int ImportedDeckRequests { get; private set; }

        /// <summary>
        /// Gets or sets the persisted metadata requests.
        /// </summary>
        public int PersistedMetadataRequests { get; private set; }

        /// <summary>
        /// Gets destination descriptions sent through metadata persistence.
        /// </summary>
        public List<string?> PersistedDescriptions { get; } = [];

        /// <summary>
        /// Gets or sets the upserted cards.
        /// </summary>
        public List<DeckCard> UpsertedCards { get; } = [];

        /// <summary>
        /// Gets or sets the removed cards.
        /// </summary>
        public List<DeckCard> RemovedCards { get; } = [];

        /// <summary>
        /// Gets categories persisted to the fake gateway.
        /// </summary>
        public List<DeckCategory> PersistedCategories { get; } = [];

        /// <summary>
        /// Gets or sets the deleted checkpoint ids.
        /// </summary>
        public List<string> DeletedCheckpointIds { get; } = [];

        /// <summary>
        /// Gets or sets whether checkpoint creation should fail.
        /// </summary>
        public bool FailCheckpointCreation { get; set; }

        /// <summary>
        /// Gets or sets whether card persistence should record requests without changing the fake remote deck.
        /// </summary>
        public bool SkipRemoteCardMutation { get; set; }

        /// <summary>
        /// Gets or sets whether removals should mutate the fake remote deck and then throw.
        /// </summary>
        public bool ThrowAfterCardRemoval { get; set; }

        /// <summary>
        /// Verifies that get auth status.
        /// </summary>
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasUsernamePassword = true });
        }

        /// <summary>
        /// Verifies that list decks.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(DeckSummaries);
        }

        /// <summary>
        /// Lists fake decks after applying folder filters.
        /// </summary>
        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(
            ArchidektDeckListRequest request,
            CancellationToken cancellationToken)
        {
            List<ArchidektDeckSummary> summaries = DeckSummaries.ToList();
            if (!string.IsNullOrWhiteSpace(request.FolderId))
            {
                summaries = summaries
                    .Where(summary => string.Equals(summary.FolderId, request.FolderId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.FolderName))
            {
                summaries = summaries
                    .Where(summary => string.Equals(summary.FolderName, request.FolderName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>(summaries);
        }

        /// <summary>
        /// Returns no fake folders by default.
        /// </summary>
        public Task<IReadOnlyList<ArchidektFolder>> ListFoldersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Folders);
        }

        /// <summary>
        /// Creates a deterministic fake folder.
        /// </summary>
        public Task<ArchidektFolder> CreateFolderAsync(
            string name,
            string? parentFolderId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArchidektFolder
            {
                Id = "folder",
                Name = name,
                ParentFolderId = parentFolderId,
            });
        }

        /// <summary>
        /// Echoes fake deck move requests.
        /// </summary>
        public Task<ArchidektMoveDecksResult> MoveDecksAsync(
            IReadOnlyList<string> deckIds,
            string? folderId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArchidektMoveDecksResult
            {
                FolderId = folderId,
                DeckIds = deckIds.ToList(),
                Moved = deckIds.Count,
            });
        }

        /// <summary>
        /// Verifies that create deck.
        /// </summary>
        public Task<DeckWorkspace> CreateDeckAsync(
            ArchidektDeckCreateRequest request,
            CancellationToken cancellationToken
        )
        {
            CreatedDeckRequests++;
            ImportedDeck = new DeckWorkspace
            {
                Id = "created-workspace",
                Name = request.Name,
                Format = request.Format,
                Description = request.Description,
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "created",
                Categories =
                [
                    new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                    new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
                ],
            };
            return Task.FromResult(ImportedDeck);
        }

        /// <summary>
        /// Verifies that import deck.
        /// </summary>
        public Task<DeckWorkspace> ImportDeckAsync(
            string deckIdOrUrl,
            bool writeBack,
            CancellationToken cancellationToken
        )
        {
            ImportedDeckRequests++;
            if (ThrowOnImport)
            {
                throw new InvalidOperationException("Archidekt source unavailable.");
            }

            DeckWorkspace copy = new()
            {
                Id = ImportedDeck.Id,
                Name = ImportedDeck.Name,
                Format = ImportedDeck.Format,
                Description = ImportedDeck.Description,
                Mode = ImportedDeck.Mode,
                WriteBack = writeBack,
                ArchidektDeckId = ImportedDeck.ArchidektDeckId,
                Categories = ImportedDeck.Categories,
                Cards = ImportedDeck.Cards.ToList(),
            };
            return Task.FromResult(copy);
        }

        /// <summary>
        /// Verifies that persist cards.
        /// </summary>
        public Task PersistCardsAsync(
            DeckWorkspace workspace,
            IReadOnlyList<DeckCard> upsertedCards,
            IReadOnlyList<DeckCard> removedCards,
            CancellationToken cancellationToken
        )
        {
            foreach (DeckCard card in upsertedCards.Where(card => string.IsNullOrWhiteSpace(card.ArchidektCardId)))
            {
                card.ArchidektCardId = $"fake-{card.Name}";
                card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "resolved";
            }

            UpsertedCards.AddRange(upsertedCards);
            RemovedCards.AddRange(removedCards);
            if (!SkipRemoteCardMutation)
            {
                foreach (DeckCard removed in removedCards)
                {
                    int index = ImportedDeck.Cards.FindIndex(card => SameFakeCardRow(card, removed));
                    if (index >= 0)
                    {
                        ImportedDeck.Cards.RemoveAt(index);
                    }
                }

                if (ThrowAfterCardRemoval && removedCards.Count > 0)
                {
                    throw new TimeoutException("Simulated Archidekt timeout after removal.");
                }

                foreach (DeckCard upserted in upsertedCards)
                {
                    ImportedDeck.Cards.Add(CloneFakeCard(upserted));
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Resolves missing fake Archidekt card ids without remote access.
        /// </summary>
        public Task ResolveCardIdsAsync(IReadOnlyList<DeckCard> cards, CancellationToken cancellationToken)
        {
            foreach (DeckCard card in cards.Where(card => string.IsNullOrWhiteSpace(card.ArchidektCardId)))
            {
                card.ArchidektCardId = $"fake-{card.Name}";
                card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "resolved";
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Verifies that persist category.
        /// </summary>
        public Task PersistCategoryAsync(
            DeckWorkspace workspace,
            DeckCategory category,
            CancellationToken cancellationToken
        )
        {
            PersistedCategories.Add(new DeckCategory
            {
                Name = category.Name,
                IncludedInDeck = category.IncludedInDeck,
                IncludedInPrice = category.IncludedInPrice,
            });
            return Task.CompletedTask;
        }

        /// <summary>
        /// Verifies that delete category.
        /// </summary>
        public Task DeleteCategoryAsync(
            DeckWorkspace workspace,
            DeckCategory category,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Verifies that persist metadata.
        /// </summary>
        public Task PersistMetadataAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
            PersistedMetadataRequests++;
            PersistedDescriptions.Add(workspace.Description);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Verifies that create checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> CreateCheckpointAsync(
            DeckWorkspace workspace,
            string name,
            string? description,
            CancellationToken cancellationToken
        )
        {
            if (FailCheckpointCreation)
            {
                throw new InvalidOperationException("Simulated checkpoint failure.");
            }

            return Task.FromResult(
                new DeckCheckpoint
                {
                    Id = "7",
                    DeckId = workspace.ArchidektDeckId ?? "",
                    Name = name,
                    Description = description,
                }
            );
        }

        /// <summary>
        /// Verifies that list checkpoints.
        /// </summary>
        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([
                new DeckCheckpoint
                {
                    Id = "7",
                    DeckId = workspace.ArchidektDeckId ?? "",
                    Name = "Before",
                },
            ]);
        }

        /// <summary>
        /// Verifies that get checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> GetCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new DeckCheckpoint
                {
                    Id = checkpointId,
                    DeckId = workspace.ArchidektDeckId ?? "",
                    Name = "Before",
                }
            );
        }

        /// <summary>
        /// Verifies that rename checkpoint.
        /// </summary>
        public Task<DeckCheckpoint> RenameCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            string name,
            string? description,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new DeckCheckpoint
                {
                    Id = checkpointId,
                    DeckId = workspace.ArchidektDeckId ?? "",
                    Name = name,
                    Description = description,
                }
            );
        }

        /// <summary>
        /// Verifies that delete checkpoint.
        /// </summary>
        public Task DeleteCheckpointAsync(
            DeckWorkspace workspace,
            string checkpointId,
            CancellationToken cancellationToken
        )
        {
            DeletedCheckpointIds.Add(checkpointId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Checks fake card identity using Archidekt relation id first, then stable card fields.
        /// </summary>
        private static bool SameFakeCardRow(DeckCard left, DeckCard right)
        {
            if (left.ArchidektDeckRelationId.HasValue && right.ArchidektDeckRelationId.HasValue)
            {
                return left.ArchidektDeckRelationId.Value == right.ArchidektDeckRelationId.Value;
            }

            return left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.PrimaryCategory, right.PrimaryCategory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.ScryfallId, right.ScryfallId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copies fake remote card rows so later local mutations do not rewrite history.
        /// </summary>
        private static DeckCard CloneFakeCard(DeckCard source)
        {
            return new DeckCard
            {
                Id = source.Id,
                Name = source.Name,
                Quantity = source.Quantity,
                PrimaryCategory = source.PrimaryCategory,
                Categories = source.Categories.ToList(),
                ScryfallId = source.ScryfallId,
                ScryfallOracleId = source.ScryfallOracleId,
                ArchidektCardId = source.ArchidektCardId,
                ArchidektDeckRelationId = source.ArchidektDeckRelationId,
                Modifier = source.Modifier,
                Companion = source.Companion,
                FlippedDefault = source.FlippedDefault,
                Snapshot = source.Snapshot,
                Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase),
            };
        }
    }
}
