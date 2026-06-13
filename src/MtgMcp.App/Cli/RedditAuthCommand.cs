using System.Text.Json;
using MtgMcp.Decklists;

namespace MtgMcp.App;

/// <summary>
/// Writes local Reddit OAuth credentials for MCP client configuration.
/// </summary>
public static class RedditAuthCommand
{
    /// <summary>
    /// Determines whether the arguments target the Reddit auth helper.
    /// </summary>
    public static bool IsCommand(IReadOnlyList<string> args)
    {
        return args.Count >= 2
            && args[0].Equals("auth", StringComparison.OrdinalIgnoreCase)
            && args[1].Equals("reddit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs the Reddit auth helper.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        return Parse(args) switch
        {
            RedditAuthOptions options => RunWithOptions(options, output, error),
            RedditAuthHelp => WriteHelp(output),
            RedditAuthParseError parseError => WriteParseError(parseError, error),
            null => WriteParseError(
                new RedditAuthParseError("Unable to parse Reddit auth arguments."),
                error),
        };
    }

    /// <summary>
    /// Writes credentials after arguments have parsed into usable options.
    /// </summary>
    private static int RunWithOptions(RedditAuthOptions options, TextWriter output, TextWriter error)
    {
        if (!TryGetCredentialsFilePath(options, error, out string credentialsFile))
        {
            return 1;
        }

        if (File.Exists(credentialsFile) && !options.Force)
        {
            error.WriteLine(
                $"Reddit credentials file '{credentialsFile}' already exists. "
                    + "Use --force to overwrite it.");
            return 1;
        }

        RedditCredentials credentials = new()
        {
            ClientId = options.ClientId,
            ClientSecret = options.ClientSecret,
            RefreshToken = options.RefreshToken,
            AccessToken = options.AccessToken,
            BearerToken = options.BearerToken,
            ExpiresAtUtc = options.ExpiresAtUtc,
            UserAgent = options.UserAgent,
            Scope = options.Scope,
            DeviceId = options.DeviceId,
        };
        string json = JsonSerializer.Serialize(credentials, RedditCredentialsFile.JsonOptions)
            + Environment.NewLine;
        if (!TryWriteCredentialsFile(credentialsFile, json, error))
        {
            return 1;
        }

        WriteSuccess(output, credentialsFile, options.StoredFieldNames);
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

        return Path.Combine(home, ".mtg-mcp", "reddit.json");
    }

    /// <summary>
    /// Writes help output and reports success.
    /// </summary>
    private static int WriteHelp(TextWriter output)
    {
        WriteUsage(output);
        return 0;
    }

    /// <summary>
    /// Writes a parse error and reports failure.
    /// </summary>
    private static int WriteParseError(RedditAuthParseError parseError, TextWriter error)
    {
        error.WriteLine(parseError.Message);
        error.WriteLine();
        WriteUsage(error);
        return 1;
    }

    /// <summary>
    /// Gets the normalized credentials file path.
    /// </summary>
    private static bool TryGetCredentialsFilePath(
        RedditAuthOptions options,
        TextWriter error,
        out string credentialsFile)
    {
        try
        {
            credentialsFile = Path.GetFullPath(
                options.CredentialsFile ?? GetDefaultCredentialsFile());
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            credentialsFile = "";
            error.WriteLine($"Reddit credentials path is invalid: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes credentials with restrictive local permissions where supported.
    /// </summary>
    private static bool TryWriteCredentialsFile(
        string credentialsFile,
        string json,
        TextWriter error)
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
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            error.WriteLine(
                $"Reddit credentials file '{credentialsFile}' could not be written: "
                    + exception.Message);
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
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Parses command options.
    /// </summary>
    private static RedditAuthParseResult Parse(IReadOnlyList<string> args)
    {
        RedditAuthOptions options = new() { Scope = "read" };
        for (int index = 2; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument is "--help" or "-h")
            {
                return new RedditAuthHelp();
            }

            if (argument.Equals("--force", StringComparison.OrdinalIgnoreCase))
            {
                options.Force = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                return new RedditAuthParseError($"Unexpected argument '{argument}'.");
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
                    return new RedditAuthParseError($"Option '--{option}' requires a value.");
                }

                value = args[++index];
            }

            string? applyError = ApplyOption(options, option, value);
            if (!string.IsNullOrWhiteSpace(applyError))
            {
                return new RedditAuthParseError(applyError);
            }
        }

        return options.HasUsableCredential
            ? options
            : new RedditAuthParseError(
                "Provide --access-token/--bearer-token, or provide --client-id with --refresh-token, --client-secret, or --device-id.");
    }

    /// <summary>
    /// Applies one parsed option.
    /// </summary>
    private static string? ApplyOption(RedditAuthOptions options, string option, string value)
    {
        string normalized = option.Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        switch (normalized)
        {
            case "credentials-file":
            case "file":
                options.CredentialsFile = value;
                return null;
            case "client-id":
                options.ClientId = EmptyToNull(value);
                return null;
            case "client-secret":
                options.ClientSecret = EmptyToNull(value);
                return null;
            case "refresh-token":
                options.RefreshToken = EmptyToNull(value);
                return null;
            case "access-token":
                options.AccessToken = EmptyToNull(value);
                return null;
            case "bearer-token":
            case "token":
                options.BearerToken = EmptyToNull(value);
                return null;
            case "expires-at-utc":
            case "expires-at":
                if (DateTimeOffset.TryParse(value, out DateTimeOffset expiresAtUtc))
                {
                    options.ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
                    return null;
                }

                return $"Option '--{option}' must be a valid UTC timestamp.";
            case "user-agent":
                options.UserAgent = EmptyToNull(value);
                return null;
            case "scope":
                options.Scope = EmptyToNull(value) ?? "read";
                return null;
            case "device-id":
                options.DeviceId = EmptyToNull(value);
                return null;
            default:
                return $"Unknown option '--{option}'.";
        }
    }

    /// <summary>
    /// Writes successful setup guidance without echoing secrets.
    /// </summary>
    private static void WriteSuccess(
        TextWriter output,
        string credentialsFile,
        IReadOnlyList<string> storedFieldNames)
    {
        output.WriteLine($"Reddit credentials file written: {credentialsFile}");
        output.WriteLine($"Stored credential fields: {string.Join(", ", storedFieldNames)}");
        output.WriteLine();
        output.WriteLine("Add this environment variable to your MCP server configuration:");
        output.WriteLine("\"env\": {");
        output.WriteLine(
            "  \"MTGMCP__REDDIT__CREDENTIALS_FILE\": "
                + JsonSerializer.Serialize(credentialsFile));
        output.WriteLine("}");
    }

    /// <summary>
    /// Writes command usage.
    /// </summary>
    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine(
            "  mtg-mcp auth reddit [--credentials-file <path>] "
                + "[--client-id <id>] [--client-secret <secret>] [--refresh-token <token>] "
                + "[--access-token <token>|--bearer-token <token>] [--expires-at-utc <timestamp>] "
                + "[--user-agent <agent>] [--scope <scope>] [--device-id <id>] [--force]");
    }

    /// <summary>
    /// Converts blank input to null.
    /// </summary>
    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Represents the closed set of outcomes from parsing Reddit auth helper arguments.
    /// </summary>
    private readonly union RedditAuthParseResult(
        RedditAuthOptions,
        RedditAuthHelp,
        RedditAuthParseError
    );

    /// <summary>
    /// Indicates that the caller requested command usage.
    /// </summary>
    private sealed record RedditAuthHelp;

    /// <summary>
    /// Carries a parse or validation error that should be shown with usage.
    /// </summary>
    private sealed record RedditAuthParseError(string Message);

    /// <summary>
    /// Holds parsed command arguments that are ready to write.
    /// </summary>
    private sealed class RedditAuthOptions
    {
        /// <summary>
        /// Gets or sets whether to overwrite an existing file.
        /// </summary>
        public bool Force { get; set; }

        /// <summary>
        /// Gets or sets the credentials file.
        /// </summary>
        public string? CredentialsFile { get; set; }

        /// <summary>
        /// Gets or sets the Reddit app client id.
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Gets or sets the Reddit app client secret.
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        /// Gets or sets a Reddit refresh token.
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// Gets or sets a temporary Reddit access token.
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>
        /// Gets or sets a temporary Reddit bearer token alias.
        /// </summary>
        public string? BearerToken { get; set; }

        /// <summary>
        /// Gets or sets the temporary token expiration timestamp.
        /// </summary>
        public DateTimeOffset? ExpiresAtUtc { get; set; }

        /// <summary>
        /// Gets or sets the Reddit user agent.
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Gets or sets the Reddit OAuth scope.
        /// </summary>
        public string? Scope { get; set; }

        /// <summary>
        /// Gets or sets the installed-client device id.
        /// </summary>
        public string? DeviceId { get; set; }

        /// <summary>
        /// Gets whether the parsed options can authenticate to Reddit OAuth.
        /// </summary>
        public bool HasUsableCredential =>
            !string.IsNullOrWhiteSpace(AccessToken)
            || !string.IsNullOrWhiteSpace(BearerToken)
            || !string.IsNullOrWhiteSpace(ClientId)
            && (!string.IsNullOrWhiteSpace(RefreshToken)
                || !string.IsNullOrWhiteSpace(ClientSecret)
                || !string.IsNullOrWhiteSpace(DeviceId));

        /// <summary>
        /// Gets the names of fields that will be stored.
        /// </summary>
        public IReadOnlyList<string> StoredFieldNames
        {
            get
            {
                List<string> fields = [];
                AddField(fields, "clientId", ClientId);
                AddField(fields, "clientSecret", ClientSecret);
                AddField(fields, "refreshToken", RefreshToken);
                AddField(fields, "accessToken", AccessToken);
                AddField(fields, "bearerToken", BearerToken);
                AddField(fields, "expiresAtUtc", ExpiresAtUtc?.ToString("O"));
                AddField(fields, "userAgent", UserAgent);
                AddField(fields, "scope", Scope);
                AddField(fields, "deviceId", DeviceId);
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
