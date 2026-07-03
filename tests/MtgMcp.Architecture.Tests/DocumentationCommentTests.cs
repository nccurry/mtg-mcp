using FluentAssertions;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Protects the usefulness of declaration documentation after analyzers verify its presence.
/// </summary>
public sealed class DocumentationCommentTests
{
    /// <summary>
    /// Rejects XML summaries that match known generated or name-restating phrases.
    /// </summary>
    [Fact]
    public void SourceSummaries_AvoidGeneratedPhrases()
    {
        string root = FindRepoRoot();
        List<string> weak = [];

        foreach (string filePath in EnumerateSourceFiles(root))
        {
            string relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            foreach ((int lineNumber, string summary) in EnumerateSummaries(filePath))
            {
                if (IsLowSignalSummary(summary))
                {
                    weak.Add($"{relativePath}:{lineNumber}: {summary}");
                }
            }
        }

        weak.Should().BeEmpty("documentation summaries should avoid empty generated phrases");
    }

    /// <summary>
    /// Verifies that common generated summary shapes remain classified as low signal.
    /// </summary>
    [Theory]
    [InlineData("Handles send json.")]
    [InlineData("Handles start deck workspace.")]
    [InlineData("Gets the string.")]
    [InlineData("Gets the card.")]
    [InlineData("Maps the card.")]
    public void LowSignalSummarySamples_AreRejected(string summary)
    {
        IsLowSignalSummary(summary).Should().BeTrue();
    }

    /// <summary>
    /// Enumerates C# source files whose summaries are quality checked.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        string[] roots = [Path.Combine(root, "src"), Path.Combine(root, "tests")];

        foreach (string sourceRoot in roots)
        {
            if (!Directory.Exists(sourceRoot))
            {
                continue;
            }

            foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsBuildArtifact(filePath))
                {
                    yield return filePath;
                }
            }
        }
    }

    /// <summary>
    /// Reads every XML summary block and its first source line from one C# file.
    /// </summary>
    private static IEnumerable<(int LineNumber, string Summary)> EnumerateSummaries(string filePath)
    {
        string[] lines = File.ReadAllLines(filePath);
        for (int index = 0; index < lines.Length; index++)
        {
            int summaryStart = lines[index].IndexOf("<summary>", StringComparison.Ordinal);
            if (summaryStart < 0)
            {
                continue;
            }

            int lineNumber = index + 1;
            List<string> parts = [];
            while (index < lines.Length)
            {
                string text = lines[index].Trim().TrimStart('/').Trim();
                text = text.Replace("<summary>", "", StringComparison.Ordinal);
                int summaryEnd = text.IndexOf("</summary>", StringComparison.Ordinal);
                if (summaryEnd >= 0)
                {
                    text = text[..summaryEnd];
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }

                if (summaryEnd >= 0)
                {
                    break;
                }

                index++;
            }

            yield return (lineNumber, string.Join(' ', parts));
        }
    }

    /// <summary>
    /// Identifies compiler output that must not participate in source-quality checks.
    /// </summary>
    private static bool IsBuildArtifact(string filePath)
    {
        return filePath.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || filePath.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a summary still looks like an empty generated placeholder.
    /// </summary>
    private static bool IsLowSignalSummary(string summary)
    {
        string normalized = summary.Trim().TrimEnd('.').ToLowerInvariant();

        return normalized.Contains("the member", StringComparison.Ordinal)
            || normalized.StartsWith("handles ", StringComparison.Ordinal)
            || normalized.EndsWith(" operation", StringComparison.Ordinal)
            || normalized is "gets the auth status"
                or "gets the bool"
                or "gets the card"
                or "gets the checkpoint"
                or "gets the date"
                or "gets the deck"
                or "gets the deck intent"
                or "gets the deck summary"
                or "gets the double"
                or "gets the effective configuration"
                or "gets the format rules"
                or "gets the int"
                or "gets the json"
                or "gets the long"
                or "gets the operation mode guidance"
                or "gets the plan"
                or "gets the prints"
                or "gets the rulings"
                or "gets the scryfall syntax cheatsheet"
                or "gets the string"
                or "gets the workspace selection guidance"
                or "maps the card"
                or "verifies that get"
                or "verifies that save"
                or "verifies that create gateway";
    }

    /// <summary>
    /// Finds the repository root from the compiled test output path.
    /// </summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "mtg-mcp.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
