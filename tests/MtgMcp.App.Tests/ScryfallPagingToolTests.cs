using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the Scryfall tools page with one cursor, refuse a stale or unusable cursor with the shared reason codes,
/// ask for an exact confirmation phrase before destroying data, and name the argument of every refusal.
/// </summary>
public sealed class ScryfallPagingToolTests
{
    /// <summary>
    /// Verifies a search pages with nextCursor, ends with a null cursor, and refuses an edited cursor at cursor.
    /// </summary>
    [Fact]
    public async Task Search_NextAndLastPage_ThenEditedCursor_IsInvalidAtCursor()
    {
        using ToolFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        ScryfallSearchResponse first = Success(await fixture.Read.SearchAsync(
            "t:knight", pageSize: 1, cancellationToken: token));
        ScryfallSearchResponse last = Success(await fixture.Read.SearchAsync(
            "t:knight", cursor: first.Page.NextCursor, pageSize: 1, cancellationToken: token));
        OperationInvalidInput edited = Assert.IsType<OperationInvalidInput>((await fixture.Read.SearchAsync(
            "t:knight", cursor: first.Page.NextCursor![..^2] + "AA", pageSize: 1, cancellationToken: token)).Value);
        OperationInvalidInput other = Assert.IsType<OperationInvalidInput>((await fixture.Read.SearchAsync(
            "t:other", cursor: first.Page.NextCursor, pageSize: 1, cancellationToken: token)).Value);

        Assert.Equal(2, first.Page.TotalCount);
        Assert.NotNull(first.Page.NextCursor);
        Assert.Single(last.Page.Items);
        Assert.Null(last.Page.NextCursor);
        Assert.Equal(("invalid-cursor", "cursor"), (edited.ReasonCode, edited.Path));
        Assert.Equal(("invalid-cursor", "cursor"), (other.ReasonCode, other.Path));
    }

    /// <summary>
    /// Verifies a cursor from before a refresh that changed the results is stale, not invalid.
    /// </summary>
    [Fact]
    public async Task Search_ResultsChangedAfterTheFirstPage_ReturnsCursorStale()
    {
        using TemporaryDirectory temporary = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        string? cursor;
        using (ScryfallService before = Service(temporary, "Venerable Knight"))
        {
            cursor = Success(await new ScryfallReadTools(before).SearchAsync(
                "t:knight", pageSize: 1, cancellationToken: token)).Page.NextCursor;
        }

        using ScryfallService after = Service(temporary, "Changed Knight");
        OperationConflict stale = Assert.IsType<OperationConflict>((await new ScryfallReadTools(after).SearchAsync(
            "t:knight", freshnessPolicy: "refresh", cursor: cursor, pageSize: 1, cancellationToken: token)).Value);

        Assert.Equal(SharedReasonCodes.CursorStale, stale.ReasonCode);
    }

    /// <summary>
    /// Verifies a collection cursor presented with another freshness policy belongs to another request.
    /// </summary>
    [Fact]
    public async Task CardsGet_CursorWithAnotherFreshnessPolicy_IsInvalidAtCursor()
    {
        using ToolFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup[] lookups = [new("exact-name", "Sol Ring"), new("exact-name", "Venerable Knight")];
        ScryfallCollectionResponse first = Success(await fixture.Read.GetCardsAsync(
            lookups, pageSize: 1, cancellationToken: token));

        OperationInvalidInput other = Assert.IsType<OperationInvalidInput>((await fixture.Read.GetCardsAsync(
            lookups,
            freshnessPolicy: "cache-only",
            cursor: first.Page.NextCursor,
            pageSize: 1,
            cancellationToken: token)).Value);
        ScryfallCollectionResponse second = Success(await fixture.Read.GetCardsAsync(
            lookups, cursor: first.Page.NextCursor, pageSize: 1, cancellationToken: token));

        Assert.Equal(("invalid-cursor", "cursor"), (other.ReasonCode, other.Path));
        Assert.Single(second.Page.Items);
    }

