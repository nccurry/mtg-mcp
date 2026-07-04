using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies manual interchange MCP wrappers preserve mode authority and service results.
/// </summary>
public sealed class DeckInterchangeToolsTests
{
    /// <summary>
    /// Verifies read wrappers and an authorized write complete one local text workflow.
    /// </summary>
    [Fact]
    public async Task LocalTools_PreviewCreateAndExportThroughSharedService()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckInterchangeService service = new(store);
        DeckInterchangeReadTools reads = new(service);
        DeckInterchangeWriteTools writes = new(service, OperationMode.Local);

        Assert.Equal(4, RequireSuccess(reads.ListFormats()).Count);
        DeckImportPreview preview = RequireSuccess(await reads.PreviewAsync(
            "generic-text-v1",
            "[commander]\n1 Commander",
            new DeckImportOptions(DeckName: "Wrapper"),
            TestContext.Current.CancellationToken));
        DeckImportCreateResult created = RequireSuccess(await writes.CreateAsync(
            "generic-text-v1",
            "[commander]\n1 Commander",
            preview.Fingerprint!,
            new DeckImportOptions(DeckName: "Wrapper"),
            TestContext.Current.CancellationToken));
        DeckExportBundle exported = RequireSuccess(await reads.ExportAsync(
            created.Deck.DeckId,
            "generic-text-v1",
            null,
            TestContext.Current.CancellationToken));

        Assert.Equal("Wrapper", created.Deck.Name);
        Assert.Equal(["deck.txt", "deck.mtg-mcp.json", "preservation.json"], exported.Artifacts.Select(value => value.FileName));
    }

    /// <summary>
    /// Verifies invocation-time enforcement rejects a write wrapper constructed in read-only mode.
    /// </summary>
    [Fact]
    public async Task ReadOnlyWriteTool_ReturnsModeDeniedWithoutParsingOrStorage()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckInterchangeWriteTools writes = new(new DeckInterchangeService(store), OperationMode.ReadOnly);

        OperationResult<DeckImportCreateResult> result = await writes.CreateAsync(
            "generic-text-v1",
            "1 Island",
            "unused",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("operation-mode-denied", Assert.IsType<OperationUnsupported>(result.Value).ReasonCode);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Extracts one successful operation payload for focused wrapper assertions.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
