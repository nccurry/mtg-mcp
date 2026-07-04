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
        Guid sideboardId = Guid.CreateVersion7();
        Guid candidateId = Guid.CreateVersion7();
        Guid excludedId = Guid.CreateVersion7();
        Guid manaSourcesId = Guid.CreateVersion7();
        Guid basicsId = Guid.CreateVersion7();
        Guid candidateCategoryId = Guid.CreateVersion7();
        Guid creaturesId = Guid.CreateVersion7();
        JsonElement original = await CallSuccessAsync(session, "deck_create", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                name = "Interchange Dummy Commander",
                description = "Disposable end-to-end fixture",
                format = "commander",
                entries = new object[]
                {
                    new { quantity = 1, cardName = "Atraxa, Praetors' Voice", setCode = "2xm", collectorNumber = "190", zone = "commander", finish = "nonfoil", entryId = commanderId },
                    new { quantity = 10, cardName = "Island", setCode = "dmu", collectorNumber = "278", zone = "main", entryId = landId },
                    new { quantity = 1, cardName = "Island", setCode = "dmu", collectorNumber = "278", zone = "sideboard", finish = "foil", entryId = sideboardId },
                    new { quantity = 1, cardName = "Abbot of Keral Keep", setCode = "2x2", collectorNumber = "446", zone = "maybeboard", finish = "etched", entryId = candidateId },
                    new { quantity = 1, cardName = "Call to the Feast", setCode = "2x2", collectorNumber = "190", zone = "excluded", entryId = excludedId },
                },
                categories = new[]
                {
                    new { name = "Mana Sources", color = "#3366ff", categoryId = manaSourcesId },
                    new { name = "Basics", color = "#88aaff", categoryId = basicsId },
                    new { name = "Candidate", color = "#ff9900", categoryId = candidateCategoryId },
                    new { name = "Creatures", color = "#cc3333", categoryId = creaturesId },
                },
                categoryAssignments = new[]
                {
                    new { entryId = landId, categoryId = manaSourcesId, isPrimary = true },
                    new { entryId = landId, categoryId = basicsId, isPrimary = false },
                    new { entryId = candidateId, categoryId = candidateCategoryId, isPrimary = true },
                    new { entryId = candidateId, categoryId = creaturesId, isPrimary = false },
                },
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
            format =>
            {
                Assert.True(
                    format.GetProperty("supportsImport").GetBoolean() &&
                    format.GetProperty("supportsExport").GetBoolean());
                Assert.Equal("available", format.GetProperty("status").GetString());
            });

        Dictionary<string, JsonElement> bundles = new(StringComparer.Ordinal);
        foreach (string formatId in new[]
                 {
                     "mtg-mcp-json-v1",
                     "generic-text-v1",
                     "archidekt-text-v1",
                     "moxfield-bulk-edit-v1",
                 })
        {
            JsonElement bundle = await CallSuccessAsync(session, "deck_export_bundle", new Dictionary<string, object?>
            {
                ["deckId"] = originalId,
                ["formatId"] = formatId,
            }).ConfigureAwait(false);
            bundles.Add(formatId, bundle);
            Assert.Equal("available", bundle.GetProperty("status").GetString());
            Assert.All(
                bundle.GetProperty("artifacts").EnumerateArray(),
                artifact => Assert.Matches("^[0-9a-f]{64}$", artifact.GetProperty("sha256").GetString()));
        }

        Dictionary<string, JsonElement> previews = new(StringComparer.Ordinal);
        foreach ((string formatId, JsonElement bundle) in bundles)
        {
            string content = Artifact(bundle, PrimaryArtifact(formatId));
            JsonElement preview = await CallSuccessAsync(session, "deck_import_preview", new Dictionary<string, object?>
            {
                ["formatId"] = formatId,
                ["content"] = content,
                ["options"] = new
                {
                    deckName = "Imported Dummy Commander",
                    format = "commander",
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
        Assert.Equal(5, restored.GetProperty("entries").GetArrayLength());
        Assert.Equal(4, restored.GetProperty("categoryAssignments").GetArrayLength());
        Assert.Contains("`Mana Sources`", Artifact(bundles["archidekt-text-v1"], "deck.archidekt.txt"), StringComparison.Ordinal);
        Assert.DoesNotContain("`Basics`", Artifact(bundles["archidekt-text-v1"], "deck.archidekt.txt"), StringComparison.Ordinal);
        Assert.DoesNotContain("Call to the Feast", Artifact(bundles["archidekt-text-v1"], "deck.archidekt.txt"), StringComparison.Ordinal);
        string moxfieldText = Artifact(bundles["moxfield-bulk-edit-v1"], "deck.moxfield.txt");
        Assert.Contains("#Mana Sources #Basics", moxfieldText, StringComparison.Ordinal);
        Assert.Contains("*F*", moxfieldText, StringComparison.Ordinal);
        Assert.Contains("*E* #Candidate #Creatures", moxfieldText, StringComparison.Ordinal);
        Assert.DoesNotContain("Call to the Feast", moxfieldText, StringComparison.Ordinal);
        Assert.DoesNotContain("#!", moxfieldText, StringComparison.Ordinal);
        Assert.Equal("companion-only", PreservationStatus(bundles["archidekt-text-v1"], "zone"));
        Assert.Equal("companion-only", PreservationStatus(bundles["archidekt-text-v1"], "finishes"));
        Assert.Equal("preserved", PreservationStatus(bundles["archidekt-text-v1"], "primary-category"));
        Assert.Equal("companion-only", PreservationStatus(bundles["archidekt-text-v1"], "secondary-categories"));
        Assert.Equal("preserved", PreservationStatus(bundles["moxfield-bulk-edit-v1"], "finishes"));
        Assert.Equal("preserved", PreservationStatus(bundles["moxfield-bulk-edit-v1"], "secondary-categories"));
        Assert.Equal("companion-only", PreservationStatus(bundles["moxfield-bulk-edit-v1"], "excluded-entries"));

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
    /// Reads one exact preservation status from a structured export bundle.
    /// </summary>
    private static string PreservationStatus(JsonElement bundle, string field)
    {
        return bundle.GetProperty("preservation")
            .EnumerateArray()
            .Single(value => value.GetProperty("field").GetString() == field)
            .GetProperty("status")
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
