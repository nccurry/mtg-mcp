using FluentAssertions;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Contains tests for declaration documentation coverage.
/// </summary>
public sealed class DocumentationCommentTests
{
    /// <summary>
    /// Verifies that source declarations have XML summary comments.
    /// </summary>
    [Fact]
    public void SourceDeclarations_HaveXmlSummaryComments()
    {
        string root = FindRepoRoot();
        List<string> missing = [];
        List<string> weak = [];

        foreach (string filePath in EnumerateSourceFiles(root))
        {
            string relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            string[] lines = File.ReadAllLines(filePath);
            int interfaceDepth = 0;

            for (int index = 0; index < lines.Length; index++)
            {
                bool inInterface = interfaceDepth > 0;

                if (!IsDeclaration(lines[index], inInterface))
                {
                    interfaceDepth = UpdateInterfaceDepth(lines[index], interfaceDepth);
                    continue;
                }

                if (!TryGetXmlSummary(lines, FindAttributeStart(lines, index), out string summary))
                {
                    missing.Add($"{relativePath}:{index + 1}: {lines[index].Trim()}");
                }
                else if (IsLowSignalSummary(summary))
                {
                    weak.Add($"{relativePath}:{index + 1}: {summary}");
                }

                interfaceDepth = UpdateInterfaceDepth(lines[index], interfaceDepth);
            }
        }

        missing
            .Should()
            .BeEmpty("every class, method, field, and property should explain its purpose");
        weak.Should().BeEmpty("documentation summaries should avoid empty generated phrases");
    }

    /// <summary>
    /// Verifies that common generated summary shapes are treated as low-signal comments.
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
    /// Enumerates C# source files that should be checked for documentation.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        string[] roots = [Path.Combine(root, "src"), Path.Combine(root, "tests")];

        return roots
            .Where(Directory.Exists)
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            )
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            );
    }

    /// <summary>
    /// Determines whether a source line declares a member that needs documentation.
    /// </summary>
    private static bool IsDeclaration(string line, bool inInterface)
    {
        string text = line.Trim();

        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (
            text.StartsWith("private set;", StringComparison.Ordinal)
            || text.StartsWith("private init;", StringComparison.Ordinal)
            || text.StartsWith("private get;", StringComparison.Ordinal)
        )
        {
            return false;
        }

        if (StartsWithAccessModifier(text))
        {
            return text.Contains(" class ", StringComparison.Ordinal)
                || text.Contains(" interface ", StringComparison.Ordinal)
                || text.Contains(" record ", StringComparison.Ordinal)
                || text.Contains(" struct ", StringComparison.Ordinal)
                || text.Contains(" enum ", StringComparison.Ordinal)
                || text.Contains('(')
                || text.Contains("{ get", StringComparison.Ordinal)
                || text.EndsWith(';')
                || text.Contains('=');
        }

        return inInterface
            && (text.Contains('(') || text.Contains("{ get", StringComparison.Ordinal))
            && !text.StartsWith('[');
    }

    /// <summary>
    /// Determines whether a line starts with a C# access modifier.
    /// </summary>
    private static bool StartsWithAccessModifier(string text)
    {
        return text.StartsWith("public ", StringComparison.Ordinal)
            || text.StartsWith("private ", StringComparison.Ordinal)
            || text.StartsWith("protected ", StringComparison.Ordinal)
            || text.StartsWith("internal ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds the first attribute line attached to a declaration.
    /// </summary>
    private static int FindAttributeStart(string[] lines, int declarationIndex)
    {
        int insertIndex = declarationIndex;
        int previousIndex = declarationIndex - 1;

        while (previousIndex >= 0)
        {
            string text = lines[previousIndex].Trim();

            if (text.Length == 0 || text.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            if (text.StartsWith('['))
            {
                insertIndex = previousIndex;
                previousIndex--;
                continue;
            }

            if (text.EndsWith(']'))
            {
                int attributeStart = previousIndex;
                while (attributeStart >= 0 && !lines[attributeStart].TrimStart().StartsWith('['))
                {
                    attributeStart--;
                }

                if (attributeStart >= 0)
                {
                    insertIndex = attributeStart;
                    previousIndex = attributeStart - 1;
                    continue;
                }
            }

            break;
        }

        return insertIndex;
    }

    /// <summary>
    /// Reads the XML summary attached to a declaration or its attributes.
    /// </summary>
    private static bool TryGetXmlSummary(string[] lines, int insertIndex, out string summary)
    {
        summary = "";
        int previousIndex = insertIndex - 1;

        while (previousIndex >= 0 && lines[previousIndex].Trim().Length == 0)
        {
            previousIndex--;
        }

        if (previousIndex < 0)
        {
            return false;
        }

        List<string> block = [];
        while (
            previousIndex >= 0
            && lines[previousIndex].TrimStart().StartsWith("///", StringComparison.Ordinal)
        )
        {
            block.Add(lines[previousIndex].TrimStart()[3..].Trim());
            previousIndex--;
        }

        block.Reverse();
        bool inSummary = false;
        List<string> parts = [];

        foreach (string line in block)
        {
            string text = line;
            int start = text.IndexOf("<summary>", StringComparison.Ordinal);
            if (start >= 0)
            {
                inSummary = true;
                text = text[(start + "<summary>".Length)..];
            }

            if (!inSummary)
            {
                continue;
            }

            int end = text.IndexOf("</summary>", StringComparison.Ordinal);
            if (end >= 0)
            {
                text = text[..end];
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text.Trim());
            }

            if (end >= 0)
            {
                break;
            }
        }

        summary = string.Join(' ', parts);
        return summary.Length > 0;
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
    /// Updates the nesting depth while scanning interface declarations.
    /// </summary>
    private static int UpdateInterfaceDepth(string line, int interfaceDepth)
    {
        string text = line.Trim();

        if (interfaceDepth < 0)
        {
            return text.Contains('{') ? Math.Max(1, Count(text, '{') - Count(text, '}')) : -1;
        }

        if (interfaceDepth == 0 && IsInterfaceDeclaration(text))
        {
            int opened = Count(text, '{');
            return opened == 0 ? -1 : Math.Max(0, opened - Count(text, '}'));
        }

        if (interfaceDepth == 0)
        {
            return 0;
        }

        return Math.Max(0, interfaceDepth + Count(text, '{') - Count(text, '}'));
    }

    /// <summary>
    /// Determines whether a trimmed line starts an interface declaration.
    /// </summary>
    private static bool IsInterfaceDeclaration(string text)
    {
        return text.StartsWith("public interface ", StringComparison.Ordinal)
            || text.StartsWith("private interface ", StringComparison.Ordinal)
            || text.StartsWith("protected interface ", StringComparison.Ordinal)
            || text.StartsWith("internal interface ", StringComparison.Ordinal)
            || text.StartsWith("interface ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts how many times a character appears in text.
    /// </summary>
    private static int Count(string text, char value)
    {
        return text.Count(character => character == value);
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
