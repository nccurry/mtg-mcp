using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the shared argument binding and error reporting rules applied to every tool call.
/// </summary>
public sealed class ToolCallPolicyTests
{
    /// <summary>
    /// Provides arguments whose only change has a kind that is not a union value.
    /// </summary>
    private const string UnknownKindChanges =
        """
        {
          "deckId": "0193e97a-ef95-7700-8000-000000000001",
          "expectedRevision": 1,
          "changes": [
            { "kind": "rename-entry" }
          ]
        }
        """;

    /// <summary>
    /// Provides arguments with one valid add-entry change.
    /// </summary>
    private const string ValidChanges =
        """
        {
          "deckId": "0193e97a-ef95-7700-8000-000000000001",
          "expectedRevision": 1,
          "changes": [
            { "kind": "add-entry", "entryDraft": { "quantity": 1, "cardName": "Sol Ring" } }
          ]
        }
        """;

    /// <summary>
    /// Verifies an unknown union kind is reported at its array position with the allowed kinds.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_UnknownKind_NamesPathAndAllowedKinds()
    {
        Dictionary<string, JsonElement> arguments = Arguments(UnknownKindChanges);

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments("deck_apply_changes", arguments);

        Assert.NotNull(failure);
        Assert.Equal(ToolCallPolicy.InvalidArgumentsReason, failure.ReasonCode);
        Assert.Equal("changes[0]", failure.Path);
        Assert.Contains(
            "has a missing or unknown kind. Allowed kinds: ",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a wrongly typed nested value is reported at its full nested path.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_WrongNestedType_NamesNestedPath()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """
            {
              "deckId": "0193e97a-ef95-7700-8000-000000000001",
              "expectedRevision": 1,
              "changes": [
                { "kind": "add-entry", "entryDraft": { "quantity": "three", "cardName": "Sol Ring" } }
              ]
            }
            """);

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments("deck_apply_changes", arguments);

        Assert.NotNull(failure);
        Assert.Equal("changes[0].entryDraft.quantity", failure.Path);
    }

