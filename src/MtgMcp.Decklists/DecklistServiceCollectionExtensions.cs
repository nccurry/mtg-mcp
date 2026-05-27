using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Registers structured decklist recommendation source adapters.
/// </summary>
public static class DecklistServiceCollectionExtensions
{
    /// <summary>
    /// Adds API-backed decklist recommendation sources.
    /// </summary>
    public static IServiceCollection AddDecklistCorpusSources(this IServiceCollection services)
    {
        services.AddHttpClient<TopDeckCorpusSignalProvider>();
        services.AddHttpClient<SpicerackCorpusSignalProvider>();
        services.AddHttpClient<EdhrecCorpusSignalProvider>();
        services.AddHttpClient<EdhTop16CorpusSignalProvider>();
        services.AddHttpClient<RedditDiscussionCorpusSignalProvider>();
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TopDeckCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<SpicerackCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EdhrecCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EdhTop16CorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<RedditDiscussionCorpusSignalProvider>());
        return services;
    }
}
