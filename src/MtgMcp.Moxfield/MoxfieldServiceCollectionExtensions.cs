using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.Moxfield;

/// <summary>
/// Registers Moxfield import services.
/// </summary>
public static class MoxfieldServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Moxfield gateway and option binding.
    /// </summary>
    public static IServiceCollection AddMoxfield(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<MoxfieldOptions>(configuration.GetSection("MtgMcp:Moxfield"));
        services.AddHttpClient<IMoxfieldGateway, MoxfieldGateway>();
        return services;
    }
}
