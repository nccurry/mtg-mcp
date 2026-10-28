using System.Text.Json;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the merged Scryfall tools: each reads the kind of data it is asked for, names that kind in its result,
/// and refuses fields that do not belong to the chosen kind. Also covers the card lookup merge.
/// </summary>
public sealed class ScryfallReferenceToolTests
{
    /// <summary>
    /// Looks up the fixture's white card by its Scryfall ID.
    /// </summary>
    private static readonly ScryfallCardLookup WhiteLookup =
        new("scryfall-id", ScryfallTestFixture.WhiteCardId.ToString("D"));

    /// <summary>
    /// Asks the card details tool for printings only.
    /// </summary>
    private static readonly string[] PrintsOnly = ["prints"];

    /// <summary>
    /// Asks the card details tool for rulings only.
    /// </summary>
    private static readonly string[] RulingsOnly = ["rulings"];

    /// <summary>
    /// Asks the card details tool for both sections.
    /// </summary>
    private static readonly string[] PrintsAndRulings = ["prints", "rulings"];

    /// <summary>
    /// Names one item the fixture provider returns for each reference kind.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedItem = new()
    {
        ["sets"] = "Test Set",
        ["catalog"] = "Knight",
        ["bulk"] = "all_cards",
    };

    /// <summary>
    /// Verifies each reference kind reads its data and the result names the same kind.
    /// </summary>
    [Theory]
    [InlineData("sets", "sets")]
    [InlineData("catalog", "catalog")]
    [InlineData("bulk", "bulk")]
    public async Task ReferenceGet_EachKind_ReturnsResultWithTheSameKind(string kind, string sectionName)
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallReferenceResult> result = await fixture.Tools.GetReferenceAsync(
            kind,
            catalogName: kind == "catalog" ? "card-types" : null,
            cancellationToken: TestContext.Current.CancellationToken);

