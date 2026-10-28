using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.App.Playgroup;
using MtgMcp.App.Scryfall;
using MtgMcp.App.Spellbook;
using MtgMcp.App.Statistics;
using MtgMcp.Archidekt;
using MtgMcp.Decks;
using MtgMcp.Playgroup;
using MtgMcp.Scryfall;
using MtgMcp.Spellbook;
using MtgMcp.Statistics;

namespace MtgMcp.App.Tests;

/// <summary>
/// Registers every toolset through the same manifests the server uses, so tests inspect the published tools.
/// </summary>
internal sealed class RegisteredToolSet : IDisposable
{
    /// <summary>
    /// Holds the working folder and the services the registered tools call into.
    /// </summary>
    private readonly List<IDisposable> owned = [];

    /// <summary>
    /// Creates the full tool surface for one operation mode.
    /// </summary>
    internal RegisteredToolSet(OperationMode mode)
    {
        TemporaryDirectory temporary = Own(new TemporaryDirectory());
        SqliteDeckStore deckStore = Own(new SqliteDeckStore(temporary.Path, "test"));
        ScryfallService scryfall = Own(new ScryfallService(
            temporary.Path,
            allowLocalWrites: true,
            "test",
            ToolCallPolicy.SerializerOptions));
        ArchidektService archidekt = Own(new ArchidektService(
            ArchidektOptions.CreateDefault("user", "secret"),
            "test"));
        PlaygroupService playgroup = Own(new PlaygroupService(PlaygroupOptions.CreateDefault("key"), "test"));
        SpellbookService spellbook = Own(new SpellbookService(SpellbookOptions.CreateDefault(temporary.Path), "test"));
        ServiceCollection services = new();
        IMcpServerBuilder builder = services.AddMcpServer();
        DeckToolsetManifest.Register(builder, deckStore, scryfall, mode);
        ScryfallToolsetManifest.Register(builder, scryfall, mode);
        StatisticsToolsetManifest.Register(builder, deckStore, new ExactStatisticsCalculator("test"));
        ArchidektToolsetManifest.Register(builder, archidekt, deckStore, mode);
        PlaygroupToolsetManifest.Register(builder, playgroup, mode);
        SpellbookToolsetManifest.Register(builder, spellbook, deckStore);
        ServiceProvider provider = services.BuildServiceProvider();
        owned.Add(provider);
        Tools = [.. provider.GetServices<McpServerTool>()];
    }

    /// <summary>
    /// Gets every tool the server would publish, in registration order.
    /// </summary>
    internal IReadOnlyList<McpServerTool> Tools { get; }

    /// <summary>
    /// Releases the services and folder behind the registered tools.
    /// </summary>
    public void Dispose()
    {
        for (int index = owned.Count - 1; index >= 0; index--)
        {
            owned[index].Dispose();
        }
    }

    /// <summary>
    /// Records one disposable so it is released with the set.
    /// </summary>
    private T Own<T>(T value)
        where T : IDisposable
    {
        owned.Add(value);
        return value;
    }
}
