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
            new PlaygroupLiveSessionCreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "operation-mode-denied",
            Assert.IsType<OperationUnsupported>(events.Value).ReasonCode);
        Assert.Equal(
            "operation-mode-denied",
            Assert.IsType<OperationUnsupported>(session.Value).ReasonCode);
    }
}
