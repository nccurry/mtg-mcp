using System.Globalization;

namespace MtgMcp.Calibration;

/// <summary>
/// Configures one offline Stats Lab calibration run.
/// </summary>
public sealed class CalibrationOptions
{
    /// <summary>
    /// Gets the default generated report directory.
    /// </summary>
    public const string DefaultOutputDirectory = "artifacts/stats-lab-calibration";

    /// <summary>
    /// Gets or sets the report output directory.
    /// </summary>
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    /// <summary>
    /// Gets or sets an optional saved baseline JSON path for drift comparison.
    /// </summary>
    public string? BaselinePath { get; set; }

    /// <summary>
    /// Gets or sets an optional benchmark corpus file or directory.
    /// </summary>
    public string? CorpusPath { get; set; }

    /// <summary>
    /// Gets or sets the Monte Carlo run count for each fixture.
    /// </summary>
    public int Simulations { get; set; } = 5_000;

    /// <summary>
    /// Gets or sets the final simulated turn.
    /// </summary>
    public int MaxTurn { get; set; } = 8;

    /// <summary>
    /// Gets or sets the shared deterministic seed.
    /// </summary>
    public int Seed { get; set; } = 2026;

    /// <summary>
    /// Gets or sets whether London mulligan heuristics are applied.
    /// </summary>
    public bool IncludeMulligans { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the process should exit zero when expectations or drift checks fail.
    /// </summary>
    public bool AllowFailures { get; set; }

    /// <summary>
    /// Gets or sets whether only built-in synthetic fixtures should run.
    /// </summary>
    public bool SyntheticOnly { get; set; }

    /// <summary>
    /// Gets or sets whether the command should validate corpus shape without running simulations.
    /// </summary>
    public bool ValidateOnly { get; set; }

    /// <summary>
    /// Gets profile ids to run as non-failing sensitivity sweeps.
    /// </summary>
    public List<string> ProfileSweepIds { get; } = [];

    /// <summary>
    /// Parses command-line options for the calibration runner.
    /// </summary>
    public static CalibrationOptions Parse(string[] args)
    {
        CalibrationOptions options = new();
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg.Equals("--allow-failures", StringComparison.OrdinalIgnoreCase))
            {
                options.AllowFailures = true;
                continue;
            }

            if (arg.Equals("--no-mulligans", StringComparison.OrdinalIgnoreCase))
            {
                options.IncludeMulligans = false;
                continue;
            }

            if (arg.Equals("--synthetic-only", StringComparison.OrdinalIgnoreCase))
            {
                options.SyntheticOnly = true;
                continue;
            }

            if (arg.Equals("--validate-only", StringComparison.OrdinalIgnoreCase))
            {
                options.ValidateOnly = true;
                continue;
            }

            string value = NextValue(args, ref index, arg);
            if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                options.OutputDirectory = value;
            }
            else if (arg.Equals("--baseline", StringComparison.OrdinalIgnoreCase))
            {
                options.BaselinePath = value;
            }
            else if (arg.Equals("--corpus", StringComparison.OrdinalIgnoreCase))
            {
                options.CorpusPath = value;
            }
            else if (arg.Equals("--simulations", StringComparison.OrdinalIgnoreCase))
            {
                options.Simulations = PositiveInt(value, arg);
            }
            else if (arg.Equals("--max-turn", StringComparison.OrdinalIgnoreCase))
            {
                options.MaxTurn = PositiveInt(value, arg);
            }
            else if (arg.Equals("--seed", StringComparison.OrdinalIgnoreCase))
            {
                options.Seed = ParseInt(value, arg);
            }
            else if (arg.Equals("--profile-sweep", StringComparison.OrdinalIgnoreCase))
            {
                options.ProfileSweepIds.AddRange(ParseProfileSweepIds(value));
            }
            else
            {
                throw new ArgumentException($"Unknown calibration option '{arg}'.");
            }
        }

        if (options.SyntheticOnly && !string.IsNullOrWhiteSpace(options.CorpusPath))
        {
            throw new ArgumentException("Options '--synthetic-only' and '--corpus' cannot be combined.");
        }

        return options;
    }

    /// <summary>
    /// Reads the value following an option name.
    /// </summary>
    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        index++;
        return args[index];
    }

    /// <summary>
    /// Parses a positive integer option.
    /// </summary>
    private static int PositiveInt(string value, string option)
    {
        int parsed = ParseInt(value, option);
        if (parsed <= 0)
        {
            throw new ArgumentOutOfRangeException(option, $"{option} must be positive.");
        }

        return parsed;
    }

    /// <summary>
    /// Parses an integer option with a calibration-specific error message.
    /// </summary>
    private static int ParseInt(string value, string option)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Option '{option}' requires an integer value.");
    }

    /// <summary>
    /// Parses comma-separated profile ids for profile-sweep diagnostics.
    /// </summary>
    private static List<string> ParseProfileSweepIds(string value)
    {
        List<string> profileIds = [];
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            if (!profileIds.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                profileIds.Add(part);
            }
        }

        if (profileIds.Count == 0)
        {
            throw new ArgumentException("Option '--profile-sweep' requires at least one profile id.");
        }

        return profileIds;
    }
}
