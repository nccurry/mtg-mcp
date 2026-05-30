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
        result.Categories.Should().Contain(DeckDefaults.Mainboard);
        result.Categories.Should().Contain(DeckDefaults.Maybeboard);
        result.Categories.Should().Contain("Ramp");
        result.Commanders.Should().ContainSingle().Which.Should().Be("Atraxa, Praetors' Voice");
        result.Warnings.Should().Contain(warning => warning.Contains("no Scryfall id", StringComparison.OrdinalIgnoreCase));
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
            ImportedDeck = new DeckWorkspace
            {
                Id = "remote",
                Name = "Existing",
                Mode = WorkspaceMode.Archidekt,
                WriteBack = true,
                ArchidektDeckId = "123",
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
                    },
                ],
            },
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
            ImportedDeck = new DeckWorkspace
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
            },
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
        SecretRedactor.Redact("authorization: Bearer secret").Should().Be("***REDACTED***");
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
        /// Gets or sets whether single-card lookup should simulate caller cancellation.
        /// </summary>
        public bool CancelGetCard { get; init; }

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
            if (CancelGetCard)
            {
                throw new TaskCanceledException("Caller cancelled card lookup.");
            }

            if (nameOrId.Contains("Missing", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<CardInfo?>(null);
            }

            CardInfo card = new()
            {
                Id = $"scryfall-{nameOrId}",
                OracleId = $"oracle-{nameOrId}",
                Name = nameOrId,
                ManaValue = nameOrId.Contains("Island", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                TypeLine = GetTypeLine(nameOrId),
                ColorIdentity = nameOrId.Contains("Lightning", StringComparison.OrdinalIgnoreCase)
                    ? ["R"]
                    : [],
                Set = "tst",
                CollectorNumber = "1",
                ScryfallUri = $"https://scryfall.test/{Uri.EscapeDataString(nameOrId)}",
            };
            return Task.FromResult<CardInfo?>(card);
        }

        /// <summary>
        /// Returns fake metadata for each requested name that resolves.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken
        )
        {
            Dictionary<string, CardInfo> cards = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                CardInfo? card = await GetCardAsync(name, cancellationToken).ConfigureAwait(false);
                if (card is not null)
                {
                    cards[name] = card;
                }
            }

            return cards;
        }

        /// <summary>
        /// Chooses a type line from the fixture card name.
        /// </summary>
        private static string GetTypeLine(string name)
        {
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
        /// Saves a workspace in memory.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken
        )
        {
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
        /// Gets or sets deck summaries returned by list requests.
        /// </summary>
        public IReadOnlyList<ArchidektDeckSummary> DeckSummaries { get; set; } =
        [
            new ArchidektDeckSummary { Id = "123", Name = "Remote" },
        ];

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
        /// Verifies that create deck.
        /// </summary>
        public Task<DeckWorkspace> CreateDeckAsync(
            ArchidektDeckCreateRequest request,
            CancellationToken cancellationToken
        )
        {
            CreatedDeckRequests++;
            return Task.FromResult(new DeckWorkspace
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
            });
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
            UpsertedCards.AddRange(upsertedCards);
            RemovedCards.AddRange(removedCards);
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
    }
}
