using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Reads adapter credentials files without exposing secret-bearing parse details.
/// </summary>
public static class MtgMcpCredentialsFile
{
    /// <summary>
    /// Loads key-value credentials from a JSON object or line-oriented key=value file.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(
        string credentialsFile,
        string providerName,
        string keyValueExample,
        string jsonObjectRequirement,
        bool jsonArrayLooksLikeJson,
        bool requireJsonStringValues)
    {
        string text;
        try
        {
            text = File.ReadAllText(credentialsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"{providerName} credentials file '{credentialsFile}' could not be read: {exception.Message}",
                exception
            );
        }

        string trimmed = text.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (LooksLikeJson(trimmed, jsonArrayLooksLikeJson))
        {
            return ParseJsonCredentials(
                credentialsFile,
                providerName,
                text,
                jsonObjectRequirement,
                requireJsonStringValues);
        }

        return ParseKeyValueCredentials(credentialsFile, providerName, text, keyValueExample);
    }

    /// <summary>
    /// Checks whether the file should be parsed as JSON before falling back to key=value lines.
    /// </summary>
    private static bool LooksLikeJson(string trimmed, bool jsonArrayLooksLikeJson)
    {
        return trimmed.StartsWith('{') || (jsonArrayLooksLikeJson && trimmed.StartsWith('['));
    }

    /// <summary>
    /// Parses JSON object credentials while producing provider-specific redacted errors.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseJsonCredentials(
        string credentialsFile,
        string providerName,
        string text,
        string jsonObjectRequirement,
        bool requireJsonStringValues)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"{providerName} credentials file '{credentialsFile}' {jsonObjectRequirement}"
                );
            }

            Dictionary<string, string> credentials = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    credentials[property.Name] = property.Value.GetString() ?? "";
                    continue;
                }

                if (requireJsonStringValues)
                {
                    throw new InvalidDataException(
                        $"{providerName} credentials file '{credentialsFile}' fields must be strings."
                    );
                }

                credentials[property.Name] = property.Value.GetRawText();
            }

            return credentials;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{providerName} credentials file '{credentialsFile}' looks like JSON but could not be parsed. "
                    + "JSON requires double quotes around keys and string values; escape backslashes as \\\\ "
                    + "and double quotes as \\\". To avoid JSON escaping, use key=value lines instead.",
                exception
            );
        }
    }

    /// <summary>
    /// Parses line-oriented credentials and reports invalid lines without echoing values.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseKeyValueCredentials(
        string credentialsFile,
        string providerName,
        string text,
        string keyValueExample)
    {
        Dictionary<string, string> credentials = new(StringComparer.OrdinalIgnoreCase);
        using StringReader reader = new(text);
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new InvalidDataException(
                    $"{providerName} credentials file '{credentialsFile}' is not valid JSON or key=value format. "
                        + $"Line {lineNumber} must look like {keyValueExample}."
                );
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            credentials[key] = value;
        }

        return credentials;
    }
}