    /// <summary>
    /// Verifies the details tool continues every section with one cursor, leaves out a section that has no more items,
    /// and ends with a null cursor.
    /// </summary>
    [Fact]
    public async Task CardDetails_OneCursor_ContinuesEverySectionAndEnds()
    {
        using ToolFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup lookup = new("scryfall-id", ScryfallTestFixture.WhiteCardId.ToString("D"));

        ScryfallCardDetailsResult first = Success(await fixture.Reference.GetCardDetailsAsync(
            lookup, ["prints", "rulings"], pageSize: 1, cancellationToken: token));
        ScryfallCardDetailsResult last = Success(await fixture.Reference.GetCardDetailsAsync(
            lookup, ["prints", "rulings"], cursor: first.NextCursor, pageSize: 1, cancellationToken: token));
        OperationInvalidInput wrongSections = Assert.IsType<OperationInvalidInput>(
            (await fixture.Reference.GetCardDetailsAsync(
                lookup, ["prints"], cursor: first.NextCursor, pageSize: 1, cancellationToken: token)).Value);

        Assert.Single(first.Prints!.Items);
        Assert.Single(first.Rulings!.Items);
        Assert.NotNull(first.NextCursor);
        Assert.Single(last.Prints!.Items);
        Assert.Null(last.Rulings);
        Assert.Null(last.NextCursor);
        Assert.Equal(("invalid-cursor", "cursor"), (wrongSections.ReasonCode, wrongSections.Path));
    }

