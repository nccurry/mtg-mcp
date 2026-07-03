using FluentAssertions;

namespace MtgMcp.Core.Tests.Collection;

/// <summary>
/// Contains tests for local collection ownership behavior.
/// </summary>
public sealed class CardCollectionServiceTests
{
    /// <summary>
    /// Verifies structured rows and pasted decklist text replace the collection with aggregated quantities.
    /// </summary>
    [Fact]
    public async Task SetCollection_ReplacesWithStructuredEntriesAndDecklistText()
    {
        InMemoryCollectionRepository collections = new();
        CardCollectionService service = CreateService(collections);

        CardCollectionSetResult result = await service.SetCollectionAsync(
            [
                new CardCollectionEntry { CardName = "Sol Ring", Quantity = 1 }
            ],
            """
            2 Counterspell
            1 Sol Ring
            """,
            replace: true,
            TestContext.Current.CancellationToken);

        result.Mode.Should().Be("replace");
        result.InputQuantity.Should().Be(4);
        result.Collection.TotalQuantity.Should().Be(4);
        result.Collection.UniqueCards.Should().Be(2);
        result.Collection.Cards.Should().ContainSingle(card =>
            card.CardName == "Counterspell" && card.Quantity == 2);
        result.Collection.Cards.Should().ContainSingle(card =>
            card.CardName == "Sol Ring" && card.Quantity == 2);

        CardCollectionSnapshot saved = await service.GetCollectionAsync(TestContext.Current.CancellationToken);
        saved.TotalQuantity.Should().Be(4);
    }

    /// <summary>
    /// Verifies merge mode adds quantities without replacing existing collection rows.
    /// </summary>
    [Fact]
    public async Task SetCollection_MergeAddsToExistingQuantities()
    {
        CardCollectionService service = CreateService(new InMemoryCollectionRepository());
        await service.SetCollectionAsync(
            [
                new CardCollectionEntry { CardName = "Sol Ring", Quantity = 1 }
            ],
            decklist: null,
            replace: true,
            TestContext.Current.CancellationToken);

        CardCollectionSetResult result = await service.SetCollectionAsync(
            [
                new CardCollectionEntry { CardName = "Sol Ring", Quantity = 2 },
                new CardCollectionEntry { CardName = "Arcane Signet", Quantity = 1 }
            ],
            decklist: null,
            replace: false,
            TestContext.Current.CancellationToken);

        result.Mode.Should().Be("merge");
        result.Collection.TotalQuantity.Should().Be(4);
        result.Collection.Cards.Should().ContainSingle(card =>
            card.CardName == "Sol Ring" && card.Quantity == 3);
        result.Collection.Cards.Should().ContainSingle(card =>
            card.CardName == "Arcane Signet" && card.Quantity == 1);
    }

