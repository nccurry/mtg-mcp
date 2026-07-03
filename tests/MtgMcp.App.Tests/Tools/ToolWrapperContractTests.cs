using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App.Tests.Tools;

/// <summary>
/// Verifies thin MCP wrappers preserve Core errors and operation-mode guards without network access.
/// </summary>
public sealed class ToolWrapperContractTests
{
    /// <summary>
    /// Verifies apply-mode card and category wrappers delegate to Core without hiding missing-workspace errors.
    /// </summary>
    [Fact]
    public async Task CardAndCategoryMutationWrappers_PropagateMissingWorkspaceErrors()
    {
        using IHost host = MtgMcpHost.Build([]);
        DeckWorkspaceService decks = host.Services.GetRequiredService<DeckWorkspaceService>();
        OperationModeGuard apply = new(Options.Create(new MtgMcpOptions
        {
            OperationMode = OperationModeGuard.Apply
        }));
        CategoryTools categories = new(decks, apply);
        DeckMutationTools mutations = new(decks, apply);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await categories.AddCardCategoryAsync(workspaceId, "Sol Ring", "Ramp"),
            async () => _ = await categories.UpdateCardCategoriesBulkAsync(
                workspaceId,
                [new BulkCardCategoryChange { CardName = "Sol Ring", Category = "Ramp" }]),
            async () => _ = await categories.RemoveCardCategoryAsync(workspaceId, "Sol Ring", "Ramp"),
            async () => _ = await categories.SetPrimaryCardCategoryAsync(workspaceId, "Sol Ring", "Ramp"),
            async () => _ = await categories.CreateCategoryAsync(workspaceId, "Ramp"),
            async () => _ = await categories.RenameCategoryAsync(workspaceId, "Ramp", "Mana"),
            async () => _ = await categories.DeleteCategoryAsync(workspaceId, "Ramp"),
            async () => _ = await mutations.AddCardAsync(workspaceId, "Sol Ring"),
            async () => _ = await mutations.AddCardsBulkAsync(
                workspaceId,
                [new BulkDeckCardAdd { CardName = "Sol Ring" }]),
            async () => _ = await mutations.RemoveCardAsync(workspaceId, "Sol Ring"),
            async () => _ = await mutations.SetCardQuantityAsync(workspaceId, "Sol Ring", 1),
            async () => _ = await mutations.MoveCardAsync(workspaceId, "Sol Ring", DeckDefaults.Maybeboard),
            async () => _ = await mutations.MoveCardsBulkAsync(
                workspaceId,
                [new BulkDeckCardMove { CardName = "Sol Ring", ToCategory = DeckDefaults.Maybeboard }]),
            async () => _ = await mutations.UpdateDeckMetadataAsync(workspaceId, name: "Updated")
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        }

        Func<Task> read = async () => _ = await categories.ListCardsByCategoryAsync(workspaceId, "Ramp");
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    /// <summary>
    /// Verifies workspace read wrappers preserve Core failures and parsing remains side-effect free.
    /// </summary>
    [Fact]
    public async Task WorkspaceReadWrappers_PropagateMissingWorkspaceErrors()
    {
        using IHost host = MtgMcpHost.Build([]);
        WorkspaceTools workspaces = ActivatorUtilities.CreateInstance<WorkspaceTools>(host.Services);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await workspaces.OpenLocalDeckAsync(workspaceId),
            async () => _ = await workspaces.ListCardsByZoneAsync(workspaceId),
            async () => _ = await workspaces.ExportDeckAsync(workspaceId),
            async () => _ = await workspaces.ValidateDeckAsync(workspaceId),
            async () => _ = await workspaces.ValidateLegalityAsync(workspaceId),
            async () => _ = await workspaces.DiffWorkspacesAsync(workspaceId, "missing-baseline"),
            async () => _ = await workspaces.DiffLastImportAsync(workspaceId),
            async () => _ = await workspaces.AnalyzeDeckAsync(workspaceId),
            async () => _ = await workspaces.CopyWorkspaceToArchidektAsync(workspaceId)
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        }

