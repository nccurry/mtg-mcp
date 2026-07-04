using MtgMcp.App.Capabilities;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Holds private runtime configuration that must not be serialized directly to clients.
/// </summary>
internal sealed record FoundationConfiguration(
    OperationMode Mode,
    CapabilityToolsetSelection Toolsets,
    TimeSpan ScryfallFreshnessTtl,
    string DataRoot,
    DataRootState DataRootState,
    bool DataRootConfigured,
    LegacyDataBoundary LegacyData)
{
    /// <summary>
    /// Creates the path-free status projection safe for future server metadata.
    /// </summary>
    internal FoundationConfigurationStatus ToPublicStatus()
    {
        DataRootState effectiveDataRootState = Directory.Exists(DataRoot)
            ? Configuration.DataRootState.DirectoryPresent
            : DataRootState;
        return new FoundationConfigurationStatus(
            DataRootConfigured,
            effectiveDataRootState switch
            {
                Configuration.DataRootState.NotCreated => "not-created",
                Configuration.DataRootState.DirectoryPresent => "directory-present",
                _ => "not-created",
            },
            LegacyData.State switch
            {
                LegacyDataState.NotDetected => "not-detected",
                LegacyDataState.Detected => "detected",
                LegacyDataState.InspectionUnavailable => "inspection-unavailable",
                _ => "inspection-unavailable",
            },
            LegacyData.Message,
            ScryfallFreshnessTtl.TotalHours);
    }
}

/// <summary>
/// Describes configuration state without exposing absolute paths or secret values.
/// </summary>
internal sealed record FoundationConfigurationStatus(
    bool DataRootConfigured,
    string DataRootState,
    string LegacyDataState,
    string MigrationBoundary,
    double ScryfallFreshnessHours);
