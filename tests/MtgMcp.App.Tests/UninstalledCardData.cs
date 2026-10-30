using MtgMcp.App.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Gives a test an entry resolver that reads a Scryfall service with no card data installed, so entries added by name
/// are kept as given and the write reports that resolution was skipped.
/// </summary>
internal sealed class UninstalledCardData : IDisposable
{
    /// <summary>
    /// Holds the folder behind the Scryfall service.
    /// </summary>
    private readonly TemporaryDirectory directory = new();

    /// <summary>
    /// Holds the Scryfall service the resolver reads.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates the service and the resolver over it.
    /// </summary>
    internal UninstalledCardData()
    {
        scryfall = DeckCategorizationSourceFixture.CreateService(
            directory.Path,
            DeckCategorizationSourceFixture.CreateHandler());
        Resolver = new DeckEntryResolver(scryfall);
    }

    /// <summary>
    /// Gets the resolver to pass to the deck write tools.
    /// </summary>
    internal DeckEntryResolver Resolver { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        scryfall.Dispose();
        directory.Dispose();
    }
}
