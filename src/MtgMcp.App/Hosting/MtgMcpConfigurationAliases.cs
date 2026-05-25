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
        ("OPERATION_MODE", "MtgMcp:OperationMode"),
        ("INTELLIGENCE:ANALYSIS_DEPTH", "MtgMcp:Intelligence:AnalysisDepth"),
        ("INTELLIGENCE:CACHE:MODE", "MtgMcp:Intelligence:Cache:Mode"),
        ("INTELLIGENCE:CACHE:MAX_BYTES", "MtgMcp:Intelligence:Cache:MaxBytes"),
        ("INTELLIGENCE:CACHE:MAX_ENTRIES", "MtgMcp:Intelligence:Cache:MaxEntries"),
        ("INTELLIGENCE:CACHE:TTLS:SCRYFALL_CARD_METADATA", "MtgMcp:Intelligence:Cache:Ttls:ScryfallCardMetadata"),
        ("INTELLIGENCE:CACHE:TTLS:SCRYFALL_SEARCH", "MtgMcp:Intelligence:Cache:Ttls:ScryfallSearch"),
        ("INTELLIGENCE:CACHE:TTLS:COMMANDERSPELLBOOK", "MtgMcp:Intelligence:Cache:Ttls:CommanderSpellbook"),
        ("INTELLIGENCE:CACHE:TTLS:DECK_SEARCH", "MtgMcp:Intelligence:Cache:Ttls:DeckSearch"),
        ("INTELLIGENCE:CACHE:TTLS:DECK_DETAILS", "MtgMcp:Intelligence:Cache:Ttls:DeckDetails"),
        ("INTELLIGENCE:CACHE:TTLS:CORPUS_SIGNALS", "MtgMcp:Intelligence:Cache:Ttls:CorpusSignals"),
        ("INTELLIGENCE:SOURCES:SCRYFALL:ENABLED", "MtgMcp:Intelligence:Sources:Scryfall:Enabled"),
        ("INTELLIGENCE:SOURCES:SCRYFALL_TAGGER:ENABLED", "MtgMcp:Intelligence:Sources:ScryfallTagger:Enabled"),
        ("INTELLIGENCE:SOURCES:COMMANDERSPELLBOOK:ENABLED", "MtgMcp:Intelligence:Sources:CommanderSpellbook:Enabled"),
        ("INTELLIGENCE:SOURCES:TOPDECK:ENABLED", "MtgMcp:Intelligence:Sources:TopDeck:Enabled"),
        ("INTELLIGENCE:SOURCES:TOPDECK:API_KEY", "MtgMcp:Intelligence:Sources:TopDeck:ApiKey"),
        ("INTELLIGENCE:SOURCES:TOPDECK:BASE_ADDRESS", "MtgMcp:Intelligence:Sources:TopDeck:BaseAddress"),
        ("INTELLIGENCE:SOURCES:SPICERACK:ENABLED", "MtgMcp:Intelligence:Sources:Spicerack:Enabled"),
        ("INTELLIGENCE:SOURCES:SPICERACK:API_KEY", "MtgMcp:Intelligence:Sources:Spicerack:ApiKey"),
        ("INTELLIGENCE:SOURCES:SPICERACK:BASE_ADDRESS", "MtgMcp:Intelligence:Sources:Spicerack:BaseAddress"),
        ("INTELLIGENCE:SOURCES:EDHTOP16:ENABLED", "MtgMcp:Intelligence:Sources:EdhTop16:Enabled"),
        ("INTELLIGENCE:SOURCES:EDHTOP16:ALLOW_UNOFFICIAL_API", "MtgMcp:Intelligence:Sources:EdhTop16:AllowUnofficialApi"),
        ("INTELLIGENCE:SOURCES:EDHTOP16:BASE_ADDRESS", "MtgMcp:Intelligence:Sources:EdhTop16:BaseAddress"),
        ("INTELLIGENCE:SOURCES:REDDIT:ENABLED", "MtgMcp:Intelligence:Sources:Reddit:Enabled"),
        ("INTELLIGENCE:SOURCES:REDDIT:API_KEY", "MtgMcp:Intelligence:Sources:Reddit:ApiKey"),
        ("INTELLIGENCE:SOURCES:REDDIT:ALLOW_UNOFFICIAL_API", "MtgMcp:Intelligence:Sources:Reddit:AllowUnofficialApi"),
        ("INTELLIGENCE:SOURCES:REDDIT:BASE_ADDRESS", "MtgMcp:Intelligence:Sources:Reddit:BaseAddress"),
        ("ARCHIDEKT:BASE_ADDRESS", "MtgMcp:Archidekt:BaseAddress"),
        ("ARCHIDEKT:JWT", "MtgMcp:Archidekt:Jwt"),
        ("ARCHIDEKT:REFRESH_TOKEN", "MtgMcp:Archidekt:RefreshToken"),
        ("ARCHIDEKT:USER_ID", "MtgMcp:Archidekt:UserId"),
        ("ARCHIDEKT:EMAIL", "MtgMcp:Archidekt:Email"),
        ("ARCHIDEKT:USERNAME", "MtgMcp:Archidekt:Username"),
        ("ARCHIDEKT:PASSWORD", "MtgMcp:Archidekt:Password"),
        ("ARCHIDEKT:CREDENTIALS_FILE", "MtgMcp:Archidekt:CredentialsFile"),
        ("PLAYGROUP:BASE_ADDRESS", "MtgMcp:Playgroup:BaseAddress"),
        ("PLAYGROUP:API_KEY", "MtgMcp:Playgroup:ApiKey"),
        ("PLAYGROUP:CREDENTIALS_FILE", "MtgMcp:Playgroup:CredentialsFile"),
        ("SIMULATION:ALLOW_EXTERNAL_PROFILE_OVERRIDES", "MtgMcp:Simulation:AllowExternalProfileOverrides"),
        ("SCRYFALL:BASE_ADDRESS", "MtgMcp:Scryfall:BaseAddress"),
        ("SCRYFALL:USER_AGENT", "MtgMcp:Scryfall:UserAgent"),
        ("SCRYFALL:MAX_RATE_LIMIT_RETRIES", "MtgMcp:Scryfall:MaxRateLimitRetries"),
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

        AddPrefixAliases(
            configuration,
            values,
            "SIMULATION:PROFILE_PATHS:",
            "MtgMcp:Simulation:ProfilePaths:");

        return values;
    }

    /// <summary>
    /// Maps indexed collection aliases such as SIMULATION__PROFILE_PATHS__0.
    /// </summary>
    private static void AddPrefixAliases(
        IConfiguration configuration,
        Dictionary<string, string?> values,
        string aliasPrefix,
        string canonicalPrefix)
    {
        foreach (KeyValuePair<string, string?> pair in configuration.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(pair.Value)
                || !pair.Key.StartsWith(aliasPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string canonicalKey = canonicalPrefix + pair.Key[aliasPrefix.Length..];
            if (!string.IsNullOrWhiteSpace(configuration[canonicalKey]))
            {
                continue;
            }

            values[canonicalKey] = pair.Value;
        }
    }
}
