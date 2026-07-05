using MtgMcp.Core.Results;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Provides an explicitly enabled authenticated read against the official public API.
/// </summary>
public sealed class PlaygroupLiveTests
{
    /// <summary>
    /// Reads only the current user and never invokes either mutation endpoint.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task CurrentUser_ReadsProviderEvidenceWithoutWriting()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_PLAYGROUP_LIVE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_PLAYGROUP_LIVE=1 to run the authenticated Playgroup read.");
        }

        string? apiKey = Environment.GetEnvironmentVariable("MTGMCP__PLAYGROUP__API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Skip("Set MTGMCP__PLAYGROUP__API_KEY to run the authenticated Playgroup read.");
        }

        using PlaygroupService service = new(
            PlaygroupOptions.CreateDefault(apiKey),
            "0.9.0-preview.1");
        OperationResult<PlaygroupEvidence> result = await service.GetCurrentUserAsync(
            TestContext.Current.CancellationToken);
        PlaygroupEvidence evidence = Assert.IsType<OperationSuccess<PlaygroupEvidence>>(result.Value).Data;

        Assert.Equal("getCurrentUser", evidence.OperationId);
        Assert.Equal("GET /me", evidence.Endpoint);
        Assert.Equal("1.0.0", evidence.ApiVersion);
        Assert.Equal(PlaygroupContract.OpenApiChecksum, evidence.ContractChecksum);
        Assert.NotEqual(System.Text.Json.JsonValueKind.Undefined, evidence.Data.ValueKind);
    }
}
