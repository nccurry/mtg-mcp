using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Provides deterministic validation, canonical serialization, and hashing for provider evidence.
/// </summary>
internal static class ArchidektContract
{
    /// <summary>
    /// Names the dated observed frontend contract represented by sanitized fixtures.
    /// </summary>
    internal const string Version = "observed-2026-07-04";

    /// <summary>
    /// Gets stable web serialization settings for provider payloads and fingerprints.
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Requires one nonblank, trimmed identifier or text field.
    /// </summary>
    internal static string Required(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Value cannot contain surrounding whitespace.", parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes an optional provider string without converting absence into an empty fact.
    /// </summary>
    internal static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Computes a lowercase SHA-256 checksum over exact UTF-8 text.
    /// </summary>
    internal static string Hash(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Serializes one canonical projection and computes its lowercase SHA-256 fingerprint.
    /// </summary>
    internal static string Fingerprint<T>(T value)
    {
        return Hash(JsonSerializer.Serialize(value, JsonOptions));
    }

    /// <summary>
    /// Derives a stable provider-scoped local identifier without storing transport identity in Core.
    /// </summary>
    internal static Guid StableGuid(string scope, string providerId)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"archidekt:{scope}:{providerId}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes, bigEndian: true);
    }

    /// <summary>
    /// Formats a provider value as stable JSON text for path-addressed comparisons.
    /// </summary>
    internal static string? JsonValue(object? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
    }
}
