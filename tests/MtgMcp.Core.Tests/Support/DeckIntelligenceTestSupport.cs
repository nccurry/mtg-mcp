using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Provides shared fixtures for deck intelligence service tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Creates a workspace service for workspace and intent tests.
    /// </summary>
    private static DeckWorkspaceService CreateWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        return new DeckWorkspaceService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            currentDateOverride,
            moxfieldGateway: null);
    }

    /// <summary>
    /// Creates an analysis service using the same dependency order as workspace fixtures.
    /// </summary>
    private static DeckAnalysisService CreateAnalysisService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        return new DeckAnalysisService(
            repository,
            cardCatalog,
            comboCatalog: comboCatalog,
            currentDateOverride: currentDateOverride);
    }

    /// <summary>
    /// Creates a simulation service using the same dependency order as workspace fixtures.
    /// </summary>
    private static DeckSimulationService CreateSimulationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        return new DeckSimulationService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            currentDateOverride,
            simulationProfiles: null);
    }

    /// <summary>
    /// Creates a recommendation service with explicit analysis and simulation collaborators.
    /// </summary>
    private static DeckRecommendationService CreateRecommendationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null)
    {
        DeckAnalysisService analysis = CreateAnalysisService(
            repository,
            cardCatalog,
            comboCatalog: comboCatalog,
            currentDateOverride: currentDateOverride);
        DeckSimulationService simulation = CreateSimulationService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            currentDateOverride: currentDateOverride);

        return new DeckRecommendationService(
            repository,
            cardCatalog,
            analysis,
            simulation,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            currentDateOverride,
            corpusSignalProviders);
    }

    /// <summary>
    /// Creates a plan service with an explicit workspace mutation collaborator.
    /// </summary>
    private static DeckPlanService CreatePlanService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null)
    {
        DeckWorkspaceService workspaceService = CreateWorkspaceService(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            currentDateOverride: currentDateOverride);

        return new DeckPlanService(
            repository,
            cardCatalog,
            workspaceService,
            archidektGateway,
            planRepository,
            currentDateOverride);
    }

    /// <summary>
    /// Creates a deck card fixture.
    /// </summary>
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

    /// <summary>
    /// Creates a small offline fixture derived from the Inga and Esika deck-tuning workflow.
    /// </summary>
    private static DeckWorkspace CreateIngaAndEsikaFixtureWorkspace()
    {
        return new DeckWorkspace
        {
            Id = $"inga-esika-{Guid.NewGuid():N}",
            Name = "Inga and Esika Fixture",
            Format = "commander",
            Description = DeckIntentText.UpsertDescription(
                "",
                """
                MTG MCP Deck Intent
                Version: 2
                Primary Goal: creature-mana Simic elves value deck
                Secondary Goal: control the board while playing creatures
                End MTG MCP Deck Intent
                """),
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Interaction, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Wincons, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                IngaFixtureCard(
                    "Inga and Esika",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Human God",
                    4,
                    "Creatures you control have vigilance and \"{T}: Add one mana of any color. Spend this mana only to cast a creature spell.\" Whenever you cast a creature spell, if three or more mana from creatures was spent to cast it, draw a card.",
                    ["G", "U"],
                    "https://scryfall.com/card/mom/229/inga-and-esika"),
                IngaFixtureCard(
                    "Forest",
                    10,
                    DeckRoles.Lands,
                    "Basic Land - Forest",
                    0,
                    "{T}: Add {G}.",
                    [],
                    "https://scryfall.com/search?q=!%22Forest%22",
                    producedMana: ["G"]),
                IngaFixtureCard(
                    "Island",
                    10,
                    DeckRoles.Lands,
                    "Basic Land - Island",
                    0,
                    "{T}: Add {U}.",
                    [],
                    "https://scryfall.com/search?q=!%22Island%22",
                    producedMana: ["U"]),
                IngaFixtureCard(
                    "Command Tower",
                    1,
                    DeckRoles.Lands,
                    "Land",
                    0,
                    "{T}: Add one mana of any color in your commander's color identity.",
                    [],
                    "https://scryfall.com/card/clu/234/command-tower",
                    producedMana: ["G", "U"],
                    extraCategories: [DeckRoles.Ramp]),
                IngaFixtureCard(
                    "Elvish Mystic",
                    1,
                    DeckRoles.Ramp,
                    "Creature - Elf Druid",
                    1,
                    "{T}: Add {G}.",
                    ["G"],
                    "https://scryfall.com/card/m14/169/elvish-mystic",
                    producedMana: ["G"]),
                IngaFixtureCard(
                    "Circle of Dreams Druid",
                    1,
                    DeckRoles.Ramp,
                    "Creature - Elf Druid",
                    3,
                    "{T}: Add {G} for each creature you control.",
                    ["G"],
                    "https://scryfall.com/card/afr/176/circle-of-dreams-druid",
                    producedMana: ["G"]),
                IngaFixtureCard(
                    "Beast Whisperer",
                    1,
                    DeckRoles.Draw,
                    "Creature - Elf Druid",
                    4,
                    "Whenever you cast a creature spell, draw a card.",
                    ["G"],
                    "https://scryfall.com/card/clb/216/beast-whisperer"),
                IngaFixtureCard(
                    "Reclamation Sage",
                    1,
                    DeckRoles.Interaction,
                    "Creature - Elf Shaman",
                    3,
                    "When Reclamation Sage enters the battlefield, you may destroy target artifact or enchantment.",
                    ["G"],
                    "https://scryfall.com/card/cmm/317/reclamation-sage"),
                IngaFixtureCard(
                    "Counterspell",
                    1,
                    DeckRoles.Interaction,
                    "Instant",
                    2,
                    "Counter target spell.",
                    ["U"],
                    "https://scryfall.com/card/dmr/45/counterspell"),
                IngaFixtureCard(
                    "Craterhoof Behemoth",
                    1,
                    DeckRoles.Wincons,
                    "Creature - Beast",
                    8,
                    "When Craterhoof Behemoth enters the battlefield, creatures you control get +X/+X and gain trample until end of turn.",
                    ["G"],
                    "https://scryfall.com/card/mm3/122/craterhoof-behemoth"),
                IngaFixtureCard(
                    "Overwhelming Stampede",
                    1,
                    DeckRoles.Wincons,
                    "Sorcery",
                    5,
                    "Until end of turn, creatures you control gain trample and get +X/+X.",
                    ["G"],
                    "https://scryfall.com/card/cmm/309/overwhelming-stampede"),
                IngaFixtureCard(
                    "Finale of Devastation",
                    1,
                    DeckDefaults.Sideboard,
                    "Sorcery",
                    2,
                    "Search your library and/or graveyard for a creature card with mana value X or less and put it onto the battlefield.",
                    ["G"],
                    "https://scryfall.com/card/war/160/finale-of-devastation"),
                IngaFixtureCard(
                    "Hydroid Krasis",
                    1,
                    DeckDefaults.Maybeboard,
                    "Creature - Jellyfish Hydra Beast",
                    2,
                    "When you cast this spell, you gain half X life and draw half X cards.",
                    ["G", "U"],
                    "https://scryfall.com/card/rna/183/hydroid-krasis"),
            ],
        };
    }

    /// <summary>
    /// Creates a small sourced workspace used for plan clone validation.
    /// </summary>
    private static DeckWorkspace CreatePlanCloneWorkspace(string id, string sourceDeckId)
    {
        return new DeckWorkspace
        {
            Id = id,
            Name = $"Clone {id}",
            Format = "commander",
            SourceReferences =
            [
                new DeckSourceReference
                {
                    Provider = DeckImportProviders.Archidekt,
                    ExternalId = sourceDeckId,
                    Url = $"https://archidekt.com/decks/{sourceDeckId}/inga_and_esika"
                }
            ],
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Interaction, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                IngaFixtureCard(
                    "Inga and Esika",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Human God",
                    4,
                    "Creatures you control have vigilance and draw cards when creature mana casts creatures.",
                    ["G", "U"],
                    "https://scryfall.com/card/mom/229/inga-and-esika"),
                IngaFixtureCard(
                    "Beast Whisperer",
                    1,
                    DeckRoles.Draw,
                    "Creature - Elf Druid",
                    4,
                    "Whenever you cast a creature spell, draw a card.",
                    ["G"],
                    "https://scryfall.com/card/grn/123/beast-whisperer"),
                IngaFixtureCard(
                    "Counterspell",
                    1,
                    DeckRoles.Interaction,
                    "Instant",
                    2,
                    "Counter target spell.",
                    ["U"],
                    "https://scryfall.com/card/2xm/50/counterspell"),
            ]
        };
    }

    /// <summary>
    /// Creates a card row for the Inga and Esika offline fixture.
    /// </summary>
    private static DeckCard IngaFixtureCard(
        string name,
        int quantity,
        string category,
        string typeLine,
        double manaValue,
        string oracleText,
        IReadOnlyList<string> colorIdentity,
        string scryfallUri,
        IReadOnlyList<string>? producedMana = null,
        IReadOnlyList<string>? extraCategories = null)
    {
        List<string> categories = [category];
        if (extraCategories is not null)
        {
            foreach (string extraCategory in extraCategories)
            {
                if (!categories.Contains(extraCategory, StringComparer.OrdinalIgnoreCase))
                {
                    categories.Add(extraCategory);
                }
            }
        }

        return new DeckCard
        {
            Name = name,
            Quantity = quantity,
            PrimaryCategory = category,
            Categories = categories,
            ScryfallId = name.ToLowerInvariant().Replace(' ', '-'),
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                ManaValue = manaValue,
                OracleText = oracleText,
                ColorIdentity = colorIdentity.ToList(),
                ProducedMana = producedMana?.ToList() ?? [],
                ScryfallUri = scryfallUri,
                Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["usd"] = manaValue >= 8 ? "12.00" : "1.00",
                },
            },
        };
    }

    /// <summary>
    /// Reads a file from the repository root.
    /// </summary>
    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }

    /// <summary>
    /// Creates an expensive ramp fixture.
    /// </summary>
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
                ScryfallUri = "https://scryfall.test/card/Mana%20Crypt",
                EdhrecRank = 20,
                Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["usd"] = "180"
                }
            }
        };
    }

    /// <summary>
    /// Verifies that a Quill description still contains rich content.
    /// </summary>
    private static void AssertRichQuillContent(string description)
    {
        using JsonDocument document = JsonDocument.Parse(description);
        JsonElement ops = document.RootElement.GetProperty("ops");

        ops.GetArrayLength().Should().BeGreaterThan(3);
        ops.EnumerateArray().Any(HasBoldAttribute).Should().BeTrue();
        ops.EnumerateArray().Any(HasItalicAttribute).Should().BeTrue();
        ops.EnumerateArray().Any(HasImageInsert).Should().BeTrue();
    }

    /// <summary>
    /// Checks whether an op has a bold attribute.
    /// </summary>
    private static bool HasBoldAttribute(JsonElement op)
    {
        return op.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("bold", out JsonElement bold)
            && bold.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Checks whether an op has an italic attribute.
    /// </summary>
    private static bool HasItalicAttribute(JsonElement op)
    {
        return op.TryGetProperty("attributes", out JsonElement attributes)
            && attributes.TryGetProperty("italic", out JsonElement italic)
            && italic.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Checks whether an op has an image insert.
    /// </summary>
    private static bool HasImageInsert(JsonElement op)
    {
        return op.TryGetProperty("insert", out JsonElement insert)
            && insert.ValueKind == JsonValueKind.Object
            && insert.TryGetProperty("image", out JsonElement image)
            && image.GetString() == "https://example.test/card.jpg";
    }

}
