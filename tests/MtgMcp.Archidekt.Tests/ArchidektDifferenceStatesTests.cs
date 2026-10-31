namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Checks which difference states count as a local change and which as a remote change.
/// </summary>
public sealed class ArchidektDifferenceStatesTests
{
    /// <summary>
    /// Verifies every named state is classified, and a concurrent change counts for both sides.
    /// </summary>
    [Theory]
    [InlineData(ArchidektDifferenceStates.LocalAdded, true, false)]
    [InlineData(ArchidektDifferenceStates.LocalRemoved, true, false)]
    [InlineData(ArchidektDifferenceStates.LocalChanged, true, false)]
    [InlineData(ArchidektDifferenceStates.LocalChangedUnverified, true, false)]
    [InlineData(ArchidektDifferenceStates.RemoteAdded, false, true)]
    [InlineData(ArchidektDifferenceStates.RemoteRemoved, false, true)]
    [InlineData(ArchidektDifferenceStates.RemoteChanged, false, true)]
    [InlineData(ArchidektDifferenceStates.ConcurrentChanged, true, true)]
    [InlineData(ArchidektDifferenceStates.Converged, false, false)]
    [InlineData("local-something-new", false, false)]
    public void Classification_FollowsTheNamedStates(string state, bool local, bool remote)
    {
        ArchidektDifference difference = new("/x", state, null, null, null);

        Assert.Equal(local, ArchidektDifferenceStates.IsLocalChange(difference));
        Assert.Equal(remote, ArchidektDifferenceStates.IsRemoteChange(difference));
    }

    /// <summary>
    /// Verifies a row that one side changed is named for that side and for whether it was added, removed, or changed.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, ArchidektDifferenceStates.LocalAdded)]
    [InlineData(true, true, false, ArchidektDifferenceStates.LocalRemoved)]
    [InlineData(true, true, true, ArchidektDifferenceStates.LocalChanged)]
    [InlineData(false, false, true, ArchidektDifferenceStates.RemoteAdded)]
    [InlineData(false, true, false, ArchidektDifferenceStates.RemoteRemoved)]
    [InlineData(false, true, true, ArchidektDifferenceStates.RemoteChanged)]
    public void OneSided_NamesTheSideAndTheKind(bool local, bool inBaseline, bool inChangedSide, string expected)
    {
        Assert.Equal(expected, ArchidektDifferenceStates.OneSided(local, inBaseline, inChangedSide));
    }
}
