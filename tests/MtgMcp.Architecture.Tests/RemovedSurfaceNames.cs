namespace MtgMcp.Architecture.Tests;

/// <summary>
/// Lists the argument names, result fields, and reason codes that the surface-consistency work removed, and finds the
/// repository root the scans start from.
/// </summary>
internal static class RemovedSurfaceNames
{
    /// <summary>
    /// Lists the reason codes the shared codes replaced.
    /// </summary>
    internal static readonly string[] ReasonCodes =
    [
        "deck-revision-conflict", "local-deck-changed", "pull-preview-changed", "push-preview-changed",
        "restore-preview-changed", "import-preview-changed", "category-preview-mismatch",
        "identity-preview-mismatch", "source-changed-between-pages", "deck-changed-between-pages",
        "deck-list-changed", "category-corpus-conflict", "invalid-deck-list-cursor", "invalid-offset",
        "invalid-page-options", "invalid-page-boundary", "data-loss-not-acknowledged",
        "activation-change-not-acknowledged", "evidence-loss-not-acknowledged",
    ];

    /// <summary>
    /// Lists the argument names and result fields that no tool takes or returns any more.
    /// </summary>
    internal static readonly string[] ArgumentsAndFields =
    [
        "offset", "operationOffset", "differenceOffset", "expectedPreviewFingerprint", "limit", "page",
        "printsCursor", "rulingsCursor", "nextOffset", "nextOperationOffset", "nextDifferenceOffset",
        "expectedFingerprint", "expectedLocalRevision", "localRevision", "deckRevision", "archidektDeckId",
        "remoteId", "acknowledgeActivationChange", "acknowledgeDataLoss", "metadataPolicy",
        "expectedCorpusGeneration",
    ];

    /// <summary>
    /// Finds the repository root by its solution file.
    /// </summary>
    internal static string FindRepositoryRoot()
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

    /// <summary>
    /// Lists the files under a folder that match a pattern, leaving out build output, in ordinal order.
    /// </summary>
    internal static IReadOnlyList<string> Files(string folder, string pattern)
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        return
        [
            .. Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{separator}obj{separator}") &&
                    !path.Contains($"{separator}bin{separator}"))
                .Order(StringComparer.Ordinal),
        ];
    }
}
