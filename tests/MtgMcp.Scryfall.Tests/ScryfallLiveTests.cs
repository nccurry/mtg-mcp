using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Provides opt-in verification against current official Scryfall contracts outside normal CI.
/// </summary>
public sealed class ScryfallLiveTests
{
    /// <summary>
    /// Produces a stable human-reviewable acceptance report without per-run serializer allocation.
    /// </summary>
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Verifies current bulk metadata and one bounded exact card request without downloading the corpus.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task OfficialApi_ReturnsFixedMetadataAndRichCardEvidence()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallService service = new(
            temporary.Path,
            allowLocalWrites: true,
            "0.9.0-preview.1");

        ScryfallBulkMetadataResult metadata = RequireSuccess(await service.GetBulkMetadataAsync(
            "refresh",
            TestContext.Current.CancellationToken));
        ScryfallCardResult card = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Venerable Knight"),
            "refresh",
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            ["all_cards", "rulings", "oracle_tags", "art_tags"],
            metadata.Datasets.Select(value => value.Type));
        Assert.All(metadata.Datasets, value =>
        {
            Assert.Equal("gzip", value.ContentEncoding);
            Assert.EndsWith(".jsonl.gz", value.JsonlDownloadUri, StringComparison.Ordinal);
        });
        Assert.Equal("Venerable Knight", card.Card.Name);
        Assert.Equal("not-cached", card.Card.TagCoverage);
        Assert.True(card.Card.Raw!.Value.TryGetProperty("object", out _));
    }

    /// <summary>
    /// Installs and reopens the real four-dataset corpus only under explicit environment opt-in.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    [Trait("Category", "ManualCorpus")]
    public async Task RealCorpus_InstallsReusesAndOptionallyExercisesRollback()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_FULL_SCRYFALL_CORPUS"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_FULL_SCRYFALL_CORPUS=1 and MTGMCP_SCRYFALL_ACCEPTANCE_DATA_DIR to run the multi-gigabyte acceptance workflow.");
        }

        string? dataRoot = Environment.GetEnvironmentVariable("MTGMCP_SCRYFALL_ACCEPTANCE_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(dataRoot));
        dataRoot = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(dataRoot);
        ScryfallCorpusSyncResult sync;
        using (ScryfallService writer = new(dataRoot, allowLocalWrites: true, "0.9.0-preview.1"))
        {
            sync = RequireSuccess(await writer.SyncCorpusAsync(
                "refresh",
                null,
                TestContext.Current.CancellationToken));
            Assert.Equal(4, sync.Datasets.Count);
        }

        using (ScryfallService reader = new(dataRoot, allowLocalWrites: false, "0.9.0-preview.1"))
        {
            ScryfallCorpusStatus status = RequireSuccess(await reader.GetCorpusStatusAsync(
                TestContext.Current.CancellationToken));
            Assert.Equal("available", status.State);
            Assert.Equal(sync.GenerationId, status.Active!.GenerationId);
            Assert.Equal("valid", status.Active.Integrity);
            Assert.Equal(
                ["all_cards", "art_tags", "oracle_tags", "rulings"],
                status.Active.Datasets.Select(value => value.Type));
            Assert.All(status.Active.Datasets, dataset =>
            {
                Assert.True(dataset.RowCount > 0);
                Assert.True(dataset.SourceBytes > 0);
                Assert.Matches("^[0-9a-f]{64}$", dataset.Checksum);
            });
            ScryfallCardResult card = RequireSuccess(await reader.GetCardAsync(
                new ScryfallCardLookup("exact-name", "Venerable Knight"),
                "cache-only",
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("corpus", card.Origin);
            Assert.Equal("complete-direct", card.Card.TagCoverage);
            Assert.NotEmpty(card.Card.Tags);
        }

        bool rollbackExercised = false;
        if (sync.PreviousGenerationId is Guid previous)
        {
            using ScryfallService writer = new(dataRoot, allowLocalWrites: true, "0.9.0-preview.1");
            ScryfallCorpusMutationResult rolledBack = RequireSuccess(await writer.RollbackCorpusAsync(
                sync.GenerationId,
                previous,
                acknowledgeActivationChange: true,
                TestContext.Current.CancellationToken));
            ScryfallCorpusMutationResult restored = RequireSuccess(await writer.RollbackCorpusAsync(
                rolledBack.ActiveGenerationId!.Value,
                rolledBack.PreviousGenerationId!.Value,
                acknowledgeActivationChange: true,
                TestContext.Current.CancellationToken));
            Assert.Equal(sync.GenerationId, restored.ActiveGenerationId);
            rollbackExercised = true;
        }

        await WriteAcceptanceReportAsync(sync, rollbackExercised).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a path-free, credential-free summary only when the caller explicitly supplies an output file.
    /// </summary>
    private static async Task WriteAcceptanceReportAsync(
        ScryfallCorpusSyncResult sync,
        bool rollbackExercised)
    {
        string? reportPath = Environment.GetEnvironmentVariable("MTGMCP_SCRYFALL_ACCEPTANCE_REPORT");
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        reportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        string json = JsonSerializer.Serialize(new
        {
            observedAtUtc = DateTimeOffset.UtcNow,
            sync.Outcome,
            datasets = sync.Datasets.Select(dataset => new
            {
                dataset.Type,
                dataset.ProviderUpdatedAtUtc,
                dataset.RowCount,
                dataset.SourceBytes,
                dataset.Checksum,
            }),
            secondProcessReuse = "passed",
            cardTagJoin = "passed",
            rollback = rollbackExercised ? "passed" : "not-applicable",
            retained = true,
        }, ReportJsonOptions);
        await File.WriteAllTextAsync(
            reportPath,
            json + Environment.NewLine,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts successful live data while retaining the closed failure case in test output.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        if (result.Value is OperationSuccess<T> success)
        {
            return success.Data;
        }

        string detail = result.Value switch
        {
            OperationUnavailable failure => $"{failure.ReasonCode}: {failure.Message}",
            OperationInvalidInput failure => $"{failure.ReasonCode}: {failure.Message}",
            OperationConflict failure => $"{failure.ReasonCode}: {failure.Message}",
            OperationNotFound failure => $"{failure.ReasonCode}: {failure.Message}",
            OperationNotCached failure => $"{failure.ReasonCode}: {failure.Message}",
            OperationUnsupported failure => $"{failure.ReasonCode}: {failure.Message}",
            _ => "The live acceptance returned an unknown result case.",
        };
        Assert.Fail(detail);
        return default!;
    }
}
