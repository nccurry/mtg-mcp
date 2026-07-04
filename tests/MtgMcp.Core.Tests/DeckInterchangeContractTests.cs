using System.Text.Json;
using MtgMcp.Core.Decks;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies manual interchange contracts snapshot caller collections and serialize stable public names.
/// </summary>
public sealed class DeckInterchangeContractTests
{
    /// <summary>
    /// Provides the same camel-case convention used by MCP structured results.
    /// </summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies every collection-bearing contract is immutable after construction and JSON-visible.
    /// </summary>
    [Fact]
    public void CollectionContracts_CopyInputsAndSerializeExpectedFields()
    {
        List<string> warnings = ["warning"];
        DeckInterchangeFormat format = new("format", "Format", true, true, false, "available", "Use it.", warnings);
        List<DeckEntry> entries = [new DeckEntry(Guid.CreateVersion7(), 1, "Card", null, null, null, null, "en", "nonfoil", "main", 0)];
        List<DeckCategory> categories = [];
        List<DeckCategoryAssignment> assignments = [];
        List<DeckProviderBinding> bindings = [];
        List<DeckSyncBaseline> baselines = [];
        DeckImportProposal proposal = new(null, null, null, null, "Deck", "", "commander", entries, categories, assignments, bindings, baselines);
        List<DeckInterchangeDiagnostic> diagnostics = [new("info", "name-only", "Name only")];
        List<string> unresolved = ["Card"];
        DeckImportPreview preview = new("format", "complete", "abc", proposal, diagnostics, 0, unresolved);
        DeckImportCreateResult created = new(
            new DeckDocument(Guid.CreateVersion7(), "Deck", "", "commander", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, entries, categories, assignments, bindings),
            "complete",
            diagnostics,
            0);
        List<DeckExportArtifact> artifacts = [new("deck.txt", "text/plain", "1 Card\n", "hash", "Manual list")];
        List<DeckFieldPreservation> preservation = [new("name", "preserved", "text")];
        DeckExportBundle bundle = new(1, "format", created.Deck.DeckId, 1, DateTimeOffset.UnixEpoch, "available", artifacts, preservation);

        warnings.Add("changed");
        entries.Clear();
        diagnostics.Clear();
        unresolved.Clear();
        artifacts.Clear();
        preservation.Clear();

        Assert.Single(format.Warnings);
        Assert.Single(proposal.Entries);
        Assert.Single(preview.Diagnostics);
        Assert.Single(preview.UnresolvedIdentities);
        Assert.Single(created.Diagnostics);
        Assert.Single(bundle.Artifacts);
        Assert.Single(bundle.Preservation);
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(bundle, WebJson));
        Assert.Equal("format", json.RootElement.GetProperty("formatId").GetString());
        Assert.Equal("deck.txt", json.RootElement.GetProperty("artifacts")[0].GetProperty("fileName").GetString());
    }

    /// <summary>
    /// Verifies scalar option and evidence records retain explicit caller values.
    /// </summary>
    [Fact]
    public void ScalarContracts_PreserveExplicitValues()
    {
        DeckImportOptions import = new("Deck", "Description", "custom", "sideboard", true, true);
        DeckExportOptions export = new(true, true);
        DeckSyncBaseline baseline = new(Guid.CreateVersion7(), "{}");

        Assert.True(import.AllowPartial);
        Assert.Equal("sideboard", import.DefaultZone);
        Assert.True(export.AllowExperimental);
        Assert.True(export.UseGlobalMoxfieldTags);
        Assert.Equal("{}", baseline.CanonicalSnapshot);
    }
}
