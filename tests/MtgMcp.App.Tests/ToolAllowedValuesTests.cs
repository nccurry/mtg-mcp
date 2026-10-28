using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies every value a tool schema publishes as an allowed choice is accepted at run time.
/// </summary>
public sealed class ToolAllowedValuesTests
{
    /// <summary>
    /// Verifies each repeated choice list is declared once and shared by every parameter that uses it.
    /// </summary>
    [Fact]
    public void RepeatedChoiceLists_UseTheirSharedAttribute()
    {
        List<string> offenders = [];
        foreach ((string toolName, MethodInfo method) in ToolCallPolicy
            .IndexToolMethods(typeof(ToolCallPolicy).Assembly.GetTypes()))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Type? expected = parameter.Name switch
                {
                    "freshnessPolicy" => typeof(FreshnessPolicyValuesAttribute),
                    "formatId" => typeof(InterchangeFormatValuesAttribute),
                    "tagType" => typeof(TagKindValuesAttribute),
                    "responseFormat" => typeof(ResponseFormatValuesAttribute),
                    _ => null,
                };
                if (expected is not null && parameter.GetCustomAttribute(expected) is null)
                {
                    offenders.Add($"{toolName}.{parameter.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Verifies the published interchange format IDs are exactly the formats the catalog lists, in both directions.
    /// </summary>
    [Fact]
    public void InterchangeFormatIds_MatchCatalog()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckInterchangeReadTools reads = new(new DeckInterchangeService(store));
        IReadOnlyList<DeckInterchangeFormat> catalog =
            Assert.IsType<OperationSuccess<IReadOnlyList<DeckInterchangeFormat>>>(reads.ListFormats().Value).Data;

        string[] published = [.. new InterchangeFormatValuesAttribute().Values.Cast<string>()];

        Assert.Equal(catalog.Select(value => value.FormatId).Order(), published.Order());
        Assert.All(catalog, value => Assert.True(value.SupportsImport && value.SupportsExport, value.FormatId));
    }

    /// <summary>
    /// Verifies the published Scryfall order list matches the service's accepted orders.
    /// </summary>
    [Fact]
    public void ScryfallOrders_MatchServiceVocabulary()
    {
        string[] published = Published(typeof(ScryfallReadTools), "SearchAsync", "order");

        Assert.Equal(ScryfallCardEvidenceOperations.SearchOrders.Order(), published.Order());
    }

    /// <summary>
    /// Verifies the published snapshot operation names match the names snapshots are recorded under.
    /// </summary>
    [Fact]
    public void ScryfallSnapshotOperations_MatchRecordedNames()
    {
        string[] published = Published(typeof(ScryfallReadTools), "ListSnapshotsAsync", "operation");

        Assert.Equal(ScryfallOperationNames.All.Order(), published.Order());
    }

    /// <summary>
    /// Verifies the search service accepts every published uniqueness, order, direction, and freshness value.
    /// </summary>
    [Fact]
    public async Task ScryfallSearch_EveryPublishedChoice_IsNotRejected()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = CreateService(temporary, allowLocalWrites: false);
        CancellationToken token = TestContext.Current.CancellationToken;

        foreach (string unique in Published(typeof(ScryfallReadTools), "SearchAsync", "unique"))
        {
            AssertNotRejected(
                await SearchAsync(service, unique, "name", "auto", "cache-only", token));
        }

        foreach (string order in Published(typeof(ScryfallReadTools), "SearchAsync", "order"))
        {
            AssertNotRejected(
                await SearchAsync(service, "cards", order, "auto", "cache-only", token));
        }

        foreach (string direction in Published(typeof(ScryfallReadTools), "SearchAsync", "direction"))
        {
            AssertNotRejected(
                await SearchAsync(service, "cards", "name", direction, "cache-only", token));
        }

        foreach (string freshness in Published(typeof(ScryfallReadTools), "SearchAsync", "freshnessPolicy"))
        {
            AssertNotRejected(
                await SearchAsync(service, "cards", "name", "auto", freshness, token));
        }

        OperationInvalidInput control = Assert.IsType<OperationInvalidInput>(
            (await SearchAsync(service, "cards", "bogus", "auto", "cache-only", token)).Value);
        Assert.Equal("invalid-scryfall-search-options", control.ReasonCode);
    }

    /// <summary>
    /// Verifies the corpus sync accepts every published metadata policy.
    /// </summary>
    [Fact]
    public async Task ScryfallCorpusSync_EveryPublishedMetadataPolicy_IsNotRejected()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = CreateService(temporary, allowLocalWrites: true);

        foreach (string policy in Published(typeof(ScryfallWriteTools), "SyncCorpusAsync", "metadataPolicy"))
        {
            AssertNotRejected(await service.SyncCorpusAsync(policy, null, TestContext.Current.CancellationToken));
        }

        OperationInvalidInput control = Assert.IsType<OperationInvalidInput>(
            (await service.SyncCorpusAsync("bogus", null, TestContext.Current.CancellationToken)).Value);
        Assert.Equal("invalid-freshness-policy", control.ReasonCode);
    }

