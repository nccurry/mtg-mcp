using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Playgroup;

/// <summary>
/// Registers Playgroup.gg adapter services.
/// </summary>
public static class PlaygroupServiceCollectionExtensions
{
    /// <summary>
    /// Adds Playgroup.gg public API services.
    /// </summary>
    public static IServiceCollection AddPlaygroup(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<PlaygroupOptions>(configuration.GetSection("MtgMcp:Playgroup"));
        services.AddHttpClient<IPlaygroupGateway, PlaygroupGateway>();
        return services;
    }
}
