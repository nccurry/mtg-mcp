using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Covers shared option helpers that are used across adapter projects.
/// </summary>
public sealed class OptionsTests
{
    /// <summary>
    /// Verifies that the default User-Agent advertises the package and project URL.
    /// </summary>
    [Fact]
    public void UserAgent_IncludesPackageAndProjectUrl()
    {
        MtgMcpHttpDefaults.UserAgent.Should().StartWith("mtg-mcp/");
        MtgMcpHttpDefaults.UserAgent.Should().Contain($"(+{MtgMcpHttpDefaults.ProjectUrl})");
    }

    /// <summary>
    /// Verifies that configured User-Agent values replace earlier client defaults.
    /// </summary>
    [Fact]
    public void ApplyUserAgent_UsesConfiguredValue()
    {
        HttpClient httpClient = new();
        MtgMcpHttpDefaults.ApplyUserAgent(httpClient, "old-agent/1.0");

        MtgMcpHttpDefaults.ApplyUserAgent(httpClient, " mtg-mcp-test/1.0 ");

        httpClient.DefaultRequestHeaders.UserAgent.ToString().Should().Be("mtg-mcp-test/1.0");
    }
}
