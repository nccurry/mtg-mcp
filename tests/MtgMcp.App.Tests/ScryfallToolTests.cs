using System.Text.Json;
using MtgMcp.App.Configuration;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies Scryfall invocation-time guards and sanitized storage failures at the MCP boundary.
/// </summary>
public sealed class ScryfallToolTests
{
    /// <summary>
    /// Verifies an invalid local database becomes a path-free structured unavailable result.
    /// </summary>
    [Fact]
    public async Task ReadTools_CorruptDatabase_ReturnsSanitizedUnavailable()
    {
        using TemporaryDirectory temporary = new();
        string dataRoot = Path.Combine(temporary.Path, "data");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "scryfall.db"), "not a sqlite database");
        using ScryfallService service = new(dataRoot, allowLocalWrites: false, "0.9.0-preview.1");
        ScryfallReadTools tools = new(service);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>((await tools.GetCorpusStatusAsync(
            TestContext.Current.CancellationToken)).Value);

        Assert.Equal("scryfall-storage-unavailable", unavailable.ReasonCode);
        Assert.DoesNotContain(dataRoot, unavailable.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies write wrappers enforce effective mode even if instantiated outside registration filtering.
    /// </summary>
    [Fact]
    public async Task WriteTools_ReadOnlyMode_DeniesInvocationBeforeAdapterWork()
    {
        using TemporaryDirectory temporary = new();
        string dataRoot = Path.Combine(temporary.Path, "data");
        using ScryfallService service = new(dataRoot, allowLocalWrites: false, "0.9.0-preview.1");
        ScryfallWriteTools tools = new(service, OperationMode.ReadOnly);

        OperationUnsupported unsupported = Assert.IsType<OperationUnsupported>((await tools.SyncCorpusAsync(
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal("operation-mode-denied", unsupported.ReasonCode);
        Assert.False(File.Exists(Path.Combine(dataRoot, "scryfall.db")));
    }

    /// <summary>
    /// Verifies recognized persistence failures are sanitized while programming failures still surface.
    /// </summary>
    [Fact]
    public async Task ExecutionBoundary_CatchesOnlyRecognizedStorageFailures()
    {
        OperationResult<int> unavailable = await ScryfallToolExecution.RunAsync<int>(
            () => throw new InvalidDataException("private detail"));
        Assert.IsType<OperationUnavailable>(unavailable.Value);
        OperationResult<int> malformedJson = await ScryfallToolExecution.RunAsync<int>(
            () => throw new JsonException("private provider fragment"));
        Assert.IsType<OperationUnavailable>(malformedJson.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ScryfallToolExecution.RunAsync<int>(() => throw new InvalidOperationException("bug")));
    }
}