    /// <summary>
    /// Verifies the snapshot list and tag search accept every published operation and tag kind.
    /// </summary>
    [Fact]
    public async Task ScryfallSnapshotAndTagFilters_EveryPublishedValue_IsNotRejected()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = CreateService(temporary, allowLocalWrites: false);
        CancellationToken token = TestContext.Current.CancellationToken;

        foreach (string operation in Published(typeof(ScryfallReadTools), "ListSnapshotsAsync", "operation"))
        {
            OperationResult<ScryfallPage<ScryfallSnapshotSummary>> listed = await service.ListSnapshotsAsync(
                operation,
                cancellationToken: token);
            Assert.IsType<OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>>>(listed.Value);
        }

        foreach (string tagType in new TagKindValuesAttribute().Values.Cast<string>())
        {
            AssertNotRejected(await service.SearchTagsAsync("ramp", tagType, cancellationToken: token));
        }
    }

    /// <summary>
    /// Reads the allowed values one tool parameter publishes in its schema.
    /// </summary>
    private static string[] Published(Type toolType, string methodName, string parameterName)
    {
        MethodInfo method = toolType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        ParameterInfo parameter = method.GetParameters().Single(value => value.Name == parameterName);
        AllowedValuesAttribute attribute = parameter.GetCustomAttribute<AllowedValuesAttribute>(inherit: true)!;
        return [.. attribute.Values.Cast<string>()];
    }

    /// <summary>
    /// Names the failures that mean a supplied choice itself was invalid.
    /// </summary>
    private static readonly string[] RejectionCodes = ["invalid-scryfall-search-options", "invalid-freshness-policy"];

    /// <summary>
    /// Runs one search with the given choices and no other options.
    /// </summary>
    private static Task<OperationResult<ScryfallSearchResponse>> SearchAsync(
        ScryfallService service,
        string unique,
        string order,
        string direction,
        string freshnessPolicy,
        CancellationToken token)
    {
        return service.SearchAsync(
            "t:elf",
            unique,
            order,
            direction,
            freshnessPolicy: freshnessPolicy,
            cancellationToken: token);
    }

    /// <summary>
    /// Fails when a result says the supplied choice itself was invalid; any other outcome means it was accepted.
    /// </summary>
    private static void AssertNotRejected<T>(OperationResult<T> result)
    {
        if (result.Value is OperationInvalidInput rejected && RejectionCodes.Contains(rejected.ReasonCode))
        {
            throw new Xunit.Sdk.XunitException($"A published value was rejected: {rejected.ReasonCode}.");
        }
    }

    /// <summary>
    /// Creates a service whose provider always answers not-found, so nothing reaches the network.
    /// </summary>
    private static ScryfallService CreateService(TemporaryDirectory temporary, bool allowLocalWrites)
    {
        return new ScryfallService(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions,
            new Uri("https://scryfall.test/"),
            handler: new NotFoundHandler());
    }

    /// <summary>
    /// Answers every request with an empty not-found response.
    /// </summary>
    private sealed class NotFoundHandler : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}"),
            });
        }
    }
}
