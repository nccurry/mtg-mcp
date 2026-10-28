namespace MtgMcp.Scryfall;

/// <summary>
/// Names each request snapshot operation once, so recording and the published snapshot filter cannot drift apart.
/// </summary>
internal static class ScryfallOperationNames
{
    /// <summary>
    /// Names a card search snapshot.
    /// </summary>
    internal const string Search = "search";

    /// <summary>
    /// Names a card name autocomplete snapshot.
    /// </summary>
    internal const string Autocomplete = "autocomplete";

    /// <summary>
    /// Names a single card lookup snapshot.
    /// </summary>
    internal const string CardGet = "card-get";

    /// <summary>
    /// Names a card collection lookup snapshot.
    /// </summary>
    internal const string CardCollection = "card-collection";

    /// <summary>
    /// Names a card printings snapshot.
    /// </summary>
    internal const string CardPrints = "card-prints";

    /// <summary>
    /// Names a card rulings snapshot.
    /// </summary>
    internal const string CardRulings = "card-rulings";

    /// <summary>
    /// Names a set list snapshot.
    /// </summary>
    internal const string Sets = "sets";

    /// <summary>
    /// Names a single set lookup snapshot.
    /// </summary>
    internal const string SetGet = "set-get";

    /// <summary>
    /// Names a catalog snapshot.
    /// </summary>
    internal const string Catalog = "catalog";

    /// <summary>
    /// Names a bulk data metadata snapshot.
    /// </summary>
    internal const string BulkMetadata = "bulk-metadata";

    /// <summary>
    /// Lists every operation name a snapshot can carry.
    /// </summary>
    internal static readonly string[] All =
    [
        Search, Autocomplete, CardGet, CardCollection, CardPrints, CardRulings, Sets, SetGet, Catalog, BulkMetadata,
    ];
}
