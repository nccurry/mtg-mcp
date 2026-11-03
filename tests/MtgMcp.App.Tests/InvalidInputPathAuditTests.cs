using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the invalid-input path audit: it records an invalid-input result without a path, ignores every other
/// result, can be limited to the tool families a test names, and catches a pathless refusal that travels through the
/// real tool call pipeline.
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
    /// Verifies a pathless invalid-input refusal from a tool served through the real pipeline is caught, and a refusal
    /// with a path from a sibling tool is not.
    /// </summary>
    [Fact]
    public async Task Pipeline_ARealToolRefusingWithoutAPath_IsCaught()
    {
        InvalidInputPathAudit audit = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        Pipe clientToServer = new();
        Pipe serverToClient = new();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithRequestFilters(filters => filters
                .AddCallToolFilter(audit.Filter)
                .AddCallToolFilter(ToolCallPolicy.Filter))
            .WithRegisteredTools(ToolRegistration.Create(new RefusingTools()));
        using IHost host = builder.Build();
        await host.StartAsync(token);
        await using McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: token);

        _ = await client.CallToolAsync("local_deck_refuse_with_path", cancellationToken: token);
        Assert.Empty(audit.Findings);

        CallToolResult refused = await client.CallToolAsync("local_deck_refuse_without_path", cancellationToken: token);

        Assert.True(refused.IsError);
        Assert.Equal(["local_deck_refuse_without_path: no-path-given"], audit.Findings);
        await host.StopAsync(token);
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
    /// Runs the audit's filter around a tool that returns the given body.
    /// </summary>
    private static async Task<CallToolResult> CallAsync(InvalidInputPathAudit audit, string tool, JsonElement body)
    {
        await using McpServer server = McpServer.Create(
            new StreamServerTransport(Stream.Null, Stream.Null),
            new McpServerOptions());
        RequestContext<CallToolRequestParams> context = new(
            server,
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = tool });
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = audit.Filter(
            (_, _) => ValueTask.FromResult(new CallToolResult { StructuredContent = body }));

        return await filter(context, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Serves two tools that refuse every call: one names the argument it refused and one does not.
    /// </summary>
    private sealed class RefusingTools
    {
        /// <summary>
        /// Refuses with a path.
        /// </summary>
        [McpServerTool(Name = "local_deck_refuse_with_path", ReadOnly = true, UseStructuredContent = true)]
        internal OperationResult<int> RefuseWithPath()
        {
            return new OperationInvalidInput("path-given", "Refused.", "argument");
        }

        /// <summary>
        /// Refuses with no path.
        /// </summary>
        [McpServerTool(Name = "local_deck_refuse_without_path", ReadOnly = true, UseStructuredContent = true)]
        internal OperationResult<int> RefuseWithoutPath()
        {
            return new OperationInvalidInput("no-path-given", "Refused.");
        }
    }
}
