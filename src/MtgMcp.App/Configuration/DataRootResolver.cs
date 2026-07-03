using MtgMcp.Core.Results;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Resolves the private v0.9 data root without creating directories or databases.
/// </summary>
internal static class DataRootResolver
{
    /// <summary>
    /// Resolves an explicit override or the platform application-data default.
    /// </summary>
    internal static OperationResult<string> Resolve(
        string? configuredDataRoot,
        string localApplicationData,
        string roamingApplicationData)
    {
        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
        {
            try
            {
                return new OperationSuccess<string>(Path.GetFullPath(configuredDataRoot.Trim()));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new OperationInvalidInput(
                    "invalid-data-root",
                    "The configured data directory is invalid.");
            }
        }

        string applicationDataRoot = !string.IsNullOrWhiteSpace(localApplicationData)
            ? localApplicationData
            : roamingApplicationData;
        if (string.IsNullOrWhiteSpace(applicationDataRoot))
        {
            return new OperationUnavailable(
                "application-data-unavailable",
                "The platform application-data directory is unavailable.");
        }

        return new OperationSuccess<string>(
            Path.Combine(applicationDataRoot, "mtg-mcp", "v0.9"));
    }
}
