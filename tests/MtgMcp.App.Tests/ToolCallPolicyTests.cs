using System.Text.Json;
using ModelContextProtocol.Protocol;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the shared argument binding and error reporting rules applied to every tool call.
/// </summary>
public sealed class ToolCallPolicyTests
{
    /// <summary>
    /// Verifies an unknown union kind is reported at its array position with the allowed kinds.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_UnknownKind_NamesPathAndAllowedKinds()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """{"deckId":"0193e97a-ef95-7700-8000-000000000001","expectedRevision":1,"changes":[{"kind":"rename-entry"}]}""");

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments("deck_apply_changes", arguments);

        Assert.NotNull(failure);
        Assert.Equal(ToolCallPolicy.InvalidArgumentsReason, failure.ReasonCode);
        Assert.StartsWith("changes[0]", failure.Path, StringComparison.Ordinal);
        Assert.Contains("add-entry", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a wrongly typed nested value is reported at its full nested path.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_WrongNestedType_NamesNestedPath()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """{"deckId":"0193e97a-ef95-7700-8000-000000000001","expectedRevision":1,"changes":[{"kind":"add-entry","entryDraft":{"quantity":"three","cardName":"Sol Ring"}}]}""");

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
            """{"deckId":"0193e97a-ef95-7700-8000-000000000001","expectedRevision":1,"changes":[{"entryDraft":{"quantity":1,"cardName":"Sol Ring"},"kind":"add-entry"}]}""");

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
    /// Verifies operation failures gain the error flag and successes do not.
    /// </summary>
    [Fact]
    public void Apply_OperationFailureAndSuccess_SetsErrorOnlyOnFailure()
    {
        CallToolResult failure = new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(new { kind = "not-found", reasonCode = "deck-not-found", message = "Missing." }),
        };
        CallToolResult success = new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(new { kind = "success", data = new { } }),
        };

        Assert.True(ToolCallPolicy.Apply(new CallToolRequestParams { Name = "deck_get" }, failure).IsError);
        Assert.NotEqual(true, ToolCallPolicy.Apply(new CallToolRequestParams { Name = "deck_get" }, success).IsError);
    }

    /// <summary>
    /// Verifies the SDK's generic binding error is replaced with a bounded invalid-input body.
    /// </summary>
    [Fact]
    public void Apply_GenericBindingError_ReplacesWithStructuredFailure()
    {
        CallToolRequestParams request = new()
        {
            Name = "deck_apply_changes",
            Arguments = Arguments(
                """{"deckId":"0193e97a-ef95-7700-8000-000000000001","expectedRevision":1,"changes":[{"kind":"rename-entry"}]}"""),
        };
        CallToolResult generic = new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "An error occurred invoking 'deck_apply_changes'." }],
        };

        CallToolResult result = ToolCallPolicy.Apply(request, generic);

        Assert.True(result.IsError);
        JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("invalid-input", body.GetProperty("kind").GetString());
        Assert.Equal("invalid-arguments", body.GetProperty("reasonCode").GetString());
        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(body.GetRawText(), text.Text);
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
}