    /// <summary>
    /// Verifies each destructive Scryfall tool refuses a missing or wrong phrase at confirmation and shows the exact
    /// phrase to send.
    /// </summary>
    [Fact]
    public async Task DestructiveTools_WrongPhrase_AreRefusedAtConfirmationWithTheExactPhrase()
    {
        using ToolFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid active = Guid.NewGuid();
        Guid previous = Guid.NewGuid();
        Guid snapshot = Guid.NewGuid();

        OperationInvalidInput rollback = Assert.IsType<OperationInvalidInput>((await fixture.Write.RollbackCorpusAsync(
            active, previous, $"rollback card-data {active:D}", token)).Value);
        OperationInvalidInput delete = Assert.IsType<OperationInvalidInput>((await fixture.Write.DeleteCorpusAsync(
            active, "yes", token)).Value);
        OperationInvalidInput snapshotDelete = Assert.IsType<OperationInvalidInput>(
            (await fixture.Write.DeleteSnapshotAsync(snapshot, "checksum", null!, token)).Value);

        Assert.All([rollback, delete, snapshotDelete], failure =>
        {
            Assert.Equal("confirmation-required", failure.ReasonCode);
            Assert.Equal("confirmation", failure.Path);
        });
        Assert.Contains($"\"rollback card-data {previous:D}\"", rollback.Message, StringComparison.Ordinal);
        Assert.Contains($"\"delete card-data {active:D}\"", delete.Message, StringComparison.Ordinal);
        Assert.Contains($"\"delete request-snapshot {snapshot:D}\"", snapshotDelete.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a phrase for another target is refused, and the exact phrase passes the confirmation check.
    /// </summary>
    [Fact]
    public async Task SnapshotDelete_PhraseBindsTheTarget()
    {
        using ToolFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid snapshot = Guid.NewGuid();

        OperationInvalidInput other = Assert.IsType<OperationInvalidInput>((await fixture.Write.DeleteSnapshotAsync(
            snapshot, "checksum", $"delete request-snapshot {Guid.NewGuid():D}", token)).Value);
        OperationResult<ScryfallSnapshotDeleteResult> confirmed = await fixture.Write.DeleteSnapshotAsync(
            snapshot, "checksum", $"delete request-snapshot {snapshot:D}", token);

        Assert.Equal("confirmation", other.Path);
        Assert.IsType<OperationNotFound>(confirmed.Value);
    }

    /// <summary>
    /// Verifies every refused argument of the Scryfall tools is named by its own path.
    /// </summary>
    [Fact]
    public async Task RefusedArguments_NameTheirOwnPath()
    {
        using ToolFixture fixture = new();
        CancellationToken token = TestContext.Current.CancellationToken;
        ScryfallCardLookup good = new("exact-name", "Sol Ring");
        ScryfallCardLookup bad = new("scryfall-id", "not-a-guid");
        string[] both = ["prints"];

        (string Expected, object? Result)[] cases =
        [
            Case("query", await fixture.Read.SearchAsync(" ", cancellationToken: token)),
            Case("unique", await fixture.Read.SearchAsync("x", unique: "bad", cancellationToken: token)),
            Case("order", await fixture.Read.SearchAsync("x", order: "bad", cancellationToken: token)),
            Case("direction", await fixture.Read.SearchAsync("x", direction: "bad", cancellationToken: token)),
            Case("freshnessPolicy", await fixture.Read.SearchAsync("x", freshnessPolicy: "bad", cancellationToken: token)),
            Case("lookups", await fixture.Read.GetCardsAsync([], cancellationToken: token)),
            Case("lookups[1]", await fixture.Read.GetCardsAsync([good, bad], cancellationToken: token)),
            Case("lookups[1].kind", await fixture.Read.GetCardsAsync(
                [good, new ScryfallCardLookup("fuzzy-name", "sol")], cancellationToken: token)),
            Case("lookup", await fixture.Reference.GetCardDetailsAsync(bad, both, cancellationToken: token)),
            Case("query", await fixture.Read.AutocompleteAsync(" ", cancellationToken: token)),
            Case("query", await fixture.Read.SearchTagsAsync(" ", cancellationToken: token)),
            Case("tagType", await fixture.Read.SearchTagsAsync("ramp", tagType: "bad", cancellationToken: token)),
            Case("tagIdentity", await fixture.Read.GetCardsByTagAsync(" ", "oracle", cancellationToken: token)),
            Case("tagType", await fixture.Read.GetCardsByTagAsync("ramp", "bad", cancellationToken: token)),
            Case("minimumWeight", await fixture.Read.GetCardsByTagAsync(
                "ramp", "oracle", minimumWeight: "bad", cancellationToken: token)),
            Case("codeOrId", await fixture.Reference.GetReferenceAsync("sets", codeOrId: " ", cancellationToken: token)),
            Case("catalogName", await fixture.Reference.GetReferenceAsync(
                "catalog", catalogName: "not-a-catalog", cancellationToken: token)),
            Case("retrievedAfterUtc", await fixture.Reference.GetRequestSnapshotsAsync(
                "list",
                retrievedAfterUtc: DateTimeOffset.UnixEpoch.AddDays(2),
                retrievedBeforeUtc: DateTimeOffset.UnixEpoch,
                cancellationToken: token)),
            Case("freshnessPolicy", await fixture.Write.SyncCorpusAsync("bogus", cancellationToken: token)),
            Case("expectedChecksum", await fixture.Write.DeleteSnapshotAsync(Guid.NewGuid(), " ", "x", token)),
        ];

        foreach ((string expected, object? result) in cases)
        {
            OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result);
            Assert.Equal(expected, invalid.Path);
        }
    }

    /// <summary>
    /// Verifies the measure that cuts raw pages keeps the leading rows that fit and always keeps one.
    /// </summary>
    [Fact]
    public void CountFitting_BigRows_KeepsTheLeadingRowsThatFitAndAtLeastOne()
    {
        ScryfallRowFit<string> fit = ScryfallToolExecution.CountFitting<string>();
        string big = new('x', 15_000);

        Assert.Equal(2, fit([big, big, big, big]));
        Assert.Equal(1, fit([new string('x', 60_000), big]));
        Assert.Equal(3, fit(["a", "b", "c"]));
    }

    /// <summary>
    /// Builds a service with the ordinary fake provider and a given name for the white card.
    /// </summary>
    private static ScryfallService Service(TemporaryDirectory temporary, string whiteName)
    {
        return new ScryfallService(
            Path.Combine(temporary.Path, "data"),
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions,
            ScryfallTestFixture.ApiBaseUri,
            handler: ScryfallTestFixture.Provider(whiteName: whiteName));
    }

    /// <summary>
    /// Pairs the path a refusal must name with the outcome of a call.
    /// </summary>
    private static (string Expected, object? Result) Case<T>(string expected, OperationResult<T> result)
    {
        return (expected, result.Value);
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Owns one service and the three tool classes around it.
    /// </summary>
    private sealed class ToolFixture : IDisposable
    {
        /// <summary>
        /// Owns the folder that holds the Scryfall database.
        /// </summary>
        private readonly TemporaryDirectory temporary = new();

        /// <summary>
        /// Holds the service behind the tools.
        /// </summary>
        private readonly ScryfallService service;

        /// <summary>
        /// Creates the service and the tools.
        /// </summary>
        internal ToolFixture()
        {
            service = Service(temporary, "Venerable Knight");
            Read = new ScryfallReadTools(service);
            Reference = new ScryfallReferenceTools(service);
            Write = new ScryfallWriteTools(service, OperationMode.Local);
        }

        /// <summary>
        /// Gets the card read tools.
        /// </summary>
        internal ScryfallReadTools Read { get; }

        /// <summary>
        /// Gets the detail tools.
        /// </summary>
        internal ScryfallReferenceTools Reference { get; }

        /// <summary>
        /// Gets the write tools.
        /// </summary>
        internal ScryfallWriteTools Write { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
            temporary.Dispose();
        }
    }
}
