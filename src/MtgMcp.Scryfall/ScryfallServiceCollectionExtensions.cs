using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Registers scryfall service collection services.
/// </summary>
public static class ScryfallServiceCollectionExtensions
{
    /// <summary>
    /// Adds the scryfall.
    /// </summary>
    public static IServiceCollection AddScryfall(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<ScryfallOptions>(configuration.GetSection("MtgMcp:Scryfall"));
        services.AddSingleton<ScryfallRequestPacer>();
        services.AddHttpClient(nameof(ScryfallClient));
        services.AddTransient<ICardCatalog>(serviceProvider =>
        {
            HttpClient httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(ScryfallClient));
            return new ScryfallClient(
                httpClient,
                serviceProvider.GetRequiredService<IOptions<ScryfallOptions>>(),
                serviceProvider.GetRequiredService<ICorpusCache>(),
                serviceProvider.GetRequiredService<IOptions<MtgMcpOptions>>(),
                serviceProvider.GetRequiredService<ScryfallRequestPacer>());
        });
        services.AddTransient<ICardTrendProvider, ScryfallCardTrendProvider>();
        services.AddTransient<ICommanderMetaProvider, ScryfallCommanderMetaProvider>();
        services.AddTransient<ICorpusSignalProvider, ScryfallCorpusSignalProvider>();
        services.AddTransient<ICorpusSignalProvider, ScryfallTaggerCorpusSignalProvider>();
        return services;
    }
}
