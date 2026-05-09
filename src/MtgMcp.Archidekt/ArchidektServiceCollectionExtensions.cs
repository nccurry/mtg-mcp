using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

public static class ArchidektServiceCollectionExtensions
{
    public static IServiceCollection AddArchidekt(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArchidektOptions>(configuration.GetSection("MtgMcp:Archidekt"));
        services.AddHttpClient<IArchidektGateway, ArchidektGateway>();
        return services;
    }
}
