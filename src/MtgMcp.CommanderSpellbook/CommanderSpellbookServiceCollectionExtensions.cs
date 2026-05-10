using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MtgMcp.Core;

namespace MtgMcp.CommanderSpellbook;

/// <summary>
/// Registers Commander Spellbook services.
/// </summary>
public static class CommanderSpellbookServiceCollectionExtensions
{
    /// <summary>
    /// Adds Commander Spellbook combo catalog support.
    /// </summary>
    public static IServiceCollection AddCommanderSpellbook(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CommanderSpellbookOptions>(configuration.GetSection("MtgMcp:CommanderSpellbook"));
        services.AddHttpClient<IComboCatalog, CommanderSpellbookComboCatalog>();
        return services;
    }
}
