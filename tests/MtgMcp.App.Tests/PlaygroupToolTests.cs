using System.Net;
using MtgMcp.App.Configuration;
using MtgMcp.App.Playgroup;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies invocation-time Playgroup authority independently from static registration filtering.
/// </summary>
public sealed class PlaygroupToolTests
{
    /// <summary>Verifies both remote writes fail before adapter work in read-only and local modes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RemoteWriteTools_DenyEveryNonRemoteMode(int modeValue)
    {
        using PlaygroupService service = new(
            PlaygroupOptions.CreateDefault(null),
            "0.9.0-preview.1");
        PlaygroupRemoteWriteTools tools = new(service, (OperationMode)modeValue);

        OperationResult<PlaygroupEvidence> events = await tools.CreateGameEventsBatchAsync(
            1,
            [new PlaygroupEventImport("Damage", "0")],
            TestContext.Current.CancellationToken);
        OperationResult<PlaygroupEvidence> session = await tools.CreateLiveSessionAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "operation-mode-denied",
            Assert.IsType<OperationUnsupported>(events.Value).ReasonCode);
        Assert.Equal(
            "operation-mode-denied",
            Assert.IsType<OperationUnsupported>(session.Value).ReasonCode);
    }

    /// <summary>Verifies a provider 400 on each remote write is invalid input that names the argument sent.</summary>
    [Fact]
    public async Task RemoteWriteTools_ProviderRejection_IsInvalidInputAtTheArgument()
    {
        using PlaygroupGamesPagingTests.PlaygroupWorld world = new("{}") { Status = HttpStatusCode.BadRequest };

        OperationResult<PlaygroupEvidence> events = await world.WriteTools.CreateGameEventsBatchAsync(
            1,
            [new PlaygroupEventImport("Damage", "0")],
            TestContext.Current.CancellationToken);
        OperationResult<PlaygroupEvidence> session = await world.WriteTools.CreateLiveSessionAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        OperationInvalidInput rejectedEvents = Assert.IsType<OperationInvalidInput>(events.Value);
        OperationInvalidInput rejectedSession = Assert.IsType<OperationInvalidInput>(session.Value);
        Assert.Equal("provider-request-rejected", rejectedEvents.ReasonCode);
        Assert.Equal("events", rejectedEvents.Path);
        Assert.Equal("provider-request-rejected", rejectedSession.ReasonCode);
        Assert.Equal("playerAmount", rejectedSession.Path);
    }

    /// <summary>Verifies a rejected live session names the first argument the caller sent.</summary>
    [Fact]
    public async Task CreateLiveSession_ProviderRejection_NamesTheFirstArgumentSent()
    {
        using PlaygroupGamesPagingTests.PlaygroupWorld world = new("{}") { Status = HttpStatusCode.BadRequest };

        OperationResult<PlaygroupEvidence> result = await world.WriteTools.CreateLiveSessionAsync(
            bracket: 3,
            playgroupId: 4,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("bracket", Assert.IsType<OperationInvalidInput>(result.Value).Path);
    }

    /// <summary>Verifies a provider 400 on every read tool that takes an argument names an argument the caller sent.</summary>
    [Theory]
    [InlineData("playgroup_commander_get", "commanderId")]
    [InlineData("playgroup_commander_get_by_name", "name")]
    [InlineData("playgroup_commander_turn_damage_get", "commanderId")]
    [InlineData("playgroup_deck_get", "playgroupDeckId")]
    [InlineData("playgroup_deck_elo_history_get", "playgroupDeckId")]
    [InlineData("playgroup_user_get", "userId")]
    [InlineData("playgroup_user_decks_list", "userId")]
    [InlineData("playgroup_user_playgroups_list", "userId")]
    [InlineData("playgroup_user_playgroup_get", "userId")]
    [InlineData("playgroup_playgroup_members_list", "playgroupId")]
    [InlineData("playgroup_playgroup_games_list", "playgroupId")]
    [InlineData("playgroup_playgroup_game_get", "playgroupId")]
    public async Task ReadTools_ProviderRejection_IsInvalidInputAtAnArgumentSent(string tool, string expectedPath)
    {
        using PlaygroupGamesPagingTests.PlaygroupWorld world = new("{}") { Status = HttpStatusCode.BadRequest };
        CancellationToken token = TestContext.Current.CancellationToken;
        PlaygroupReadTools tools = world.Tools;

        object? result = tool switch
        {
            "playgroup_commander_get" => (await tools.GetCommanderAsync(1, token)).Value,
            "playgroup_commander_get_by_name" => (await tools.GetCommanderByNameAsync("x", token)).Value,
            "playgroup_commander_turn_damage_get" => (await tools.GetCommanderTurnDamageAsync(1, token)).Value,
            "playgroup_deck_get" => (await tools.GetDeckAsync(1, cancellationToken: token)).Value,
            "playgroup_deck_elo_history_get" => (await tools.GetDeckEloHistoryAsync(1, cancellationToken: token)).Value,
            "playgroup_user_get" => (await tools.GetUserAsync(1, token)).Value,
            "playgroup_user_decks_list" => (await tools.ListUserDecksAsync(1, cancellationToken: token)).Value,
            "playgroup_user_playgroups_list" => (await tools.ListUserPlaygroupsAsync(1, token)).Value,
            "playgroup_user_playgroup_get" => (await tools.GetUserPlaygroupAsync(1, 2, token)).Value,
            "playgroup_playgroup_members_list" => (await tools.ListPlaygroupMembersAsync(1, token)).Value,
            "playgroup_playgroup_games_list" => (await tools.ListPlaygroupGamesAsync(
                1,
                cancellationToken: token)).Value,
            "playgroup_playgroup_game_get" => (await tools.GetPlaygroupGameAsync(1, 2, cancellationToken: token)).Value,
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result);
        Assert.Equal("provider-request-rejected", invalid.ReasonCode);
        Assert.Equal(expectedPath, invalid.Path);
    }
}
