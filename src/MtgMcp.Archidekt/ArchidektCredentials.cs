using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Loads and retains Archidekt credentials without exposing secret values, identities, or file locations.
/// </summary>
internal sealed class ArchidektCredentials
{
    /// <summary>
    /// Stores validated adapter configuration.
    /// </summary>
    private readonly ArchidektOptions options;

    /// <summary>
    /// Serializes the first credential-file read.
    /// </summary>
    private readonly object gate = new();

    /// <summary>
    /// Caches the redacted load result for this process.
    /// </summary>
    private CredentialLoad? cached;

    /// <summary>
    /// Creates one lazy credential source.
    /// </summary>
    internal ArchidektCredentials(ArchidektOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Loads credentials once and returns values only to the transport boundary.
    /// </summary>
    internal CredentialLoad Load()
    {
        lock (gate)
        {
            return cached ??= LoadCore();
        }
    }

    /// <summary>
    /// Combines explicit secret configuration with the optional strict credentials file.
    /// </summary>
    private CredentialLoad LoadCore()
    {
        string? username = ArchidektContract.Optional(options.Username);
        string? password = ArchidektContract.Optional(options.Password);
        string? credentialsFile = ArchidektContract.Optional(options.CredentialsFile);
        if (credentialsFile is not null)
        {
            if (!File.Exists(credentialsFile))
            {
                return new CredentialLoad(null, null, "error", "Archidekt credentials could not be loaded.");
            }

            try
            {
                (string? fileUsername, string? filePassword) = ReadFile(credentialsFile);
                username ??= fileUsername;
                password ??= filePassword;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return new CredentialLoad(null, null, "error", "Archidekt credentials could not be loaded.");
            }
        }

        bool usernamePresent = !string.IsNullOrWhiteSpace(username);
        bool passwordPresent = !string.IsNullOrWhiteSpace(password);
        if (!usernamePresent && !passwordPresent)
        {
            return new CredentialLoad(null, null, "not-configured", "Archidekt credentials are not configured.");
        }

        if (!usernamePresent || !passwordPresent)
        {
            return new CredentialLoad(null, null, "error", "Archidekt credentials are incomplete.");
        }

        return new CredentialLoad(username, password, "configured", "Archidekt credentials are configured.");
    }

    /// <summary>
    /// Reads a strict JSON object or line-oriented username/password file.
    /// </summary>
    private static (string? Username, string? Password) ReadFile(string path)
    {
        string content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, null);
        }

        if (content.TrimStart().StartsWith('{'))
        {
            using JsonDocument document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Credentials must be an object.");
            }

            string? username = null;
            string? password = null;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Credential fields must be strings.");
                }

                if (property.Name.Equals("username", StringComparison.OrdinalIgnoreCase))
                {
                    username = property.Value.GetString();
                }
                else if (property.Name.Equals("password", StringComparison.OrdinalIgnoreCase))
                {
                    password = property.Value.GetString();
                }
                else
                {
                    throw new InvalidDataException("Unknown credential field.");
                }
            }

            return (ArchidektContract.Optional(username), ArchidektContract.Optional(password));
        }

        string? lineUsername = null;
        string? linePassword = null;
        using StringReader reader = new(content);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new InvalidDataException("Credential line is invalid.");
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (key.Equals("username", StringComparison.OrdinalIgnoreCase))
            {
                lineUsername = value;
            }
            else if (key.Equals("password", StringComparison.OrdinalIgnoreCase))
            {
                linePassword = value;
            }
            else
            {
                throw new InvalidDataException("Unknown credential field.");
            }
        }

        return (ArchidektContract.Optional(lineUsername), ArchidektContract.Optional(linePassword));
    }

    /// <summary>
    /// Carries secret values internally together with a safe public load state.
    /// </summary>
    internal sealed record CredentialLoad(
        string? Username,
        string? Password,
        string State,
        string Message)
    {
        /// <summary>
        /// Gets whether both values required for login are available.
        /// </summary>
        internal bool IsUsable => Username is not null && Password is not null;

        /// <summary>
        /// Gets a non-secret hash used only to share one pacing lane for this account.
        /// </summary>
        internal string PacingKey => ArchidektContract.Hash(Username?.ToUpperInvariant() ?? "anonymous");
    }
}
