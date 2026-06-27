using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Playgroup;

namespace MtgMcp.App;

/// <summary>
/// Writes local Playgroup.gg credentials for MCP client configuration.
/// </summary>
public static class PlaygroupAuthCommand
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
    /// Determines whether the arguments target the Playgroup auth helper.
    /// </summary>
    public static bool IsCommand(IReadOnlyList<string> args)
    {
        return args.Count >= 2
            && args[0].Equals("auth", StringComparison.OrdinalIgnoreCase)
            && args[1].Equals("playgroup", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs the Playgroup auth helper.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        return Parse(args) switch
        {
            PlaygroupAuthOptions options => RunWithOptions(options, output, error),
            PlaygroupAuthHelp => WriteHelp(output),
            PlaygroupAuthParseError parseError => WriteParseError(parseError, error),
            null => WriteParseError(
                new PlaygroupAuthParseError("Unable to parse Playgroup auth arguments."),
                error
            ),
        };
    }

    /// <summary>
    /// Writes credentials after arguments have parsed into usable options.
    /// </summary>
    private static int RunWithOptions(PlaygroupAuthOptions options, TextWriter output, TextWriter error)
    {
        if (!TryGetCredentialsFilePath(options, error, out string credentialsFile))
        {
            return 1;
        }

        if (File.Exists(credentialsFile) && !options.Force)
        {
            error.WriteLine(
                $"Playgroup credentials file '{credentialsFile}' already exists. "
                    + "Use --force to overwrite it."
            );
            return 1;
        }

        PlaygroupCredentials credentials = new()
        {
            ApiKey = options.ApiKey,
        };
        string json = JsonSerializer.Serialize(credentials, JsonOptions) + Environment.NewLine;
        if (!TryWriteCredentialsFile(credentialsFile, json, error))
        {
            return 1;
        }

        bool isDefaultLocation = string.Equals(
            credentialsFile,
            Path.GetFullPath(GetDefaultCredentialsFile()),
            StringComparison.OrdinalIgnoreCase
        );
        WriteSuccess(output, credentialsFile, options.StoredFieldNames, isDefaultLocation);
        return 0;
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
    private static int WriteParseError(PlaygroupAuthParseError parseError, TextWriter error)
    {
        error.WriteLine(parseError.Message);
        error.WriteLine();
        WriteUsage(error);
        return 1;
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

        return Path.Combine(home, ".mtg-mcp", "playgroup.json");
    }

    /// <summary>
    /// Gets the normalized credentials file path.
    /// </summary>
    private static bool TryGetCredentialsFilePath(
        PlaygroupAuthOptions options,
        TextWriter error,
        out string credentialsFile
    )
    {
        try
        {
            credentialsFile = Path.GetFullPath(
                options.CredentialsFile ?? GetDefaultCredentialsFile()
            );
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            credentialsFile = "";
            error.WriteLine($"Playgroup credentials path is invalid: {exception.Message}");
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
                $"Playgroup credentials file '{credentialsFile}' could not be written: "
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
    private static PlaygroupAuthParseResult Parse(IReadOnlyList<string> args)
    {
        PlaygroupAuthOptions options = new();
        for (int index = 2; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument is "--help" or "-h")
            {
                return new PlaygroupAuthHelp();
            }

            if (argument.Equals("--force", StringComparison.OrdinalIgnoreCase))
            {
                options.Force = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                return new PlaygroupAuthParseError($"Unexpected argument '{argument}'.");
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
                    return new PlaygroupAuthParseError($"Option '--{option}' requires a value.");
                }

                value = args[++index];
            }

            string? applyError = ApplyOption(options, option, value);
            if (!string.IsNullOrWhiteSpace(applyError))
            {
                return new PlaygroupAuthParseError(applyError);
            }
        }

        return options.HasUsableCredential
            ? options
            : new PlaygroupAuthParseError("Provide --api-key.");
    }

    /// <summary>
    /// Applies one parsed option.
    /// </summary>
    private static string? ApplyOption(PlaygroupAuthOptions options, string option, string value)
    {
        string normalized = option.Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        switch (normalized)
        {
            case "credentials-file":
            case "file":
                options.CredentialsFile = value;
                return null;
            case "api-key":
                options.ApiKey = EmptyToNull(value);
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
        IReadOnlyList<string> storedFieldNames,
        bool isDefaultLocation
    )
    {
        output.WriteLine($"Playgroup credentials file written: {credentialsFile}");
        output.WriteLine($"Stored credential fields: {string.Join(", ", storedFieldNames)}");
        output.WriteLine();
        if (isDefaultLocation)
        {
            output.WriteLine(
                "mtg-mcp reads this default location automatically; no MCP client "
                    + "configuration is needed. Restart the server to pick up the credentials."
            );
            return;
        }

        output.WriteLine(
            "This is a custom location. Point the server at it with this MCP server "
                + "configuration entry:"
        );
        output.WriteLine("\"env\": {");
        output.WriteLine(
            "  \"MTGMCP__PLAYGROUP__CREDENTIALS_FILE\": "
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
            "  mtg-mcp auth playgroup [--credentials-file <path>] "
                + "--api-key <key> [--force]"
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
    /// Represents the closed set of outcomes from parsing auth helper arguments.
    /// </summary>
    private readonly union PlaygroupAuthParseResult(
        PlaygroupAuthOptions,
        PlaygroupAuthHelp,
        PlaygroupAuthParseError
    );

    /// <summary>
    /// Indicates that the caller requested command usage.
    /// </summary>
    private sealed record PlaygroupAuthHelp;

    /// <summary>
    /// Carries a parse or validation error that should be shown with usage.
    /// </summary>
    private sealed record PlaygroupAuthParseError(string Message);

    /// <summary>
    /// Holds parsed command arguments that are ready to write.
    /// </summary>
    private sealed class PlaygroupAuthOptions
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
        /// Gets or sets the Playgroup API key used for authenticated requests.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Gets whether the parsed options can authenticate to Playgroup.
        /// </summary>
        public bool HasUsableCredential => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>
        /// Gets the names of fields that will be stored.
        /// </summary>
        public IReadOnlyList<string> StoredFieldNames
        {
            get
            {
                List<string> fields = [];
                AddField(fields, "apiKey", ApiKey);
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
