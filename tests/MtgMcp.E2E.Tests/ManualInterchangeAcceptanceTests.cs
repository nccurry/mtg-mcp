using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Generates deterministic provider artifacts for explicitly authorized manual UI acceptance.
/// </summary>
public sealed class ManualInterchangeAcceptanceTests
{
    /// <summary>
    /// Produces a stable human-reviewable manifest without repeated serializer allocation.
    /// </summary>
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Gives the repository owner the stable manual workflow recorded by every generated manifest.
    /// </summary>
    private static readonly string[] AcceptanceInstructions =
    [
        "Import each primary artifact into a new disposable provider deck.",
        "Record which quantities, printings, boards, finishes, categories, and tags the UI actually applies.",
        "Delete each disposable provider deck and verify it no longer appears.",
    ];

    /// <summary>
    /// Writes Archidekt and Moxfield bundles only to a new caller-selected untracked directory.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    [Trait("Category", "ManualProvider")]
    public async Task GenerateProviderBundlesForDisposableUiChecks()
    {
        string? outputRoot = Environment.GetEnvironmentVariable("MTGMCP_PROVIDER_ACCEPTANCE_DIR");
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            Assert.Skip("Set MTGMCP_PROVIDER_ACCEPTANCE_DIR to a new untracked directory for manual UI artifacts.");
        }

        outputRoot = Path.GetFullPath(outputRoot);
        Assert.False(Directory.Exists(outputRoot), "The provider acceptance output directory must be new.");
        Directory.CreateDirectory(outputRoot);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        Guid commanderId = Guid.Parse("10000000-0000-4000-8000-000000000001");
        Guid landId = Guid.Parse("10000000-0000-4000-8000-000000000002");
        Guid sideboardId = Guid.Parse("10000000-0000-4000-8000-000000000003");
        Guid candidateId = Guid.Parse("10000000-0000-4000-8000-000000000004");
        Guid excludedId = Guid.Parse("10000000-0000-4000-8000-000000000005");
        Guid manaSourcesId = Guid.Parse("20000000-0000-4000-8000-000000000001");
        Guid basicsId = Guid.Parse("20000000-0000-4000-8000-000000000002");
        Guid candidateCategoryId = Guid.Parse("20000000-0000-4000-8000-000000000003");
        Guid creaturesId = Guid.Parse("20000000-0000-4000-8000-000000000004");
        JsonElement created = await CallSuccessAsync(session, "deck_create", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                name = "mtg-mcp Manual Interchange Acceptance",
                description = "Disposable provider UI acceptance fixture",
                format = "commander",
                entries = new object[]
                {
                    new { quantity = 1, cardName = "Atraxa, Praetors' Voice", setCode = "2xm", collectorNumber = "190", zone = "commander", finish = "nonfoil", entryId = commanderId },
                    new { quantity = 10, cardName = "Island", setCode = "dmu", collectorNumber = "278", zone = "main", finish = "nonfoil", entryId = landId },
                    new { quantity = 1, cardName = "Island", setCode = "dmu", collectorNumber = "278", zone = "sideboard", finish = "foil", entryId = sideboardId },
                    new { quantity = 1, cardName = "Abbot of Keral Keep", setCode = "2x2", collectorNumber = "446", zone = "maybeboard", finish = "etched", entryId = candidateId },
                    new { quantity = 1, cardName = "Call to the Feast", setCode = "2x2", collectorNumber = "190", zone = "excluded", finish = "nonfoil", entryId = excludedId },
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
        Guid deckId = created.GetProperty("deckId").GetGuid();
        long revision = created.GetProperty("revision").GetInt64();
        List<object> providerRows = [];
        foreach ((string FormatId, string DirectoryName, string PrimaryFile) provider in new[]
                 {
                     ("archidekt-text-v1", "archidekt", "deck.archidekt.txt"),
                     ("moxfield-bulk-edit-v1", "moxfield", "deck.moxfield.txt"),
                 })
        {
            JsonElement bundle = await CallSuccessAsync(session, "deck_export_bundle", new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["formatId"] = provider.FormatId,
            }).ConfigureAwait(false);
            string providerDirectory = Path.Combine(outputRoot, provider.DirectoryName);
            Directory.CreateDirectory(providerDirectory);
            foreach (JsonElement artifact in bundle.GetProperty("artifacts").EnumerateArray())
            {
                string fileName = artifact.GetProperty("fileName").GetString()!;
                Assert.Equal(Path.GetFileName(fileName), fileName);
                string content = artifact.GetProperty("content").GetString()!;
                string expectedHash = artifact.GetProperty("sha256").GetString()!;
                string actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
                    .ToLowerInvariant();
                Assert.Equal(expectedHash, actualHash);
                await File.WriteAllTextAsync(
                    Path.Combine(providerDirectory, fileName),
                    content,
                    new UTF8Encoding(false),
                    TestContext.Current.CancellationToken).ConfigureAwait(false);
            }

            JsonElement primary = bundle.GetProperty("artifacts").EnumerateArray().Single(
                value => value.GetProperty("fileName").GetString() == provider.PrimaryFile);
            providerRows.Add(new
            {
                provider = provider.DirectoryName,
                formatId = provider.FormatId,
                primaryArtifact = provider.PrimaryFile,
                sha256 = primary.GetProperty("sha256").GetString(),
            });
        }

        string manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            disposableDeckName = "mtg-mcp Manual Interchange Acceptance",
            providers = providerRows,
            sourceFixture = new
            {
                providerTextRows = 4,
                excludedFromProviderText = "Call to the Feast",
                commander = "Atraxa, Praetors' Voice (2XM) 190",
                main = "10 Island (DMU) 278, nonfoil, Mana Sources and Basics",
                sideboard = "1 Island (DMU) 278, foil",
                maybeboard = "1 Abbot of Keral Keep (2X2) 446, etched, Candidate and Creatures",
            },
            instructions = AcceptanceInstructions,
        }, ManifestJsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, "acceptance-manifest.json"),
            manifest + Environment.NewLine,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        _ = await CallSuccessAsync(session, "deck_delete", new Dictionary<string, object?>
        {
            ["deckId"] = deckId,
            ["expectedRevision"] = revision,
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls one MCP tool and returns its successful structured payload.
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
