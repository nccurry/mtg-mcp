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
        Assert.Equal("0.12.0", FoundationServerIdentity.PackageVersion);
        Assert.DoesNotContain('+', FoundationServerIdentity.PackageVersion);
    }

    /// <summary>
    /// Verifies unsupported implicit logging and dynamic-tool advertisements are removed.
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
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = true,
                    ["stableField"] = true,
                },
            },
        };

        FoundationProtocolPolicy.RemoveUnsupportedImplicitCapabilities(result);

        JsonObject capabilities = Assert.IsType<JsonObject>(result["capabilities"]);
        Assert.Null(capabilities["logging"]);
        Assert.NotNull(capabilities["resources"]);
        JsonObject tools = Assert.IsType<JsonObject>(capabilities["tools"]);
        Assert.Null(tools["listChanged"]);
        Assert.True(tools["stableField"]?.GetValue<bool>());
    }

    /// <summary>
    /// Verifies the policy also fixes the result of <c>server/discover</c>, which the 2026-07-28 protocol uses instead of
    /// initialize: it has <c>supportedVersions</c>, keeps the server information in <c>_meta</c>, and has no
    /// top-level <c>serverInfo</c>. Logging and every list-changed claim are removed; other fields stay.
    /// </summary>
    [Fact]
    public void ProtocolPolicy_FixesTheDiscoverResult()
    {
        JsonObject result = JsonNode.Parse("""
            {
              "supportedVersions": ["2026-07-28"],
              "capabilities": {
                "logging": {},
                "prompts": { "listChanged": true },
                "resources": { "listChanged": true, "subscribe": false },
                "tools": { "listChanged": true }
              },
              "_meta": { "io.modelcontextprotocol/serverInfo": { "name": "x", "version": "1" } }
            }
            """)!.AsObject();
        JsonObject withoutMarkers = JsonNode.Parse("""
            { "capabilities": { "logging": {}, "prompts": { "listChanged": true } } }
            """)!.AsObject();

        FoundationProtocolPolicy.RemoveUnsupportedImplicitCapabilities(result);
        FoundationProtocolPolicy.RemoveUnsupportedImplicitCapabilities(withoutMarkers);

        JsonObject capabilities = Assert.IsType<JsonObject>(result["capabilities"]);
        Assert.Null(capabilities["logging"]);
        Assert.Null(capabilities["prompts"]!["listChanged"]);
        Assert.Null(capabilities["tools"]!["listChanged"]);
        Assert.Null(capabilities["resources"]!["listChanged"]);
        Assert.False(capabilities["resources"]!["subscribe"]!.GetValue<bool>());
        Assert.NotNull(result["_meta"]);
        Assert.NotNull(withoutMarkers["capabilities"]!["logging"]);
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

        FoundationProtocolPolicy.RemoveUnsupportedImplicitCapabilities(null);
        FoundationProtocolPolicy.RemoveUnsupportedImplicitCapabilities(new JsonArray());
        FoundationProtocolPolicy.RemoveUnsupportedImplicitCapabilities(result);

        Assert.NotNull(result["capabilities"]?["logging"]);
    }

    /// <summary>
    /// Verifies tool discovery is canonicalized without changing tool payloads or cursors.
    /// </summary>
    [Fact]
    public void ProtocolPolicy_CanonicalizesToolListByExactName()
    {
        JsonObject first = new() { ["name"] = "z_tool", ["description"] = "last" };
        JsonObject second = new() { ["name"] = "a_tool", ["description"] = "first" };
        JsonObject result = new()
        {
            ["tools"] = new JsonArray(first, second),
            ["nextCursor"] = "cursor",
        };

        FoundationProtocolPolicy.CanonicalizeToolList(result);

        JsonArray tools = Assert.IsType<JsonArray>(result["tools"]);
        Assert.Equal(["a_tool", "z_tool"], tools.Select(tool => tool?["name"]?.GetValue<string>()));
        Assert.Equal("first", tools[0]?["description"]?.GetValue<string>());
        Assert.Equal("cursor", result["nextCursor"]?.GetValue<string>());
    }

    /// <summary>
    /// Verifies malformed or non-list tool shapes remain unchanged.
    /// </summary>
    [Fact]
    public void ProtocolPolicy_IgnoresMalformedToolLists()
    {
        JsonObject malformed = new()
        {
            ["tools"] = new JsonArray(new JsonObject { ["description"] = "missing name" }),
        };
        string expected = malformed.ToJsonString();

        FoundationProtocolPolicy.CanonicalizeToolList(null);
        FoundationProtocolPolicy.CanonicalizeToolList(new JsonArray());
        FoundationProtocolPolicy.CanonicalizeToolList(malformed);

        Assert.Equal(expected, malformed.ToJsonString());
    }
}
