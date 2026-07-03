using System.Security;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Classifies whether legacy data was observed without interpreting its contents.
/// </summary>
internal enum LegacyDataState
{
    /// <summary>
    /// No legacy entry was found under the application data root.
    /// </summary>
    NotDetected,

    /// <summary>
    /// At least one entry outside the v0.9 schema directory was found.
    /// </summary>
    Detected,

    /// <summary>
    /// The application data root could not be inspected safely.
    /// </summary>
    InspectionUnavailable,
}

/// <summary>
/// Reports the clean-break legacy-data boundary using a path-free message.
/// </summary>
internal sealed record LegacyDataBoundary(LegacyDataState State, string Message);

/// <summary>
/// Detects the presence of legacy entries without parsing, loading, or changing them.
/// </summary>
internal static class LegacyDataInspector
{
    /// <summary>
    /// Inspects the application data root and returns a path-free clean-break status.
    /// </summary>
    internal static LegacyDataBoundary Inspect(string applicationDataRoot)
    {
        if (string.IsNullOrWhiteSpace(applicationDataRoot))
        {
            return new LegacyDataBoundary(
                LegacyDataState.InspectionUnavailable,
                "Legacy data was not inspected and will not be loaded or migrated.");
        }

        string legacyRoot = Path.Combine(applicationDataRoot, "mtg-mcp");
        try
        {
            if (!Directory.Exists(legacyRoot))
            {
                return new LegacyDataBoundary(
                    LegacyDataState.NotDetected,
                    "Legacy data was not detected; automatic migration remains disabled.");
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(legacyRoot))
            {
                if (!Path.GetFileName(entry).Equals("v0.9", StringComparison.Ordinal))
                {
                    return new LegacyDataBoundary(
                        LegacyDataState.Detected,
                        "Legacy data was detected and will not be loaded, migrated, or modified.");
                }
            }

            return new LegacyDataBoundary(
                LegacyDataState.NotDetected,
                "Legacy data was not detected; automatic migration remains disabled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new LegacyDataBoundary(
                LegacyDataState.InspectionUnavailable,
                "Legacy data was not inspected and will not be loaded or migrated.");
        }
    }
}
