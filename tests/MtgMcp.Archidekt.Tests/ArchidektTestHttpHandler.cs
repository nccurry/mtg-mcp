using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Provides deterministic queued Archidekt HTTP responses and captured sanitized requests.
/// </summary>
internal sealed class ArchidektTestHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// Stores queued response builders by exact method and relative URI. A builder gets the request body.
    /// </summary>
    private readonly ConcurrentDictionary<string, Queue<Func<string, HttpResponseMessage>>> responses =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets captured requests in provider start order.
    /// </summary>
    internal List<CapturedArchidektRequest> Requests { get; } = [];

    /// <summary>
    /// Gets whether an owning HTTP client disposed this handler.
    /// </summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>
    /// Adds one JSON response for an exact route.
    /// </summary>
    internal void Add(
        HttpMethod method,
        string path,
        string json = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? retryAfter = null)
    {
        Enqueue(method, path, _ => Response(json, statusCode, retryAfter));
    }

    /// <summary>
    /// Adds the answer Archidekt gives to accepted card adds: 201 with one <c>add</c> object per added card in request
    /// order, each echoing the patch ID that was sent. A card at <paramref name="omitIndex"/> among the added cards is
    /// left out of the answer.
    /// </summary>
    internal void AddAcceptedCards(HttpMethod method, string path, int? omitIndex = null)
    {
        Enqueue(method, path, body => Response(AcceptedCardsBody(body, omitIndex), HttpStatusCode.Created, null));
    }

    /// <summary>
    /// Builds the answer to a card request from the patch IDs it carried.
    /// </summary>
    private static string AcceptedCardsBody(string requestBody, int? omitIndex)
    {
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonArray added = [];
        int number = 0;
        foreach (JsonElement card in document.RootElement.GetProperty("cards").EnumerateArray())
        {
            if (card.GetProperty("action").GetString() != "add")
            {
                continue;
            }

            if (number++ != omitIndex)
            {
                added.Add(new JsonObject
                {
                    ["deckRelationId"] = 9000 + number,
                    ["patchId"] = card.GetProperty("patchId").GetString(),
                    ["categories"] = null,
                    ["quantity"] = 1,
                    ["companion"] = false,
                    ["flippedDefault"] = false,
                    ["modifier"] = "Normal",
                    ["cardId"] = card.GetProperty("cardid").GetInt32(),
                    ["createdAt"] = "2026-01-01T00:00:00.000000-06:00",
                });
            }
        }

        return new JsonObject { ["add"] = added, ["createdCategories"] = null }.ToJsonString();
    }

    /// <summary>
    /// Builds one JSON response.
    /// </summary>
    private static HttpResponseMessage Response(string json, HttpStatusCode statusCode, string? retryAfter)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return response;
    }

    /// <summary>
    /// Queues one response builder for an exact route.
    /// </summary>
    private void Enqueue(HttpMethod method, string path, Func<string, HttpResponseMessage> build)
    {
        Queue<Func<string, HttpResponseMessage>> queue = responses.GetOrAdd(
            Key(method, path),
            static _ => new Queue<Func<string, HttpResponseMessage>>());
        lock (queue)
        {
            queue.Enqueue(build);
        }
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty;
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new CapturedArchidektRequest(
            request.Method,
            path,
            body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));
        if (!responses.TryGetValue(Key(request.Method, path), out Queue<Func<string, HttpResponseMessage>>? queue))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }

        lock (queue)
        {
            return queue.Count > 0
                ? queue.Dequeue()(body)
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
        }
    }

    /// <summary>
    /// Creates an exact route queue key.
    /// </summary>
    private static string Key(HttpMethod method, string path)
    {
        return $"{method.Method} {path.TrimStart('/')}";
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            IsDisposed = true;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Captures one outbound request without retaining any response or exception details.
/// </summary>
internal sealed record CapturedArchidektRequest(
    HttpMethod Method,
    string Path,
    string Body,
    string? AuthorizationScheme,
    string? AuthorizationValue);

/// <summary>
/// Supplies reusable sanitized provider payloads for adapter tests.
/// </summary>
internal static class ArchidektTestPayloads
{
    /// <summary>
    /// Gets a complete private Commander deck with categories, exact printing IDs, and extensions.
    /// </summary>
    internal const string Deck = """
        {
          "id": 42,
          "name": "Rate Safe Weenies",
          "description": "Dummy deck",
          "deckFormat": 3,
          "private": true,
          "parentFolder": 9,
          "customExtension": { "kept": true },
          "categories": [
            { "id": 10, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true, "sortOrder": 0 },
            { "id": 11, "name": "Commander", "includedInDeck": true, "includedInPrice": true, "isPremier": true, "sortOrder": 1 }
          ],
          "cards": [
            {
              "id": 100,
              "quantity": 1,
              "categories": [11],
              "modifier": "Foil",
              "card": {
                "id": 500,
                "uid": "11111111-1111-1111-1111-111111111111",
                "setCode": "2xm",
                "collectorNumber": "190",
                "oracleCard": {
                  "uid": "22222222-2222-2222-2222-222222222222",
                  "name": "Atraxa, Praetors' Voice"
                }
              }
            },
            {
              "deckRelationId": 101,
              "quantity": 3,
              "categories": ["Mainboard"],
              "card": {
                "id": 501,
                "uid": "33333333-3333-3333-3333-333333333333",
                "edition": { "editioncode": "dmu" },
                "collectorNumber": "278",
                "oracleCard": {
                  "uid": "44444444-4444-4444-4444-444444444444",
                  "name": "Island"
                }
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Gets a list response containing the reusable dummy deck.
    /// </summary>
    internal const string DeckList = """
        {
          "decks": [
            {
              "id": 42,
              "name": "Rate Safe Weenies",
              "description": "Dummy deck",
              "deckFormat": 3,
              "private": true,
              "cardCount": 4,
              "updatedAt": "2026-07-04T12:00:00Z",
              "folder": { "id": 9, "name": "Tests", "path": "Root/Tests" }
            }
          ],
          "next": "opaque-next"
        }
        """;

    /// <summary>
    /// Gets a recursive folder tree containing the reusable dummy deck.
    /// </summary>
    internal const string FolderTree = """
        {
          "results": [
            {
              "id": 9,
              "name": "Tests",
              "private": true,
              "path": "Tests",
              "unknown": "kept",
              "decks": [ { "id": 42, "name": "Rate Safe Weenies", "deckFormat": 3, "private": true } ],
              "children": [ { "id": 12, "name": "Child", "parent": 9, "children": [], "decks": [] } ]
            }
          ]
        }
        """;

    /// <summary>
    /// Gets one named snapshot collection row.
    /// </summary>
    internal const string SnapshotList = """
        {
          "results": [
            { "id": 77, "deck": 42, "name": "Before test", "description": "safe", "createdAt": "2026-07-04T12:00:00Z", "extra": 1 }
          ]
        }
        """;

    /// <summary>
    /// Gets one full named snapshot whose nested deck has complete saved state.
    /// </summary>
    internal static string Snapshot => $$"""
        {
          "id": 77,
          "name": "Before test",
          "description": "safe",
          "createdAt": "2026-07-04T12:00:00Z",
          "deck": {{Deck}}
        }
        """;
}