        ParsedDecklist parsed = workspaces.ParseDecklist("1 Sol Ring\n2 Island");
        parsed.Cards.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies default plan mode blocks workspace writes before storage or provider access.
    /// </summary>
    [Fact]
    public async Task WorkspaceMutationWrappers_DefaultPlanModeBlocksBeforeWork()
    {
        using IHost host = MtgMcpHost.Build([]);
        WorkspaceTools workspaces = ActivatorUtilities.CreateInstance<WorkspaceTools>(host.Services);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await workspaces.CreateLocalDeckAsync("Blocked"),
            async () => _ = await workspaces.StartDeckWorkspaceAsync(mode: "local", name: "Blocked"),
            async () => _ = await workspaces.RefreshWorkspaceFromSourceAsync(workspaceId),
            async () => _ = await workspaces.OpenArchidektDeckAsync("123", writeBack: false),
            async () => _ = await workspaces.ReopenWorkspaceWithWritebackAsync(workspaceId),
            async () => _ = await workspaces.OpenMoxfieldDeckAsync("fixture"),
            async () => _ = await workspaces.CreateArchidektDeckAsync("Blocked"),
            async () => _ = await workspaces.CopyWorkspaceToArchidektAsync(workspaceId, dryRun: false),
            async () => _ = await workspaces.CreateArchidektFolderAsync("Blocked"),
            async () => _ = await workspaces.MoveArchidektDecksAsync(["123"]),
            async () => _ = await workspaces.ImportDecklistAsync("1 Sol Ring", "Blocked")
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<OperationModeBlockedException>()
                .Where(exception => exception.CurrentMode == OperationModeGuard.Plan);
        }
    }

    /// <summary>
    /// Verifies analysis, simulation, facet, and corpus wrappers reject a missing workspace consistently.
    /// </summary>
    [Fact]
    public async Task ReadOnlyToolWrappers_PropagateMissingWorkspaceErrors()
    {
        using IHost host = MtgMcpHost.Build([]);
        AnalysisTools analysis = ActivatorUtilities.CreateInstance<AnalysisTools>(host.Services);
        SimulationTools simulation = ActivatorUtilities.CreateInstance<SimulationTools>(host.Services);
        FacetTools facets = ActivatorUtilities.CreateInstance<FacetTools>(host.Services);
        CorpusTools corpus = ActivatorUtilities.CreateInstance<CorpusTools>(host.Services);
        RecommendationTools recommendations = ActivatorUtilities.CreateInstance<RecommendationTools>(host.Services);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await analysis.SummarizeDeckWorkspaceAsync(workspaceId),
            async () => _ = await analysis.ExplainRoleCountsAsync(workspaceId, "Ramp"),
            async () => _ = await analysis.RefreshDeckCardSnapshotsAsync(workspaceId),
            async () => _ = await analysis.ReviewWeakSpotsAsync(workspaceId),
            async () => _ = await analysis.AnalyzeDrawOddsAsync(workspaceId),
            async () => _ = await analysis.AnalyzeLandDropOddsAsync(workspaceId),
            async () => _ = await analysis.AnalyzeDeckCostAsync(workspaceId),
            async () => _ = await analysis.EstimateCommanderBracketAsync(workspaceId),
            async () => _ = await analysis.AnalyzeManaBaseAsync(workspaceId),
            async () => _ = await analysis.AnalyzeDeckConsistencyAsync(workspaceId),
            async () => _ = await analysis.AnalyzeDeckBestPracticesAsync(workspaceId),
            async () => _ = await analysis.AnalyzeCombosAsync(workspaceId),
            async () => _ = await analysis.ClassifyWinRoutesAsync(workspaceId: workspaceId),
            async () => _ = await simulation.ProjectBoardStateAsync(workspaceId),
            async () => _ = await simulation.EstimateWinTurnAsync(workspaceId),
            async () => _ = await facets.GetDeckFacetsAsync(workspaceId),
            async () => _ = await facets.CountDeckCardsMatchingAsync(workspaceId, "{}"),
            async () => _ = await facets.ExplainCardMatchAsync(workspaceId, "Sol Ring", "{}"),
            async () => _ = await facets.SetCardFacetAnnotationsAsync(workspaceId, "Sol Ring", userTags: ["ramp"]),
            async () => _ = await corpus.AnalyzeCommanderTrendsAsync(workspaceId),
            async () => _ = await corpus.FindLesserKnownCardsAsync(workspaceId),
            async () => _ = await corpus.FindTopExemplarDecksAsync(workspaceId),
            async () => _ = await corpus.ExplainCardCorpusSignalAsync(workspaceId, "Sol Ring"),
            async () => _ = await corpus.SearchCorpusEvidenceAsync(workspaceId, "edhrec"),
            async () => _ = await recommendations.ReviewNewCardSwapsAsync(workspaceId),
            async () => _ = await recommendations.QueryCardsForDeckAsync(workspaceId, "ramp", "o:mana"),
            async () => _ = await recommendations.EvaluateCardAsync(workspaceId, "Sol Ring")
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        }

        corpus.ListCorpusSources().Sources.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies recommendation wrappers enforce caller-controlled sources before provider or workspace access.
    /// </summary>
    [Fact]
    public async Task RecommendationWrappers_ValidateRoutesAndCandidateSources()
    {
        using IHost host = MtgMcpHost.Build([]);
        RecommendationTools recommendations = ActivatorUtilities.CreateInstance<RecommendationTools>(host.Services);

        Func<Task> unsupportedRoute = async () =>
            _ = await recommendations.FindWinconPayoffsAsync("unsupported", "UG");
        Func<Task> blankSource = async () =>
            _ = await recommendations.ScoreCardsForPlaygroupMetaAsync("workspace", "20", " ");
        Func<Task> missingExplicitCards = async () =>
            _ = await recommendations.ScoreCardsForPlaygroupMetaAsync(
                "workspace",
                "20",
                "explicit-cards");
        Func<Task> unexpectedExcludedCards = async () =>
            _ = await recommendations.ScoreCardsForPlaygroupMetaAsync(
                "workspace",
                "20",
                "excluded-workspace-cards",
                ["Sol Ring"]);
        Func<Task> unknownSource = async () =>
            _ = await recommendations.ScoreCardsForPlaygroupMetaAsync("workspace", "20", "generated");

        await unsupportedRoute.Should().ThrowAsync<ArgumentException>().WithParameterName("route");
        await blankSource.Should().ThrowAsync<ArgumentException>().WithParameterName("candidateSource");
        await missingExplicitCards.Should().ThrowAsync<ArgumentException>().WithParameterName("candidateCards");
        await unexpectedExcludedCards.Should().ThrowAsync<ArgumentException>().WithParameterName("candidateCards");
        await unknownSource.Should().ThrowAsync<ArgumentException>().WithParameterName("candidateSource");
    }

    /// <summary>
    /// Verifies plan mode blocks every mutating checkpoint wrapper before repository or adapter work.
    /// </summary>
    [Fact]
    public async Task CheckpointMutationWrappers_DefaultPlanModeBlocksBeforeWork()
    {
        using IHost host = MtgMcpHost.Build([]);
        CheckpointTools checkpoints = ActivatorUtilities.CreateInstance<CheckpointTools>(host.Services);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await checkpoints.CreateWorkspaceCheckpointAsync(workspaceId, "Before edits"),
            async () => _ = await checkpoints.RestoreWorkspaceCheckpointAsync(workspaceId, "checkpoint"),
            () => checkpoints.DeleteWorkspaceCheckpointAsync(workspaceId, "checkpoint"),
            async () => _ = await checkpoints.CheckpointDeckAsync(workspaceId, "Before edits"),
            async () => _ = await checkpoints.RenameDeckCheckpointAsync(
                workspaceId,
                "checkpoint",
                "Renamed"),
            () => checkpoints.DeleteDeckCheckpointAsync(workspaceId, "checkpoint")
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<OperationModeBlockedException>()
                .Where(exception => exception.CurrentMode == OperationModeGuard.Plan);
        }
    }

    /// <summary>
    /// Verifies apply-mode checkpoint wrappers preserve missing-workspace failures from Core.
    /// </summary>
    [Fact]
    public async Task CheckpointMutationWrappers_ApplyModeDelegatesToCore()
    {
        using IHost host = MtgMcpHost.Build([]);
        DeckWorkspaceService decks = host.Services.GetRequiredService<DeckWorkspaceService>();
        OperationModeGuard apply = new(Options.Create(new MtgMcpOptions
        {
            OperationMode = OperationModeGuard.Apply
        }));
        CheckpointTools checkpoints = new(decks, apply);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await checkpoints.CreateWorkspaceCheckpointAsync(workspaceId, "Before edits"),
            async () => _ = await checkpoints.RestoreWorkspaceCheckpointAsync(workspaceId, "checkpoint"),
            () => checkpoints.DeleteWorkspaceCheckpointAsync(workspaceId, "checkpoint"),
            async () => _ = await checkpoints.CheckpointDeckAsync(workspaceId, "Before edits"),
            async () => _ = await checkpoints.RenameDeckCheckpointAsync(
                workspaceId,
                "checkpoint",
                "Renamed"),
            () => checkpoints.DeleteDeckCheckpointAsync(workspaceId, "checkpoint")
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        }
    }

    /// <summary>
    /// Verifies checkpoint read wrappers preserve missing-workspace failures from Core.
    /// </summary>
    [Fact]
    public async Task CheckpointReadWrappers_PropagateMissingWorkspaceErrors()
    {
        using IHost host = MtgMcpHost.Build([]);
        CheckpointTools checkpoints = ActivatorUtilities.CreateInstance<CheckpointTools>(host.Services);
        const string workspaceId = "missing-wrapper-workspace";

        List<Func<Task>> actions =
        [
            async () => _ = await checkpoints.ListWorkspaceCheckpointsAsync(workspaceId),
            async () => _ = await checkpoints.GetWorkspaceCheckpointAsync(workspaceId, "checkpoint"),
            async () => _ = await checkpoints.ListDeckCheckpointsAsync(workspaceId),
            async () => _ = await checkpoints.GetDeckCheckpointAsync(workspaceId, "checkpoint")
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        }
    }

    /// <summary>
    /// Verifies Playgroup wrappers normalize ids and expose derived empty collections without network access.
    /// </summary>
    [Fact]
    public async Task PlaygroupWrappers_MapIdsAndDelegateToTheAggregationService()
    {
        FakePlaygroupGateway gateway = new();
        PlaygroupTools tools = new(new PlaygroupService(gateway));

        PlaygroupAuthStatus auth = await tools.GetPlaygroupAuthStatusAsync();
        PlaygroupSummary playgroup = await tools.GetPlaygroupAsync("https://playgroup.gg/playgroups/20", userId: 10);
        PlaygroupDeck numericDeck = await tools.GetPlaygroupDeckAsync("101");
        PlaygroupDeck urlDeck = await tools.GetPlaygroupDeckAsync("https://playgroup.gg/profiles/user/decks/102");
        PlaygroupDeckListResult decks = await tools.ListPlaygroupDecksAsync("20");
        PlaygroupUserListResult users = await tools.ListPlaygroupUsersAsync("20");
        PlaygroupUserDeckListResult userDecks = await tools.ListPlaygroupUserDecksAsync("20", "10");
        PlaygroupDeckRankingResult ranking = await tools.RankPlaygroupDecksAsync("20");

        auth.HasApiKey.Should().BeTrue();
        playgroup.Id.Should().Be(20);
        numericDeck.Id.Should().Be(101);
        urlDeck.Id.Should().Be(102);
        decks.Decks.Should().BeEmpty();
        users.Users.Should().BeEmpty();
        userDecks.Decks.Should().ContainSingle().Which.DeckId.Should().Be(101);
        ranking.Rankings.Should().BeEmpty();

        Func<Task> blank = async () => _ = await tools.GetPlaygroupDeckAsync(" ");
        Func<Task> invalid = async () => _ = await tools.GetPlaygroupDeckAsync("not-a-deck");
        await blank.Should().ThrowAsync<ArgumentException>().WithParameterName("deckIdOrUrl");
        await invalid.Should().ThrowAsync<ArgumentException>().WithParameterName("deckIdOrUrl");
    }

    /// <summary>
    /// Supplies deterministic Playgroup responses to wrapper tests.
    /// </summary>
    private sealed class FakePlaygroupGateway : IPlaygroupGateway
    {
        /// <inheritdoc/>
        public Task<PlaygroupAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupAuthStatus { HasApiKey = true });
        }

        /// <inheritdoc/>
        public Task<PlaygroupUser> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupUser { Id = 10, Username = "user" });
        }

        /// <inheritdoc/>
        public Task<PlaygroupSummary> GetUserPlaygroupAsync(
            long userId,
            long playgroupId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupSummary { Id = playgroupId, Name = $"User {userId}" });
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<PlaygroupGame>> ListPlaygroupGamesAsync(
            long playgroupId,
            int page,
            int limit,
            bool includeEvents,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PlaygroupGame>>([]);
        }

        /// <inheritdoc/>
        public Task<PlaygroupDeck> GetDeckAsync(long deckId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupDeck { Id = deckId, Name = $"Deck {deckId}" });
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<PlaygroupDeck>> ListUserDecksAsync(
            long userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PlaygroupDeck>>(
            [
                new PlaygroupDeck
                {
                    Id = 101,
                    UserId = userId,
                    Name = "Fixture Deck",
                    DecklistUrl = "https://archidekt.com/decks/101"
                }
            ]);
        }

        /// <inheritdoc/>
        public Task<PlaygroupEloHistory> GetDeckEloHistoryAsync(
            long deckId,
            long? playgroupId,
            long? leagueId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupEloHistory { DeckId = deckId });
        }
    }
}
