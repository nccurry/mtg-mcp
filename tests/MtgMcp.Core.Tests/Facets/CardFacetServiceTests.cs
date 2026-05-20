using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies factual card facets and explicit predicate matching.
/// </summary>
public sealed class CardFacetServiceTests
{
    /// <summary>
    /// Verifies that facets expose Scryfall snapshots, workspace categories, and local annotations.
    /// </summary>
    [Fact]
    public async Task GetCardFacets_ReturnsConcreteFacetSources()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreateWorkspace(), TestContext.Current.CancellationToken);
        CardFacetService service = new(repository);

        CardFacetSnapshot facets = await service.GetCardFacetsAsync(
            workspace.Id,
            "Phyrexian Arena",
            TestContext.Current.CancellationToken);

        facets.Facets[CardFacetNames.WorkspaceCategories].Values.Should().Contain(DeckRoles.Draw);
        facets.Facets["scryfall.oracle_text"].Values.Should().Contain(value => value.Contains("draw a card", StringComparison.OrdinalIgnoreCase));
        facets.Facets[CardFacetNames.UserTags].Values.Should().Contain("card-advantage");
        facets.Facets[CardFacetNames.TaggerOracleTags].Values.Should().Contain("repeatable-draw");
    }

    /// <summary>
    /// Verifies that deck counts use only the predicate supplied by the caller.
    /// </summary>
    [Fact]
    public async Task CountDeckCardsMatching_UsesCallerPredicate()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreateWorkspace(), TestContext.Current.CancellationToken);
        CardFacetService service = new(repository);
        const string predicateJson = """
            {
              "any": [
                { "facet": "tagger.oracle_tags", "equals": "repeatable-draw" },
                { "facet": "user.tags", "equals": "card-advantage" }
              ]
            }
            """;

        DeckFacetCountResult result = await service.CountDeckCardsMatchingAsync(
            workspace.Id,
            predicateJson,
            includedOnly: true,
            TestContext.Current.CancellationToken);

        result.TotalQuantity.Should().Be(1);
        result.Matches.Should().ContainSingle().Which.CardName.Should().Be("Phyrexian Arena");
        result.Matches.Single().Evidence.Should().Contain(row => row.Facet == CardFacetNames.TaggerOracleTags && row.Matched);
    }

    /// <summary>
    /// Verifies that annotation writes persist local metadata without requiring Archidekt writeback.
    /// </summary>
    [Fact]
    public async Task SetCardAnnotations_PersistsLocalFacetMetadata()
    {
        InMemoryRepository repository = new();
        DeckWorkspace workspace = await repository.SaveAsync(CreateWorkspace(), TestContext.Current.CancellationToken);
        CardFacetService service = new(repository);

        CardFacetAnnotationResult result = await service.SetCardAnnotationsAsync(
            workspace.Id,
            "Swords to Plowshares",
            userTags: ["removal"],
            userCategories: ["Interaction"],
            taggerOracleTags: ["exile-removal"],
            taggerArtTags: null,
            TestContext.Current.CancellationToken);

        result.Facets.Facets[CardFacetNames.UserTags].Values.Should().Contain("removal");
        result.Facets.Facets[CardFacetNames.TaggerOracleTags].Values.Should().Contain("exile-removal");
        DeckWorkspace saved = await repository.GetAsync(workspace.Id, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Saved workspace was not found.");
        saved.Cards.Single(card => card.Name == "Swords to Plowshares")
            .Metadata[CardFacetNames.UserCategories]
            .Should()
            .Be("Interaction");
    }

    /// <summary>
    /// Creates a small workspace with factual and annotated card data.
    /// </summary>
    private static DeckWorkspace CreateWorkspace()
    {
        return new DeckWorkspace
        {
            Name = "Facet Test",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Interaction, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false }
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Phyrexian Arena",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Draw,
                    Categories = [DeckRoles.Draw],
                    ScryfallId = "phyrexian-arena",
                    ScryfallOracleId = "oracle-phyrexian-arena",
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Enchantment",
                        OracleText = "At the beginning of your upkeep, you draw a card and you lose 1 life.",
                        ManaValue = 3,
                        ColorIdentity = ["B"],
                        Keywords = [],
                        Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["commander"] = "legal"
                        },
                        Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["usd"] = "3.00"
                        }
                    },
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CardFacetNames.UserTags] = "card-advantage",
                        [CardFacetNames.TaggerOracleTags] = "repeatable-draw"
                    }
                },
                new DeckCard
                {
                    Name = "Swords to Plowshares",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Interaction,
                    Categories = [DeckRoles.Interaction],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Instant",
                        OracleText = "Exile target creature. Its controller gains life equal to its power.",
                        ManaValue = 1,
                        ColorIdentity = ["W"]
                    }
                },
                new DeckCard
                {
                    Name = "Maybe Draw",
                    Quantity = 1,
                    PrimaryCategory = DeckDefaults.Maybeboard,
                    Categories = [DeckDefaults.Maybeboard],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Sorcery",
                        OracleText = "Draw two cards.",
                        ManaValue = 3
                    },
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [CardFacetNames.UserTags] = "card-advantage"
                    }
                }
            ]
        };
    }

    /// <summary>
    /// Stores workspaces in memory for facet tests.
    /// </summary>
    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Stores saved workspaces by id.
        /// </summary>
        private readonly Dictionary<string, DeckWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a workspace.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
        {
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets a workspace by id.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
        {
            workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Lists saved workspaces.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(workspaces.Values.ToList());
        }
    }
}
