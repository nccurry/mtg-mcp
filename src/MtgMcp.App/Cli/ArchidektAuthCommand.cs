using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Archidekt;

namespace MtgMcp.App;

/// <summary>
/// Writes local Archidekt credentials for MCP client configuration.
/// </summary>
public static class ArchidektAuthCommand
{
    /// <summary>
    /// Stores JSON options for credential files.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// Determines whether the arguments target the Archidekt auth helper.
    /// </summary>
    public static bool IsCommand(IReadOnlyList<string> args)
    {
        return args.Count >= 2
            && args[0].Equals("auth", StringComparison.OrdinalIgnoreCase)
            && args[1].Equals("archidekt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs the Archidekt auth helper.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ParseResult parse = Parse(args);
        if (!string.IsNullOrWhiteSpace(parse.Error))
        {
            error.WriteLine(parse.Error);
            error.WriteLine();
            WriteUsage(error);
            return 1;
        }

        if (parse.ShowHelp)
        {
            WriteUsage(output);
            return 0;
        }

        if (!parse.HasUsableCredential)
        {
            error.WriteLine(
                "Provide --jwt, --access-token, --refresh-token, or --email/--username with --password."
            );
            error.WriteLine();
            WriteUsage(error);
            return 1;
        }

        if (!TryGetCredentialsFilePath(parse, error, out string credentialsFile))
        {
            return 1;
        }

        if (File.Exists(credentialsFile) && !parse.Force)
        {
            error.WriteLine(
                $"Archidekt credentials file '{credentialsFile}' already exists. "
                    + "Use --force to overwrite it."
            );
            return 1;
        }

        string? directory = Path.GetDirectoryName(credentialsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ArchidektCredentials credentials = new()
        {
            AccessToken = parse.AccessToken,
            Jwt = parse.Jwt,
            RefreshToken = parse.RefreshToken,
            UserId = parse.UserId,
            Email = parse.Email,
            Username = parse.Username,
            Password = parse.Password,
        };
        string json = JsonSerializer.Serialize(credentials, JsonOptions) + Environment.NewLine;
        if (!TryWriteCredentialsFile(credentialsFile, json, error))
        {
            return 1;
        }

        WriteSuccess(output, credentialsFile, parse.StoredFieldNames);
        return 0;
    }

    /// <summary>
    /// Gets the default credentials file path.
    /// </summary>
    public static string GetDefaultCredentialsFile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Directory.GetCurrentDirectory();
        }

        return Path.Combine(home, ".mtg-mcp", "archidekt.json");
    }

