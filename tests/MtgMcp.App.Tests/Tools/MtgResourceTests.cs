using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MtgMcp.App.Tests.Tools;

/// <summary>
/// Exercises durable MCP guidance and diagnostic resources through the configured host.
/// </summary>
public sealed class MtgResourceTests
{
    /// <summary>
    /// Verifies static guidance resources remain useful, bounded, and consistent with plan mode.
    /// </summary>
    [Fact]
    public void GuidanceResources_DescribeSupportedSafetyAndAnalysisBoundaries()
    {
        using IHost host = MtgMcpHost.Build([]);
        MtgResources resources = ActivatorUtilities.CreateInstance<MtgResources>(host.Services);

        resources.GetScryfallSyntaxCheatsheet().Should().Contain("legal:commander");
        resources.GetWorkspaceSelectionGuidance().Should().Contain("writeback");
        resources.GetSimulationToolSelectionGuidance().Should().Contain("not full Magic rules simulation");
        resources.GetOperationModeGuidance().Should().Contain("\"effectiveMode\": \"plan\"");
        resources.GetDeckIntentGuidance().Should().Contain("MTG MCP Deck Intent");
        resources.GetFormatRules("commander").Should().Contain("100 included cards");
        resources.GetFormatRules("standard").Should().Contain("Standard");
        resources.GetFormatRules("modern").Should().Contain("Modern");
        resources.GetFormatRules("legacy").Should().Contain("Legacy");
        resources.GetFormatRules("pauper").Should().Contain("Pauper");
        resources.GetFormatRules("limited").Should().Contain("Generic constructed");
    }

    /// <summary>
    /// Verifies diagnostic resources report effective non-secret state and attributed sources.
    /// </summary>
    [Fact]
    public void DiagnosticResources_ReturnRedactedStructuredJson()
    {
        using IHost host = MtgMcpHost.Build([]);
        MtgResources resources = ActivatorUtilities.CreateInstance<MtgResources>(host.Services);

        using JsonDocument configuration = JsonDocument.Parse(resources.GetEffectiveConfiguration());
        using JsonDocument sources = JsonDocument.Parse(resources.GetCorpusSources());
        using JsonDocument server = JsonDocument.Parse(resources.GetServerInfo());

        configuration.RootElement.GetProperty("MtgMcp:OperationMode").GetString().Should().Be("plan");
        configuration.RootElement.GetRawText().Should().NotContain("Password");
        sources.RootElement.GetProperty("sources").GetArrayLength().Should().BeGreaterThan(0);
        server.RootElement.GetProperty("assemblyName").GetString().Should().Be("MtgMcp.App");
        server.RootElement.GetProperty("operationMode").GetString().Should().Be("plan");
    }

    /// <summary>
    /// Verifies provider-auth resources reject blank and unknown provider keys before adapter calls.
    /// </summary>
    [Fact]
    public async Task ProviderAuthResource_RejectsUnsupportedProviderKeys()
    {
        using IHost host = MtgMcpHost.Build([]);
        MtgResources resources = ActivatorUtilities.CreateInstance<MtgResources>(host.Services);

        Func<Task> blank = () => resources.GetProviderAuthStatusAsync(
            " ",
            TestContext.Current.CancellationToken);
        Func<Task> unknown = () => resources.GetProviderAuthStatusAsync(
            "moxfield",
            TestContext.Current.CancellationToken);

        await blank.Should().ThrowAsync<ArgumentException>().WithParameterName("provider");
        await unknown.Should().ThrowAsync<ArgumentException>().WithParameterName("provider");
    }

    /// <summary>
    /// Verifies workspace resources serialize their index and preserve missing-workspace failures.
    /// </summary>
    [Fact]
    public async Task WorkspaceResources_ListAndPreserveMissingWorkspaceErrors()
    {
        using IHost host = MtgMcpHost.Build([]);
        MtgResources resources = ActivatorUtilities.CreateInstance<MtgResources>(host.Services);
        const string workspaceId = "missing-resource-workspace";

        using JsonDocument index = JsonDocument.Parse(await resources.ListWorkspacesAsync());
        index.RootElement.TryGetProperty("workspaces", out JsonElement workspaces).Should().BeTrue();
        workspaces.ValueKind.Should().Be(JsonValueKind.Array);

        List<Func<Task>> actions =
        [
            async () => _ = await resources.GetDeckAsync(workspaceId),
            async () => _ = await resources.GetDeckSummaryAsync(workspaceId),
            async () => _ = await resources.GetDeckStateAsync(workspaceId),
            async () => _ = await resources.GetDeckIntentAsync(workspaceId),
            async () => _ = await resources.GetAssistantContextAsync(workspaceId)
        ];

        foreach (Func<Task> action in actions)
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        }
    }

    /// <summary>
    /// Verifies supported provider-auth resources return redacted status without contacting remote APIs.
    /// </summary>
    [Fact]
    public async Task ProviderAuthResource_ReturnsSupportedProviderStatuses()
    {
        using IHost host = MtgMcpHost.Build([]);
        MtgResources resources = ActivatorUtilities.CreateInstance<MtgResources>(host.Services);

        using JsonDocument archidekt = JsonDocument.Parse(await resources.GetProviderAuthStatusAsync(" ARCHIDEKT "));
        using JsonDocument playgroup = JsonDocument.Parse(await resources.GetProviderAuthStatusAsync("playgroup"));

        archidekt.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        playgroup.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        archidekt.RootElement.GetRawText().Should().NotContain("test-password");
        playgroup.RootElement.GetRawText().Should().NotContain("test-api-key");
    }
}
