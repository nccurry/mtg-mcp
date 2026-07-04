using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies bounded manual interchange, exact native transfer, and provider artifact evidence.
/// </summary>
public sealed class DeckInterchangeServiceTests
{
    /// <summary>
    /// Verifies the catalog exposes every manually accepted provider format without an opt-in.
    /// </summary>
    [Fact]
    public async Task Formats_ExposeStableAcceptedCapabilities()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckInterchangeService service = new(store);

        IReadOnlyList<DeckInterchangeFormat> formats = RequireSuccess(service.ListFormats());
        DeckImportPreview enabled = RequireSuccess(await service.PreviewAsync(
            "archidekt-text-v1",
            "1 Sol Ring (CMM) 396 `Ramp`",
            new DeckImportOptions(DeckName: "Archidekt"),
            TestContext.Current.CancellationToken));

        Assert.Equal(
            ["mtg-mcp-json-v1", "generic-text-v1", "archidekt-text-v1", "moxfield-bulk-edit-v1"],
            formats.Select(value => value.FormatId));
        Assert.All(formats, value => Assert.True(value.SupportsImport && value.SupportsExport));
        Assert.All(formats, value => Assert.Equal("available", value.Status));
        Assert.Equal("cmm", Assert.Single(enabled.Proposal!.Entries).SetCode);
        Assert.Equal("Ramp", Assert.Single(enabled.Proposal.Categories).Name);
    }

    /// <summary>
    /// Verifies generic previews are deterministic and create independent local identities on repetition.
    /// </summary>
    [Fact]
    public async Task GenericText_PreviewAndCreate_AreDeterministicAndRepeatable()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckInterchangeService service = new(store);
        const string content = "[commander]\n1 Atraxa, Praetors' Voice (2XM) 190\n\n[main]\n3 Île\n1 Sol Ring";
        DeckImportOptions options = new(DeckName: "Unicode Commander", Description: "fixture");

        DeckImportPreview first = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            content,
            options,
            TestContext.Current.CancellationToken));
        DeckImportPreview second = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            content,
            options,
            TestContext.Current.CancellationToken));
        DeckImportCreateResult created = RequireSuccess(await service.CreateAsync(
            "generic-text-v1",
            content,
            first.Fingerprint!,
            options,
            TestContext.Current.CancellationToken));
        DeckImportCreateResult repeated = RequireSuccess(await service.CreateAsync(
            "generic-text-v1",
            content,
            first.Fingerprint!,
            options,
            TestContext.Current.CancellationToken));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Proposal!.Entries, second.Proposal!.Entries);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal("complete", first.Completeness);
        Assert.Equal(3, first.UnresolvedIdentities.Count);
        Assert.Equal("commander", first.Proposal.Entries[0].Zone);
        Assert.Equal("2xm", first.Proposal.Entries[0].SetCode);
        Assert.Equal("190", first.Proposal.Entries[0].CollectorNumber);
        Assert.NotEqual(created.Deck.DeckId, repeated.Deck.DeckId);
        Assert.Empty(created.Diagnostics);
        Assert.Equal("Île", created.Deck.Entries.Single(value => value.CardName == "Île").CardName);
        Assert.Equal(2, RequireSuccess(await store.ListAsync(
            null,
            10,
            TestContext.Current.CancellationToken)).Items.Count);
    }

    /// <summary>
    /// Verifies skipped lines require partial opt-in and fingerprints reject changed content.
    /// </summary>
    [Fact]
    public async Task PartialText_RequiresExplicitAcceptanceAndMatchingFingerprint()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckInterchangeService service = new(store);
        const string content = "[main]\nnot a card line\n1 Island";
        DeckImportPreview preview = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            content,
            null,
            TestContext.Current.CancellationToken));

        OperationResult<DeckImportCreateResult> refused = await service.CreateAsync(
            "generic-text-v1",
            content,
            preview.Fingerprint!,
            null,
            TestContext.Current.CancellationToken);
        OperationResult<DeckImportCreateResult> stale = await service.CreateAsync(
            "generic-text-v1",
            content + "\n1 Plains",
            preview.Fingerprint!,
            new DeckImportOptions(AllowPartial: true),
            TestContext.Current.CancellationToken);
        DeckImportCreateResult accepted = RequireSuccess(await service.CreateAsync(
            "generic-text-v1",
            content,
            preview.Fingerprint!,
            new DeckImportOptions(AllowPartial: true),
            TestContext.Current.CancellationToken));

        Assert.Equal("partial", preview.Completeness);
        Assert.Equal(2, preview.Diagnostics[0].Line);
        Assert.IsType<OperationInvalidInput>(refused.Value);
        Assert.IsType<OperationConflict>(stale.Value);
        Assert.Single(accepted.Deck.Entries);
    }

    /// <summary>
    /// Verifies native export and import preserve all local fields, stable IDs, revisions, and baselines.
    /// </summary>
    [Fact]
    public async Task NativeJson_RoundTripAcrossStores_IsLossless()
    {
        using TemporaryDeckDirectory sourceDirectory = new();
        using TemporaryDeckDirectory targetDirectory = new();
        using SqliteDeckStore source = CreateStore(sourceDirectory.Path);
        using SqliteDeckStore target = CreateStore(targetDirectory.Path);
        Guid entryId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await source.CreateAsync(
            new DeckCreateRequest(
                "Native",
                "all fields",
                "commander",
                [new DeckEntryDraft(
                    1,
                    "Atraxa",
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    "2x2",
                    "190",
                    "fr",
                    "foil",
                    "commander",
                    9,
                    entryId)],
                [new DeckCategoryDraft("Commander", "#abcdef", 3, categoryId)],
                [new DeckCategoryAssignment(entryId, categoryId, true)]),
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await source.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpsertDeckProviderBindingChange(
                new DeckProviderBinding(
                    bindingId,
                    "archidekt",
                    "42",
                    "https://example.invalid/42",
                    "remote-v1",
                    "fingerprint",
                    new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero)),
                "{\"remote\":\"snapshot\"}")],
            TestContext.Current.CancellationToken));
        DeckInterchangeService sourceService = new(source);
        DeckExportBundle exported = RequireSuccess(await sourceService.ExportAsync(
            deck.DeckId,
            "mtg-mcp-json-v1",
            null,
            TestContext.Current.CancellationToken));
        string content = exported.Artifacts.Single(value => value.FileName == "deck.mtg-mcp.json").Content;
        DeckInterchangeService targetService = new(target);
        DeckImportPreview preview = RequireSuccess(await targetService.PreviewAsync(
            "mtg-mcp-json-v1",
            content,
            null,
            TestContext.Current.CancellationToken));
        DeckDocument restored = RequireSuccess(await targetService.CreateAsync(
            "mtg-mcp-json-v1",
            content,
            preview.Fingerprint!,
            null,
            TestContext.Current.CancellationToken)).Deck;
        OperationResult<DeckImportCreateResult> duplicate = await targetService.CreateAsync(
            "mtg-mcp-json-v1",
            content,
            preview.Fingerprint!,
            null,
            TestContext.Current.CancellationToken);
        DeckExportBundle reexported = RequireSuccess(await targetService.ExportAsync(
            restored.DeckId,
            "mtg-mcp-json-v1",
            null,
            TestContext.Current.CancellationToken));

        Assert.Equal(deck.DeckId, restored.DeckId);
        Assert.Equal(deck.Revision, restored.Revision);
        Assert.Equal(deck.Entries, restored.Entries);
        Assert.Equal(deck.Categories, restored.Categories);
        Assert.Equal(deck.CategoryAssignments, restored.CategoryAssignments);
        Assert.Equal(deck.ProviderBindings, restored.ProviderBindings);
        Assert.IsType<OperationInvalidInput>(duplicate.Value);
        Assert.Single(RequireSuccess(await target.ListAsync(
            null,
            10,
            TestContext.Current.CancellationToken)).Items);
        Assert.Equal(content, reexported.Artifacts[0].Content);
        Assert.Equal(exported.Artifacts[0].Sha256, reexported.Artifacts[0].Sha256);
        Assert.Equal(["deck.mtg-mcp.json", "preservation.json"], exported.Artifacts.Select(value => value.FileName));
    }

    /// <summary>
    /// Verifies provider bundles retain all assignments and never create global Moxfield tags implicitly.
    /// </summary>
    [Fact]
    public async Task ProviderBundles_EmitAcceptedTextAndLosslessCompanions()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        Guid entryId = Guid.CreateVersion7();
        Guid rampId = Guid.CreateVersion7();
        Guid manaId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Provider",
                Format: "commander",
                Entries: [new DeckEntryDraft(1, "Sol Ring", SetCode: "cmm", CollectorNumber: "396", Finish: "foil", EntryId: entryId)],
                Categories: [
                    new DeckCategoryDraft("Ramp", CategoryId: rampId),
                    new DeckCategoryDraft("Mana", CategoryId: manaId),
                ],
                CategoryAssignments: [
                    new DeckCategoryAssignment(entryId, rampId, true),
                    new DeckCategoryAssignment(entryId, manaId, false),
                ]),
            TestContext.Current.CancellationToken));
        DeckInterchangeService service = new(store);
        DeckExportBundle archidekt = RequireSuccess(await service.ExportAsync(
            deck.DeckId,
            "archidekt-text-v1",
            null,
            TestContext.Current.CancellationToken));
        DeckExportBundle moxfield = RequireSuccess(await service.ExportAsync(
            deck.DeckId,
            "moxfield-bulk-edit-v1",
            null,
            TestContext.Current.CancellationToken));
        DeckExportBundle global = RequireSuccess(await service.ExportAsync(
            deck.DeckId,
            "moxfield-bulk-edit-v1",
            new DeckExportOptions(UseGlobalMoxfieldTags: true),
            TestContext.Current.CancellationToken));

        Assert.Equal("available", archidekt.Status);
        Assert.Equal("available", moxfield.Status);
        Assert.Equal(
            ["deck.archidekt.txt", "category-assignments.csv", "deck.mtg-mcp.json", "preservation.json", "README.txt"],
            archidekt.Artifacts.Select(value => value.FileName));
        Assert.Contains("1 Sol Ring (CMM) 396 `Ramp`", archidekt.Artifacts[0].Content, StringComparison.Ordinal);
        Assert.Equal(2, archidekt.Artifacts[1].Content.Count(value => value == '\n') - 1);
        Assert.Contains("*F* #Ramp #Mana", moxfield.Artifacts[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("#!", moxfield.Artifacts[0].Content, StringComparison.Ordinal);
        Assert.Contains("#!Ramp #!Mana", global.Artifacts[0].Content, StringComparison.Ordinal);
        Assert.Equal(
            "companion-only",
            global.Preservation.Single(value => value.Field == "primary-category").Status);
        Assert.Equal(
            "companion-only",
            global.Preservation.Single(value => value.Field == "secondary-categories").Status);
        Assert.All(moxfield.Artifacts, value => Assert.Matches("^[0-9a-f]{64}$", value.Sha256));
        Assert.Equal("companion-only", archidekt.Preservation.Single(value => value.Field == "finishes").Status);
        Assert.Equal("companion-only", archidekt.Preservation.Single(value => value.Field == "secondary-categories").Status);
        Assert.Equal("preserved", moxfield.Preservation.Single(value => value.Field == "finishes").Status);
        Assert.Equal("preserved", moxfield.Preservation.Single(value => value.Field == "secondary-categories").Status);
        Assert.All(
            [archidekt, moxfield],
            bundle => Assert.Equal("companion-only", bundle.Preservation.Single(value => value.Field == "zone").Status));
    }

    /// <summary>
    /// Verifies malformed formats, oversized input, missing decks, and native schema errors stay structured.
    /// </summary>
    [Fact]
    public async Task InvalidAndBoundedInputs_ReturnStructuredFailuresWithoutStorage()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckInterchangeService service = new(store);
        string oversized = new('x', (5 * 1024 * 1024) + 1);

        Assert.IsType<OperationUnsupported>((await service.PreviewAsync(
            "unknown",
            "1 Island",
            null,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.PreviewAsync(
            "generic-text-v1",
            oversized,
            null,
            TestContext.Current.CancellationToken)).Value);
        DeckImportPreview invalidNative = RequireSuccess(await service.PreviewAsync(
            "mtg-mcp-json-v1",
            "{\"schema\":\"future\"}",
            null,
            TestContext.Current.CancellationToken));
        OperationResult<DeckImportCreateResult> invalidCreate = await service.CreateAsync(
            "mtg-mcp-json-v1",
            "{\"schema\":\"future\"}",
            "unused",
            null,
            TestContext.Current.CancellationToken);
        Assert.IsType<OperationNotFound>((await service.ExportAsync(
            Guid.CreateVersion7(),
            "generic-text-v1",
            null,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.ExportAsync(
            Guid.Empty,
            "generic-text-v1",
            null,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationUnsupported>((await service.ExportAsync(
            Guid.CreateVersion7(),
            "unknown",
            null,
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("invalid", invalidNative.Completeness);
        Assert.Null(invalidNative.Fingerprint);
        Assert.Equal("$", Assert.Single(invalidNative.Diagnostics).Source);
        Assert.IsType<OperationInvalidInput>(invalidCreate.Value);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Verifies exact input, entry, diagnostic, Unicode-scalar, and cancellation boundaries.
    /// </summary>
    [Fact]
    public async Task PreviewBounds_AcceptExactLimitsAndRejectOrTruncateOverflow()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckInterchangeService service = new(store);
        string exactBytes = new('x', 5 * 1024 * 1024);
        string exactEntries = string.Join('\n', Enumerable.Range(1, 10_000).Select(value => $"1 Card {value}"));
        string overflowEntries = exactEntries + "\n1 Overflow";
        string diagnostics = string.Join('\n', Enumerable.Repeat("malformed", 201));

        DeckImportPreview exactBytePreview = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            exactBytes,
            null,
            TestContext.Current.CancellationToken));
        DeckImportPreview exactEntryPreview = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            exactEntries,
            null,
            TestContext.Current.CancellationToken));
        OperationResult<DeckImportPreview> overflow = await service.PreviewAsync(
            "generic-text-v1",
            overflowEntries,
            null,
            TestContext.Current.CancellationToken);
        DeckImportPreview bounded = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            diagnostics,
            null,
            TestContext.Current.CancellationToken));
        DeckInterchangeDiagnostic longDiagnostic = new(
            "error",
            "long-message",
            string.Concat(Enumerable.Repeat("😀", 513)));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Equal("invalid", exactBytePreview.Completeness);
        Assert.Equal(10_000, exactEntryPreview.Proposal!.Entries.Count);
        Assert.IsType<OperationInvalidInput>(overflow.Value);
        Assert.Equal(200, bounded.Diagnostics.Count);
        Assert.Equal(1, bounded.OmittedDiagnosticCount);
        Assert.Equal(512, DeckInterchangeService.BoundDiagnostic(longDiagnostic).Message.EnumerateRunes().Count());
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.PreviewAsync(
                "generic-text-v1",
                "1 Island",
                null,
                cancellation.Token).ConfigureAwait(false));
    }

    /// <summary>
    /// Verifies exact artifact-count and UTF-8 bundle-size limits independently of deck content.
    /// </summary>
    [Fact]
    public void ExportBounds_AcceptExactLimitsAndRejectOverflow()
    {
        DeckExportBundle exactCount = Bundle(Enumerable.Range(0, 16)
            .Select(value => new DeckExportArtifact($"{value}.txt", "text/plain", string.Empty, "hash", "test"))
            .ToArray());
        DeckExportBundle overflowCount = Bundle(Enumerable.Range(0, 17)
            .Select(value => new DeckExportArtifact($"{value}.txt", "text/plain", string.Empty, "hash", "test"))
            .ToArray());
        DeckExportBundle exactBytes = Bundle([
            new DeckExportArtifact("exact.txt", "text/plain", new string('a', 20 * 1024 * 1024), "hash", "test"),
        ]);
        DeckExportBundle overflowBytes = Bundle([
            new DeckExportArtifact("overflow.txt", "text/plain", new string('a', (20 * 1024 * 1024) + 1), "hash", "test"),
        ]);

        Assert.True(DeckInterchangeService.IsBundleWithinLimits(exactCount));
        Assert.False(DeckInterchangeService.IsBundleWithinLimits(overflowCount));
        Assert.True(DeckInterchangeService.IsBundleWithinLimits(exactBytes));
        Assert.False(DeckInterchangeService.IsBundleWithinLimits(overflowBytes));
    }

    /// <summary>
    /// Verifies text dialects preserve supported zones, print hints, finishes, and multiple local tags.
    /// </summary>
    [Fact]
    public async Task TextDialects_RoundTripSupportedEvidenceAndReportProviderLimits()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Zones",
                Format: "custom",
                Entries:
                [
                    new DeckEntryDraft(1, "Commander", Zone: "commander"),
                    new DeckEntryDraft(2, "Main", SetCode: "tst", CollectorNumber: "1", Zone: "main"),
                    new DeckEntryDraft(1, "Side", Zone: "sideboard"),
                    new DeckEntryDraft(1, "Maybe", Zone: "maybeboard"),
                    new DeckEntryDraft(1, "Excluded", Zone: "excluded"),
                ]),
            TestContext.Current.CancellationToken));
        DeckInterchangeService service = new(store);
        DeckExportBundle generic = RequireSuccess(await service.ExportAsync(
            deck.DeckId,
            "generic-text-v1",
            null,
            TestContext.Current.CancellationToken));
        DeckExportBundle repeated = RequireSuccess(await service.ExportAsync(
            deck.DeckId,
            "generic-text-v1",
            null,
            TestContext.Current.CancellationToken));
        string genericText = generic.Artifacts[0].Content;
        DeckImportPreview genericPreview = RequireSuccess(await service.PreviewAsync(
            "generic-text-v1",
            genericText,
            new DeckImportOptions(Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckImportPreview moxfield = RequireSuccess(await service.PreviewAsync(
            "moxfield-bulk-edit-v1",
            "1 Test Card (TST) 7 *E* #First Tag #Second Tag",
            null,
            TestContext.Current.CancellationToken));
        DeckImportPreview archidekt = RequireSuccess(await service.PreviewAsync(
            "archidekt-text-v1",
            "1 Test Card `Primary`",
            new DeckImportOptions(DefaultZone: "sideboard"),
            TestContext.Current.CancellationToken));

        Assert.Equal(["commander", "excluded", "main", "maybeboard", "sideboard"],
            genericPreview.Proposal!.Entries.Select(value => value.Zone).Order(StringComparer.Ordinal));
        Assert.Equal("tst", genericPreview.Proposal.Entries.Single(value => value.CardName == "Main").SetCode);
        Assert.Equal(generic.Artifacts, repeated.Artifacts);
        Assert.Equal(deck.UpdatedAtUtc, generic.GeneratedAtUtc);
        Assert.DoesNotContain('\r', genericText);
        Assert.Equal(
            "preserved",
            generic.Preservation.Single(value => value.Field == "excluded-entries").Status);
        Assert.All(
            generic.Preservation,
            value => Assert.Matches("^(preserved|companion-only|unsupported)$", value.Status));
        Assert.Equal(
            generic.Artifacts[0].Sha256,
            DeckInterchangeCodec.Sha256(generic.Artifacts[0].Content));
        DeckEntry moxfieldEntry = Assert.Single(moxfield.Proposal!.Entries);
        Assert.Equal("etched", moxfieldEntry.Finish);
        Assert.Equal(["First Tag", "Second Tag"], moxfield.Proposal.Categories.Select(value => value.Name));
        Assert.Equal(2, moxfield.Proposal.CategoryAssignments.Count);
        Assert.True(moxfield.Proposal.CategoryAssignments[0].IsPrimary);
        Assert.Equal("sideboard", Assert.Single(archidekt.Proposal!.Entries).Zone);
    }

    /// <summary>
    /// Verifies exact-native persistence rejects malformed lifecycle identities and safe lookup boundaries.
    /// </summary>
    [Fact]
    public async Task ExactNativeStore_RejectsInvalidStateAndReturnsStructuredLookupFailures()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = CreateStore(temporary.Path);
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        DeckDocument invalid = new(
            Guid.CreateVersion7(),
            "Invalid",
            string.Empty,
            "custom",
            0,
            now,
            now,
            [],
            [],
            [],
            []);
        OperationResult<DeckDocument> invalidRevision = await store.CreateExactAsync(
            new DeckInterchangeSnapshot(invalid, []),
            TestContext.Current.CancellationToken);
        DeckDocument invalidBinding = invalid with
        {
            Revision = 1,
            ProviderBindings = [new DeckProviderBinding(Guid.Empty, "provider", "remote", null, null, null, null, null)],
        };
        OperationResult<DeckDocument> emptyBinding = await store.CreateExactAsync(
            new DeckInterchangeSnapshot(invalidBinding, []),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationInvalidInput>(invalidRevision.Value);
        Assert.IsType<OperationInvalidInput>(emptyBinding.Value);
        Assert.IsType<OperationInvalidInput>((await store.GetInterchangeSnapshotAsync(
            Guid.Empty,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationNotFound>((await store.GetInterchangeSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken)).Value);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Verifies structurally null native collections become sanitized parse failures rather than exceptions.
    /// </summary>
    [Fact]
    public void NativeCodec_NullCollections_ReturnSafeFailure()
    {
        Guid deckId = Guid.CreateVersion7();
        string json = $$"""
            {
              "schema": "mtg-mcp.deck/v1",
              "deck": {
                "deckId": "{{deckId:D}}",
                "name": "Deck",
                "description": "",
                "format": "custom",
                "revision": 1,
                "createdAtUtc": "1970-01-01T00:00:00Z",
                "updatedAtUtc": "1970-01-01T00:00:00Z",
                "entries": null,
                "categories": [],
                "categoryAssignments": [],
                "providerBindings": []
              },
              "syncBaselines": []
            }
            """;

        bool parsed = DeckInterchangeCodec.TryParseNative(json, out DeckInterchangeSnapshot? snapshot, out string failure);

        Assert.False(parsed);
        Assert.Null(snapshot);
        Assert.DoesNotContain(deckId.ToString("D"), failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates one store with the current preview application version.
    /// </summary>
    private static SqliteDeckStore CreateStore(string root)
    {
        return new SqliteDeckStore(root, "0.9.0-preview.1");
    }

    /// <summary>
    /// Creates one synthetic bundle for exact boundary checks.
    /// </summary>
    private static DeckExportBundle Bundle(IReadOnlyList<DeckExportArtifact> artifacts)
    {
        return new DeckExportBundle(
            1,
            "test",
            Guid.CreateVersion7(),
            1,
            DateTimeOffset.UnixEpoch,
            "available",
            artifacts,
            []);
    }

    /// <summary>
    /// Extracts one successful operation payload for focused assertions.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
