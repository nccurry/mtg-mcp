using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        services.AddSingleton<ArchidektRequestPacer>();
        services.AddHttpClient(nameof(ArchidektGateway));
        services.AddTransient<IArchidektGateway>(serviceProvider =>
        {
            HttpClient httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(ArchidektGateway));
            return new ArchidektGateway(
                httpClient,
                serviceProvider.GetRequiredService<IOptions<ArchidektOptions>>(),
                serviceProvider.GetRequiredService<ArchidektRequestPacer>());
        });
        return services;
    }
}
