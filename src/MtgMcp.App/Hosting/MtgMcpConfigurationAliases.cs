using Microsoft.Extensions.Configuration;

namespace MtgMcp.App;

/// <summary>
/// Provides mtg mcp configuration aliases behavior.
/// </summary>
public static class MtgMcpConfigurationAliases
{
    /// <summary>
    /// Handles readonly.
    /// </summary>
    private static readonly (string Alias, string Canonical)[] Aliases =
    [
        ("DATA_DIR", "MtgMcp:DataDir"),
        ("DataDir", "MtgMcp:DataDir"),
        ("OPERATION_MODE", "MtgMcp:OperationMode"),
        ("MODE", "MtgMcp:OperationMode"),
        ("ARCHIDEKT:BASE_ADDRESS", "MtgMcp:Archidekt:BaseAddress"),
        ("ARCHIDEKT:JWT", "MtgMcp:Archidekt:Jwt"),
        ("ARCHIDEKT:REFRESH_TOKEN", "MtgMcp:Archidekt:RefreshToken"),
        ("ARCHIDEKT:USER_ID", "MtgMcp:Archidekt:UserId"),
        ("ARCHIDEKT_USER_ID", "MtgMcp:Archidekt:UserId"),
        ("ARCHIDEKT:EMAIL", "MtgMcp:Archidekt:Email"),
        ("ARCHIDEKT:USERNAME", "MtgMcp:Archidekt:Username"),
        ("ARCHIDEKT:PASSWORD", "MtgMcp:Archidekt:Password"),
        ("ARCHIDEKT:CREDENTIALS_FILE", "MtgMcp:Archidekt:CredentialsFile"),
        ("SCRYFALL:BASE_ADDRESS", "MtgMcp:Scryfall:BaseAddress"),
        ("SCRYFALL:USER_AGENT", "MtgMcp:Scryfall:UserAgent"),
        ("COMMANDERSPELLBOOK:BASE_ADDRESS", "MtgMcp:CommanderSpellbook:BaseAddress"),
    ];

    /// <summary>
    /// Creates the configuration.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Create(IConfiguration configuration)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string alias, string canonical) in Aliases)
        {
            if (!string.IsNullOrWhiteSpace(configuration[canonical]))
            {
                continue;
            }

            string? value = configuration[alias];
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[canonical] = value;
            }
        }

        return values;
    }
}
