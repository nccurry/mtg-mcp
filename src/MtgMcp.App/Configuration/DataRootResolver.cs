using MtgMcp.Core.Results;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Describes the non-mutating state of the resolved application-data directory.
/// </summary>
internal enum DataRootState
{
    /// <summary>
    /// The resolved directory does not exist and was not created.
    /// </summary>
    NotCreated,

    /// <summary>
    /// The resolved path exists as a directory.
    /// </summary>
    DirectoryPresent,
}

/// <summary>
/// Carries the private resolved path and its non-mutating boundary state.
/// </summary>
internal sealed record DataRootResolution(string Path, DataRootState State);

/// <summary>
/// Resolves the private v0.9 data root without creating directories or databases.
/// </summary>
internal static class DataRootResolver
{
    /// <summary>
    /// Resolves an explicit override or the platform application-data default.
    /// </summary>
    internal static OperationResult<DataRootResolution> Resolve(
        string? configuredDataRoot,
        string localApplicationData,
        string roamingApplicationData)
    {
        string path;
        try
        {
            if (!string.IsNullOrWhiteSpace(configuredDataRoot))
            {
                path = Path.GetFullPath(configuredDataRoot.Trim());
            }
            else
            {
                string applicationDataRoot = !string.IsNullOrWhiteSpace(localApplicationData)
                    ? localApplicationData
                    : roamingApplicationData;
                if (string.IsNullOrWhiteSpace(applicationDataRoot))
                {
                    return new OperationUnavailable(
                        "application-data-unavailable",
                        "The platform application-data directory is unavailable.");
                }

                path = Path.Combine(applicationDataRoot, "mtg-mcp", "v0.9");
            }

            if (File.Exists(path))
            {
                return new OperationInvalidInput(
                    "invalid-data-root",
                    "The configured data directory is invalid.");
            }

            DataRootState state = Directory.Exists(path)
                ? DataRootState.DirectoryPresent
                : DataRootState.NotCreated;
            return new OperationSuccess<DataRootResolution>(new DataRootResolution(path, state));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            return new OperationInvalidInput(
                "invalid-data-root",
                "The configured data directory is invalid.");
        }
    }
}
