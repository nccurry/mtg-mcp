using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Registers structured decklist corpus source adapters.
/// </summary>
public static class DecklistServiceCollectionExtensions
{
    /// <summary>
    /// Adds API-backed decklist corpus sources.
    /// </summary>
    public static IServiceCollection AddDecklistCorpusSources(this IServiceCollection services)
    {
        services.AddHttpClient<TopDeckCorpusSignalProvider>();
        services.AddHttpClient<SpicerackCorpusSignalProvider>();
        services.AddHttpClient<EdhTop16CorpusSignalProvider>();
        services.AddHttpClient<RedditDiscussionCorpusSignalProvider>();
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TopDeckCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<SpicerackCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EdhTop16CorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<RedditDiscussionCorpusSignalProvider>());
        return services;
    }
}
