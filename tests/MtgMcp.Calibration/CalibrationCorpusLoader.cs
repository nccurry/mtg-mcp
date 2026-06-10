using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Calibration;

/// <summary>
/// Loads checked-in offline benchmark corpus files for Stats Lab calibration.
/// </summary>
internal static class CalibrationCorpusLoader
{
    /// <summary>
    /// Gets the default benchmark corpus directory from the build output or source tree.
    /// </summary>
    public static string DefaultCorpusPath => ResolveDefaultCorpusPath();

    /// <summary>
    /// Loads all benchmark fixtures and expectations from a file or directory.
    /// </summary>
    public static CalibrationCorpusLoadResult Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A corpus path is required.", nameof(path));
        }

        List<string> files = ResolveCorpusFiles(path);
        CalibrationCorpusLoadResult result = new();
        HashSet<string> fixtureIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectationIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            CalibrationCorpusDocument document = ReadDocument(file);
            foreach (CalibrationCorpusFixture fixture in document.Fixtures)
            {
                CalibrationFixture mapped = MapFixture(file, fixture);
                if (!fixtureIds.Add(mapped.FixtureId))
                {
                    throw new InvalidOperationException($"Corpus fixture '{mapped.FixtureId}' is defined more than once.");
                }

                result.Fixtures.Add(mapped);
            }

            foreach (CalibrationExpectation expectation in document.Expectations)
            {
                NormalizeExpectation(expectation);
                ValidateExpectationShape(file, expectation);
                if (!expectationIds.Add(expectation.ExpectationId))
                {
                    throw new InvalidOperationException($"Corpus expectation '{expectation.ExpectationId}' is defined more than once.");
                }

                result.Expectations.Add(expectation);
            }
        }

        ValidateExpectationReferences(result);
        return result;
    }

    /// <summary>
    /// Resolves a corpus path into sorted JSON file paths.
    /// </summary>
    private static List<string> ResolveCorpusFiles(string path)
    {
        string fullPath = Path.GetFullPath(path);
        List<string> files = [];
        if (File.Exists(fullPath))
        {
            files.Add(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            files.AddRange(Directory.GetFiles(fullPath, "*.json"));
            files.Sort(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            throw new InvalidOperationException($"Calibration corpus path '{path}' was not found.");
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"Calibration corpus path '{path}' did not contain any JSON files.");
        }

        return files;
    }

    /// <summary>
    /// Finds the checked-in corpus when tests execute from a separate output directory.
    /// </summary>
    private static string ResolveDefaultCorpusPath()
    {
        string outputCorpus = Path.Combine(AppContext.BaseDirectory, "Corpus");
        if (Directory.Exists(outputCorpus))
        {
            return outputCorpus;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string sourceCorpus = Path.Combine(directory.FullName, "tests", "MtgMcp.Calibration", "Corpus");
            if (Directory.Exists(sourceCorpus))
            {
                return sourceCorpus;
            }

            directory = directory.Parent;
        }

        return outputCorpus;
    }

    /// <summary>
    /// Reads one corpus document.
    /// </summary>
    private static CalibrationCorpusDocument ReadDocument(string file)
    {
        try
        {
            string json = File.ReadAllText(file);
            CalibrationCorpusDocument document = JsonSerializer.Deserialize<CalibrationCorpusDocument>(
                json,
                StatsLabCalibrationReportWriter.JsonOptions)
                ?? throw new InvalidOperationException($"Corpus file '{file}' did not contain a document.");
            if (document.SchemaVersion is not 1 and not 2)
            {
                throw new InvalidOperationException(
                    $"Corpus file '{file}' uses unsupported schemaVersion {document.SchemaVersion}; supported versions are 1 and 2.");
            }

            if (document.Fixtures is null)
            {
                throw new InvalidOperationException($"Corpus file '{file}' is missing fixtures.");
            }

            if (document.Expectations is null)
            {
                throw new InvalidOperationException($"Corpus file '{file}' is missing expectations.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Corpus file '{file}' is not valid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Maps and validates one persisted fixture.
    /// </summary>
    private static CalibrationFixture MapFixture(string file, CalibrationCorpusFixture fixture)
    {
        ValidateFixtureShape(file, fixture);
        fixture.Workspace.Id = fixture.FixtureId;
        fixture.Workspace.Name = fixture.Name;
        return new CalibrationFixture
        {
            FixtureId = fixture.FixtureId,
            Name = fixture.Name,
            Label = fixture.Label,
            GroupId = fixture.GroupId,
            Profile = fixture.Profile,
            SourceNote = fixture.SourceNote,
            SourceKind = fixture.SourceKind,
            SourceUri = fixture.SourceUri,
            CapturedAt = fixture.CapturedAt,
            Workspace = fixture.Workspace,
        };
    }

    /// <summary>
    /// Validates one fixture before analysis.
    /// </summary>
    private static void ValidateFixtureShape(string file, CalibrationCorpusFixture fixture)
    {
        if (string.IsNullOrWhiteSpace(fixture.FixtureId))
        {
            throw new InvalidOperationException($"Corpus file '{file}' contains a fixture without fixtureId.");
        }

        if (string.IsNullOrWhiteSpace(fixture.Name))
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing name.");
        }

        if (string.IsNullOrWhiteSpace(fixture.Label))
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing label.");
        }

        if (string.IsNullOrWhiteSpace(fixture.GroupId))
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing groupId.");
        }

        if (string.IsNullOrWhiteSpace(fixture.Profile))
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing profile.");
        }

        if (string.IsNullOrWhiteSpace(fixture.SourceKind))
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing sourceKind.");
        }

        if (fixture.Workspace is null)
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing workspace.");
        }

        if (fixture.Workspace.Cards.Count == 0)
        {
            throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' workspace has no cards.");
        }

        if (!fixture.SourceKind.Equals("synthetic", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(fixture.SourceUri))
            {
                throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing sourceUri.");
            }

            if (string.IsNullOrWhiteSpace(fixture.CapturedAt))
            {
                throw new InvalidOperationException($"Corpus fixture '{fixture.FixtureId}' is missing capturedAt.");
            }
        }
    }

    /// <summary>
    /// Validates one expectation's required fields.
    /// </summary>
    private static void ValidateExpectationShape(string file, CalibrationExpectation expectation)
    {
        if (string.IsNullOrWhiteSpace(expectation.ExpectationId))
        {
            throw new InvalidOperationException($"Corpus file '{file}' contains an expectation without expectationId.");
        }

        if (string.IsNullOrWhiteSpace(expectation.GroupId))
        {
            throw new InvalidOperationException($"Corpus expectation '{expectation.ExpectationId}' is missing groupId.");
        }

        if (!expectation.Severity.Equals(CalibrationExpectationSeverity.Required, StringComparison.OrdinalIgnoreCase)
            && !expectation.Severity.Equals(CalibrationExpectationSeverity.Advisory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' severity must be 'required' or 'advisory'.");
        }

        foreach (string tag in expectation.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidOperationException(
                    $"Corpus expectation '{expectation.ExpectationId}' contains an empty tag.");
            }
        }

        if (expectation.Kind.Equals(CalibrationExpectationKind.Pairwise, StringComparison.OrdinalIgnoreCase))
        {
            ValidatePairwiseExpectationShape(expectation);
        }
        else if (expectation.Kind.Equals(CalibrationExpectationKind.Pressure, StringComparison.OrdinalIgnoreCase))
        {
            ValidatePressureExpectationShape(expectation);
        }
        else
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' kind must be 'pairwise' or 'pressure'.");
        }
    }

    /// <summary>
    /// Validates required fields for a pairwise expectation.
    /// </summary>
    private static void ValidatePairwiseExpectationShape(CalibrationExpectation expectation)
    {
        if (string.IsNullOrWhiteSpace(expectation.Metric))
        {
            throw new InvalidOperationException($"Corpus expectation '{expectation.ExpectationId}' is missing metric.");
        }

        if (string.IsNullOrWhiteSpace(expectation.PreferredFixtureId)
            || string.IsNullOrWhiteSpace(expectation.OtherFixtureId))
        {
            throw new InvalidOperationException($"Corpus expectation '{expectation.ExpectationId}' is missing fixture ids.");
        }

        if (expectation.PreferredFixtureId.Equals(expectation.OtherFixtureId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' must compare two different fixtures.");
        }

        if (!expectation.Direction.Equals("higher", StringComparison.OrdinalIgnoreCase)
            && !expectation.Direction.Equals("lower", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' direction must be 'higher' or 'lower'.");
        }

        if (expectation.MinimumDelta < 0)
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' minimumDelta must be nonnegative.");
        }
    }

    /// <summary>
    /// Validates required fields for a pressure diagnostic expectation.
    /// </summary>
    private static void ValidatePressureExpectationShape(CalibrationExpectation expectation)
    {
        if (string.IsNullOrWhiteSpace(expectation.TargetFixtureId))
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' is missing targetFixtureId.");
        }

        if (string.IsNullOrWhiteSpace(expectation.PressureSourceFixtureId))
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' is missing pressureSourceFixtureId.");
        }

        if (string.IsNullOrWhiteSpace(expectation.PressureProfileId))
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' is missing pressureProfileId.");
        }

        if (expectation.Threshold is <= 0 or > 1)
        {
            throw new InvalidOperationException(
                $"Corpus expectation '{expectation.ExpectationId}' threshold must be greater than 0 and at most 1.");
        }
    }

    /// <summary>
    /// Applies schema-compatible defaults before validation.
    /// </summary>
    private static void NormalizeExpectation(CalibrationExpectation expectation)
    {
        if (string.IsNullOrWhiteSpace(expectation.Kind))
        {
            expectation.Kind = CalibrationExpectationKind.Pairwise;
        }

        expectation.Kind = expectation.Kind.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(expectation.Severity))
        {
            expectation.Severity = CalibrationExpectationSeverity.Required;
        }

        expectation.Severity = expectation.Severity.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(expectation.Metric)
            && expectation.Kind.Equals(CalibrationExpectationKind.Pressure, StringComparison.OrdinalIgnoreCase))
        {
            expectation.Metric = "pressure:composite";
        }

        expectation.Tags ??= [];
        for (int index = 0; index < expectation.Tags.Count; index++)
        {
            expectation.Tags[index] = expectation.Tags[index].Trim();
        }
    }

    /// <summary>
    /// Ensures every loaded expectation points at loaded fixtures.
    /// </summary>
    private static void ValidateExpectationReferences(CalibrationCorpusLoadResult result)
    {
        HashSet<string> fixtureIds = new(result.Fixtures.Select(fixture => fixture.FixtureId), StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationExpectation expectation in result.Expectations)
        {
            if (expectation.Kind.Equals(CalibrationExpectationKind.Pressure, StringComparison.OrdinalIgnoreCase))
            {
                if (!fixtureIds.Contains(expectation.TargetFixtureId))
                {
                    throw new InvalidOperationException(
                        $"Corpus expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.TargetFixtureId}'.");
                }

                if (!fixtureIds.Contains(expectation.PressureSourceFixtureId))
                {
                    throw new InvalidOperationException(
                        $"Corpus expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.PressureSourceFixtureId}'.");
                }

                continue;
            }

            if (!fixtureIds.Contains(expectation.PreferredFixtureId))
            {
                throw new InvalidOperationException(
                    $"Corpus expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.PreferredFixtureId}'.");
            }

            if (!fixtureIds.Contains(expectation.OtherFixtureId))
            {
                throw new InvalidOperationException(
                    $"Corpus expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.OtherFixtureId}'.");
            }
        }
    }
}

