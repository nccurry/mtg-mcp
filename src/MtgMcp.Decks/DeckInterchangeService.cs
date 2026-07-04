using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks;

/// <summary>
/// Owns bounded, network-free manual deck preview, import, and artifact workflows.
/// </summary>
public sealed class DeckInterchangeService
{
    /// <summary>
    /// Limits one caller-supplied manual document to five mebibytes of UTF-8 data.
    /// </summary>
    private const int MaximumInputBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Limits one normalized proposal to ten thousand independently addressable entries.
    /// </summary>
    private const int MaximumEntries = 10_000;

    /// <summary>
    /// Limits one preview to two hundred detailed diagnostics.
    /// </summary>
    private const int MaximumDiagnostics = 200;

    /// <summary>
    /// Limits one export response to sixteen artifacts.
    /// </summary>
    private const int MaximumArtifacts = 16;

    /// <summary>
    /// Limits total UTF-8 artifact content to twenty mebibytes.
    /// </summary>
    private const int MaximumExportBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Stores the transactional deck persistence owner.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Creates manual interchange workflows over one local deck store.
    /// </summary>
    public DeckInterchangeService(SqliteDeckStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Lists every manual interchange format with exact direction support and cautions.
    /// </summary>
    public OperationResult<IReadOnlyList<DeckInterchangeFormat>> ListFormats()
    {
        return new OperationSuccess<IReadOnlyList<DeckInterchangeFormat>>(
            DeckInterchangeCatalog.All.ToArray());
    }

    /// <summary>
    /// Parses one document into a deterministic proposal without creating local storage.
    /// </summary>
    public Task<OperationResult<DeckImportPreview>> PreviewAsync(
        string formatId,
        string content,
        DeckImportOptions? options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new DeckImportOptions();
        OperationResult<DeckImportPreview>? initialFailure = ValidateInput(
            formatId,
            content,
            options.AllowExperimental);
        if (initialFailure is not null)
        {
            return Task.FromResult(initialFailure.Value);
        }

        DeckImportProposal proposal;
        List<DeckInterchangeDiagnostic> diagnostics = [];
        if (formatId == DeckInterchangeCatalog.Native)
        {
            if (!DeckInterchangeCodec.TryParseNative(
                    content,
                    out DeckInterchangeSnapshot? snapshot,
                    out string failure))
            {
                DeckImportPreview invalidPreview = new(
                    formatId,
                    "invalid",
                    null,
                    null,
                    [new DeckInterchangeDiagnostic("error", "invalid-native-document", failure, Source: "$")],
                    0,
                    []);
                return Task.FromResult<OperationResult<DeckImportPreview>>(
                    new OperationSuccess<DeckImportPreview>(invalidPreview));
            }

            proposal = ToProposal(snapshot!);
        }
        else
        {
            proposal = DeckTextParser.Parse(formatId, content, options, diagnostics, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (proposal.Entries.Count > MaximumEntries)
        {
            return Task.FromResult<OperationResult<DeckImportPreview>>(Invalid<DeckImportPreview>(
                "The document contains more than 10000 deck entries."));
        }

        bool partial = diagnostics.Any(value => value.Severity == "error");
        bool invalid = partial && proposal.Entries.Count == 0;
        string[] unresolved = proposal.Entries
            .Where(value => value.OracleId is null)
            .Select(FormatUnresolvedIdentity)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        int omitted = Math.Max(0, diagnostics.Count - MaximumDiagnostics);
        DeckInterchangeDiagnostic[] bounded = diagnostics
            .Take(MaximumDiagnostics)
            .Select(BoundDiagnostic)
            .ToArray();
        DeckImportPreview preview = new(
            formatId,
            invalid ? "invalid" : partial ? "partial" : "complete",
            invalid ? null : Fingerprint(formatId, proposal),
            invalid ? null : proposal,
            bounded,
            omitted,
            unresolved);
        return Task.FromResult<OperationResult<DeckImportPreview>>(new OperationSuccess<DeckImportPreview>(preview));
    }

    /// <summary>
    /// Re-parses caller content, verifies its preview fingerprint, and atomically creates one local deck.
    /// </summary>
    public async Task<OperationResult<DeckImportCreateResult>> CreateAsync(
        string formatId,
        string content,
        string expectedFingerprint,
        DeckImportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new DeckImportOptions();
        OperationResult<DeckImportPreview> previewResult = await PreviewAsync(
            formatId,
            content,
            options,
            cancellationToken).ConfigureAwait(false);
        if (previewResult is not OperationSuccess<DeckImportPreview> success)
        {
            return ForwardFailure<DeckImportCreateResult, DeckImportPreview>(previewResult);
        }

        DeckImportPreview preview = success.Data;
        if (preview.Proposal is null || preview.Fingerprint is null)
        {
            return new OperationInvalidInput(
                "invalid-import-preview",
                "The document does not contain a creatable import proposal.");
        }

        if (string.IsNullOrWhiteSpace(expectedFingerprint) ||
            !string.Equals(preview.Fingerprint, expectedFingerprint.Trim(), StringComparison.Ordinal))
        {
            return new OperationConflict(
                "import-preview-changed",
                "The import proposal does not match the supplied preview fingerprint.");
        }

        if (preview.Completeness == "partial" && !options.AllowPartial)
        {
            return new OperationInvalidInput(
                "partial-import-not-allowed",
                "The import is partial; set allowPartial explicitly to accept skipped lines.");
        }

        DeckImportProposal proposal = preview.Proposal!;
        OperationResult<DeckDocument> createResult;
        if (formatId == DeckInterchangeCatalog.Native)
        {
            createResult = await store.CreateExactAsync(
                ToSnapshot(proposal),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            createResult = await store.CreateAsync(
                ToCreateRequest(proposal),
                cancellationToken).ConfigureAwait(false);
        }

        return createResult switch
        {
            OperationSuccess<DeckDocument> created => new OperationSuccess<DeckImportCreateResult>(
                new DeckImportCreateResult(
                    created.Data,
                    preview.Completeness,
                    preview.Diagnostics,
                    preview.OmittedDiagnosticCount)),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Exports one current local revision into deterministic manual and preservation artifacts.
    /// </summary>
    public async Task<OperationResult<DeckExportBundle>> ExportAsync(
        Guid deckId,
        string formatId,
        DeckExportOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new DeckExportOptions();
        DeckInterchangeFormat? format = DeckInterchangeCatalog.Find(formatId);
        if (format is null || !format.SupportsExport)
        {
            return new OperationUnsupported(
                "unsupported-interchange-format",
                "The requested manual interchange format is not supported.");
        }

        if (format.Status == "experimental" && !options.AllowExperimental)
        {
            return new OperationUnsupported(
                "experimental-format-not-enabled",
                "The requested format requires explicit experimental opt-in.");
        }

        OperationResult<DeckInterchangeSnapshot> snapshotResult = await store.GetInterchangeSnapshotAsync(
            deckId,
            cancellationToken).ConfigureAwait(false);
        if (snapshotResult is not OperationSuccess<DeckInterchangeSnapshot> success)
        {
            return ForwardFailure<DeckExportBundle, DeckInterchangeSnapshot>(snapshotResult);
        }

        cancellationToken.ThrowIfCancellationRequested();
        DeckExportBundle bundle = DeckArtifactWriter.Write(formatId, success.Data, options);
        if (!IsBundleWithinLimits(bundle))
        {
            return new OperationUnavailable(
                "export-bundle-too-large",
                "The generated artifact bundle exceeds the supported response limits.");
        }

        return new OperationSuccess<DeckExportBundle>(bundle);
    }

    /// <summary>
    /// Validates format availability and exact UTF-8 input bounds.
    /// </summary>
    private static OperationResult<DeckImportPreview>? ValidateInput(
        string formatId,
        string content,
        bool allowExperimental)
    {
        DeckInterchangeFormat? format = DeckInterchangeCatalog.Find(formatId);
        if (format is null || !format.SupportsImport)
        {
            return new OperationUnsupported(
                "unsupported-interchange-format",
                "The requested manual interchange format is not supported.");
        }

        if (format.Status == "experimental" && !allowExperimental)
        {
            return new OperationUnsupported(
                "experimental-format-not-enabled",
                "The requested format requires explicit experimental opt-in.");
        }

        if (content is null || Encoding.UTF8.GetByteCount(content) > MaximumInputBytes)
        {
            return Invalid<DeckImportPreview>("The document exceeds the 5 MiB UTF-8 input limit.");
        }

        return null;
    }

    /// <summary>
    /// Projects one stored native snapshot into its public preview form.
    /// </summary>
    private static DeckImportProposal ToProposal(DeckInterchangeSnapshot snapshot)
    {
        DeckDocument deck = snapshot.Deck;
        return new DeckImportProposal(
            deck.DeckId,
            deck.Revision,
            deck.CreatedAtUtc,
            deck.UpdatedAtUtc,
            deck.Name,
            deck.Description,
            deck.Format,
            deck.Entries,
            deck.Categories,
            deck.CategoryAssignments,
            deck.ProviderBindings,
            snapshot.SyncBaselines);
    }

    /// <summary>
    /// Restores the exact native snapshot represented by one validated proposal.
    /// </summary>
    private static DeckInterchangeSnapshot ToSnapshot(DeckImportProposal proposal)
    {
        DeckDocument deck = new(
            proposal.SourceDeckId!.Value,
            proposal.Name,
            proposal.Description,
            proposal.Format,
            proposal.SourceRevision!.Value,
            proposal.SourceCreatedAtUtc!.Value,
            proposal.SourceUpdatedAtUtc!.Value,
            proposal.Entries,
            proposal.Categories,
            proposal.CategoryAssignments,
            proposal.ProviderBindings);
        return new DeckInterchangeSnapshot(deck, proposal.SyncBaselines);
    }

    /// <summary>
    /// Remaps deterministic preview IDs to fresh local IDs so repeated text imports remain independent.
    /// </summary>
    private static DeckCreateRequest ToCreateRequest(DeckImportProposal proposal)
    {
        Dictionary<Guid, Guid> entryIds = proposal.Entries.ToDictionary(
            value => value.EntryId,
            _ => Guid.CreateVersion7());
        Dictionary<Guid, Guid> categoryIds = proposal.Categories.ToDictionary(
            value => value.CategoryId,
            _ => Guid.CreateVersion7());
        return new DeckCreateRequest(
            proposal.Name,
            proposal.Description,
            proposal.Format,
            proposal.Entries.Select(value => new DeckEntryDraft(
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder,
                entryIds[value.EntryId])).ToArray(),
            proposal.Categories.Select(value => new DeckCategoryDraft(
                value.Name,
                value.Color,
                value.SortOrder,
                categoryIds[value.CategoryId])).ToArray(),
            proposal.CategoryAssignments.Select(value => new DeckCategoryAssignment(
                entryIds[value.EntryId],
                categoryIds[value.CategoryId],
                value.IsPrimary)).ToArray(),
            proposal.ProviderBindings.Select(value => value with { BindingId = Guid.CreateVersion7() }).ToArray());
    }

    /// <summary>
    /// Derives the stable proposal fingerprint used to guard import creation.
    /// </summary>
    private static string Fingerprint(string formatId, DeckImportProposal proposal)
    {
        string canonical = JsonSerializer.Serialize(
            new { formatId, proposal },
            DeckInterchangeCodec.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// Formats unresolved name and optional printing hints without claiming a card identity.
    /// </summary>
    private static string FormatUnresolvedIdentity(DeckEntry entry)
    {
        if (entry.SetCode is null)
        {
            return entry.CardName;
        }

        return entry.CollectorNumber is null
            ? $"{entry.CardName} ({entry.SetCode.ToUpperInvariant()})"
            : $"{entry.CardName} ({entry.SetCode.ToUpperInvariant()}) {entry.CollectorNumber}";
    }

    /// <summary>
    /// Truncates a diagnostic message to 512 Unicode scalar values.
    /// </summary>
    internal static DeckInterchangeDiagnostic BoundDiagnostic(DeckInterchangeDiagnostic value)
    {
        string message = string.Concat(value.Message.EnumerateRunes().Take(512));
        return value with { Message = message };
    }

    /// <summary>
    /// Reports whether an already generated bundle satisfies exact artifact and UTF-8 response bounds.
    /// </summary>
    internal static bool IsBundleWithinLimits(DeckExportBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.Artifacts.Count <= MaximumArtifacts &&
            bundle.Artifacts.Sum(value => Encoding.UTF8.GetByteCount(value.Content)) <= MaximumExportBytes;
    }

    /// <summary>
    /// Creates a stable caller-input failure.
    /// </summary>
    private static OperationResult<T> Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-interchange-input", message);
    }

    /// <summary>
    /// Forwards every closed failure case while rejecting an unexpected success.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TTarget, TSource>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationSuccess<TSource> => new OperationUnavailable(
                "interchange-operation-failed",
                "The manual interchange operation could not be completed."),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}
