namespace MtgMcp.App.Configuration;

/// <summary>
/// Holds private runtime configuration that must not be serialized directly to clients.
/// </summary>
internal sealed record FoundationConfiguration(
    OperationMode Mode,
    string DataRoot,
    bool DataRootConfigured,
    LegacyDataBoundary LegacyData)
{
    /// <summary>
    /// Creates the path-free status projection safe for future server metadata.
    /// </summary>
    internal FoundationConfigurationStatus ToPublicStatus()
    {
        return new FoundationConfigurationStatus(
            OperationModeParser.Format(Mode),
            DataRootConfigured,
            LegacyData.State switch
            {
                LegacyDataState.NotDetected => "not-detected",
                LegacyDataState.Detected => "detected",
                LegacyDataState.InspectionUnavailable => "inspection-unavailable",
                _ => "inspection-unavailable",
            },
            LegacyData.Message);
    }
}

/// <summary>
/// Describes configuration state without exposing absolute paths or secret values.
/// </summary>
internal sealed record FoundationConfigurationStatus(
    string Mode,
    bool DataRootConfigured,
    string LegacyDataState,
    string MigrationBoundary);
