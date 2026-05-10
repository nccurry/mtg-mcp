using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddHttpClient<ICardCatalog, ScryfallClient>();
        services.AddTransient<ICardTrendProvider, ScryfallCardTrendProvider>();
        services.AddTransient<ICommanderMetaProvider, ScryfallCommanderMetaProvider>();
        return services;
    }
}
