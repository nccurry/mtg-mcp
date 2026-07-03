using System.Globalization;
using FluentAssertions;

namespace MtgMcp.Core.Tests.Playgroups;

/// <summary>
/// Contains tests for Playgroup service aggregation behavior.
/// </summary>
public sealed class PlaygroupServiceTests
{
    /// <summary>
    /// Verifies that playgroup URLs are parsed and the current user is used when no user id is supplied.
    /// </summary>
    [Fact]
    public async Task GetPlaygroup_ParsesUrlAndDiscoversCurrentUser()
    {
        FakePlaygroupGateway gateway = new()
        {
            CurrentUser = new PlaygroupUser { Id = 7, Username = "chase" },
            Playgroup = new PlaygroupSummary { Id = 49295, Name = "Heaters" },
        };
        PlaygroupService service = new(gateway);

        PlaygroupSummary playgroup = await service.GetPlaygroupAsync(
            "https://playgroup.gg/playgroups/49295-heaters",
            null,
            TestContext.Current.CancellationToken
        );

        playgroup.Name.Should().Be("Heaters");
        gateway.PlaygroupRequests.Should().ContainSingle()
            .Which.Should().Be((7, 49295));
    }

    /// <summary>
    /// Verifies that deck lists are derived from game participations and enriched with deck details.
    /// </summary>
    [Fact]
    public async Task ListDecks_DerivesDecksAndEnrichesDetails()
    {
        FakePlaygroupGateway gateway = CreateGatewayWithGames();
        PlaygroupService service = new(gateway);

        PlaygroupDeckListResult result = await service.ListDecksAsync(
            "49295-heaters",
            maxGames: 100,
            limit: 10,
            TestContext.Current.CancellationToken
        );

        result.PlaygroupId.Should().Be(49295);
        result.FetchedGames.Should().Be(2);
        result.Warnings.Should().Contain(warning => warning.Contains("derived from fetched game participations", StringComparison.Ordinal));
        result.Decks.Should().HaveCount(2);
        PlaygroupDeckSummary alesha = result.Decks.Single(deck => deck.DeckId == 101);
        alesha.Name.Should().Be("Alesha");
        alesha.OwnerName.Should().Be("Nick");
        alesha.CommanderNames.Should().Equal("Alesha, Who Smiles at Death");
        alesha.FetchedPlaygroupGames.Should().Be(2);
        alesha.FetchedPlaygroupWins.Should().Be(1);
        alesha.Elo.Should().Be(1567);
        alesha.EstimatedPower.Should().Be(7.4);
    }

    /// <summary>
    /// Verifies that deck enrichment overlaps bounded Playgroup detail requests while preserving output order.
    /// </summary>
    [Fact]
    public async Task ListDecks_EnrichesDetailsWithBoundedParallelism()
    {
        FakePlaygroupGateway gateway = new()
        {
            ReleaseDeckDetailsAfterStartedCount = 2,
        };
        List<PlaygroupParticipation> participations = [];
        for (int index = 0; index < 6; index++)
        {
            long deckId = 1_000 + index;
            participations.Add(new PlaygroupParticipation
            {
                DeckId = deckId,
                DeckName = $"Deck {index}",
                UserId = 10 + index,
                UserName = $"Player {index}",
            });
            gateway.Decks[deckId] = new PlaygroupDeck
            {
                Id = deckId,
                Name = $"Deck {index}",
                PowerLevel = 5 + index,
                ConfidenceFactor = 0.9,
            };
            gateway.Elo[deckId] = new PlaygroupEloHistory
            {
                DeckId = deckId,
                CurrentRating = 1_500 + index,
            };
        }

        gateway.Games.Add(new PlaygroupGame { Id = 1, Participations = participations });
        PlaygroupService service = new(gateway);

        PlaygroupDeckListResult result = await service.ListDecksAsync(
            "49295",
            maxGames: 10,
            limit: 6,
            TestContext.Current.CancellationToken
        );

        result.Decks.Select(deck => deck.DeckId).Should().Equal(1000, 1001, 1002, 1003, 1004, 1005);
        gateway.MaxConcurrentDeckDetailRequests.Should().BeGreaterThan(1);
    }

    /// <summary>
    /// Verifies that playgroup users are derived from fetched game participations.
    /// </summary>
    [Fact]
    public async Task ListUsers_DerivesUsersFromParticipations()
    {
        FakePlaygroupGateway gateway = CreateGatewayWithGames();
        PlaygroupService service = new(gateway);

        PlaygroupUserListResult result = await service.ListUsersAsync(
            "49295",
            maxGames: 100,
            limit: 10,
            TestContext.Current.CancellationToken
        );

        result.PlaygroupId.Should().Be(49295);
        result.FetchedGames.Should().Be(2);
        result.Warnings.Should().Contain(warning => warning.Contains("derived from fetched game participations", StringComparison.Ordinal));
        result.Users.Should().HaveCount(2);
        PlaygroupUserSummary nick = result.Users.Single(user => user.UserName == "Nick");
        nick.UserId.Should().Be(10);
        nick.FetchedPlaygroupGames.Should().Be(2);
        nick.DecksSeen.Should().Be(1);
    }

