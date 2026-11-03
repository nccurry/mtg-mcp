namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Guards the end state of the surface-consistency work: the change and cursor reason codes that were replaced by the
/// shared codes no longer appear in production source.
/// </summary>
public sealed class RemovedReasonCodeTests
{
    /// <summary>
    /// Lists the reason codes the shared codes replaced.
    /// </summary>
    private static readonly string[] RemovedCodes =
    [
        "deck-revision-conflict", "local-deck-changed", "pull-preview-changed", "push-preview-changed",
        "restore-preview-changed", "import-preview-changed", "category-preview-mismatch",
        "identity-preview-mismatch", "source-changed-between-pages", "deck-changed-between-pages",
        "deck-list-changed", "category-corpus-conflict",
    ];

    /// <summary>
    /// Verifies no production source file contains a removed reason code.
    /// </summary>
    [Fact]
    public void ProductionSource_ContainsNoRemovedReasonCode()
    {
        string source = Path.Combine(FindRepositoryRoot(), "src");
        string separator = Path.DirectorySeparatorChar.ToString();
        List<string> found = [];
        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}") && !path.Contains($"{separator}bin{separator}"))
            .Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file);
            foreach (string code in RemovedCodes.Where(code => text.Contains(code, StringComparison.Ordinal)))
            {
                found.Add($"{Path.GetRelativePath(source, file)}: {code}");
            }
        }

        Assert.True(found.Count == 0, "Removed reason codes still in src:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// Finds the repository root by its solution file.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mtg-mcp repository root.");
    }
}
