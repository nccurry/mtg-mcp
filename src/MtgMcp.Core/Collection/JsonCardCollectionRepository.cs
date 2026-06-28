namespace MtgMcp.Core;

/// <summary>
/// Persists the local card collection as JSON under the mtg-mcp data directory.
/// </summary>
public sealed class JsonCardCollectionRepository : ICardCollectionRepository
{
    /// <summary>
    /// Stores the single collection document with atomic writes.
    /// </summary>
    private readonly JsonFileStore<CardCollectionDocument> store;

    /// <summary>
    /// Creates a collection repository rooted under the mtg-mcp data directory.
    /// </summary>
    public JsonCardCollectionRepository(string dataDirectory)
    {
        store = new JsonFileStore<CardCollectionDocument>(
            Path.Combine(dataDirectory, "collection"),
            "Collection",
            static collection => collection.Id);
    }

    /// <summary>
    /// Saves the local card collection.
    /// </summary>
    public async Task<CardCollectionDocument> SaveAsync(
        CardCollectionDocument collection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        collection.Id = CardCollectionIds.Default;
        return await store
            .SaveAsync(CardCollectionIds.Default, collection, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the local card collection when one has been saved.
    /// </summary>
    public async Task<CardCollectionDocument?> GetAsync(CancellationToken cancellationToken)
    {
        return await store.GetAsync(CardCollectionIds.Default, cancellationToken).ConfigureAwait(false);
    }
}
