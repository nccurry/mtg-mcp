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
