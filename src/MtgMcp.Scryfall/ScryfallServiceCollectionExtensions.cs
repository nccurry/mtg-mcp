using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

public static class ScryfallServiceCollectionExtensions
{
    public static IServiceCollection AddScryfall(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ScryfallOptions>(configuration.GetSection("MtgMcp:Scryfall"));
        services.AddHttpClient<ICardCatalog, ScryfallClient>();
        return services;
    }
}
