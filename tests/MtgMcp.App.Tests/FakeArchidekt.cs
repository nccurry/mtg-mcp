using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace MtgMcp.App.Tests;

/// <summary>
/// Stands in for Archidekt in offline workflow tests. It keeps one deck and applies the same writes the server sends,
/// then answers every read the way the real service has been seen to: it returns each entry's other category names in
/// a new order on every read, renumbers entry order numbers and category ranks on every read (the category order stays
/// the same), and keeps no zone, so the zone of a card is derived by the client from its categories. A double-faced
/// card is named by both faces, as Archidekt names it: a read-only query on 2026-10-29,
/// <c>GET /api/cards/v2/?name=Delver%20of%20Secrets</c>, returned 9 printings whose <c>oracleCard.name</c> is
/// "Delver of Secrets // Insectile Aberration" (faces "Delver of Secrets" and "Insectile Aberration", layout
/// transform), and the same query with the full name returned 8 printings with that same name. So the search matches
/// the front face, and the card is always named by both faces.
/// </summary>
internal sealed class FakeArchidekt : HttpMessageHandler
{
    /// <summary>
    /// Holds the deck id every route uses.
    /// </summary>
    private const string DeckId = "42";

    /// <summary>
    /// Holds how many decks and child folders the list routes show.
    /// </summary>
    private const int ListedDecks = 120;

    /// <summary>
    /// Holds how many card search results one page holds.
    /// </summary>
    private const int SearchPageSize = 25;

    /// <summary>
    /// Holds how many named snapshots the snapshot list shows.
    /// </summary>
    private const int ListedSnapshots = 60;

    /// <summary>
    /// Holds the categories in their stored order.
    /// </summary>
    private readonly List<FakeCategory> categories = [];

    /// <summary>
    /// Holds the entries in the order they were added.
    /// </summary>
    private readonly List<FakeEntry> entries = [];

    /// <summary>
    /// Holds the cards the card search and the card-id lookup know, by Archidekt card id.
    /// </summary>
    private readonly Dictionary<int, FakeCard> catalog = [];

    /// <summary>
    /// Counts the deck reads, which seeds the reordering of the next read.
    /// </summary>
    private int reads;

    /// <summary>
    /// Holds the next id for a new category.
    /// </summary>
    private int nextCategoryId = 100;

    /// <summary>
    /// Holds the next id for a new entry.
    /// </summary>
    private int nextRelationId = 1000;

    /// <summary>
    /// Gets or sets the name the fake deck reports.
    /// </summary>
    internal string DeckName { get; set; } = "Fake deck";

    /// <summary>
    /// Gets or sets the description the fake deck reports.
    /// </summary>
    internal string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets the methods and paths of every request, in order.
    /// </summary>
    internal List<string> Requests { get; } = [];

    /// <summary>
    /// Gets how many requests changed the deck.
    /// </summary>
    internal int Mutations { get; private set; }

    /// <summary>
    /// Adds a category.
    /// </summary>
    internal void AddCategory(string categoryName, bool includedInDeck = true)
    {
        categories.Add(new FakeCategory(nextCategoryId++, categoryName, includedInDeck));
    }

