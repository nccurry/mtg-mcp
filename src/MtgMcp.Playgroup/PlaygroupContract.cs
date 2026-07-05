using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Playgroup;

/// <summary>
/// Owns the pinned public API identity, provider paths, validation, and evidence construction.
/// </summary>
internal static class PlaygroupContract
{
    /// <summary>
    /// Identifies the pinned provider API version.
    /// </summary>
    internal const string ApiVersion = "1.0.0";

    /// <summary>
    /// Identifies the exact checked-in OpenAPI bytes.
    /// </summary>
    internal const string OpenApiChecksum =
        "2996db9134045e255987dda80ec1110dc28d2a84f2705622833d2ab339cb7ad4";

    /// <summary>
    /// Lists provider limitations returned with every observation.
    /// </summary>
    internal static IReadOnlyList<string> Limitations { get; } = Array.AsReadOnly<string>(
    [
        "Provider state may change after retrieval.",
        "The official public API does not expose deck updates.",
        "Provider observations are evidence, not deck-quality judgments.",
    ]);

    /// <summary>
    /// Requires one positive provider identifier.
    /// </summary>
    internal static int PositiveId(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.InvalidInput,
                "invalid-provider-id",
                $"{parameterName} must be a positive provider identifier.");
        }

        return value;
    }

    /// <summary>
    /// Requires one trimmed nonblank provider name.
    /// </summary>
    internal static string Required(string? value, string parameterName, int maximumLength = 200)
    {
        string? normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.InvalidInput,
                "invalid-provider-input",
                $"{parameterName} must be nonblank and no longer than {maximumLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return normalized;
    }

    /// <summary>
    /// Computes a lowercase SHA-256 checksum for provider evidence.
    /// </summary>
    internal static string Checksum(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Parses one complete JSON response and preserves every provider field.
    /// </summary>
    internal static JsonElement ParseData(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.Unsupported,
                "provider-contract-unsupported",
                "Playgroup returned data that does not match the pinned JSON contract.",
                exception);
        }
    }
}
