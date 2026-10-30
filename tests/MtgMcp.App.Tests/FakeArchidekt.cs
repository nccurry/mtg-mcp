using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace MtgMcp.App.Tests;

/// <summary>
/// Stands in for Archidekt in offline workflow tests. It keeps one deck and applies the same writes the server sends,
/// then answers every read the way the real service has been seen to: it returns each entry's other category names in
/// a new order on every read, renumbers entry order numbers and category ranks on every read (the category order stays
/// the same), and keeps no zone, so the zone of a card is derived by the client from its categories.
/// </summary>
internal sealed class FakeArchidekt : HttpMessageHandler
{
    /// <summary>
    /// Holds the deck id every route uses.
    /// </summary>
    private const string DeckId = "42";

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
    /// Holds the deck name.
    /// </summary>
    private string name = "Fake deck";

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
    internal FakeCard AddCardToCatalog(int cardId, string cardName, string setCode = "dmu", string? collectorNumber = null)
    {
        FakeCard card = new(
            cardId,
            cardName,
            new Guid(cardId, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1),
            new Guid(cardId, 2, 2, 0, 0, 0, 0, 0, 0, 0, 2),
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
    /// Gets the quantity of a card by name across the deck, or zero when it is not in the deck.
    /// </summary>
    internal int QuantityOf(string cardName)
    {
        return entries.Where(value => catalog[value.CardId].Name == cardName).Sum(value => value.Quantity);
    }

    /// <summary>
    /// Gets the category names of the one entry for a card, primary first, as stored.
    /// </summary>
    internal IReadOnlyList<string> CategoriesOf(string cardName)
    {
        return entries.Single(value => catalog[value.CardId].Name == cardName).Categories;
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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

        if (path.StartsWith("api/cards/v2/?name=", StringComparison.Ordinal))
        {
            string wanted = Uri.UnescapeDataString(path["api/cards/v2/?name=".Length..].Split('&')[0]);
            JsonArray results = [];
            foreach (FakeCard card in catalog.Values.Where(value => value.Name == wanted))
            {
                results.Add(new JsonObject
                {
                    ["id"] = card.Id,
                    ["uid"] = card.PrintingId.ToString("D"),
                    ["setCode"] = card.SetCode,
                    ["collectorNumber"] = card.CollectorNumber,
                    ["oracleCard"] = new JsonObject { ["name"] = card.Name },
                });
            }

            return Json(new JsonObject { ["results"] = results }.ToJsonString());
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Applies one write route to the stored deck.
    /// </summary>
    private void Write(HttpMethod method, string path, string body)
    {
        if (method == HttpMethod.Patch && path == $"api/decks/{DeckId}/")
        {
            JsonNode metadata = JsonNode.Parse(body)!;
            name = metadata["name"]?.GetValue<string>() ?? name;
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
            int id = int.Parse(path["api/decks/category/".Length..].TrimEnd('/'), System.Globalization.CultureInfo.InvariantCulture);
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
    /// Writes the deck the way one read of the real service shows it.
    /// </summary>
    private JsonObject SerializeDeck()
    {
        int read = ++reads;
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
            ["name"] = name,
            ["description"] = string.Empty,
            ["deckFormat"] = 3,
            ["private"] = true,
            ["categories"] = categoryArray,
            ["cards"] = shuffledCards,
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
    internal sealed record FakeCard(int Id, string Name, Guid PrintingId, Guid OracleId, string SetCode, string CollectorNumber);

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
