using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies exact installed tag resolution and complete parent evidence for deck categorization.
/// </summary>
public sealed class ScryfallDeckTagOperationsTests
{
    /// <summary>
    /// Resolves exact source tags and returns every parent of a direct child tag without provider reads.
    /// </summary>
    [Fact]
    public async Task ResolveAndRead_UsesOneGenerationAndAllDirectTagParents()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCorpusSyncResult sync = RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken));
        int requestsAfterSync = handler.Requests.Count;

        ScryfallTagResolution resolution = RequireSuccess(await service.ResolveDeckTagIdentitiesAsync(
            [
                new ScryfallTagIdentity("oracle", ScryfallTestFixture.AggroTagId),
                new ScryfallTagIdentity("oracle", ExactSlug: "creature"),
            ],
            "cache-only",
            TestContext.Current.CancellationToken));
        Assert.Equal(sync.GenerationId, resolution.GenerationId);
        Assert.Equal([ScryfallTestFixture.AggroTagId, ScryfallTestFixture.CreatureTagId], resolution.TagIds);

        ScryfallDeckTagEvidence evidence = RequireSuccess(await service.ReadDeckTagEvidenceAsync(
            resolution.GenerationId,
            [
                new ScryfallCardLookup("exact-name", "Venerable Knight"),
                new ScryfallCardLookup("exact-name", "Missing Fixture"),
            ],
            TestContext.Current.CancellationToken));
        Assert.Equal(resolution.GenerationId, evidence.GenerationId);
        Assert.True(evidence.Entries[0].IsComplete);
        Assert.False(evidence.Entries[1].IsComplete);
        ScryfallDirectTagEvidence directTag = Assert.Single(evidence.Entries[0].Tags);
        Assert.Equal(ScryfallTestFixture.WeenieTagId, directTag.TagId);
        Assert.Equal(
            [ScryfallTestFixture.AggroTagId, ScryfallTestFixture.CreatureTagId],
            directTag.AncestorTagIds);
        Assert.Empty(evidence.Entries[1].Tags);
        Assert.Equal(requestsAfterSync, handler.Requests.Count);
    }

    /// <summary>
    /// Returns explicit outcomes for missing tag identities and forbidden implicit refreshes.
    /// </summary>
    [Fact]
    public async Task Resolve_RequiresInstalledExactTagsAndExplicitSync()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        _ = RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken));
        int requestsAfterSync = handler.Requests.Count;

        OperationResult<ScryfallTagResolution> missing = await service.ResolveDeckTagIdentitiesAsync(
            [new ScryfallTagIdentity("oracle", ExactSlug: "not-a-source-tag")],
            "default",
            TestContext.Current.CancellationToken);
        Assert.Equal("scryfall-tag-not-found", Assert.IsType<OperationNotFound>(missing.Value).ReasonCode);

        OperationResult<ScryfallTagResolution> refresh = await service.ResolveDeckTagIdentitiesAsync(
            [new ScryfallTagIdentity("oracle", ScryfallTestFixture.AggroTagId)],
            "refresh",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "category-rules-require-explicit-card-data-sync",
            Assert.IsType<OperationUnsupported>(refresh.Value).ReasonCode);
        Assert.Equal(requestsAfterSync, handler.Requests.Count);
    }

    /// <summary>
    /// Creates a fixture-backed Scryfall service with local writes enabled.
    /// </summary>
    private static ScryfallService CreateService(string dataRoot, HttpMessageHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ApiBaseUri,
            TimeSpan.FromHours(24),
            timeProvider: null,
            handler);
    }

    /// <summary>
    /// Extracts successful data for concise fixture assertions.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