    /// <summary>
    /// Verifies workspace imports use included cards and skip maybeboard rows.
    /// </summary>
    [Fact]
    public async Task SetCollection_CanImportIncludedWorkspaceCards()
    {
        InMemoryWorkspaceRepository workspaces = new();
        CardCollectionService service = CreateService(new InMemoryCollectionRepository(), workspaces);
        await workspaces.SaveAsync(
            new DeckWorkspace
            {
                Id = "workspace-1",
                Cards =
                [
                    new DeckCard
                    {
                        Name = "Sol Ring",
                        Quantity = 1,
                        PrimaryCategory = DeckDefaults.Mainboard,
                        Categories = [DeckDefaults.Mainboard]
                    },
                    new DeckCard
                    {
                        Name = "Maybe Card",
                        Quantity = 1,
                        PrimaryCategory = DeckDefaults.Maybeboard,
                        Categories = [DeckDefaults.Maybeboard]
                    }
                ]
            },
            TestContext.Current.CancellationToken);

        CardCollectionSetResult result = await service.SetCollectionAsync(
            entries: null,
            decklist: null,
            workspaceId: "workspace-1",
            replace: true,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Collection.Cards.Should().ContainSingle(card =>
            card.CardName == "Sol Ring" && card.Quantity == 1);
        result.Collection.Cards.Should().NotContain(card => card.CardName == "Maybe Card");
    }

    /// <summary>
    /// Verifies workspace diffs report missing quantities and known missing replacement cost.
    /// </summary>
    [Fact]
    public async Task DiffWorkspace_ReturnsMissingQuantitiesAndKnownCost()
    {
        InMemoryCollectionRepository collections = new();
        InMemoryWorkspaceRepository workspaces = new();
        CardCollectionService service = CreateService(collections, workspaces);
        await service.SetCollectionAsync(
            [
                new CardCollectionEntry { CardName = "Sol Ring", Quantity = 1 }
            ],
            decklist: null,
            replace: true,
            TestContext.Current.CancellationToken);
        await workspaces.SaveAsync(
            new DeckWorkspace
            {
                Id = "workspace-1",
                Name = "Test Deck",
                Cards =
                [
                    new DeckCard
                    {
                        Name = "Sol Ring",
                        Quantity = 1,
                        PrimaryCategory = DeckDefaults.Mainboard,
                        Categories = [DeckDefaults.Mainboard],
                        Snapshot = CreatePricedSnapshot("2.00")
                    },
                    new DeckCard
                    {
                        Name = "Counterspell",
                        Quantity = 2,
                        PrimaryCategory = DeckDefaults.Mainboard,
                        Categories = [DeckDefaults.Mainboard],
                        Snapshot = CreatePricedSnapshot("1.25")
                    },
                    new DeckCard
                    {
                        Name = "Maybe Card",
                        Quantity = 1,
                        PrimaryCategory = DeckDefaults.Maybeboard,
                        Categories = [DeckDefaults.Maybeboard],
                        Snapshot = CreatePricedSnapshot("10.00")
                    }
                ]
            },
            TestContext.Current.CancellationToken);

        CollectionWorkspaceDiffResult diff = await service.DiffWorkspaceAsync(
            "workspace-1",
            TestContext.Current.CancellationToken);

        diff.FullyOwned.Should().BeFalse();
        diff.TotalNeededQuantity.Should().Be(3);
        diff.TotalOwnedForWorkspaceQuantity.Should().Be(1);
        diff.TotalMissingQuantity.Should().Be(2);
        diff.KnownMissingUsd.Should().Be(2.50m);
        diff.MissingPriceCards.Should().BeEmpty();
        diff.MissingCards.Should().ContainSingle(card =>
            card.CardName == "Counterspell"
            && card.NeededQuantity == 2
            && card.MissingQuantity == 2
            && card.MissingUsd == 2.50m);
        diff.Cards.Should().NotContain(card => card.CardName == "Maybe Card");
    }

    /// <summary>
    /// Creates a collection service with in-memory dependencies.
    /// </summary>
    private static CardCollectionService CreateService(
        InMemoryCollectionRepository collections,
        InMemoryWorkspaceRepository? workspaces = null)
    {
        return new CardCollectionService(
            collections,
            workspaces ?? new InMemoryWorkspaceRepository(),
            CatalogPriceSource.Instance,
            () => new DateOnly(2026, 6, 28));
    }

    /// <summary>
    /// Creates a released paper snapshot with one USD price.
    /// </summary>
    private static CardSnapshot CreatePricedSnapshot(string price)
    {
        return new CardSnapshot
        {
            ReleasedAt = new DateOnly(2025, 1, 1),
            Language = "en",
            Games = ["paper"],
            Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["usd"] = price
            }
        };
    }

    /// <summary>
    /// Stores one collection document in memory.
    /// </summary>
    private sealed class InMemoryCollectionRepository : ICardCollectionRepository
    {
        /// <summary>
        /// Stores the current collection.
        /// </summary>
        private CardCollectionDocument? collection;

        /// <summary>
        /// Saves the local card collection.
        /// </summary>
        public Task<CardCollectionDocument> SaveAsync(
            CardCollectionDocument collection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.collection = collection;
            return Task.FromResult(collection);
        }

        /// <summary>
        /// Loads the local card collection when one has been saved.
        /// </summary>
        public Task<CardCollectionDocument?> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(collection);
        }
    }

    /// <summary>
    /// Stores workspaces in memory.
    /// </summary>
    private sealed class InMemoryWorkspaceRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Stores workspaces by id.
        /// </summary>
        private readonly Dictionary<string, DeckWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves the workspace.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Loads a workspace by id.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Lists workspaces.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DeckWorkspace> result = [.. workspaces.Values];
            return Task.FromResult(result);
        }
    }
}
