using Microsoft.Extensions.Configuration;
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

        OperationInvalidInput? commandLineFailure = ValidateCommandLine(arguments);
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
                .AddCommandLine(arguments.ToArray(), SwitchMappings)
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
            // The parser intentionally has only success and invalid-input outcomes.
            return (OperationInvalidInput)modeResult.Value!;
        }

        string? configuredDataRoot = configuration["DATA_DIR"];
        OperationResult<string> dataRootResult = DataRootResolver.Resolve(
            configuredDataRoot,
            localApplicationData,
            roamingApplicationData);
        if (dataRootResult is not OperationSuccess<string> dataRoot)
        {
            if (dataRootResult.Value is OperationInvalidInput invalidDataRoot)
            {
                return invalidDataRoot;
            }

            // The resolver's remaining failure outcome is unavailable platform data.
            return (OperationUnavailable)dataRootResult.Value!;
        }

        string applicationDataRoot = !string.IsNullOrWhiteSpace(localApplicationData)
            ? localApplicationData
            : roamingApplicationData;
        LegacyDataBoundary legacyData = LegacyDataInspector.Inspect(applicationDataRoot);
        return new OperationSuccess<FoundationConfiguration>(
            new FoundationConfiguration(
                mode.Data,
                dataRoot.Data,
                !string.IsNullOrWhiteSpace(configuredDataRoot),
                legacyData));
    }

    /// <summary>
    /// Rejects unknown switches and incomplete key/value pairs without echoing their contents.
    /// </summary>
    private static OperationInvalidInput? ValidateCommandLine(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index += 2)
        {
            bool hasValue = index + 1 < arguments.Count &&
                !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);
            if (!SwitchMappings.ContainsKey(arguments[index]) || !hasValue)
            {
                return new OperationInvalidInput(
                    "invalid-command-line",
                    "Configuration contains an unsupported or incomplete command-line option.");
            }
        }

        return null;
    }
}