        JsonElement data = Data(result);
        Assert.Equal(kind, data.GetProperty("kind").GetString());
        Assert.True(data.TryGetProperty(sectionName, out _), data.GetRawText());
        Assert.Contains(ExpectedItem[kind], data.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies one exact set can be read by code.
    /// </summary>
    [Fact]
    public async Task ReferenceGet_SetsWithCode_ReturnsThatSet()
    {
        using ServiceFixture fixture = new();

        JsonElement data = Data(await fixture.Tools.GetReferenceAsync(
            "sets",
            codeOrId: "tst",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "tst",
            data.GetProperty("sets").GetProperty("page").GetProperty("items")[0].GetProperty("code").GetString());
    }

    /// <summary>
    /// Verifies a field that does not belong to the chosen reference kind is refused and named.
    /// </summary>
    [Theory]
    [InlineData("sets", "catalog-name", "catalogName")]
    [InlineData("catalog", "code", "codeOrId")]
    [InlineData("catalog", "raw", "includeRaw")]
    [InlineData("bulk", "code", "codeOrId")]
    [InlineData("bulk", "catalog-name", "catalogName")]
    [InlineData("bulk", "raw", "includeRaw")]
    [InlineData("bulk", "cursor", "cursor")]
    [InlineData("bulk", "size", "pageSize")]
    public async Task ReferenceGet_FieldOutsideTheKind_ReturnsFieldNotAllowed(
        string kind,
        string misplaced,
        string expectedPath)
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallReferenceResult> result = await fixture.Tools.GetReferenceAsync(
            kind,
            codeOrId: misplaced == "code" ? "tst" : null,
            catalogName: kind == "catalog" ? "card-types" : misplaced == "catalog-name" ? "card-types" : null,
            includeRaw: misplaced == "raw",
            cursor: misplaced == "cursor" ? "abc" : null,
            pageSize: misplaced == "size" ? 5 : null,
            cancellationToken: TestContext.Current.CancellationToken);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("field-not-allowed", invalid.ReasonCode);
        Assert.Equal(expectedPath, invalid.Path);
    }

    /// <summary>
    /// Verifies a catalog read without a name, and an unknown kind, are refused.
    /// </summary>
    [Fact]
    public async Task ReferenceGet_MissingCatalogNameOrUnknownKind_ReturnsInvalidInput()
    {
        using ServiceFixture fixture = new();

        OperationInvalidInput missing = Assert.IsType<OperationInvalidInput>((await fixture.Tools.GetReferenceAsync(
            "catalog",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        OperationInvalidInput unknown = Assert.IsType<OperationInvalidInput>((await fixture.Tools.GetReferenceAsync(
            "sources",
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal(("missing-field", "catalogName"), (missing.ReasonCode, missing.Path));
        Assert.Equal(("invalid-kind", "kind"), (unknown.ReasonCode, unknown.Path));
    }

    /// <summary>
    /// Verifies the card details tool returns only the sections that were asked for.
    /// </summary>
    [Fact]
    public async Task CardDetails_Include_ReturnsOnlyTheAskedSections()
    {
        using ServiceFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        JsonElement printsOnly = Data(await fixture.Tools.GetCardDetailsAsync(
            WhiteLookup,
            PrintsOnly,
            cancellationToken: token));
        JsonElement rulingsOnly = Data(await fixture.Tools.GetCardDetailsAsync(
            WhiteLookup,
            RulingsOnly,
            cancellationToken: token));
        JsonElement both = Data(await fixture.Tools.GetCardDetailsAsync(
            WhiteLookup,
            PrintsAndRulings,
            cancellationToken: token));

        Assert.True(printsOnly.TryGetProperty("prints", out _));
        Assert.False(printsOnly.TryGetProperty("rulings", out _));
        Assert.False(rulingsOnly.TryGetProperty("prints", out _));
        Assert.Equal(
            "Provider ruling.",
            rulingsOnly.GetProperty("rulings").GetProperty("page").GetProperty("items")[0]
                .GetProperty("comment").GetString());
        Assert.True(both.TryGetProperty("prints", out _));
        Assert.True(both.TryGetProperty("rulings", out _));
        Assert.Equal(ScryfallTestFixture.WhiteOracleId, both.GetProperty("oracleId").GetGuid());
    }

    /// <summary>
    /// Verifies a failure in one requested section is returned as the whole result.
    /// </summary>
    [Fact]
    public async Task CardDetails_SectionFails_ReturnsThatFailure()
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallCardDetailsResult> result = await fixture.Tools.GetCardDetailsAsync(
            WhiteLookup,
            PrintsAndRulings,
            freshnessPolicy: "cache-only",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("scryfall-request-not-cached", Assert.IsType<OperationNotCached>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies bad include lists and sections' own fields are refused and named.
    /// </summary>
    [Theory]
    [InlineData(new string[0], null, null, "include")]
    [InlineData(new[] { "prints", "prints" }, null, null, "include")]
    [InlineData(new[] { "reviews" }, null, null, "include")]
    [InlineData(new[] { "prints" }, "x", null, "rulingsCursor")]
    [InlineData(new[] { "rulings" }, null, "x", "printsCursor")]
    public async Task CardDetails_InvalidIncludeOrMisplacedField_ReturnsInvalidInputNamingTheField(
        string[] include,
        string? rulingsCursor,
        string? printsCursor,
        string expectedPath)
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallCardDetailsResult> result = await fixture.Tools.GetCardDetailsAsync(
            WhiteLookup,
            include,
            printsCursor: printsCursor,
            rulingsCursor: rulingsCursor,
            cancellationToken: TestContext.Current.CancellationToken);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal(expectedPath, invalid.Path);
    }

    /// <summary>
    /// Verifies a missing include list is refused.
    /// </summary>
    [Fact]
    public async Task CardDetails_NullInclude_ReturnsInvalidInput()
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallCardDetailsResult> result = await fixture.Tools.GetCardDetailsAsync(
            WhiteLookup,
            null!,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("include", Assert.IsType<OperationInvalidInput>(result.Value).Path);
    }

    /// <summary>
    /// Verifies a page size outside the allowed range, or too large for raw output, is refused before the card is
    /// resolved, so the provider is never called.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(26, true)]
    public async Task CardDetails_BadPageSize_FailsBeforeAnyProviderCall(int pageSize, bool includeRaw)
    {
        using TemporaryDirectory temporary = new();
        int providerCalls = 0;
        using ScryfallService service = new(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions,
            ScryfallTestFixture.ApiBaseUri,
            handler: ScryfallTestFixture.Provider(intercept: request =>
            {
                providerCalls++;
                return null;
            }));
        ScryfallReferenceTools tools = new(service);

        OperationResult<ScryfallCardDetailsResult> result = await tools.GetCardDetailsAsync(
            WhiteLookup,
            ["prints"],
            includeRaw: includeRaw,
            pageSize: pageSize,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal(0, providerCalls);
    }

    /// <summary>
    /// Verifies the snapshot tool lists saved snapshots and replays one by ID, with a result that names the kind.
    /// </summary>
    [Fact]
    public async Task SearchSnapshots_ListThenGet_ReturnsResultsWithTheSameKind()
    {
        using ServiceFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        OperationResult<ScryfallSearchResponse> search = await fixture.Service.SearchAsync(
            "t:knight",
            cancellationToken: token);
        Guid snapshotId = Assert.IsType<OperationSuccess<ScryfallSearchResponse>>(search.Value)
            .Data.Snapshot.SnapshotId;

        JsonElement list = Data(await fixture.Tools.GetSearchSnapshotsAsync(
            "list",
            operation: "search",
            cancellationToken: token));
        JsonElement get = Data(await fixture.Tools.GetSearchSnapshotsAsync(
            "get",
            snapshotId,
            cancellationToken: token));

        Assert.Equal("list", list.GetProperty("kind").GetString());
        Assert.Contains(
            list.GetProperty("snapshots").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("snapshotId").GetGuid() == snapshotId);
        Assert.Equal("get", get.GetProperty("kind").GetString());
        Assert.Equal(
            snapshotId,
            get.GetProperty("snapshot").GetProperty("summary").GetProperty("snapshotId").GetGuid());
    }

    /// <summary>
    /// Verifies a snapshot that does not exist is reported as not found, not as an empty result.
    /// </summary>
    [Fact]
    public async Task SearchSnapshots_GetUnknownSnapshot_ReturnsNotFound()
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallSnapshotsResult> result = await fixture.Tools.GetSearchSnapshotsAsync(
            "get",
            Guid.CreateVersion7(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<OperationNotFound>(result.Value);
    }

    /// <summary>
    /// Verifies fields that do not belong to the chosen snapshot kind, a missing ID, and an unknown kind are refused.
    /// </summary>
    [Theory]
    [InlineData("list", "id", "snapshotId")]
    [InlineData("list", "raw", "includeRaw")]
    [InlineData("get", "operation", "operation")]
    [InlineData("get", "after", "retrievedAfterUtc")]
    [InlineData("get", "before", "retrievedBeforeUtc")]
    [InlineData("get", "none", "snapshotId")]
    [InlineData("other", "none", "kind")]
    public async Task SearchSnapshots_InvalidFieldCombination_ReturnsInvalidInputNamingTheField(
        string kind,
        string misplaced,
        string expectedPath)
    {
        using ServiceFixture fixture = new();

        OperationResult<ScryfallSnapshotsResult> result = await fixture.Tools.GetSearchSnapshotsAsync(
            kind,
            snapshotId: kind == "get" && misplaced == "none" ? null : misplaced == "id" || kind == "get"
                ? Guid.CreateVersion7()
                : null,
            operation: misplaced == "operation" ? "search" : null,
            retrievedAfterUtc: misplaced == "after" ? DateTimeOffset.UnixEpoch : null,
            retrievedBeforeUtc: misplaced == "before" ? DateTimeOffset.UnixEpoch : null,
            includeRaw: misplaced == "raw",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expectedPath, Assert.IsType<OperationInvalidInput>(result.Value).Path);
    }

    /// <summary>
    /// Verifies one fuzzy-name lookup is answered as a single row, and fuzzy names cannot be mixed with others.
    /// </summary>
    [Fact]
    public async Task CardsGet_FuzzyNameLookup_ReturnsOneRowAndRefusesMixing()
    {
        using ServiceFixture fixture = new();
        ScryfallReadTools tools = new(fixture.Service);
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationResult<ScryfallCollectionResponse> single = await tools.GetCardsAsync(
            [new ScryfallCardLookup("fuzzy-name", "venerable knite")],
            cancellationToken: token);
        OperationResult<ScryfallCollectionResponse> mixed = await tools.GetCardsAsync(
            [
                new ScryfallCardLookup("fuzzy-name", "venerable knite"),
                new ScryfallCardLookup("exact-name", "Sol Ring"),
            ],
            cancellationToken: token);

        ScryfallCollectionResponse response = Assert.IsType<OperationSuccess<ScryfallCollectionResponse>>(
            single.Value).Data;
        ScryfallCollectionRowResponse row = Assert.Single(response.Page.Items);
        Assert.Equal(("found", 0), (row.Status, row.Index));
        Assert.NotNull(row.Card);
        Assert.Equal(1, response.Page.TotalCount);
        Assert.Equal(
            "fuzzy-collection-lookup-unsupported",
            Assert.IsType<OperationInvalidInput>(mixed.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a fuzzy-name lookup that matches nothing returns one row with the status not-found.
    /// </summary>
    [Fact]
    public async Task CardsGet_FuzzyNameLookupWithoutAMatch_ReturnsANotFoundRow()
    {
        using TemporaryDirectory temporary = new();
        using ScryfallService service = new(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions,
            ScryfallTestFixture.ApiBaseUri,
            handler: ScryfallTestFixture.Provider(intercept: request =>
                request.RequestUri!.AbsolutePath == "/cards/named"
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{\"object\":\"error\",\"status\":404}"),
                    }
                    : null));
        ScryfallReadTools tools = new(service);

        OperationResult<ScryfallCollectionResponse> result = await tools.GetCardsAsync(
            [new ScryfallCardLookup("fuzzy-name", "zzzz")],
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallCollectionResponse response = Assert.IsType<OperationSuccess<ScryfallCollectionResponse>>(
            result.Value).Data;
        ScryfallCollectionRowResponse row = Assert.Single(response.Page.Items);
        Assert.Equal(("not-found", 0), (row.Status, row.Index));
        Assert.Null(row.Card);
    }

    /// <summary>
    /// Verifies a fuzzy-name lookup in cache-only mode with nothing cached returns one not-cached row, like the
    /// collection path, and never reaches the provider.
    /// </summary>
    [Fact]
    public async Task CardsGet_FuzzyNameLookupCacheOnlyWithNothingCached_ReturnsANotCachedRow()
    {
        using TemporaryDirectory temporary = new();
        int providerCalls = 0;
        using ScryfallService service = new(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions,
            ScryfallTestFixture.ApiBaseUri,
            handler: ScryfallTestFixture.Provider(intercept: request =>
            {
                providerCalls++;
                return null;
            }));
        ScryfallReadTools tools = new(service);

        OperationResult<ScryfallCollectionResponse> result = await tools.GetCardsAsync(
            [new ScryfallCardLookup("fuzzy-name", "venerable knight")],
            freshnessPolicy: "cache-only",
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallCollectionResponse response = Assert.IsType<OperationSuccess<ScryfallCollectionResponse>>(
            result.Value).Data;
        ScryfallCollectionRowResponse row = Assert.Single(response.Page.Items);
        Assert.Equal(("not-cached", 0), (row.Status, row.Index));
        Assert.Null(row.Card);
        Assert.Equal(0, providerCalls);
    }

    /// <summary>
    /// Reads the successful data of a result as JSON, written the way the server writes it.
    /// </summary>
    private static JsonElement Data<T>(OperationResult<T> result)
    {
        OperationSuccess<T> success = Assert.IsType<OperationSuccess<T>>(result.Value);
        return JsonSerializer.SerializeToElement(success, ToolCallPolicy.SerializerOptions).GetProperty("data");
    }

    /// <summary>
    /// Holds a Scryfall service over a fake provider and the tools built on it.
    /// </summary>
    private sealed class ServiceFixture : IDisposable
    {
        /// <summary>
        /// Owns the folder that holds the Scryfall database.
        /// </summary>
        private readonly TemporaryDirectory temporary = new();

        /// <summary>
        /// Creates the service and the reference tools.
        /// </summary>
        internal ServiceFixture()
        {
            Service = new ScryfallService(
                Path.Combine(temporary.Path, "data"),
                allowLocalWrites: true,
                "0.9.0-preview.1",
                ToolCallPolicy.SerializerOptions,
                ScryfallTestFixture.ApiBaseUri,
                handler: ScryfallTestFixture.Provider());
            Tools = new ScryfallReferenceTools(Service);
        }

        /// <summary>
        /// Gets the service behind the tools.
        /// </summary>
        internal ScryfallService Service { get; }

        /// <summary>
        /// Gets the merged reference tools under test.
        /// </summary>
        internal ScryfallReferenceTools Tools { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Service.Dispose();
            temporary.Dispose();
        }
    }
}
