using Microsoft.Extensions.Configuration;
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
    public static IServiceCollection AddDecklistCorpusSources(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddHttpClient<TopDeckCorpusSignalProvider>();
        services.AddHttpClient<EdhrecCorpusSignalProvider>();
        services.AddHttpClient<EdhTop16CorpusSignalProvider>();
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TopDeckCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EdhrecCorpusSignalProvider>());
        services.AddTransient<ICorpusSignalProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<EdhTop16CorpusSignalProvider>());
        return services;
    }
}
