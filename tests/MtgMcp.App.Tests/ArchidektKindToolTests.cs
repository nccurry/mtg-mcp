using System.Text.Json;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Archidekt.Tests;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the merged Archidekt folder and snapshot tools: each kind reads the right provider route, the result
/// names the kind, and fields that do not belong to the kind are refused with the field named.
/// </summary>
public sealed class ArchidektKindToolTests
{
    /// <summary>
    /// Provides a folder detail the provider returns for one folder.
    /// </summary>
    private const string FolderDetail =
        """{"id":9,"name":"Tests","private":true,"children":[{"id":12,"name":"Child","private":true,"children":[]}]}""";

    /// <summary>
    /// Verifies the folder tool reads the folder tree for list and one folder for get, and names the kind.
    /// </summary>
    [Fact]
    public async Task Folders_ListAndGet_ReadTheRightRouteAndNameTheKind()
    {
        using Fixture fixture = new();
        fixture.Handler.Add(HttpMethod.Get, "api/decks/folderTree/", ArchidektTestPayloads.FolderTree);
        fixture.Handler.Add(HttpMethod.Get, "api/decks/folders/9/", FolderDetail);
        fixture.Handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        fixture.Handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);

        JsonElement list = Data(await fixture.Tools.GetFoldersAsync(
            "list",
            cancellationToken: TestContext.Current.CancellationToken));
        JsonElement get = Data(await fixture.Tools.GetFoldersAsync(
            "get",
            "9",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("list", list.GetProperty("kind").GetString());
        Assert.Equal("Tests", list.GetProperty("folders").GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("get", get.GetProperty("kind").GetString());
        Assert.Equal(2, get.GetProperty("folder").GetProperty("items").GetArrayLength());
        Assert.Contains("api/decks/folderTree/", fixture.Handler.Requests.Select(request => request.Path));
        Assert.Contains("api/decks/folders/9/", fixture.Handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Verifies the snapshot tool reads the snapshot list for list and one snapshot for get, and names the kind.
    /// </summary>
    [Fact]
    public async Task Snapshots_ListAndGet_ReadTheRightRouteAndNameTheKind()
    {
        using Fixture fixture = new();
        fixture.Handler.Add(HttpMethod.Get, "api/decks/42/snapshots/", ArchidektTestPayloads.SnapshotList);
        fixture.Handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", ArchidektTestPayloads.Snapshot);

        JsonElement list = Data(await fixture.Tools.GetSnapshotsAsync(
            "list",
            "42",
            cancellationToken: TestContext.Current.CancellationToken));
        JsonElement get = Data(await fixture.Tools.GetSnapshotsAsync(
            "get",
            "42",
            "77",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("list", list.GetProperty("kind").GetString());
        Assert.Equal("77", list.GetProperty("snapshots").GetProperty("items")[0].GetProperty("snapshotId").GetString());
        Assert.Equal("get", get.GetProperty("kind").GetString());
        Assert.Equal("77", get.GetProperty("snapshot").GetProperty("summary").GetProperty("snapshotId").GetString());
        Assert.Contains("api/decks/42/snapshots/", fixture.Handler.Requests.Select(request => request.Path));
        Assert.Contains("api/decks/snapshots/77/", fixture.Handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Verifies an unknown kind, an ID given with list, and a missing ID for get are refused with reason and path.
    /// </summary>
    [Theory]
    [InlineData("folders", "other", null, "invalid-kind", "kind")]
    [InlineData("folders", "list", "9", "field-not-allowed", "folderId")]
    [InlineData("folders", "get", null, "missing-field", "folderId")]
    [InlineData("folders", "get", " ", "missing-field", "folderId")]
    [InlineData("snapshots", "other", null, "invalid-kind", "kind")]
    [InlineData("snapshots", "list", "77", "field-not-allowed", "snapshotId")]
    [InlineData("snapshots", "get", null, "missing-field", "snapshotId")]
    public async Task Tools_InvalidKindFieldCombination_ReturnsReasonAndPath(
        string tool,
        string kind,
        string? id,
        string expectedReason,
        string expectedPath)
    {
        using Fixture fixture = new();

        OperationInvalidInput invalid = tool == "folders"
            ? Assert.IsType<OperationInvalidInput>(
                (await fixture.Tools.GetFoldersAsync(
                    kind,
                    id,
                    cancellationToken: TestContext.Current.CancellationToken)).Value)
            : Assert.IsType<OperationInvalidInput>(
                (await fixture.Tools.GetSnapshotsAsync(
                    kind,
                    "42",
                    id,
                    cancellationToken: TestContext.Current.CancellationToken))
                .Value);

        Assert.Equal((expectedReason, expectedPath), (invalid.ReasonCode, invalid.Path));
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>
    /// Verifies a folder the provider does not have is returned as a provider-entity-not-found failure, and the
    /// requested folder path was read, not an empty union.
    /// </summary>
    [Fact]
    public async Task Folders_ProviderMiss_ReturnsTheFailure()
    {
        using Fixture fixture = new();

        OperationResult<ArchidektFoldersResult> result = await fixture.Tools.GetFoldersAsync(
            "get",
            "404",
            cancellationToken: TestContext.Current.CancellationToken);

        OperationNotFound missing = Assert.IsType<OperationNotFound>(result.Value);
        Assert.Equal("provider-entity-not-found", missing.ReasonCode);
        Assert.Contains("api/decks/folders/404/", fixture.Handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Reads the successful data of a result as JSON, written the way the server writes it.
    /// </summary>
    private static JsonElement Data<T>(OperationResult<T> result)
    {
        Assert.True(
            result.Value is OperationSuccess<T>,
            JsonSerializer.Serialize(result.Value, ToolCallPolicy.SerializerOptions));
        return JsonSerializer.SerializeToElement(result.Value, ToolCallPolicy.SerializerOptions).GetProperty("data");
    }

    /// <summary>
    /// Holds the tools over a fake Archidekt provider and a temporary deck store.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>
        /// Owns the folder that holds the deck database.
        /// </summary>
        private readonly TemporaryDirectory temporary = new();

        /// <summary>
        /// Owns the deck store behind the coordinator.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Owns the provider service.
        /// </summary>
        private readonly ArchidektService service;

        /// <summary>
        /// Creates the fake provider, the service, and the read tools.
        /// </summary>
        internal Fixture()
        {
            Handler = new ArchidektTestHttpHandler();
            Handler.Add(HttpMethod.Post, "api/rest-auth/login/", """{ "access_token": "test-token" }""");
            ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                MinimumRequestInterval = TimeSpan.Zero,
                MaximumRequestsPerWindow = 1_000,
            };
            HttpClient client = new(Handler) { BaseAddress = options.BaseAddress };
            service = new ArchidektService(
                new ArchidektSession(client, ownsHttpClient: true, options),
                options.MaximumRequestsPerOperation);
            store = new SqliteDeckStore(temporary.Path, "test");
            Tools = new ArchidektReadTools(new ArchidektCoordinator(service, store), OperationMode.Remote);
        }

        /// <summary>
        /// Gets the fake provider whose responses the test queues.
        /// </summary>
        internal ArchidektTestHttpHandler Handler { get; }

        /// <summary>
        /// Gets the merged read tools under test.
        /// </summary>
        internal ArchidektReadTools Tools { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
            store.Dispose();
            temporary.Dispose();
        }
    }
}
