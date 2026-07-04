using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises every manual interchange tool through the official MCP client and disposable storage.
/// </summary>
public sealed class DeckInterchangeMcpTests
{
    /// <summary>
    /// Runs native, generic, Archidekt, and Moxfield workflows over one dummy Commander deck.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalMode_AllInterchangeTools_CompleteDummyCommanderWorkflow()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        Guid commanderId = Guid.CreateVersion7();
        Guid landId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        JsonElement original = await CallSuccessAsync(session, "deck_create", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                name = "Interchange Dummy Commander",
                description = "Disposable end-to-end fixture",
                format = "commander",
                entries = new object[]
                {
                    new { quantity = 1, cardName = "Atraxa, Praetors' Voice", setCode = "2x2", collectorNumber = "190", zone = "commander", entryId = commanderId },
                    new { quantity = 10, cardName = "Island", setCode = "dmu", collectorNumber = "278", zone = "main", entryId = landId },
                },
                categories = new[] { new { name = "Mana Sources", color = "#3366ff", categoryId } },
                categoryAssignments = new[] { new { entryId = landId, categoryId, isPrimary = true } },
            },
        }).ConfigureAwait(false);
        Guid originalId = original.GetProperty("deckId").GetGuid();
        long originalRevision = original.GetProperty("revision").GetInt64();

        JsonElement interchangeFormats = await CallSuccessAsync(
            session,
            "deck_interchange_formats",
            new Dictionary<string, object?>()).ConfigureAwait(false);
        Assert.Equal(4, interchangeFormats.GetArrayLength());
        Assert.All(
            interchangeFormats.EnumerateArray(),
            format => Assert.True(
                format.GetProperty("supportsImport").GetBoolean() &&
                format.GetProperty("supportsExport").GetBoolean()));

        Dictionary<string, JsonElement> bundles = new(StringComparer.Ordinal);
        foreach (string formatId in new[]
                 {
                     "mtg-mcp-json-v1",
                     "generic-text-v1",
                     "archidekt-text-v1",
                     "moxfield-bulk-edit-v1",
                 })
        {
            bool experimental = formatId is "archidekt-text-v1" or "moxfield-bulk-edit-v1";
            JsonElement bundle = await CallSuccessAsync(session, "deck_export_bundle", new Dictionary<string, object?>
            {
                ["deckId"] = originalId,
                ["formatId"] = formatId,
                ["options"] = experimental ? new { allowExperimental = true } : null,
            }).ConfigureAwait(false);
            bundles.Add(formatId, bundle);
            Assert.All(
                bundle.GetProperty("artifacts").EnumerateArray(),
                artifact => Assert.Matches("^[0-9a-f]{64}$", artifact.GetProperty("sha256").GetString()));
        }

        Dictionary<string, JsonElement> previews = new(StringComparer.Ordinal);
        foreach ((string formatId, JsonElement bundle) in bundles)
        {
            string content = Artifact(bundle, PrimaryArtifact(formatId));
            bool experimental = formatId is "archidekt-text-v1" or "moxfield-bulk-edit-v1";
            JsonElement preview = await CallSuccessAsync(session, "deck_import_preview", new Dictionary<string, object?>
            {
                ["formatId"] = formatId,
                ["content"] = content,
                ["options"] = new
                {
                    deckName = "Imported Dummy Commander",
                    format = "commander",
                    allowExperimental = experimental,
                },
            }).ConfigureAwait(false);
            previews.Add(formatId, preview);
            Assert.Equal("complete", preview.GetProperty("completeness").GetString());
        }

        string genericContent = Artifact(bundles["generic-text-v1"], "deck.txt");
        JsonElement genericImported = await CallSuccessAsync(session, "deck_import_create", new Dictionary<string, object?>
        {
            ["formatId"] = "generic-text-v1",
            ["content"] = genericContent,
            ["expectedFingerprint"] = previews["generic-text-v1"].GetProperty("fingerprint").GetString(),
            ["options"] = new { deckName = "Imported Dummy Commander", format = "commander" },
        }).ConfigureAwait(false);
        Guid genericId = genericImported.GetProperty("deck").GetProperty("deckId").GetGuid();

        _ = await CallSuccessAsync(session, "deck_delete", new Dictionary<string, object?>
        {
            ["deckId"] = originalId,
            ["expectedRevision"] = originalRevision,
        }).ConfigureAwait(false);
        string nativeContent = Artifact(bundles["mtg-mcp-json-v1"], "deck.mtg-mcp.json");
        JsonElement nativeImported = await CallSuccessAsync(session, "deck_import_create", new Dictionary<string, object?>
        {
            ["formatId"] = "mtg-mcp-json-v1",
            ["content"] = nativeContent,
            ["expectedFingerprint"] = previews["mtg-mcp-json-v1"].GetProperty("fingerprint").GetString(),
            ["options"] = new { },
        }).ConfigureAwait(false);
        JsonElement restored = nativeImported.GetProperty("deck");

        Assert.Equal(originalId, restored.GetProperty("deckId").GetGuid());
        Assert.Equal(originalRevision, restored.GetProperty("revision").GetInt64());
        Assert.Equal(2, restored.GetProperty("entries").GetArrayLength());
        Assert.Single(restored.GetProperty("categoryAssignments").EnumerateArray());
        Assert.Contains("`Mana Sources`", Artifact(bundles["archidekt-text-v1"], "deck.archidekt.txt"), StringComparison.Ordinal);
        Assert.Contains("#Mana Sources", Artifact(bundles["moxfield-bulk-edit-v1"], "deck.moxfield.txt"), StringComparison.Ordinal);
        Assert.DoesNotContain("#!", Artifact(bundles["moxfield-bulk-edit-v1"], "deck.moxfield.txt"), StringComparison.Ordinal);

        _ = await CallSuccessAsync(session, "deck_delete", new Dictionary<string, object?>
        {
            ["deckId"] = genericId,
            ["expectedRevision"] = genericImported.GetProperty("deck").GetProperty("revision").GetInt64(),
        }).ConfigureAwait(false);
        _ = await CallSuccessAsync(session, "deck_delete", new Dictionary<string, object?>
        {
            ["deckId"] = originalId,
            ["expectedRevision"] = restored.GetProperty("revision").GetInt64(),
        }).ConfigureAwait(false);
        JsonElement decks = await CallSuccessAsync(
            session,
            "deck_list",
            new Dictionary<string, object?>()).ConfigureAwait(false);
        Assert.Empty(decks.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// Finds exact artifact content by stable bundle filename.
    /// </summary>
    private static string Artifact(JsonElement bundle, string fileName)
    {
        return bundle.GetProperty("artifacts")
            .EnumerateArray()
            .Single(value => value.GetProperty("fileName").GetString() == fileName)
            .GetProperty("content")
            .GetString()!;
    }

    /// <summary>
    /// Maps each exact format ID to its primary manual artifact name.
    /// </summary>
    private static string PrimaryArtifact(string formatId)
    {
        return formatId switch
        {
            "mtg-mcp-json-v1" => "deck.mtg-mcp.json",
            "generic-text-v1" => "deck.txt",
            "archidekt-text-v1" => "deck.archidekt.txt",
            "moxfield-bulk-edit-v1" => "deck.moxfield.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(formatId)),
        };
    }

    /// <summary>
    /// Calls one tool and extracts its successful structured result payload.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult call = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, call.IsError);
        JsonElement content = Assert.IsType<JsonElement>(call.StructuredContent);
        JsonElement result = content.GetProperty("result");
        Assert.Equal("success", result.GetProperty("kind").GetString());
        return result.GetProperty("data");
    }
}
