using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the hash of the request arguments that a cursor binds to.
/// </summary>
public sealed class RequestHashTests
{
    /// <summary>
    /// Verifies the request hash depends on the parts and their order, and is stable.
    /// </summary>
    [Fact]
    public void Of_IsStable_AndDependsOnThePartsAndTheirOrder()
    {
        string first = RequestHash.Of(("a", typeof(string)), (1, typeof(int)));

        Assert.Equal(first, RequestHash.Of(("a", typeof(string)), (1, typeof(int))));
        Assert.NotEqual(first, RequestHash.Of((1, typeof(int)), ("a", typeof(string))));
        Assert.NotEqual(first, RequestHash.Of(("a", typeof(string)), (2, typeof(int))));
        Assert.Equal(32, first.Length);
    }
}
