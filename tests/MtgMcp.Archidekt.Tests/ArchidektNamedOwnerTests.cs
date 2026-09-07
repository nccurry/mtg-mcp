using System.Net;
using System.Reflection;
using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Proves each named Archidekt route and workflow owner directly performs its assigned behavior.
/// </summary>
public sealed class ArchidektNamedOwnerTests
{
    /// <summary>
    /// Verifies the deck route owner refreshes once and replays an authenticated read with the new token.
    /// </summary>
    [Fact]
    public async Task DeckTransport_RefreshesAuthenticationAndReplaysRead()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler, "first-token");
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", "{}", HttpStatusCode.Unauthorized);
        AddLogin(handler, "second-token");
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        using ArchidektSession session = CreateSession(handler);
        ArchidektDeckTransport decks = new(session);

        RemoteDeckPage page = await decks.ListAsync(
            cursor: null,
            pageSize: 50,
            new ArchidektOperationBudget(10),
            TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("first-token", handler.Requests[1].AuthorizationValue);
        Assert.Equal("second-token", handler.Requests[3].AuthorizationValue);
    }

    /// <summary>
    /// Verifies the folder route owner fetches the authenticated folder tree from its exact provider route.
    /// </summary>
    [Fact]
    public async Task FolderTransport_ReadsAuthenticatedTree()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/folderTree/", ArchidektTestPayloads.FolderTree);
        using ArchidektSession session = CreateSession(handler);
        ArchidektFolderTransport folders = new(session);

        RemoteFolderTree tree = await folders.ListAsync(
            new ArchidektOperationBudget(10),
            TestContext.Current.CancellationToken);

        Assert.Equal("9", Assert.Single(tree.Items, folder => folder.FolderId == "9").FolderId);
        Assert.Equal(
            ["api/rest-auth/login/", "api/decks/folderTree/"],
            handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Verifies the snapshot route owner fetches the named snapshot collection from its exact provider route.
    /// </summary>
    [Fact]
    public async Task SnapshotTransport_ReadsNamedSnapshotCollection()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/snapshots/", ArchidektTestPayloads.SnapshotList);
        using ArchidektSession session = CreateSession(handler);
        ArchidektSnapshotTransport snapshots = new(session);

        RemoteNamedSnapshotPage page = await snapshots.ListAsync(
            "42",
            new ArchidektOperationBudget(10),
            TestContext.Current.CancellationToken);

        Assert.Equal("77", Assert.Single(page.Items).SnapshotId);
        Assert.Equal(
            ["api/rest-auth/login/", "api/decks/42/snapshots/"],
            handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Verifies the deck workflow owner returns a public deck without an authentication request.
    /// </summary>
    [Fact]
    public async Task DeckOperations_ReadsPublicDeck()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektSession session = CreateSession(handler, username: null, password: null);
        ArchidektDeckOperations decks = new(new ArchidektDeckTransport(session), 150);

        RemoteDeckSnapshot deck = Success(await decks.GetAsync(
            "42",
            TestContext.Current.CancellationToken));

        Assert.Equal("42", deck.RemoteId);
        CapturedArchidektRequest request = Assert.Single(handler.Requests);
        Assert.Equal("api/decks/42/", request.Path);
        Assert.Null(request.AuthorizationScheme);
    }

    /// <summary>
    /// Verifies the folder workflow owner joins the authenticated tree to the caller's deck listing.
    /// </summary>
    [Fact]
    public async Task FolderOperations_EnrichesTreeWithOwnedDecks()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/folderTree/", ArchidektTestPayloads.FolderTree);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        using ArchidektSession session = CreateSession(handler);
        ArchidektFolderOperations folders = new(
            new ArchidektFolderTransport(session),
            new ArchidektDeckTransport(session),
            150);

        RemoteFolderTree tree = Success(await folders.ListAsync(TestContext.Current.CancellationToken));

        RemoteFolderRecord folder = Assert.Single(tree.Items, folder => folder.FolderId == "9");
        Assert.Equal("42", Assert.Single(folder.Decks).RemoteId);
        Assert.Equal(
            ["api/rest-auth/login/", "api/decks/folderTree/", "api/decks/v3/?ownerUsername=user"],
            handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Verifies the snapshot workflow owner returns the complete saved deck state for the requested deck.
    /// </summary>
    [Fact]
    public async Task SnapshotOperations_ReadsCompleteSavedDeck()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", ArchidektTestPayloads.Snapshot);
        using ArchidektSession session = CreateSession(handler);
        ArchidektDeckTransport deckTransport = new(session);
        ArchidektSnapshotOperations snapshots = new(
            new ArchidektSnapshotTransport(session),
            deckTransport,
            new ArchidektDeckOperations(deckTransport, 150),
            150);

        RemoteNamedSnapshot snapshot = Success(await snapshots.GetAsync(
            "42",
            "77",
            TestContext.Current.CancellationToken));

        Assert.Equal("77", snapshot.Summary.SnapshotId);
        Assert.Equal("42", snapshot.Deck.RemoteId);
        Assert.Equal(
            ["api/rest-auth/login/", "api/decks/snapshots/77/"],
            handler.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// Verifies the retired shared context types are absent from the adapter assembly.
    /// </summary>
    [Fact]
    public void RetiredContextTypes_AreAbsent()
    {
        Assembly assembly = typeof(ArchidektService).Assembly;

        Assert.Null(assembly.GetType("MtgMcp.Archidekt.ArchidektOperationContext"));
        Assert.Null(assembly.GetType("MtgMcp.Archidekt.ArchidektTransportContext"));
    }

    /// <summary>
    /// Verifies the shared session disposes only HTTP clients it owns.
    /// </summary>
    [Fact]
    public async Task Session_DisposesOnlyOwnedHttpClients()
    {
        ArchidektTestHttpHandler ownedHandler = new();
        HttpClient ownedClient = CreateHttpClient(ownedHandler);
        using (ArchidektSession session = new(ownedClient, ownsHttpClient: true, CreateOptions()))
        {
        }

        Assert.True(ownedHandler.IsDisposed);

        ArchidektTestHttpHandler borrowedHandler = new();
        using HttpClient borrowedClient = CreateHttpClient(borrowedHandler);
        using (ArchidektSession session = new(borrowedClient, ownsHttpClient: false, CreateOptions()))
        {
        }

        Assert.False(borrowedHandler.IsDisposed);
        borrowedHandler.Add(HttpMethod.Get, "still-available", "{}");
        using HttpResponseMessage response = await borrowedClient.GetAsync(
            "still-available",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Adds one successful login response with a caller-selected bearer token.
    /// </summary>
    private static void AddLogin(ArchidektTestHttpHandler handler, string token = "test-token")
    {
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", $$"""{ "access_token": "{{token}}" }""");
    }

    /// <summary>
    /// Creates a shared provider session over deterministic fake HTTP.
    /// </summary>
    private static ArchidektSession CreateSession(
        ArchidektTestHttpHandler handler,
        string? username = "user",
        string? password = "secret")
    {
        ArchidektOptions options = CreateOptions(username, password);
        return new ArchidektSession(CreateHttpClient(handler), ownsHttpClient: true, options);
    }

    /// <summary>
    /// Creates a client that sends requests to the deterministic fake provider.
    /// </summary>
    private static HttpClient CreateHttpClient(ArchidektTestHttpHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://archidekt.test/"),
        };
    }

    /// <summary>
    /// Creates zero-delay provider settings with production request ceilings otherwise unchanged.
    /// </summary>
    private static ArchidektOptions CreateOptions(string? username = "user", string? password = "secret")
    {
        return ArchidektOptions.CreateDefault(username, password) with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
    }

    /// <summary>
    /// Extracts one successful result while retaining useful union-case failures.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
