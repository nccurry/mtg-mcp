using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Registers archidekt service collection services.
/// </summary>
public static class ArchidektServiceCollectionExtensions
{
    /// <summary>
    /// Adds the archidekt.
    /// </summary>
    public static IServiceCollection AddArchidekt(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<ArchidektOptions>(configuration.GetSection("MtgMcp:Archidekt"));
        services.AddHttpClient<IArchidektGateway, ArchidektGateway>();
        return services;
    }
}
