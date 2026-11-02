using System.Text.Json;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
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
        using ScryfallService service = new(
            dataRoot,
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions);
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
        using ScryfallService service = new(
            dataRoot,
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions);
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

    /// <summary>
    /// Verifies a page size outside its bounds is refused with the argument named in the failure path, so a caller
    /// can tell which argument to change.
    /// </summary>
    [Fact]
    public async Task PageSizeFailures_NameThePageSizeArgument()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = new(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions);
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationInvalidInput search = Assert.IsType<OperationInvalidInput>(
            (await service.SearchAsync("x", pageSize: 0, cancellationToken: token)).Value);
        OperationInvalidInput list = Assert.IsType<OperationInvalidInput>(
            (await service.ListSnapshotsAsync(pageSize: 101, cancellationToken: token)).Value);
        OperationInvalidInput snapshot = Assert.IsType<OperationInvalidInput>(
            (await service.GetSnapshotAsync(Guid.NewGuid(), pageSize: 0, cancellationToken: token)).Value);

        Assert.All([search, list, snapshot], failure =>
        {
            Assert.Equal("invalid-page-size", failure.ReasonCode);
            Assert.Equal("pageSize", failure.Path);
        });
    }
}
