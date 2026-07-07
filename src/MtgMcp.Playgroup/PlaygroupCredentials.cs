using System.Text.Json;

namespace MtgMcp.Playgroup;

/// <summary>
/// Loads one Playgroup API key without exposing its value or credential-file location.
/// </summary>
internal sealed class PlaygroupCredentials
{
    /// <summary>Stores validated adapter configuration.</summary>
    private readonly PlaygroupOptions options;

    /// <summary>Serializes the first credential-file read.</summary>
    private readonly object gate = new();

    /// <summary>Caches the redacted load result for this process.</summary>
    private CredentialLoad? cached;

    /// <summary>Creates one lazy credential source.</summary>
    internal PlaygroupCredentials(PlaygroupOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Loads the configured key once and returns a safe public state alongside it.</summary>
    internal CredentialLoad Load()
    {
        lock (gate)
        {
            return cached ??= LoadCore();
        }
    }

    /// <summary>Combines an explicit key with the optional strict JSON credential file.</summary>
    private CredentialLoad LoadCore()
    {
        string? apiKey = Optional(options.ApiKey);
        if (apiKey is null && options.CredentialsFile is not null)
        {
            try
            {
                apiKey = ReadFile(options.CredentialsFile);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return new CredentialLoad(null, "error", "Playgroup credentials could not be loaded.");
            }
        }

        return apiKey is null
            ? new CredentialLoad(null, "not-configured", "A Playgroup API key is not configured.")
            : new CredentialLoad(apiKey, "configured", "A Playgroup API key is configured.");
    }

    /// <summary>Reads a strict object containing only one string-valued <c>apiKey</c> field.</summary>
    private static string? ReadFile(string path)
    {
        string content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Credentials must be an object.");
        }

        string? apiKey = null;
        bool foundApiKey = false;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals("apiKey", StringComparison.OrdinalIgnoreCase) ||
                property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The credential file contains an unsupported field.");
            }

            if (foundApiKey)
            {
                throw new InvalidDataException("The credential file contains a duplicate field.");
            }

            foundApiKey = true;
            apiKey = property.Value.GetString();
        }

        return Optional(apiKey);
    }

    /// <summary>Normalizes an optional secret without inventing a missing value.</summary>
    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Carries the private key together with a safe public load state.</summary>
    internal sealed record CredentialLoad(string? ApiKey, string State, string Message)
    {
        /// <summary>Gets whether authenticated Playgroup operations can be attempted.</summary>
        internal bool IsUsable => ApiKey is not null;
    }
}