    /// <summary>
    /// Gets the normalized credentials file path.
    /// </summary>
    private static bool TryGetCredentialsFilePath(
        ParseResult parse,
        TextWriter error,
        out string credentialsFile
    )
    {
        try
        {
            credentialsFile = Path.GetFullPath(
                parse.CredentialsFile ?? GetDefaultCredentialsFile()
            );
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            credentialsFile = "";
            error.WriteLine($"Archidekt credentials path is invalid: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes credentials with restrictive local permissions where supported.
    /// </summary>
    private static bool TryWriteCredentialsFile(
        string credentialsFile,
        string json,
        TextWriter error
    )
    {
        try
        {
            string? directory = Path.GetDirectoryName(credentialsFile);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                ProtectCredentialsDirectory(directory);
            }

            File.WriteAllText(credentialsFile, json);
            ProtectCredentialsFile(credentialsFile);
            return true;
        }
        catch (Exception exception) when (
            exception
                is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException
                    or PathTooLongException
        )
        {
            error.WriteLine(
                $"Archidekt credentials file '{credentialsFile}' could not be written: "
                    + exception.Message
            );
            return false;
        }
    }

    /// <summary>
    /// Restricts the credentials directory to the current user on Unix-like systems.
    /// </summary>
    private static void ProtectCredentialsDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    /// <summary>
    /// Restricts the credentials file to the current user on Unix-like systems.
    /// </summary>
    private static void ProtectCredentialsFile(string credentialsFile)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                credentialsFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
            );
        }
    }

    /// <summary>
    /// Parses command options.
    /// </summary>
    private static ParseResult Parse(IReadOnlyList<string> args)
    {
        ParseResult result = new();
        for (int index = 2; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument is "--help" or "-h")
            {
                result.ShowHelp = true;
                return result;
            }

            if (argument.Equals("--force", StringComparison.OrdinalIgnoreCase))
            {
                result.Force = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                result.Error = $"Unexpected argument '{argument}'.";
                return result;
            }

            string option = argument[2..];
            string? value = null;
            int equals = option.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                value = option[(equals + 1)..];
                option = option[..equals];
            }
            else
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    result.Error = $"Option '--{option}' requires a value.";
                    return result;
                }

                value = args[++index];
            }

            ApplyOption(result, option, value);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return result;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies one parsed option.
    /// </summary>
    private static void ApplyOption(ParseResult result, string option, string value)
    {
        string normalized = option.Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        switch (normalized)
        {
            case "credentials-file":
            case "file":
                result.CredentialsFile = value;
                break;
            case "jwt":
                result.Jwt = EmptyToNull(value);
                break;
            case "access-token":
                result.AccessToken = EmptyToNull(value);
                break;
            case "refresh-token":
                result.RefreshToken = EmptyToNull(value);
                break;
            case "user-id":
                result.UserId = EmptyToNull(value);
                break;
            case "email":
                result.Email = EmptyToNull(value);
                break;
            case "username":
                result.Username = EmptyToNull(value);
                break;
            case "password":
                result.Password = EmptyToNull(value);
                break;
            default:
                result.Error = $"Unknown option '--{option}'.";
                break;
        }
    }

    /// <summary>
    /// Writes successful setup guidance without echoing secrets.
    /// </summary>
    private static void WriteSuccess(
        TextWriter output,
        string credentialsFile,
        IReadOnlyList<string> storedFieldNames
    )
    {
        output.WriteLine($"Archidekt credentials file written: {credentialsFile}");
        output.WriteLine($"Stored credential fields: {string.Join(", ", storedFieldNames)}");
        output.WriteLine();
        output.WriteLine("Add this environment variable to your MCP server configuration:");
        output.WriteLine("\"env\": {");
        output.WriteLine(
            "  \"MTGMCP__ARCHIDEKT__CREDENTIALS_FILE\": "
                + JsonSerializer.Serialize(credentialsFile)
        );
        output.WriteLine("}");
    }

    /// <summary>
    /// Writes command usage.
    /// </summary>
    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine(
            "  mtg-mcp auth archidekt [--credentials-file <path>] "
                + "[--jwt <token> | --access-token <token> | --refresh-token <token> "
                + "| --email <email> --password <password> | --username <name> --password <password>] "
                + "[--user-id <id>] [--force]"
        );
    }

    /// <summary>
    /// Converts blank input to null.
    /// </summary>
    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Holds parsed command arguments.
    /// </summary>
    private sealed class ParseResult
    {
        /// <summary>
        /// Gets or sets whether to show help.
        /// </summary>
        public bool ShowHelp { get; set; }

        /// <summary>
        /// Gets or sets whether to overwrite an existing file.
        /// </summary>
        public bool Force { get; set; }

        /// <summary>
        /// Gets or sets the credentials file.
        /// </summary>
        public string? CredentialsFile { get; set; }

        /// <summary>
        /// Gets or sets the jwt.
        /// </summary>
        public string? Jwt { get; set; }

        /// <summary>
        /// Gets or sets the access token.
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>
        /// Gets or sets the refresh token.
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Gets or sets the parse error.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets whether the parsed options can authenticate to Archidekt.
        /// </summary>
        public bool HasUsableCredential =>
            !string.IsNullOrWhiteSpace(Jwt)
            || !string.IsNullOrWhiteSpace(AccessToken)
            || !string.IsNullOrWhiteSpace(RefreshToken)
            || (
                !string.IsNullOrWhiteSpace(Password)
                && (
                    !string.IsNullOrWhiteSpace(Email)
                    || !string.IsNullOrWhiteSpace(Username)
                )
            );

        /// <summary>
        /// Gets the names of fields that will be stored.
        /// </summary>
        public IReadOnlyList<string> StoredFieldNames
        {
            get
            {
                List<string> fields = [];
                AddField(fields, "jwt", Jwt);
                AddField(fields, "accessToken", AccessToken);
                AddField(fields, "refreshToken", RefreshToken);
                AddField(fields, "userId", UserId);
                AddField(fields, "email", Email);
                AddField(fields, "username", Username);
                AddField(fields, "password", Password);
                return fields;
            }
        }

        /// <summary>
        /// Adds a populated field name.
        /// </summary>
        private static void AddField(List<string> fields, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields.Add(name);
            }
        }
    }
}
