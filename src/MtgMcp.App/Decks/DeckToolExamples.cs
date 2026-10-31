namespace MtgMcp.App.Decks;

/// <summary>
/// Holds the example arguments objects published in the descriptions of the batch edit and category rule preview
/// tools. Tests read them back from the descriptions and run them, so the examples cannot drift from the tools.
/// </summary>
internal static class DeckToolExamples
{
    /// <summary>
    /// Opens each example in a description: <c>Example (name): {json}</c>. Tests find examples by this prefix.
    /// </summary>
    internal const string ExamplePrefix = " Example (";

    /// <summary>
    /// Stands in for a real local deck ID in the examples.
    /// </summary>
    internal const string PlaceholderDeckId = "0193e97a-ef95-7700-8000-000000000001";

    /// <summary>
    /// Stands in for the ID of an entry that already exists in the deck.
    /// </summary>
    internal const string PlaceholderEntryId = "0193e97a-ef95-7700-8000-000000000011";

    /// <summary>
    /// Stands in for the ID of a category that already exists in the deck.
    /// </summary>
    internal const string PlaceholderCategoryId = "0193e97a-ef95-7700-8000-000000000021";

    /// <summary>
    /// Stands in for the revision the deck is at.
    /// </summary>
    internal const string PlaceholderRevision = "7";

    /// <summary>
    /// Names the ID of the new entry that the full batch example adds, then files in the new category.
    /// </summary>
    private const string NewEntryId = "0193e97a-ef95-7700-8000-0000000000e1";

    /// <summary>
    /// Names the ID of the new category that the full batch example adds.
    /// </summary>
    private const string NewCategoryId = "0193e97a-ef95-7700-8000-0000000000c1";

    /// <summary>
    /// Opens every example with the deck and revision it applies to.
    /// </summary>
    private const string DeckAndRevision =
        "{\"deckId\":\"" + PlaceholderDeckId + "\",\"expectedRevision\":" + PlaceholderRevision;

    /// <summary>
    /// Adds one card, the most common single change.
    /// </summary>
    internal const string ApplyChangesMinimal =
        DeckAndRevision +
        ",\"changes\":[{\"kind\":\"add-entry\",\"entryDraft\":{\"quantity\":1,\"cardName\":\"Sol Ring\"}}]}";

    /// <summary>
    /// Puts an existing entry into an existing category as its primary category.
    /// </summary>
    internal const string ApplyChangesCategorize =
        DeckAndRevision +
        ",\"changes\":[{\"kind\":\"assign-category\",\"entryId\":\"" + PlaceholderEntryId +
        "\",\"categoryId\":\"" + PlaceholderCategoryId + "\",\"isPrimary\":true}]}";

    /// <summary>
    /// Renames the deck, adds a category and a card, files the card in the category, and removes another card.
    /// </summary>
    internal const string ApplyChangesFull =
        DeckAndRevision + ",\"changes\":[" +
        "{\"kind\":\"update-metadata\",\"name\":\"Golgari Ramp\",\"description\":null,\"format\":\"commander\"}," +
        "{\"kind\":\"add-category\",\"categoryDraft\":{\"name\":\"Draw\",\"color\":\"#2255aa\"," +
        "\"categoryId\":\"" + NewCategoryId + "\"}}," +
        "{\"kind\":\"add-entry\",\"entryDraft\":{\"quantity\":1,\"cardName\":\"Rhystic Study\"," +
        "\"zone\":\"main\",\"entryId\":\"" + NewEntryId + "\"}}," +
        "{\"kind\":\"assign-category\",\"entryId\":\"" + NewEntryId + "\"," +
        "\"categoryId\":\"" + NewCategoryId + "\",\"isPrimary\":true}," +
        "{\"kind\":\"remove-entry\",\"entryId\":\"" + PlaceholderEntryId + "\"}]}";

    /// <summary>
    /// Evaluates one rule that files cards with one oracle tag into one category.
    /// </summary>
    internal const string RulesPreviewMinimal =
        "{\"deckId\":\"" + PlaceholderDeckId + "\",\"source\":{\"kind\":\"inline\",\"ruleSet\":" +
        "{\"assignmentMode\":\"add-only\",\"rules\":[{\"categoryId\":\"" + PlaceholderCategoryId + "\"," +
        "\"allOf\":[{\"tagType\":\"oracle\",\"exactSlug\":\"ramp\"}],\"anyOf\":[],\"noneOf\":[]}]}}}";

    /// <summary>
    /// Evaluates a synchronizing rule with descendant tags, a minimum weight, and an excluded tag, at a stated
    /// revision and with cached card data only.
    /// </summary>
    internal const string RulesPreviewFull =
        DeckAndRevision + ",\"freshnessPolicy\":\"cache-only\",\"source\":{\"kind\":\"inline\",\"ruleSet\":" +
        "{\"assignmentMode\":\"synchronize-listed-categories\",\"rules\":[" +
        "{\"categoryId\":\"" + PlaceholderCategoryId + "\",\"primaryPriority\":1," +
        "\"allOf\":[{\"tagType\":\"oracle\",\"exactSlug\":\"ramp\",\"includeDescendants\":true," +
        "\"minimumWeight\":\"weak\"}],\"anyOf\":[],\"noneOf\":[" +
        "{\"tagType\":\"oracle\",\"exactSlug\":\"removal\"}]}]}}}";
}
