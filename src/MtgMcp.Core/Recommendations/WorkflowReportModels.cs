namespace MtgMcp.Core;

/// <summary>
/// Reports bounded commander discovery with catalog and EDHREC evidence.
/// </summary>
public sealed class CommanderCandidateSearchResult
{
    /// <summary>
    /// Requested color identity after WUBRG normalization.
    /// </summary>
    public string ColorIdentity { get; set; } = "";

    /// <summary>
    /// True when candidates were restricted to exactly the requested color identity.
    /// </summary>
    public bool ExactColorIdentity { get; set; }

    /// <summary>
    /// Minimum EDHREC eligible deck count accepted for returned commanders.
    /// </summary>
    public int MinEligibleDecks { get; set; }

    /// <summary>
    /// Maximum EDHREC eligible deck count accepted for returned commanders when supplied.
    /// </summary>
    public int? MaxEligibleDecks { get; set; }

    /// <summary>
    /// Bounded number of catalog candidates inspected before EDHREC evidence lookups.
    /// </summary>
    public int ScryfallCandidateCap { get; set; }

    /// <summary>
    /// Actual number of catalog candidates returned by the catalog and inspected.
    /// </summary>
    public int ScryfallCandidatesInspected { get; set; }

    /// <summary>
    /// Bounded number of EDHREC aggregate lookups attempted.
    /// </summary>
    public int EdhrecFetchCap { get; set; }

    /// <summary>
    /// Actual number of EDHREC aggregate lookups attempted.
    /// </summary>
    public int EdhrecFetchesAttempted { get; set; }

    /// <summary>
    /// Commander rows that satisfied the requested eligible-deck bounds.
    /// </summary>
    public List<CommanderCandidateRow> Commanders { get; set; } = [];

    /// <summary>
    /// Corpus source status rows observed while fetching EDHREC evidence.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Non-fatal source, cache, and bound notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one commander candidate found by bounded discovery.
/// </summary>
public sealed class CommanderCandidateRow
{
    /// <summary>
    /// Commander card name.
    /// </summary>
    public string CommanderName { get; set; } = "";

    /// <summary>
    /// Commander color identity in catalog order.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// EDHREC eligible deck count when the aggregate source exposed it.
    /// </summary>
    public int? EligibleDeckCount { get; set; }

    /// <summary>
    /// Scryfall card page when available from catalog metadata.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// EDHREC aggregate source URI when provided by the corpus row.
    /// </summary>
    public string? EdhrecUri { get; set; }
}

/// <summary>
/// Reports read-only tuning signals for several local workspaces.
/// </summary>
public sealed class DeckBatchTuningReport
{
    /// <summary>
    /// Target turn used for every goldfish simulation.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Simulation count used for every deck.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Shared random seed used for every deck.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Optional budget ceiling used for report-level risk notes.
    /// </summary>
    public decimal? MaxBudget { get; set; }

    /// <summary>
    /// Completed per-workspace tuning rows.
    /// </summary>
    public List<DeckBatchTuningDeckReport> Decks { get; set; } = [];

    /// <summary>
    /// Workspace-level failures that did not abort the batch.
    /// </summary>
    public List<DeckBatchTuningFailure> Failures { get; set; } = [];

    /// <summary>
    /// Report-level notes about scope and source behavior.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Bundles validation, analysis, and goldfish signals for one workspace.
/// </summary>
public sealed class DeckBatchTuningDeckReport
{
    /// <summary>
    /// Workspace id for follow-up tools.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Workspace deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Commander deck validation result.
    /// </summary>
    public DeckValidationResult Validation { get; set; } = new();

    /// <summary>
    /// Cached price analysis for the workspace.
    /// </summary>
    public DeckCostAnalysis Cost { get; set; } = new();

    /// <summary>
    /// Commander bracket estimate for the workspace.
    /// </summary>
    public CommanderBracketEstimate Bracket { get; set; } = new();

    /// <summary>
    /// Mana-base analysis for the workspace.
    /// </summary>
    public ManaBaseAnalysis Mana { get; set; } = new();

    /// <summary>
    /// Consistency analysis for the workspace.
    /// </summary>
    public DeckConsistencyAnalysis Consistency { get; set; } = new();

    /// <summary>
    /// Best-practice role and profile analysis for the workspace.
    /// </summary>
    public DeckBestPracticeAnalysis BestPractices { get; set; } = new();

    /// <summary>
    /// Goldfish simulation result for the workspace.
    /// </summary>
    public GoldfishSimulationResult Goldfish { get; set; } = new();

    /// <summary>
    /// Concise high-priority risks collected from the included reports.
    /// </summary>
    public List<string> Risks { get; set; } = [];
}

/// <summary>
/// Records one workspace that failed during a batch tuning report.
/// </summary>
public sealed class DeckBatchTuningFailure
{
    /// <summary>
    /// Caller-supplied workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Non-fatal failure reason for this workspace.
    /// </summary>
    public string Reason { get; set; } = "";
}
