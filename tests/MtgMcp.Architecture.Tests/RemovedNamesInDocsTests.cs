using System.Text.RegularExpressions;

namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Guards the documentation end state of the surface-consistency work: no removed argument, result field, or reason
/// code is named in the README or the docs, except where the history of the change is kept.
/// </summary>
public sealed class RemovedNamesInDocsTests
{
    /// <summary>
    /// Verifies the README and the docs outside the change log and the plan records name no removed argument, field, or
    /// reason code.
    /// </summary>
    [Fact]
    public void Docs_NameNoRemovedArgumentFieldOrReasonCode()
    {
        string root = RemovedSurfaceNames.FindRepositoryRoot();
        IReadOnlyList<string> files = DocumentedFiles(root);

        Assert.NotEmpty(files);
        List<string> found = [];
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file);
            found.AddRange(ScanText(File.ReadAllText(file)).Select(name => $"{relative}: {name}"));
        }

        Assert.True(found.Count == 0, "Removed names still in the docs:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// Verifies the scan reports a removed argument written as a code span and a removed reason code, and ignores a
    /// word that only contains a removed name.
    /// </summary>
    [Fact]
    public void Scan_RemovedNames_AreReportedOnlyAsWholeCodeSpansOrCodes()
    {
        Assert.Equal(["offset"], ScanText("Pass `offset` to page."));
        Assert.Equal(["deck-revision-conflict"], ScanText("The code deck-revision-conflict means a stale deck."));
        Assert.Empty(ScanText("The offset of the first row, and `offsetBy`, and `stale-deck-revision`."));
    }

    /// <summary>
    /// Lists the README and the Markdown files under docs, leaving out the plan records.
    /// </summary>
    private static IReadOnlyList<string> DocumentedFiles(string root)
    {
        string plans = Path.Combine(root, "docs", "llms", "plcs") + Path.DirectorySeparatorChar;
        return
        [
            Path.Combine(root, "README.md"),
            .. RemovedSurfaceNames.Files(Path.Combine(root, "docs"), "*.md")
                .Where(path => !path.StartsWith(plans, StringComparison.Ordinal)),
        ];
    }

    /// <summary>
    /// Lists the removed names a text uses. An argument or field counts only as a whole code span, because words such
    /// as offset and limit are ordinary prose; a reason code counts anywhere.
    /// </summary>
    private static IReadOnlyList<string> ScanText(string text)
    {
        List<string> found = [];
        foreach (string name in RemovedSurfaceNames.ArgumentsAndFields)
        {
            if (Regex.IsMatch(text, $"`{Regex.Escape(name)}`"))
            {
                found.Add(name);
            }
        }

        foreach (string code in RemovedSurfaceNames.ReasonCodes)
        {
            if (text.Contains(code, StringComparison.Ordinal))
            {
                found.Add(code);
            }
        }

        return found;
    }
}
