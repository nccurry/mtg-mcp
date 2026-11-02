using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the invalid-input path audit that <see cref="ToolCallPolicy.Filter"/> feeds: it records an invalid-input
/// result without a path, ignores every other result, and can be limited to the tool families a test names.
/// </summary>
public sealed class InvalidInputPathAuditTests
{
    /// <summary>
    /// Verifies an invalid-input result with no path, or with a blank one, is recorded with its tool and reason.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Filter_InvalidInputWithoutAPath_IsRecorded(string? path)
    {
        InvalidInputPathAudit audit = new();

        _ = await CallAsync(audit, "local_deck_get", Body("invalid-input", path));

        Assert.Equal(["local_deck_get: some-reason"], audit.Findings);
    }

    /// <summary>
    /// Verifies an invalid-input result with a path, and results of other kinds, are not recorded.
    /// </summary>
    [Theory]
    [InlineData("invalid-input", "cursor")]
    [InlineData("conflict", null)]
    [InlineData("not-found", null)]
    [InlineData("success", null)]
    public async Task Filter_ResultsThatAreNotPathlessInvalidInput_AreNotRecorded(string kind, string? path)
    {
        InvalidInputPathAudit audit = new();

        await CallAsync(audit, "local_deck_get", Body(kind, path));

        Assert.Empty(audit.Findings);
    }

    /// <summary>
    /// Verifies an audit limited to tool prefixes records only the tools that start with one of them.
    /// </summary>
    [Fact]
    public async Task Filter_AnAuditLimitedToPrefixes_RecordsOnlyThoseTools()
    {
        InvalidInputPathAudit audit = new("archidekt_", "playgroup_");

        await CallAsync(audit, "local_deck_get", Body("invalid-input", null));
        await CallAsync(audit, "archidekt_deck_get", Body("invalid-input", null));
        await CallAsync(audit, "playgroup_me_get", Body("invalid-input", null));

        Assert.Equal(["archidekt_deck_get: some-reason", "playgroup_me_get: some-reason"], audit.Findings);
    }

    /// <summary>
    /// Verifies a server without an audit in its services runs the filter unchanged.
    /// </summary>
    [Fact]
    public async Task Filter_WithoutAnAuditService_ReturnsTheResult()
    {
        CallToolResult result = await CallAsync(audit: null, "local_deck_get", Body("invalid-input", null));

        Assert.True(result.IsError);
    }

    /// <summary>
    /// Builds the structured body of a result of one kind, with an optional path.
    /// </summary>
    private static JsonElement Body(string kind, string? path)
    {
        return path is null
            ? JsonSerializer.SerializeToElement(new { kind, reasonCode = "some-reason", message = "Message." })
            : JsonSerializer.SerializeToElement(new { kind, reasonCode = "some-reason", message = "Message.", path });
    }

    /// <summary>
    /// Runs the shared filter around a tool that returns the given body, with the audit in the server's services.
    /// </summary>
    private static async Task<CallToolResult> CallAsync(InvalidInputPathAudit? audit, string tool, JsonElement body)
    {
        await using McpServer server = McpServer.Create(
            new StreamServerTransport(Stream.Null, Stream.Null),
            new McpServerOptions());
        ServiceCollection services = new();
        if (audit is not null)
        {
            services.AddSingleton(audit);
        }

        RequestContext<CallToolRequestParams> context = new(
            server,
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = tool })
        {
            Services = services.BuildServiceProvider(),
        };
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = ToolCallPolicy.Filter(
            (_, _) => ValueTask.FromResult(new CallToolResult { StructuredContent = body }));

        return await filter(context, TestContext.Current.CancellationToken);
    }
}
