using System.Text.Json.Nodes;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the stable identity and exact initialization-capability policy.
/// </summary>
public sealed class FoundationHostingTests
{
    /// <summary>
    /// Verifies the MCP identity uses the evaluated preview package version without metadata.
    /// </summary>
    [Fact]
    public void ServerIdentity_MatchesFoundationContract()
    {
        Assert.Equal("io.github.nccurry/mtg-mcp", FoundationServerIdentity.Name);
        Assert.Equal("mtg-mcp", FoundationServerIdentity.Title);
        Assert.Equal("0.9.0-preview.1", FoundationServerIdentity.PackageVersion);
        Assert.DoesNotContain('+', FoundationServerIdentity.PackageVersion);
    }

    /// <summary>
    /// Verifies only the SDK's implicit logging advertisement is removed from initialization.
    /// </summary>
    [Fact]
    public void ProtocolPolicy_RemovesOnlyImplicitInitializationLogging()
    {
        JsonObject result = new()
        {
            ["serverInfo"] = new JsonObject(),
            ["capabilities"] = new JsonObject
            {
                ["logging"] = new JsonObject(),
                ["resources"] = new JsonObject(),
            },
        };

        FoundationProtocolPolicy.RemoveImplicitLoggingCapability(result);

        JsonObject capabilities = Assert.IsType<JsonObject>(result["capabilities"]);
        Assert.Null(capabilities["logging"]);
        Assert.NotNull(capabilities["resources"]);
    }

    /// <summary>
    /// Verifies non-initialization messages and malformed results are left untouched.
    /// </summary>
    [Fact]
    public void ProtocolPolicy_IgnoresOtherMessageShapes()
    {
        JsonObject result = new()
        {
            ["capabilities"] = new JsonObject
            {
                ["logging"] = new JsonObject(),
            },
        };

        FoundationProtocolPolicy.RemoveImplicitLoggingCapability(null);
        FoundationProtocolPolicy.RemoveImplicitLoggingCapability(new JsonArray());
        FoundationProtocolPolicy.RemoveImplicitLoggingCapability(result);

        Assert.NotNull(result["capabilities"]?["logging"]);
    }
}