/// <summary>
/// Stores fixtures and expectations loaded from offline corpus files.
/// </summary>
internal sealed class CalibrationCorpusLoadResult
{
    /// <summary>
    /// Gets loaded fixtures.
    /// </summary>
    public List<CalibrationFixture> Fixtures { get; } = [];

    /// <summary>
    /// Gets loaded pairwise expectations.
    /// </summary>
    public List<CalibrationExpectation> Expectations { get; } = [];
}

/// <summary>
/// Describes one checked-in offline corpus file.
/// </summary>
internal sealed class CalibrationCorpusDocument
{
    /// <summary>
    /// Gets or sets the corpus document schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets benchmark fixtures.
    /// </summary>
    public List<CalibrationCorpusFixture> Fixtures { get; set; } = [];

    /// <summary>
    /// Gets or sets expected pairwise relationships.
    /// </summary>
    public List<CalibrationExpectation> Expectations { get; set; } = [];
}

/// <summary>
/// Describes one persisted fixture with a serialized workspace snapshot.
/// </summary>
internal sealed class CalibrationCorpusFixture
{
    /// <summary>
    /// Gets or sets the stable fixture id.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the advisory benchmark label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation profile id.
    /// </summary>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Gets or sets the source kind.
    /// </summary>
    public string SourceKind { get; set; } = "";

    /// <summary>
    /// Gets or sets the source URI.
    /// </summary>
    public string SourceUri { get; set; } = "";

    /// <summary>
    /// Gets or sets the snapshot capture date.
    /// </summary>
    public string CapturedAt { get; set; } = "";

    /// <summary>
    /// Gets or sets source notes.
    /// </summary>
    public string SourceNote { get; set; } = "";

    /// <summary>
    /// Gets or sets the serialized deck workspace.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();
}
