using System.Globalization;
using Microsoft.Extensions.Configuration;
using MtgMcp.App.Capabilities;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Builds and validates the standard JSON, environment, and command-line configuration sources.
/// </summary>
internal static class FoundationConfigurationLoader
{
    /// <summary>
    /// Names the optional JSON configuration file read from the process working directory.
    /// </summary>
    private const string DefaultConfigurationFile = "mtg-mcp.json";

    /// <summary>
    /// Selects only environment variables owned by this application.
    /// </summary>
    private const string EnvironmentPrefix = "MTGMCP__";

    /// <summary>
    /// Maps stable command-line switches to configuration keys.
    /// </summary>
    private static readonly IDictionary<string, string> SwitchMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--data-dir"] = "DATA_DIR",
            ["--mode"] = "MODE",
            ["--toolsets"] = "TOOLSETS",
            ["--scryfall-ttl-hours"] = "SCRYFALL_TTL_HOURS",
        };

    /// <summary>
    /// Loads configuration from the process environment and platform application-data locations.
    /// </summary>
    internal static OperationResult<FoundationConfiguration> Load(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string configurationFile = Path.GetFullPath(DefaultConfigurationFile);
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Load(arguments, configurationFile, localApplicationData, roamingApplicationData);
    }

    /// <summary>
    /// Loads configuration using explicit filesystem roots for deterministic cross-platform tests.
    /// </summary>
    internal static OperationResult<FoundationConfiguration> Load(
        IReadOnlyList<string> arguments,
        string configurationFile,
        string localApplicationData,
        string roamingApplicationData)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFile);

        OperationInvalidInput? commandLineFailure = NormalizeCommandLine(
            arguments,
            out string[] normalizedArguments);
        if (commandLineFailure is not null)
        {
            return commandLineFailure;
        }

        IConfigurationRoot configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile(configurationFile, optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(EnvironmentPrefix)
                .AddCommandLine(normalizedArguments, SwitchMappings)
                .Build();
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or IOException or InvalidDataException)
        {
            return new OperationInvalidInput(
                "invalid-configuration",
                "Configuration could not be loaded because it is invalid.");
        }

        return Resolve(configuration, localApplicationData, roamingApplicationData);
    }

    /// <summary>
    /// Resolves validated runtime configuration from an already composed source set.
    /// </summary>
    internal static OperationResult<FoundationConfiguration> Resolve(
        IConfiguration configuration,
        string localApplicationData,
        string roamingApplicationData)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        OperationResult<OperationMode> modeResult = OperationModeParser.Parse(configuration["MODE"]);
        if (modeResult is not OperationSuccess<OperationMode> mode)
        {
            return ForwardFailure(modeResult);
        }

        OperationResult<CapabilityToolsetSelection> toolsetsResult =
            CapabilityToolsetRegistry.Resolve(configuration["TOOLSETS"]);
        if (toolsetsResult is not OperationSuccess<CapabilityToolsetSelection> toolsets)
        {
            return ForwardFailure(toolsetsResult);
        }

        OperationResult<TimeSpan> ttlResult = ParseScryfallTtl(configuration["SCRYFALL_TTL_HOURS"]);
        if (ttlResult is not OperationSuccess<TimeSpan> ttl)
        {
            return ForwardFailure(ttlResult);
        }

        string? configuredDataRoot = configuration["DATA_DIR"];
        OperationResult<DataRootResolution> dataRootResult = DataRootResolver.Resolve(
            configuredDataRoot,
            localApplicationData,
            roamingApplicationData);
        if (dataRootResult is not OperationSuccess<DataRootResolution> dataRoot)
        {
            return ForwardFailure(dataRootResult);
        }

        string applicationDataRoot = !string.IsNullOrWhiteSpace(localApplicationData)
            ? localApplicationData
            : roamingApplicationData;
        LegacyDataBoundary legacyData = LegacyDataInspector.Inspect(applicationDataRoot);
        OperationResult<ArchidektOptions> archidektResult = ResolveArchidekt(configuration);
        if (archidektResult is not OperationSuccess<ArchidektOptions> archidekt)
        {
            return ForwardFailure(archidektResult);
        }

        return new OperationSuccess<FoundationConfiguration>(
            new FoundationConfiguration(
                mode.Data,
                toolsets.Data,
                ttl.Data,
                dataRoot.Data.Path,
                dataRoot.Data.State,
                !string.IsNullOrWhiteSpace(configuredDataRoot),
                legacyData,
                archidekt.Data));
    }

    /// <summary>
    /// Resolves the private Archidekt transport configuration without loading or echoing credentials.
    /// </summary>
    private static OperationResult<ArchidektOptions> ResolveArchidekt(IConfiguration configuration)
    {
        string? credentialsFile = configuration["ARCHIDEKT:CREDENTIALS_FILE"];
        if (string.IsNullOrWhiteSpace(credentialsFile))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string defaultFile = Path.Combine(userProfile, ".mtg-mcp", "archidekt.json");
            credentialsFile = File.Exists(defaultFile) ? defaultFile : null;
        }

        ArchidektOptions options = ArchidektOptions.CreateDefault(
            configuration["ARCHIDEKT:USERNAME"],
            configuration["ARCHIDEKT:PASSWORD"],
            credentialsFile);
        return new OperationSuccess<ArchidektOptions>(options);
    }

    /// <summary>
    /// Parses the configurable positive Scryfall evidence TTL with a 24-hour default.
    /// </summary>
    private static OperationResult<TimeSpan> ParseScryfallTtl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new OperationSuccess<TimeSpan>(TimeSpan.FromHours(24));
        }

        bool valid = double.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double hours) &&
            hours is > 0 and <= 8_760;
        return valid
            ? new OperationSuccess<TimeSpan>(TimeSpan.FromHours(hours))
            : new OperationInvalidInput(
                "invalid-scryfall-ttl",
                "Scryfall freshness hours must be a positive number no greater than 8760.");
    }

    /// <summary>
    /// Normalizes accepted switch forms and rejects duplicates or incomplete pairs.
    /// </summary>
    private static OperationInvalidInput? NormalizeCommandLine(
        IReadOnlyList<string> arguments,
        out string[] normalizedArguments)
    {
        List<string> normalized = [];
        HashSet<string> seenKeys = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            int separatorIndex = argument.IndexOf('=', StringComparison.Ordinal);
            string switchName = separatorIndex >= 0 ? argument[..separatorIndex] : argument;
            if (!SwitchMappings.ContainsKey(switchName) || !seenKeys.Add(switchName))
            {
                normalizedArguments = [];
                return new OperationInvalidInput(
                    "invalid-command-line",
                    "Configuration contains an unsupported or incomplete command-line option.");
            }

            string value;
            if (separatorIndex >= 0)
            {
                value = argument[(separatorIndex + 1)..];
            }
            else
            {
                bool hasValue = index + 1 < arguments.Count &&
                    !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);
                if (!hasValue)
                {
                    normalizedArguments = [];
                    return new OperationInvalidInput(
                        "invalid-command-line",
                        "Configuration contains an unsupported or incomplete command-line option.");
                }

                index++;
                value = arguments[index];
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                normalizedArguments = [];
                return new OperationInvalidInput(
                    "invalid-command-line",
                    "Configuration contains an unsupported or incomplete command-line option.");
            }

            normalized.Add(switchName);
            normalized.Add(value);
        }

        normalizedArguments = normalized.ToArray();
        return null;
    }

    /// <summary>
    /// Preserves a structured failure while rejecting an unexpected successful branch.
    /// </summary>
    internal static OperationResult<FoundationConfiguration> ForwardFailure<T>(OperationResult<T> result)
    {
        return result switch
        {
            OperationSuccess<T> => new OperationUnavailable(
                "configuration-resolution-failed",
                "Configuration could not be resolved."),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}