    /// <summary>
    /// Verifies that user deck listing resolves names and filters Archidekt deck URLs.
    /// </summary>
    [Fact]
    public async Task ListUserDecks_ResolvesNameAndFiltersArchidektUrls()
    {
        FakePlaygroupGateway gateway = CreateGatewayWithGames();
        gateway.UserDecks[10] =
        [
            new PlaygroupDeck
            {
                Id = 101,
                Name = "Alesha",
                UserId = 10,
                DecklistUrl = "https://archidekt.com/decks/101",
                Commander = new PlaygroupCommander { Name = "Alesha, Who Smiles at Death" },
            },
            new PlaygroupDeck
            {
                Id = 404,
                Name = "Other Site Brew",
                UserId = 10,
                DecklistUrl = "https://moxfield.com/decks/404",
            },
        ];
        PlaygroupService service = new(gateway);

        PlaygroupUserDeckListResult result = await service.ListUserDecksAsync(
            "49295",
            "nick",
            PlaygroupUserDeckSources.Archidekt,
            maxGames: 100,
            limit: 10,
            TestContext.Current.CancellationToken
        );

        result.UserId.Should().Be(10);
        result.UserName.Should().Be("Nick");
        result.Source.Should().Be(PlaygroupUserDeckSources.Archidekt);
        result.Decks.Should().ContainSingle();
        result.Decks[0].DeckId.Should().Be(101);
        result.Decks[0].DecklistUrl.Should().Be("https://archidekt.com/decks/101");
        result.Decks[0].FetchedPlaygroupGames.Should().Be(2);
        result.Warnings.Should().Contain(warning => warning.Contains("source filter", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that ambiguous observed user names fail with candidate ids.
    /// </summary>
    [Fact]
    public async Task ListUserDecks_ReportsAmbiguousObservedNames()
    {
        FakePlaygroupGateway gateway = CreateGatewayWithGames();
        gateway.Games.Add(
            new PlaygroupGame
            {
                Id = 3,
                Participations =
                [
                    new PlaygroupParticipation
                    {
                        Id = 5,
                        DeckId = 303,
                        UserId = 12,
                        DeckName = "Niv",
                        UserName = "Nick Two",
                    },
                ],
            }
        );
        PlaygroupService service = new(gateway);

        Func<Task> act = () => service.ListUserDecksAsync(
            "49295",
            "ni",
            PlaygroupUserDeckSources.Any,
            maxGames: 100,
            limit: 10,
            TestContext.Current.CancellationToken
        );

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ambiguous*10:Nick*12:Nick Two*");
    }

    /// <summary>
    /// Verifies that powerful-deck ranking filters low-confidence and low-sample decks.
    /// </summary>
    [Fact]
    public async Task RankDecks_FiltersLowConfidenceAndMinimumGames()
    {
        FakePlaygroupGateway gateway = CreateGatewayWithGames();
        gateway.Decks[303] = new PlaygroupDeck
        {
            Id = 303,
            Name = "Tiny Sample",
            PowerLevel = 9.9,
            ConfidenceFactor = 0.95,
        };
        gateway.Elo[303] = new PlaygroupEloHistory { DeckId = 303, CurrentRating = 1700 };
        gateway.Games.Add(
            new PlaygroupGame
            {
                Id = 3,
                EndedAt = DateTimeOffset.Parse(
                    "2026-05-22T02:00:00Z",
                    CultureInfo.InvariantCulture
                ),
                Participations =
                [
                    new PlaygroupParticipation
                    {
                        Id = 5,
                        DeckId = 303,
                        UserId = 12,
                        DeckName = "Tiny Sample",
                        UserName = "Alex",
                    },
                ],
            }
        );
        PlaygroupService service = new(gateway);

        PlaygroupDeckRankingResult result = await service.RankDecksAsync(
            "49295",
            PlaygroupDeckRankingMetrics.EstimatedPower,
            minGames: 2,
            includeLowConfidence: false,
            maxGames: 100,
            limit: 10,
            TestContext.Current.CancellationToken
        );

        result.Rankings.Should().ContainSingle();
        result.Rankings[0].Deck.DeckId.Should().Be(101);
        result.Rankings[0].Score.Should().Be(7.4);
        result.Warnings.Should().Contain(warning => warning.Contains("low-confidence", StringComparison.OrdinalIgnoreCase));
        result.Warnings.Should().Contain(warning => warning.Contains("fewer than 2", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that average-win-turn ranking treats lower turn counts as stronger.
    /// </summary>
    [Fact]
    public async Task RankDecks_SortsAverageWinTurnAscending()
    {
        FakePlaygroupGateway gateway = CreateGatewayWithGames();
        PlaygroupService service = new(gateway);

        PlaygroupDeckRankingResult result = await service.RankDecksAsync(
            "49295",
            PlaygroupDeckRankingMetrics.AverageWinTurn,
            minGames: 0,
            includeLowConfidence: true,
            maxGames: 100,
            limit: 10,
            TestContext.Current.CancellationToken
        );

        result.Rankings.Select(ranking => ranking.Deck.DeckId).Should().Equal(202, 101);
        result.Rankings.Select(ranking => ranking.Score).Should().Equal(5, 7);
    }

    /// <summary>
    /// Creates a fake gateway with repeated playgroup deck participations.
    /// </summary>
    private static FakePlaygroupGateway CreateGatewayWithGames()
    {
        FakePlaygroupGateway gateway = new();
        gateway.Games.AddRange(
            [
                new PlaygroupGame
                {
                    Id = 1,
                    EndedAt = DateTimeOffset.Parse(
                        "2026-05-20T02:00:00Z",
                        CultureInfo.InvariantCulture
                    ),
                    Participations =
                    [
                        new PlaygroupParticipation
                        {
                            Id = 1,
                            Winner = true,
                            DeckId = 101,
                            UserId = 10,
                            DeckName = "Alesha",
                            UserName = "Nick",
                        },
                        new PlaygroupParticipation
                        {
                            Id = 2,
                            DeckId = 202,
                            UserId = 11,
                            DeckName = "Yuriko",
                            UserName = "Sam",
                        },
                    ],
                },
                new PlaygroupGame
                {
                    Id = 2,
                    EndedAt = DateTimeOffset.Parse(
                        "2026-05-21T02:00:00Z",
                        CultureInfo.InvariantCulture
                    ),
                    Participations =
                    [
                        new PlaygroupParticipation
                        {
                            Id = 3,
                            DeckId = 101,
                            UserId = 10,
                            DeckName = "Alesha",
                            UserName = "Nick",
                        },
                        new PlaygroupParticipation
                        {
                            Id = 4,
                            Winner = true,
                            DeckId = 202,
                            UserId = 11,
                            DeckName = "Yuriko",
                            UserName = "Sam",
                        },
                    ],
                },
            ]
        );
        gateway.Decks[101] = new PlaygroupDeck
        {
            Id = 101,
            Name = "Alesha",
            UserId = 10,
            GamesWon = 5,
            GamesLost = 3,
            WinRatePercentage = 62.5,
            PowerLevel = 7.4,
            ConfidenceFactor = 0.91,
            AverageWinsByRound = 7,
            Commander = new PlaygroupCommander { Name = "Alesha, Who Smiles at Death" },
        };
        gateway.Decks[202] = new PlaygroupDeck
        {
            Id = 202,
            Name = "Yuriko",
            UserId = 11,
            GamesWon = 10,
            GamesLost = 2,
            WinRatePercentage = 83.3,
            PowerLevel = 8.2,
            ConfidenceFactor = 0.2,
            AverageWinsByRound = 5,
            Commander = new PlaygroupCommander { Name = "Yuriko, the Tiger's Shadow" },
        };
        gateway.Elo[101] = new PlaygroupEloHistory { DeckId = 101, CurrentRating = 1567 };
        gateway.Elo[202] = new PlaygroupEloHistory { DeckId = 202, CurrentRating = 1601 };
        return gateway;
    }

    /// <summary>
    /// Provides deterministic Playgroup gateway data for service tests.
    /// </summary>
    private sealed class FakePlaygroupGateway : IPlaygroupGateway
    {
        /// <summary>
        /// Releases blocked fake deck-detail requests once the configured request count has started.
        /// </summary>
        private readonly TaskCompletionSource<object?> deckDetailBarrier = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Coordinates fake deck-detail concurrency counters.
        /// </summary>
        private readonly object deckDetailLock = new();

        /// <summary>
        /// Tracks currently running fake deck-detail requests.
        /// </summary>
        private int activeDeckDetailRequests;

        /// <summary>
        /// Stores the largest overlapping fake deck-detail request count observed.
        /// </summary>
        private int maxConcurrentDeckDetailRequests;

        /// <summary>
        /// Counts fake deck-detail requests that reached the deterministic test barrier.
        /// </summary>
        private int deckDetailBarrierStartedCount;

        /// <summary>
        /// Gets or sets the current user returned by the fake gateway.
        /// </summary>
        public PlaygroupUser CurrentUser { get; set; } = new() { Id = 1, Username = "user" };

        /// <summary>
        /// Gets or sets the playgroup returned by the fake gateway.
        /// </summary>
        public PlaygroupSummary Playgroup { get; set; } = new() { Id = 49295, Name = "Heaters" };

        /// <summary>
        /// Gets fake playgroup games.
        /// </summary>
        public List<PlaygroupGame> Games { get; } = [];

        /// <summary>
        /// Gets fake deck details keyed by deck id.
        /// </summary>
        public Dictionary<long, PlaygroupDeck> Decks { get; } = [];

        /// <summary>
        /// Gets fake Elo histories keyed by deck id.
        /// </summary>
        public Dictionary<long, PlaygroupEloHistory> Elo { get; } = [];

        /// <summary>
        /// Gets fake accessible user decks keyed by user id.
        /// </summary>
        public Dictionary<long, IReadOnlyList<PlaygroupDeck>> UserDecks { get; } = [];

        /// <summary>
        /// Gets playgroup summary requests made by the service.
        /// </summary>
        public List<(long UserId, long PlaygroupId)> PlaygroupRequests { get; } = [];

        /// <summary>
        /// Gets or sets the number of started deck details required before the fake barrier releases.
        /// </summary>
        public int ReleaseDeckDetailsAfterStartedCount { get; set; }

        /// <summary>
        /// Gets the largest number of overlapping fake deck-detail requests observed.
        /// </summary>
        public int MaxConcurrentDeckDetailRequests
        {
            get
            {
                lock (deckDetailLock)
                {
                    return maxConcurrentDeckDetailRequests;
                }
            }
        }

        /// <summary>
        /// Returns configured fake authentication status.
        /// </summary>
        public Task<PlaygroupAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlaygroupAuthStatus { HasApiKey = true });
        }

        /// <summary>
        /// Returns the configured fake current user.
        /// </summary>
        public Task<PlaygroupUser> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CurrentUser);
        }

        /// <summary>
        /// Records and returns the configured fake playgroup.
        /// </summary>
        public Task<PlaygroupSummary> GetUserPlaygroupAsync(
            long userId,
            long playgroupId,
            CancellationToken cancellationToken
        )
        {
            PlaygroupRequests.Add((userId, playgroupId));
            return Task.FromResult(Playgroup);
        }

        /// <summary>
        /// Returns fake games on the first requested page.
        /// </summary>
        public Task<IReadOnlyList<PlaygroupGame>> ListPlaygroupGamesAsync(
            long playgroupId,
            int page,
            int limit,
            bool includeEvents,
            CancellationToken cancellationToken
        )
        {
            IReadOnlyList<PlaygroupGame> pageGames = page == 1 ? Games.Take(limit).ToList() : [];
            return Task.FromResult(pageGames);
        }

        /// <summary>
        /// Returns fake deck details by id.
        /// </summary>
        public async Task<PlaygroupDeck> GetDeckAsync(long deckId, CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref activeDeckDetailRequests);
            try
            {
                lock (deckDetailLock)
                {
                    maxConcurrentDeckDetailRequests = Math.Max(maxConcurrentDeckDetailRequests, active);
                }

                if (ReleaseDeckDetailsAfterStartedCount > 0)
                {
                    await WaitForDeckDetailBarrierAsync(cancellationToken).ConfigureAwait(false);
                }

                return Decks[deckId];
            }
            finally
            {
                Interlocked.Decrement(ref activeDeckDetailRequests);
            }
        }

        /// <summary>
        /// Returns fake accessible user decks by user id.
        /// </summary>
        public Task<IReadOnlyList<PlaygroupDeck>> ListUserDecksAsync(
            long userId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                UserDecks.TryGetValue(userId, out IReadOnlyList<PlaygroupDeck>? decks)
                    ? decks
                    : []
            );
        }

        /// <summary>
        /// Returns fake Elo history by deck id.
        /// </summary>
        public Task<PlaygroupEloHistory> GetDeckEloHistoryAsync(
            long deckId,
            long? playgroupId,
            long? leagueId,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(Elo[deckId]);
        }

        /// <summary>
        /// Waits until enough fake deck-detail requests have started to prove service overlap.
        /// </summary>
        private async Task WaitForDeckDetailBarrierAsync(CancellationToken cancellationToken)
        {
            int started = Interlocked.Increment(ref deckDetailBarrierStartedCount);
            if (started >= ReleaseDeckDetailsAfterStartedCount)
            {
                deckDetailBarrier.TrySetResult(null);
            }

            await deckDetailBarrier.Task
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
