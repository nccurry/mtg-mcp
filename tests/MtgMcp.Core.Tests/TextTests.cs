using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Covers shared text helpers.
/// </summary>
public sealed class TextTests
{
    /// <summary>
    /// Verifies that the first non-whitespace value is returned.
    /// </summary>
    [Fact]
    public void FirstNonEmpty_ReturnsFirstValueWithText()
    {
        MtgMcpText.FirstNonEmpty(null, "", "   ", "alpha", "beta")
            .Should().Be("alpha");
    }

    /// <summary>
    /// Verifies that all blank candidates return null.
    /// </summary>
    [Fact]
    public void FirstNonEmpty_ReturnsNullWhenAllValuesAreBlank()
    {
        MtgMcpText.FirstNonEmpty(null, "", "   ").Should().BeNull();
    }
}
