namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Guards the end state of the surface-consistency work: the change and cursor reason codes that were replaced by the
/// shared codes no longer appear in production source.
/// </summary>
public sealed class RemovedReasonCodeTests
{
    /// <summary>
    /// Verifies no production source file contains a removed reason code, and that the scan reads source files.
    /// </summary>
    [Fact]
    public void ProductionSource_ContainsNoRemovedReasonCode()
    {
        string source = Path.Combine(RemovedSurfaceNames.FindRepositoryRoot(), "src");
        IReadOnlyList<string> files = RemovedSurfaceNames.Files(source, "*.cs");

        Assert.NotEmpty(files);
        IReadOnlyList<string> found = FindCodes(source, files);
        Assert.True(found.Count == 0, "Removed reason codes still in src:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// Verifies the scan reports a removed reason code when a source file holds one.
    /// </summary>
    [Fact]
    public void Scan_AFileWithARemovedCode_IsReported()
    {
        string folder = Path.Combine(Path.GetTempPath(), "removed-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Sample.cs"), "return new Conflict(\"deck-revision-conflict\");");

            IReadOnlyList<string> found = FindCodes(folder, RemovedSurfaceNames.Files(folder, "*.cs"));

            Assert.Equal(["Sample.cs: deck-revision-conflict"], found);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Lists each removed reason code found in each file, as the file path under the root and the code.
    /// </summary>
    private static IReadOnlyList<string> FindCodes(string root, IReadOnlyList<string> files)
    {
        List<string> found = [];
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string code in RemovedSurfaceNames.ReasonCodes
                .Where(code => text.Contains(code, StringComparison.Ordinal)))
            {
                found.Add($"{Path.GetRelativePath(root, file)}: {code}");
            }
        }

        return found;
    }
}