    /// <summary>
    /// Makes a card known to the fake, so it can be searched for and added.
    /// </summary>
    internal FakeCard AddCardToCatalog(
        int cardId,
        string cardName,
        string setCode = "dmu",
        string? collectorNumber = null,
        Guid? oracleId = null,
        Guid? printingId = null)
    {
        FakeCard card = new(
            cardId,
            cardName,
            printingId ?? new Guid(cardId, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1),
            oracleId ?? new Guid(cardId, 2, 2, 0, 0, 0, 0, 0, 0, 0, 2),
            setCode,
            collectorNumber ?? cardId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        catalog[cardId] = card;
        return card;
    }

    /// <summary>
    /// Adds a card to the deck in the given categories; the first category is the primary one.
    /// </summary>
    internal void AddEntry(FakeCard card, int quantity, params string[] categoryNames)
    {
        catalog[card.Id] = card;
        entries.Add(new FakeEntry(nextRelationId++, card.Id, quantity, [.. categoryNames]));
    }

    /// <summary>
    /// Replaces the categories of the one entry for a card, as an edit made on the Archidekt site would; the first
    /// category is the primary one. It is not counted as a mutation, since no client request made it.
    /// </summary>
    internal void SetCategories(string cardName, params string[] categoryNames)
    {
        entries.Single(value => catalog[value.CardId].Name == cardName).Categories = [.. categoryNames];
    }

    /// <summary>
    /// Renames a category, with every entry that uses it, as an edit made on the Archidekt site would. The category
    /// keeps its id. It is not counted as a mutation, since no client request made it.
    /// </summary>
    internal void RenameCategory(string categoryName, string newName)
    {
        categories.Single(value => value.Name == categoryName).Name = newName;
        foreach (FakeEntry entry in entries)
        {
            entry.Categories = [.. entry.Categories.Select(value => value == categoryName ? newName : value)];
        }
    }

    /// <summary>
    /// Gets the quantity of a card by name across the deck, or zero when it is not in the deck.
    /// </summary>
    internal int QuantityOf(string cardName)
    {
        return entries.Where(value => catalog[value.CardId].Name == cardName).Sum(value => value.Quantity);
    }

    /// <summary>
    /// Gets the Archidekt card id of the one entry for a card by name, as the deck read shows it.
    /// </summary>
    internal string CardIdOf(string cardName)
    {
        return entries.Single(value => catalog[value.CardId].Name == cardName).CardId
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the category names of the one entry for a card, primary first, as stored.
    /// </summary>
    internal IReadOnlyList<string> CategoriesOf(string cardName)
    {
        return entries.Single(value => catalog[value.CardId].Name == cardName).Categories;
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.PathAndQuery.TrimStart('/');
        string body = request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        Requests.Add($"{request.Method.Method} {path}");
        if (request.Method == HttpMethod.Post && path == "api/rest-auth/login/")
        {
            return Task.FromResult(Json("""{ "token": "fake-token" }"""));
        }

        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(Read(path));
        }

        Mutations++;
        Write(request.Method, path, body);
        return Task.FromResult(Json("{}"));
    }

    /// <summary>
    /// Answers one read route.
    /// </summary>
    private HttpResponseMessage Read(string path)
    {
        if (path == $"api/decks/{DeckId}/")
        {
            return Json(SerializeDeck().ToJsonString());
        }

        if (path.StartsWith("api/decks/v3/?ownerUsername=", StringComparison.Ordinal))
        {
            return Json(SerializeDeckList().ToJsonString());
        }

        if (path == "api/decks/folderTree/")
        {
            return Json(new JsonObject { ["results"] = new JsonArray(SerializeFolder()) }.ToJsonString());
        }

        if (path.StartsWith("api/decks/folders/", StringComparison.Ordinal))
        {
            return Json(new JsonObject { ["folder"] = SerializeFolder() }.ToJsonString());
        }

        if (path == $"api/decks/{DeckId}/snapshots/")
        {
            return Json(SerializeSnapshotList().ToJsonString());
        }

        if (path.StartsWith("api/decks/snapshots/", StringComparison.Ordinal))
        {
            return Json(SerializeSnapshot().ToJsonString());
        }

        if (path.StartsWith("api/cards/v2/?name=", StringComparison.Ordinal))
        {
            return SearchCards(path);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Answers one card search the way the real service does: results of 25 per page in <c>results</c>, the number of
    /// matches in <c>count</c>, and an absolute <c>next</c> link while more pages follow. A result names its set in
    /// <c>edition.editioncode</c> and has no top-level set code. A recorded read-only query on 2026-10-31,
    /// <c>GET /api/cards/v2/?name=Assassin%27s%20Trophy&amp;pageSize=25</c>, returned count 18 and next null; the same
    /// service returns count 141 and a next link for Sol Ring.
    /// </summary>
    private HttpResponseMessage SearchCards(string path)
    {
        // Matches the full name or the front face, as the recorded Archidekt search does for a double-faced card, and
        // ignores case.
        Dictionary<string, string> query = path["api/cards/v2/?".Length..]
            .Split('&')
            .Select(value => value.Split('=', 2))
            .ToDictionary(value => value[0], value => Uri.UnescapeDataString(value[1]), StringComparer.Ordinal);
        string wanted = query["name"];
        int page = query.TryGetValue("page", out string? pageText) ? int.Parse(pageText, System.Globalization.CultureInfo.InvariantCulture) : 1;
        List<FakeCard> matches = [.. catalog.Values.Where(value =>
            string.Equals(value.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
            value.Name.StartsWith(wanted + " // ", StringComparison.OrdinalIgnoreCase))];
        JsonArray results = [];
        foreach (FakeCard card in matches.Skip((page - 1) * SearchPageSize).Take(SearchPageSize))
        {
            results.Add(new JsonObject
            {
                ["id"] = card.Id,
                ["uid"] = card.PrintingId.ToString("D"),
                ["collectorNumber"] = card.CollectorNumber,
                ["edition"] = new JsonObject { ["editioncode"] = card.SetCode },
                ["oracleCard"] = new JsonObject { ["name"] = card.Name, ["uid"] = card.OracleId.ToString("D") },
            });
        }

        JsonNode next = page * SearchPageSize < matches.Count
            ? JsonValue.Create($"https://archidekt.com/api/cards/v2/?name={Uri.EscapeDataString(wanted)}&page={page + 1}&pageSize=25")!
            : null!;
        return Json(new JsonObject
        {
            ["count"] = matches.Count,
            ["next"] = next,
            ["previous"] = null,
            ["results"] = results,
        }.ToJsonString());
    }

    /// <summary>
    /// Applies one write route to the stored deck.
    /// </summary>
    private void Write(HttpMethod method, string path, string body)
    {
        if (method == HttpMethod.Patch && path == $"api/decks/{DeckId}/")
        {
            JsonNode metadata = JsonNode.Parse(body)!;
            DeckName = metadata["name"]?.GetValue<string>() ?? DeckName;
        }
        else if (method == HttpMethod.Post && path == "api/decks/createCategory/")
        {
            JsonNode created = JsonNode.Parse(body)!;
            categories.Add(new FakeCategory(
                nextCategoryId++,
                created["name"]!.GetValue<string>(),
                created["includedInDeck"]?.GetValue<bool>() ?? true));
        }
        else if (path.StartsWith("api/decks/category/", StringComparison.Ordinal))
        {
            int id = int.Parse(
                path["api/decks/category/".Length..].TrimEnd('/'),
                System.Globalization.CultureInfo.InvariantCulture);
            FakeCategory existing = categories.Single(value => value.Id == id);
            if (method == HttpMethod.Delete)
            {
                categories.Remove(existing);
            }
            else
            {
                JsonNode updated = JsonNode.Parse(body)!;
                existing.Name = updated["name"]!.GetValue<string>();
                existing.IncludedInDeck = updated["includedInDeck"]?.GetValue<bool>() ?? true;
            }
        }
        else if (method == HttpMethod.Patch && path == $"api/decks/{DeckId}/modifyCards/v2/")
        {
            foreach (JsonNode? card in JsonNode.Parse(body)!["cards"]!.AsArray())
            {
                ModifyCard(card!);
            }
        }
        else
        {
            throw new InvalidOperationException($"The fake has no route for {method.Method} {path}.");
        }
    }

    /// <summary>
    /// Applies one add, modify, or remove of a card relation.
    /// </summary>
    private void ModifyCard(JsonNode card)
    {
        string action = card["action"]!.GetValue<string>();
        List<string> names = [.. card["categories"]!.AsArray().Select(value => value!.GetValue<string>())];
        int quantity = card["modifications"]!["quantity"]!.GetValue<int>();
        if (action == "add")
        {
            entries.Add(new FakeEntry(nextRelationId++, card["cardid"]!.GetValue<int>(), quantity, names));
            return;
        }

        FakeEntry entry = entries.Single(value => value.RelationId == card["deckRelationId"]!.GetValue<int>());
        if (action == "remove")
        {
            entries.Remove(entry);
            return;
        }

        entry.Quantity = quantity;
        entry.Categories = names;
    }

    /// <summary>
    /// Writes the deck the way one read of the real service shows it. A saved snapshot is a stored document, so it
    /// passes a fixed read number and comes back the same on every read.
    /// </summary>
    private JsonObject SerializeDeck(int? fixedRead = null)
    {
        int read = fixedRead ?? ++reads;
        Random random = new(read);
        JsonArray categoryArray = [];
        for (int index = 0; index < categories.Count; index++)
        {
            FakeCategory category = categories[index];
            categoryArray.Add(new JsonObject
            {
                ["id"] = category.Id,
                ["name"] = category.Name,
                ["includedInDeck"] = category.IncludedInDeck,
                ["includedInPrice"] = true,
                ["sortOrder"] = (index * 10) + read,
            });
        }

        int[] orders = [.. Enumerable.Range(0, entries.Count).OrderBy(_ => random.Next())];
        JsonArray cardArray = [];
        for (int index = 0; index < entries.Count; index++)
        {
            FakeEntry entry = entries[index];
            FakeCard card = catalog[entry.CardId];
            JsonArray shown = [entry.Categories[0]];
            foreach (string other in entry.Categories.Skip(1).OrderBy(_ => random.Next()))
            {
                shown.Add(other);
            }

            cardArray.Add(new JsonObject
            {
                ["deckRelationId"] = entry.RelationId,
                ["quantity"] = entry.Quantity,
                ["order"] = orders[index],
                ["categories"] = shown,
                ["card"] = new JsonObject
                {
                    ["id"] = card.Id,
                    ["uid"] = card.PrintingId.ToString("D"),
                    ["edition"] = new JsonObject { ["editioncode"] = card.SetCode },
                    ["collectorNumber"] = card.CollectorNumber,
                    ["oracleCard"] = new JsonObject
                    {
                        ["uid"] = card.OracleId.ToString("D"),
                        ["name"] = card.Name,
                    },
                },
            });
        }

        JsonArray shuffledCards = [];
        foreach (JsonNode item in cardArray.Select(value => value!).OrderBy(_ => random.Next()))
        {
            shuffledCards.Add(item.DeepClone());
        }

        return new JsonObject
        {
            ["id"] = int.Parse(DeckId, System.Globalization.CultureInfo.InvariantCulture),
            ["name"] = DeckName,
            ["description"] = Description,
            ["deckFormat"] = 3,
            ["private"] = true,
            ["categories"] = categoryArray,
            ["cards"] = shuffledCards,
        };
    }

    /// <summary>
    /// Writes a list of decks the owner has, one of them the fake deck, to show how a long list reads.
    /// </summary>
    private JsonObject SerializeDeckList()
    {
        JsonArray decks = [];
        for (int index = 0; index < ListedDecks; index++)
        {
            decks.Add(new JsonObject
            {
                ["id"] = index == 0 ? 42 : 5000 + index,
                ["name"] = index == 0 ? DeckName : $"Listed deck {index:000}",
                ["description"] = "A deck in the owner's list.",
                ["deckFormat"] = 3,
                ["private"] = true,
                ["cardCount"] = 100,
                ["updatedAt"] = "2026-07-04T12:00:00Z",
                ["folder"] = new JsonObject { ["id"] = 9, ["name"] = "Tests", ["path"] = "Root/Tests" },
            });
        }

        return new JsonObject { ["decks"] = decks };
    }

    /// <summary>
    /// Writes one folder that holds the fake deck and many child folders.
    /// </summary>
    private JsonObject SerializeFolder()
    {
        JsonArray children = [];
        JsonArray decks = [new JsonObject { ["id"] = 42, ["name"] = DeckName, ["deckFormat"] = 3, ["private"] = true }];
        for (int index = 0; index < ListedDecks; index++)
        {
            children.Add(new JsonObject
            {
                ["id"] = 100 + index,
                ["name"] = $"Child folder {index:000}",
                ["parent"] = 9,
                ["children"] = new JsonArray(),
                ["decks"] = new JsonArray(),
            });
            decks.Add(new JsonObject
            {
                ["id"] = 5000 + index,
                ["name"] = $"Foldered deck {index:000}",
                ["deckFormat"] = 3,
                ["private"] = true,
            });
        }

        return new JsonObject
        {
            ["id"] = 9,
            ["name"] = "Tests",
            ["private"] = true,
            ["path"] = "Tests",
            ["decks"] = decks,
            ["children"] = children,
        };
    }

    /// <summary>
    /// Writes the named snapshots of the fake deck.
    /// </summary>
    private static JsonObject SerializeSnapshotList()
    {
        JsonArray rows = [];
        for (int index = 0; index < ListedSnapshots; index++)
        {
            rows.Add(new JsonObject
            {
                ["id"] = 77 + index,
                ["deck"] = 42,
                ["name"] = $"Snapshot {index:000}",
                ["description"] = "A named snapshot.",
                ["createdAt"] = "2026-07-04T12:00:00Z",
            });
        }

        return new JsonObject { ["results"] = rows };
    }

    /// <summary>
    /// Writes one named snapshot whose saved deck holds one more of every card than the deck now does.
    /// </summary>
    private JsonObject SerializeSnapshot()
    {
        JsonObject deck = SerializeDeck(fixedRead: 1);
        foreach (JsonNode? card in deck["cards"]!.AsArray())
        {
            card!["quantity"] = card["quantity"]!.GetValue<int>() + 1;
        }

        return new JsonObject
        {
            ["id"] = 77,
            ["name"] = "Snapshot 000",
            ["description"] = "A named snapshot.",
            ["createdAt"] = "2026-07-04T12:00:00Z",
            ["deck"] = deck,
        };
    }

    /// <summary>
    /// Builds one JSON response.
    /// </summary>
    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Describes one card Archidekt knows.
    /// </summary>
    internal sealed record FakeCard(
        int Id,
        string Name,
        Guid PrintingId,
        Guid OracleId,
        string SetCode,
        string CollectorNumber);

    /// <summary>
    /// Stores one category of the fake deck.
    /// </summary>
    private sealed class FakeCategory(int id, string name, bool includedInDeck)
    {
        /// <summary>Gets the category id.</summary>
        internal int Id { get; } = id;

        /// <summary>Gets or sets the category name.</summary>
        internal string Name { get; set; } = name;

        /// <summary>Gets or sets whether cards in the category count in the deck.</summary>
        internal bool IncludedInDeck { get; set; } = includedInDeck;
    }

    /// <summary>
    /// Stores one card relation of the fake deck.
    /// </summary>
    private sealed class FakeEntry(int relationId, int cardId, int quantity, List<string> categories)
    {
        /// <summary>Gets the relation id.</summary>
        internal int RelationId { get; } = relationId;

        /// <summary>Gets the Archidekt card id.</summary>
        internal int CardId { get; } = cardId;

        /// <summary>Gets or sets the quantity.</summary>
        internal int Quantity { get; set; } = quantity;

        /// <summary>Gets or sets the category names, the primary first.</summary>
        internal List<string> Categories { get; set; } = categories;
    }
}
