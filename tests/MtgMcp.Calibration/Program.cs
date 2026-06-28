namespace MtgMcp.Calibration;

/// <summary>
/// Console entry point for offline Stats Lab calibration.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the calibration corpus and writes report artifacts.
    /// </summary>
    public static int Main(string[] args)
    {
        try
        {
            CalibrationOptions options = CalibrationOptions.Parse(args);
            StatsLabCalibrationRunner runner = new();
            if (options.ValidateOnly)
            {
                CalibrationCorpusValidationResult validation = runner.Validate(options);
                Console.WriteLine(
                    "Stats Lab calibration corpus validation passed: "
                    + $"{validation.FixtureCount} fixtures, "
                    + $"{validation.ExpectationCount} expectations "
                    + $"({validation.RequiredExpectationCount} required, {validation.AdvisoryExpectationCount} advisory).");
                return 0;
            }

            StatsLabCalibrationReport report = runner.Run(options);
            StatsLabCalibrationReportWriter.Write(report, options.OutputDirectory);
            Console.WriteLine($"Stats Lab calibration report written to {options.OutputDirectory}");
            Console.WriteLine(
                $"{report.Summary.PassedRequiredExpectations}/{report.Summary.RequiredExpectationCount} required expectations passed; "
                + $"{report.Summary.PassedAdvisoryExpectations}/{report.Summary.AdvisoryExpectationCount} advisory expectations passed; "
                + $"{report.Summary.PressureDiagnosticCount} pressure diagnostics; "
                + $"{report.Summary.BracketDiagnosticCount} bracket diagnostics; "
                + $"{report.Summary.DriftFailures} drift failures.");
            if (options.AllowFailures || (report.Summary.FailedRequiredExpectations == 0 && report.Summary.DriftFailures == 0))
            {
                return 0;
            }

            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }
}