    /// <summary>
    /// Verifies a union kind after other properties binds without a failure.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_KindLast_Binds()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """
            {
              "deckId": "0193e97a-ef95-7700-8000-000000000001",
              "expectedRevision": 1,
              "changes": [
                { "entryDraft": { "quantity": 1, "cardName": "Sol Ring" }, "kind": "add-entry" }
              ]
            }
            """);

        Assert.Null(ToolCallPolicy.DiagnoseArguments("deck_apply_changes", arguments));
    }

    /// <summary>
    /// Verifies a missing required argument is named, and unknown tools are left alone.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_MissingRequiredOrUnknownTool_ReportsOrSkips()
    {
        OperationInvalidInput? missing = ToolCallPolicy.DiagnoseArguments("deck_apply_changes", Arguments("{}"));

        Assert.NotNull(missing);
        Assert.Equal("deckId", missing.Path);
        Assert.Null(ToolCallPolicy.DiagnoseArguments("not_a_tool", Arguments("{}")));
    }

    /// <summary>
    /// Verifies a value the serializer cannot read at all is reported at the parameter name.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_UnsupportedParameterType_ReportsParameterName()
    {
        MethodInfo method = typeof(SampleTools).GetMethod(nameof(SampleTools.TakesType))!;

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments(method, Arguments("""{ "value": "x" }"""));

        Assert.NotNull(failure);
        Assert.Equal("value", failure.Path);
        Assert.StartsWith("The value of 'value' could not be read", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a bad kind in a union the search cannot reach keeps its path and gives no allowed-kind list.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_UnionBeyondSearchDepth_ReportsPathWithoutAllowedKinds()
    {
        MethodInfo method = typeof(SampleTools).GetMethod(nameof(SampleTools.TakesDeepUnion))!;

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments(
            method,
            Arguments(
                """
                { "value": { "next": { "next": { "next": { "next": { "union": { "kind": "nope" } } } } } } }
                """));

        Assert.NotNull(failure);
        Assert.Equal("value.next.next.next.next.union", failure.Path);
        Assert.DoesNotContain("Allowed kinds", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies two unions reachable from one argument give no allowed-kind list, since the failing one is unknown.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_SeveralUnions_ReportsPathWithoutAllowedKinds()
    {
        MethodInfo method = typeof(SampleTools).GetMethod(nameof(SampleTools.TakesTwoUnions))!;

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments(
            method,
            Arguments("""{ "value": { "first": { "kind": "nope" } } }"""));

        Assert.NotNull(failure);
        Assert.Equal("value.first", failure.Path);
        Assert.DoesNotContain("Allowed kinds", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a repeated tool name fails when the index is built.
    /// </summary>
    [Fact]
    public void IndexToolMethods_DuplicateToolName_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ToolCallPolicy.IndexToolMethods([typeof(SampleTools), typeof(DuplicateSampleTools)]));

        Assert.Contains("'sample_tool'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the real tool index holds a known tool and finds no duplicate.
    /// </summary>
    [Fact]
    public void IndexToolMethods_RealAssembly_FindsKnownTool()
    {
        System.Collections.Frozen.FrozenDictionary<string, MethodInfo> methods =
            ToolCallPolicy.IndexToolMethods(typeof(ToolCallPolicy).Assembly.GetTypes());

        Assert.True(methods.ContainsKey("deck_apply_changes"));
    }

    /// <summary>
    /// Verifies operation failures gain the error flag and successes do not.
    /// </summary>
    [Fact]
    public void MarkOperationFailure_FailureAndSuccess_SetsErrorOnlyOnFailure()
    {
        CallToolResult failure = new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(
                new { kind = "not-found", reasonCode = "deck-not-found", message = "Missing." }),
        };
        CallToolResult success = new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(new { kind = "success", data = new { } }),
        };

        Assert.True(ToolCallPolicy.MarkOperationFailure(failure).IsError);
        Assert.NotEqual(true, ToolCallPolicy.MarkOperationFailure(success).IsError);
    }

    /// <summary>
    /// Verifies a binding exception from the pipeline becomes a bounded invalid-arguments failure.
    /// </summary>
    [Fact]
    public async Task Filter_BindingException_ReturnsInvalidArgumentsResult()
    {
        await using McpServer server = CreateServer();
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = ToolCallPolicy.Filter(
            (_, _) => throw new InvalidOperationException("An error occurred invoking 'deck_apply_changes'."));

        CallToolResult result = await filter(
            Context(server, "deck_apply_changes", UnknownKindChanges),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("invalid-input", body.GetProperty("kind").GetString());
        Assert.Equal("invalid-arguments", body.GetProperty("reasonCode").GetString());
        Assert.Equal("changes[0]", body.GetProperty("path").GetString());
        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(body.GetRawText(), text.Text);
    }

    /// <summary>
    /// Verifies a tool exception with valid arguments is not hidden behind an argument failure.
    /// </summary>
    [Fact]
    public async Task Filter_ToolExceptionWithValidArguments_IsRethrown()
    {
        await using McpServer server = CreateServer();
        InvalidOperationException toolFailure = new("The tool itself failed.");
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = ToolCallPolicy.Filter(
            (_, _) => throw toolFailure);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => filter(Context(server, "deck_apply_changes", ValidChanges), TestContext.Current.CancellationToken)
                .AsTask());

        Assert.Same(toolFailure, thrown);
    }

    /// <summary>
    /// Verifies cancellation reaches the host even when the arguments are also invalid.
    /// </summary>
    [Fact]
    public async Task Filter_Cancellation_Propagates()
    {
        await using McpServer server = CreateServer();
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = ToolCallPolicy.Filter(
            (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => filter(
                Context(server, "deck_apply_changes", UnknownKindChanges),
                TestContext.Current.CancellationToken)
                .AsTask());
    }

    /// <summary>
    /// Verifies a request with no parameters rethrows the pipeline exception.
    /// </summary>
    [Fact]
    public async Task Filter_ExceptionWithoutParameters_IsRethrown()
    {
        await using McpServer server = CreateServer();
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = ToolCallPolicy.Filter(
            (_, _) => throw new InvalidOperationException("No parameters."));
        RequestContext<CallToolRequestParams> context = new(
            server,
            new JsonRpcRequest { Method = "tools/call" },
            null!);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => filter(context, TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>
    /// Verifies a returned operation failure gains the error flag and a success is left alone.
    /// </summary>
    [Theory]
    [InlineData("not-found", true)]
    [InlineData("success", false)]
    public async Task Filter_ReturnedResult_AppliesKindRule(string kind, bool expectedError)
    {
        await using McpServer server = CreateServer();
        CallToolResult returned = new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(new { kind }),
        };
        McpRequestHandler<CallToolRequestParams, CallToolResult> filter = ToolCallPolicy.Filter(
            (_, _) => ValueTask.FromResult(returned));

        CallToolResult result = await filter(
            Context(server, "deck_get", """{ "deckId": "0193e97a-ef95-7700-8000-000000000001" }"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedError, result.IsError == true);
    }

    /// <summary>
    /// Creates a server over empty streams; the filter tests need one only to build a request context.
    /// </summary>
    private static McpServer CreateServer()
    {
        return McpServer.Create(
            new StreamServerTransport(Stream.Null, Stream.Null),
            new McpServerOptions());
    }

    /// <summary>
    /// Builds a tool call context with the given tool name and JSON arguments.
    /// </summary>
    private static RequestContext<CallToolRequestParams> Context(
        McpServer server,
        string toolName,
        string argumentsJson)
    {
        return new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = toolName, Arguments = Arguments(argumentsJson) });
    }

    /// <summary>
    /// Parses one argument object into the dictionary shape the protocol request carries.
    /// </summary>
    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            arguments[property.Name] = property.Value.Clone();
        }

        return arguments;
    }

    /// <summary>
    /// Declares sample tool methods whose parameter types exercise the diagnosis edge cases.
    /// </summary>
    private static class SampleTools
    {
        /// <summary>
        /// Takes a type the serializer cannot read.
        /// </summary>
        [McpServerTool(Name = "sample_tool")]
        public static void TakesType(Type value)
        {
            _ = value;
        }

        /// <summary>
        /// Takes a union nested deeper than the allowed-kind search walks.
        /// </summary>
        [McpServerTool(Name = "deep_union_tool")]
        public static void TakesDeepUnion(Level1 value)
        {
            _ = value;
        }

        /// <summary>
        /// Takes two different unions in one argument.
        /// </summary>
        [McpServerTool(Name = "two_unions_tool")]
        public static void TakesTwoUnions(TwoUnions value)
        {
            _ = value;
        }
    }

    /// <summary>
    /// Declares a second tool with a name already used, to prove duplicates are rejected.
    /// </summary>
    private static class DuplicateSampleTools
    {
        /// <summary>
        /// Repeats a tool name.
        /// </summary>
        [McpServerTool(Name = "sample_tool")]
        public static void Repeat()
        {
        }
    }

    /// <summary>
    /// First of five distinct nested types that put a union beyond the allowed-kind search depth.
    /// </summary>
    internal sealed record Level1(Level2? Next);

    /// <summary>
    /// Second nested type.
    /// </summary>
    internal sealed record Level2(Level3? Next);

    /// <summary>
    /// Third nested type.
    /// </summary>
    internal sealed record Level3(Level4? Next);

    /// <summary>
    /// Fourth nested type.
    /// </summary>
    internal sealed record Level4(Level5? Next);

    /// <summary>
    /// Fifth nested type, which holds the union.
    /// </summary>
    internal sealed record Level5(SampleUnion? Union);

    /// <summary>
    /// Holds two unions of different types.
    /// </summary>
    internal sealed record TwoUnions(SampleUnion? First, OtherUnion? Second);

    /// <summary>
    /// Declares a sample union keyed by a kind property.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(SampleCase), "sample")]
    internal abstract record SampleUnion;

    /// <summary>
    /// Declares a second sample union keyed by a kind property.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(OtherCase), "other")]
    internal abstract record OtherUnion;

    /// <summary>
    /// Provides the only case of the first sample union.
    /// </summary>
    internal sealed record SampleCase : SampleUnion;

    /// <summary>
    /// Provides the only case of the second sample union.
    /// </summary>
    internal sealed record OtherCase : OtherUnion;
}
