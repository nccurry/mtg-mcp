using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Checks one small public Commander Spellbook response outside normal offline tests.
/// </summary>
public sealed class SpellbookLiveTests
{
    /// <summary>
    /// Reads one grouped variant result without following pages or changing source data.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task VariantSearch_ReturnsOneBoundedSourceResponse()
    {
        using TemporarySpellbookDirectory temporary = new();
        using SpellbookService service = new(
            SpellbookOptions.CreateDefault(temporary.Path),
            "0.9.0");

        OperationResult<SpellbookEvidence> result = await service.SearchVariantsAsync(
            new SpellbookVariantSearchRequest(
                "type:combo",
                new SpellbookPageOptions(Limit: 1, Offset: 0, GroupByCombo: true)),
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        SpellbookEvidence evidence = Assert.IsType<OperationSuccess<SpellbookEvidence>>(result.Value).Data;
        Assert.Equal("variant-search", evidence.Operation);
        Assert.Equal("GET /variants/", evidence.Endpoint);
        Assert.Equal("network", evidence.CacheStatus);
        Assert.Equal("type:combo", evidence.Request.SourceQuery);
        Assert.Equal(new SpellbookPageRequest(1, 0, true), evidence.Request.Page);
        Assert.Equal(JsonValueKind.Object, evidence.Data.ValueKind);
        Assert.True(evidence.Data.TryGetProperty("results", out _));
    }
}
