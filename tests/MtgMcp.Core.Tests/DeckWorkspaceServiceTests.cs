using System.Text.Json;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

public sealed class DeckWorkspaceServiceTests
{
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

    [Fact]
    public async Task LocalMutations_MoveCardsAndReplacePrimaryCategory()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync("Brew", "commander", null, TestContext.Current.CancellationToken);

        await service.AddCardAsync(deck.Id, "Lightning Bolt", 1, DeckDefaults.Mainboard, TestContext.Current.CancellationToken);
        await service.AddCardCategoryAsync(deck.Id, "Lightning Bolt", "Testing", TestContext.Current.CancellationToken);
        DeckChangeResult result = await service.MoveCardAsync(deck.Id, "Lightning Bolt", DeckDefaults.Maybeboard, null, TestContext.Current.CancellationToken);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);

        result.Persistence.Should().Be(DeckPersistence.LocalOnly);
        opened.Cards.Should().ContainSingle();
        opened.Cards[0].PrimaryCategory.Should().Be(DeckDefaults.Maybeboard);
        opened.Cards[0].Categories.Should().NotContain(DeckDefaults.Mainboard);
        opened.Cards[0].Categories.Should().Contain(DeckDefaults.Maybeboard);
        opened.Cards[0].Categories.Should().Contain("Testing");
        opened.Cards[0].Snapshot.TypeLine.Should().Be("Instant");
        opened.Cards[0].Snapshot.ColorIdentity.Should().BeEquivalentTo(["R"]);
        opened.Cards[0].Snapshot.Set.Should().Be("tst");
        opened.Cards[0].Snapshot.CollectorNumber.Should().Be("1");
        opened.Cards[0].Snapshot.ScryfallUri.Should().Contain("Lightning%20Bolt");
    }

    [Fact]
    public async Task ListLocalWorkspaces_ExcludesCachedArchidektDecks()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace local = await service.CreateLocalDeckAsync("Local", "commander", null, TestContext.Current.CancellationToken);
        await repository.SaveAsync(new DeckWorkspace
        {
            Id = "remote-cache",
            Name = "Remote Cache",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        }, TestContext.Current.CancellationToken);

        IReadOnlyList<DeckWorkspace> workspaces = await service.ListLocalWorkspacesAsync(TestContext.Current.CancellationToken);

        workspaces.Should().ContainSingle();
        workspaces[0].Id.Should().Be(local.Id);
    }

    [Fact]
    public async Task StartDeckWorkspace_RejectsAmbiguousMode()
    {
        DeckWorkspaceService service = new(new InMemoryRepository(), new FakeCardCatalog());

        Func<Task> act = () => service.StartDeckWorkspaceAsync(
            mode: null,
            name: "Brew",
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: null,
            writeBack: null,
            decklist: null,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Ask the user*local*Archidekt*");
    }

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
            writeBack: null,
            decklist: null,
            TestContext.Current.CancellationToken);
        DeckWorkspace imported = await service.StartDeckWorkspaceAsync(
            "local",
            "Import",
            "modern",
            description: null,
            archidektDeckIdOrUrl: null,
            writeBack: null,
            decklist: "1 Lightning Bolt",
            TestContext.Current.CancellationToken);

        created.Mode.Should().Be(WorkspaceMode.Local);
        created.Description.Should().Be("notes");
        imported.Cards.Should().ContainSingle(card => card.Name == "Lightning Bolt");
    }

    [Fact]
    public async Task StartDeckWorkspace_RequiresExplicitArchidektWriteBackChoice()
    {
        DeckWorkspaceService service = new(new InMemoryRepository(), new FakeCardCatalog(), new FakeArchidektGateway());

        Func<Task> act = () => service.StartDeckWorkspaceAsync(
            "archidekt",
            name: null,
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: "123",
            writeBack: null,
            decklist: null,
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*writeback intent is ambiguous*Ask*");
    }

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
                ArchidektDeckId = "123"
            }
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), gateway);

        DeckWorkspace workspace = await service.StartDeckWorkspaceAsync(
            "archidekt",
            name: null,
            format: "commander",
            description: null,
            archidektDeckIdOrUrl: "123",
            writeBack: false,
            decklist: null,
            TestContext.Current.CancellationToken);

        workspace.Mode.Should().Be(WorkspaceMode.Archidekt);
        workspace.WriteBack.Should().BeFalse();
        gateway.ImportedDeckRequests.Should().Be(1);
    }

    [Fact]
    public async Task SetQuantityToZero_RemovesCard()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync("Brew", "commander", null, TestContext.Current.CancellationToken);

        await service.AddCardAsync(deck.Id, "Lightning Bolt", 1, DeckDefaults.Mainboard, TestContext.Current.CancellationToken);
        await service.SetCardQuantityAsync(deck.Id, "Lightning Bolt", 0, null, TestContext.Current.CancellationToken);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);

        opened.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveCard_DecrementsThenRemovesCard()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync("Brew", "modern", null, TestContext.Current.CancellationToken);

        await service.AddCardAsync(deck.Id, "Lightning Bolt", 3, DeckDefaults.Mainboard, TestContext.Current.CancellationToken);
        DeckChangeResult decremented = await service.RemoveCardAsync(deck.Id, "Lightning Bolt", 1, null, TestContext.Current.CancellationToken);
        DeckChangeResult removed = await service.RemoveCardAsync(deck.Id, "Lightning Bolt", 2, null, TestContext.Current.CancellationToken);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);

        decremented.Kind.Should().Be(DeckMutationKind.CardRemoved);
        removed.Kind.Should().Be(DeckMutationKind.CardRemoved);
        opened.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportExportAnalyzeAndSummarize_UseDeckTextComponents()
    {
        const string decklist = """
        Mainboard
        2 Lightning Bolt
        1 Sol Ring

        Sideboard
        1 Island

        // Maybeboard
        1 Missing Card
        """;

        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());

        DeckWorkspace imported = await service.ImportDecklistAsync(decklist, "Imported", "modern", TestContext.Current.CancellationToken);
        string exported = await service.ExportDeckAsync(imported.Id, TestContext.Current.CancellationToken);
        DeckAnalysis analysis = await service.AnalyzeDeckAsync(imported.Id, TestContext.Current.CancellationToken);
        DeckValidationResult validation = await service.ValidateDeckAsync(imported.Id, TestContext.Current.CancellationToken);
        object summary = await service.GetDeckSummaryAsync(imported.Id, TestContext.Current.CancellationToken);
        DeckWorkspace resource = await service.GetDeckResourceAsync(imported.Id, TestContext.Current.CancellationToken);

        exported.Should().Contain("Mainboard");
        exported.Should().Contain("2 Lightning Bolt");
        exported.Should().Contain("1 Sol Ring");
        exported.Should().Contain("Sideboard");
        analysis.TotalCards.Should().Be(5);
        analysis.IncludedCards.Should().Be(3);
        analysis.TypeCounts["Instant"].Should().Be(2);
        analysis.TypeCounts["Artifact"].Should().Be(1);
        analysis.TypeCounts["Land"].Should().Be(1);
        analysis.ManaCurve["1"].Should().Be(3);
        analysis.ColorIdentityCounts["R"].Should().Be(2);
        analysis.Notes.Should().Contain(note => note.Contains("Missing Card", StringComparison.OrdinalIgnoreCase));
        validation.Errors.Should().Contain(error => error.Contains("60", StringComparison.OrdinalIgnoreCase));
        summary.Should().NotBeNull();
        resource.Name.Should().Be("Imported");
        resource.Cards.Single(card => card.Name == "Lightning Bolt").Snapshot.ManaValue.Should().Be(1);
    }

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
                        ["colorIdentity"] = "R"
                    }
                }
            ]
        };

        DeckAnalysis analysis = DeckAnalyzer.Analyze(deck);

        analysis.TypeCounts["Instant"].Should().Be(2);
        analysis.ManaCurve["1"].Should().Be(2);
        analysis.ColorIdentityCounts["R"].Should().Be(2);
        analysis.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task CategoryMutations_UpdateCardsAndCatalog()
    {
        InMemoryRepository repository = new();
        DeckWorkspaceService service = new(repository, new FakeCardCatalog());
        DeckWorkspace deck = await service.CreateLocalDeckAsync("Brew", "commander", null, TestContext.Current.CancellationToken);
        await service.AddCardAsync(deck.Id, "Sol Ring", 1, DeckDefaults.Mainboard, TestContext.Current.CancellationToken);

        await service.CreateCategoryAsync(deck.Id, " Ramp ", includedInDeck: true, includedInPrice: false, TestContext.Current.CancellationToken);
        await service.AddCardCategoryAsync(deck.Id, "Sol Ring", "Ramp", TestContext.Current.CancellationToken);
        await service.SetPrimaryCardCategoryAsync(deck.Id, "Sol Ring", "Ramp", TestContext.Current.CancellationToken);
        await service.RenameCategoryAsync(deck.Id, "Ramp", "Acceleration", TestContext.Current.CancellationToken);
        await service.RemoveCardCategoryAsync(deck.Id, "Sol Ring", "Acceleration", TestContext.Current.CancellationToken);
        await service.DeleteCategoryAsync(deck.Id, "Acceleration", DeckDefaults.Mainboard, TestContext.Current.CancellationToken);
        DeckWorkspace opened = await service.OpenLocalDeckAsync(deck.Id, TestContext.Current.CancellationToken);

        opened.Categories.Should().NotContain(category => category.Name == "Acceleration");
        opened.Cards.Should().ContainSingle();
        opened.Cards[0].PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        opened.Cards[0].Categories.Should().Contain(DeckDefaults.Mainboard);
    }

    [Fact]
    public void CommanderValidation_FlagsNonBasicDuplicates()
    {
        DeckWorkspace deck = new()
        {
            Format = "commander",
            Cards =
            [
                new DeckCard { Name = "Lightning Bolt", Quantity = 2 },
                new DeckCard { Name = "Island", Quantity = 20 }
            ]
        };

        DeckValidationResult result = DeckValidator.Validate(deck);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Lightning Bolt", StringComparison.OrdinalIgnoreCase));
        result.Warnings.Should().Contain(warning => warning.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

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
        parsed.Warnings.Should().Contain(warning => warning.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

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
            Cards = [new DeckCard { Name = "Sol Ring", Quantity = 1, ArchidektCardId = "10", ArchidektDeckRelationId = 99 }]
        };
        archidekt.ImportedDeck = remoteDeck;

        DeckWorkspace cached = new()
        {
            Id = "workspace",
            Name = "Cached",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123"
        };
        await repository.SaveAsync(cached, TestContext.Current.CancellationToken);
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        DeckChangeResult result = await service.AddCardAsync("workspace", "Lightning Bolt", 1, DeckDefaults.Mainboard, TestContext.Current.CancellationToken);

        result.Persistence.Should().Be(DeckPersistence.ArchidektWriteBack);
        archidekt.ImportedDeckRequests.Should().Be(1);
        archidekt.UpsertedCards.Should().ContainSingle(card => card.Name == "Lightning Bolt");
        DeckWorkspace saved = await service.OpenLocalDeckAsync("workspace", TestContext.Current.CancellationToken);
        saved.Name.Should().Be("Remote");
        saved.Cards.Should().Contain(card => card.Name == "Sol Ring");
        saved.Cards.Should().Contain(card => card.Name == "Lightning Bolt");
    }

    [Fact]
    public async Task ArchidektDeckOperations_DelegateToGateway()
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
                ArchidektDeckId = "123"
            }
        };
        DeckWorkspaceService service = new(repository, new FakeCardCatalog(), archidekt);

        IReadOnlyList<ArchidektDeckSummary> deckSummaries = await service.ListArchidektDecksAsync(TestContext.Current.CancellationToken);
        DeckWorkspace opened = await service.OpenArchidektDeckAsync("https://archidekt.com/decks/123/deck", writeBack: true, TestContext.Current.CancellationToken);
        DeckChangeResult metadata = await service.UpdateDeckMetadataAsync(opened.Id, "Renamed", "legacy", "Updated", TestContext.Current.CancellationToken);
        DeckCheckpoint created = await service.CheckpointDeckAsync(opened.Id, "Before", "baseline", TestContext.Current.CancellationToken);
        IReadOnlyList<DeckCheckpoint> listed = await service.ListDeckCheckpointsAsync(opened.Id, TestContext.Current.CancellationToken);
        DeckCheckpoint fetched = await service.GetDeckCheckpointAsync(opened.Id, "7", TestContext.Current.CancellationToken);
        DeckCheckpoint renamed = await service.RenameDeckCheckpointAsync(opened.Id, "7", "After", null, TestContext.Current.CancellationToken);
        await service.DeleteDeckCheckpointAsync(opened.Id, "7", TestContext.Current.CancellationToken);

        deckSummaries.Should().ContainSingle(summary => summary.Id == "123");
        metadata.Workspace.Name.Should().Be("Renamed");
        archidekt.PersistedMetadataRequests.Should().Be(1);
        created.Name.Should().Be("Before");
        listed.Should().ContainSingle(checkpoint => checkpoint.Id == "7");
        fetched.Id.Should().Be("7");
        renamed.Name.Should().Be("After");
        archidekt.DeletedCheckpointIds.Should().ContainSingle().Which.Should().Be("7");
    }

    [Fact]
    public async Task JsonRepository_CancelledSaveKeepsExistingWorkspace()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", Guid.NewGuid().ToString("N"));
        try
        {
            JsonDeckWorkspaceRepository repository = new(dataDirectory);
            DeckWorkspace original = new()
            {
                Id = "workspace",
                Name = "Original"
            };
            await repository.SaveAsync(original, TestContext.Current.CancellationToken);

            DeckWorkspace changed = new()
            {
                Id = original.Id,
                Name = "Changed"
            };
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();

            Func<Task> act = () => repository.SaveAsync(changed, cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            DeckWorkspace? loaded = await repository.GetAsync(original.Id, TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task JsonRepository_ListsSavedWorkspacesAndRejectsInvalidIds()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", Guid.NewGuid().ToString("N"));
        try
        {
            JsonDeckWorkspaceRepository repository = new(dataDirectory);
            await repository.SaveAsync(new DeckWorkspace { Id = "one", Name = "One" }, TestContext.Current.CancellationToken);
            await repository.SaveAsync(new DeckWorkspace { Id = "two", Name = "Two" }, TestContext.Current.CancellationToken);

            IReadOnlyList<DeckWorkspace> workspaces = await repository.ListAsync(TestContext.Current.CancellationToken);
            Func<Task> invalidSave = () => repository.SaveAsync(new DeckWorkspace { Id = "!!!" }, TestContext.Current.CancellationToken);

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

    [Fact]
    public void SecretRedactor_RedactsStringAndJsonValues()
    {
        SecretRedactor.Redact("authorization: Bearer secret").Should().Be("***REDACTED***");
        SecretRedactor.Redact("ordinary text").Should().Be("ordinary text");

        using JsonDocument document = JsonDocument.Parse("""{ "jwt": "secret", "nested": { "name": "deck" }, "count": 3 }""");
        using JsonDocument redacted = SecretRedactor.Redact(document);

        redacted.RootElement.GetProperty("jwt").GetString().Should().Be("***REDACTED***");
        redacted.RootElement.GetProperty("nested").GetProperty("name").GetString().Should().Be("deck");
        redacted.RootElement.GetProperty("count").GetInt64().Should().Be(3);
    }

    private sealed class FakeCardCatalog : ICardCatalog
    {
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
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
                ColorIdentity = nameOrId.Contains("Lightning", StringComparison.OrdinalIgnoreCase) ? ["R"] : [],
                Set = "tst",
                CollectorNumber = "1",
                ScryfallUri = $"https://scryfall.test/{Uri.EscapeDataString(nameOrId)}"
            };
            return Task.FromResult<CardInfo?>(card);
        }

        public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
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
    }

    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        private readonly Dictionary<string, DeckWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);

        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(workspaces.Values.ToList());
        }
    }

    private sealed class FakeArchidektGateway : IArchidektGateway
    {
        public DeckWorkspace ImportedDeck { get; set; } = new();
        public int ImportedDeckRequests { get; private set; }
        public int PersistedMetadataRequests { get; private set; }
        public List<DeckCard> UpsertedCards { get; } = [];
        public List<DeckCard> RemovedCards { get; } = [];
        public List<string> DeletedCheckpointIds { get; } = [];

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthStatus { HasJwt = true });
        }

        public Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ArchidektDeckSummary>>(
            [
                new ArchidektDeckSummary { Id = "123", Name = "Remote" }
            ]);
        }

        public Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken)
        {
            ImportedDeckRequests++;
            DeckWorkspace copy = new()
            {
                Id = ImportedDeck.Id,
                Name = ImportedDeck.Name,
                Format = ImportedDeck.Format,
                Mode = ImportedDeck.Mode,
                WriteBack = writeBack,
                ArchidektDeckId = ImportedDeck.ArchidektDeckId,
                Categories = ImportedDeck.Categories,
                Cards = ImportedDeck.Cards.ToList()
            };
            return Task.FromResult(copy);
        }

        public Task PersistCardsAsync(DeckWorkspace workspace, IReadOnlyList<DeckCard> upsertedCards, IReadOnlyList<DeckCard> removedCards, CancellationToken cancellationToken)
        {
            UpsertedCards.AddRange(upsertedCards);
            RemovedCards.AddRange(removedCards);
            return Task.CompletedTask;
        }

        public Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            PersistedMetadataRequests++;
            return Task.CompletedTask;
        }

        public Task<DeckCheckpoint> CreateCheckpointAsync(DeckWorkspace workspace, string name, string? description, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = "7", DeckId = workspace.ArchidektDeckId ?? "", Name = name, Description = description });
        }

        public Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckCheckpoint>>([new DeckCheckpoint { Id = "7", DeckId = workspace.ArchidektDeckId ?? "", Name = "Before" }]);
        }

        public Task<DeckCheckpoint> GetCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = "Before" });
        }

        public Task<DeckCheckpoint> RenameCheckpointAsync(DeckWorkspace workspace, string checkpointId, string name, string? description, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeckCheckpoint { Id = checkpointId, DeckId = workspace.ArchidektDeckId ?? "", Name = name, Description = description });
        }

        public Task DeleteCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken)
        {
            DeletedCheckpointIds.Add(checkpointId);
            return Task.CompletedTask;
        }
    }
}
