using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App.Tests.Hosting;

/// <summary>
/// Verifies host-side simulation profile path expansion without external services.
/// </summary>
public sealed class SimulationProfileLoaderTests
{
    /// <summary>
    /// Verifies literal, environment-expanded, wildcard, blank, and missing paths are handled deterministically.
    /// </summary>
    [Fact]
    public void Load_ExpandsConfiguredPathsAndReportsUnreadableInputs()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-profiles-{Guid.NewGuid():N}");
        const string variable = "MTGMCP_TEST_PROFILE_ROOT";
        string? previousValue = Environment.GetEnvironmentVariable(variable);
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "fixture.json"),
            """
            { "id": "fixture-profile", "displayName": "Fixture profile" }
            """);
        File.WriteAllText(
            Path.Combine(root, "wildcard.extra.json"),
            """
            { "id": "wildcard-profile", "displayName": "Wildcard profile" }
            """);
        Environment.SetEnvironmentVariable(variable, root);

        try
        {
            MtgMcpOptions options = new();
            options.Simulation.ProfilePaths =
            [
                " ",
                $"%{variable}%\\fixture.json",
                $"%{variable}%\\*.extra.json",
                $"%{variable}%\\missing\\*.json",
                $"%{variable}%\\missing.json"
            ];

            SimulationProfileCatalog catalog = new SimulationProfileLoader(Options.Create(options)).Load();

            catalog.Profiles.Should().Contain(profile => profile.Id == "fixture-profile");
            catalog.Profiles.Should().Contain(profile => profile.Id == "wildcard-profile");
            catalog.ConfigurationWarnings.Should().Contain(message => message.Contains("was not found", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previousValue);
            Directory.Delete(root, recursive: true);
        }
    }
}
