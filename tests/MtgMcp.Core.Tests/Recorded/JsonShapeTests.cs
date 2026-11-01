namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Verifies the shape comparison that holds a test double to a recorded response.
/// </summary>
public sealed class JsonShapeTests
{
    /// <summary>
    /// Holds a recording with a scalar, a many-item array, and an empty array.
    /// </summary>
    private const string Recorded = "{\"a\":[1,2],\"b\":\"x\",\"c\":[]}";

    /// <summary>
    /// Verifies a different value of the same kind and an extra field do not count as divergence.
    /// </summary>
    [Fact]
    public void Divergences_SameShapeWithExtraField_IsEmpty()
    {
        Assert.Empty(JsonShape.Divergences(Recorded, "{\"a\":[1,2,3],\"b\":\"y\",\"c\":[],\"d\":1}", []));
    }

    /// <summary>
    /// Verifies a dropped field is reported.
    /// </summary>
    [Fact]
    public void Divergences_DroppedField_ReportsMissingPath()
    {
        Assert.Equal(
            ["$.b is missing; recorded string"],
            JsonShape.Divergences(Recorded, "{\"a\":[1,2],\"c\":[]}", []));
    }

    /// <summary>
    /// Verifies a renamed field is reported as a missing recorded path.
    /// </summary>
    [Fact]
    public void Divergences_RenamedField_ReportsMissingPath()
    {
        Assert.Equal(
            ["$.b is missing; recorded string"],
            JsonShape.Divergences(Recorded, "{\"a\":[1,2],\"bb\":\"x\",\"c\":[]}", []));
    }

    /// <summary>
    /// Verifies one item where the recording has many is reported.
    /// </summary>
    [Fact]
    public void Divergences_OneItemWhereRecordingHasMany_ReportsBucket()
    {
        List<string> problems = JsonShape.Divergences(Recorded, "{\"a\":[1],\"b\":\"x\",\"c\":[]}", []);

        Assert.Contains("$.a is array:1; recorded array:many", problems);
        Assert.Contains("$.a never shows array:many; seen array:1", problems);
    }

    /// <summary>
    /// Verifies a value kind the recording does not show is reported.
    /// </summary>
    [Fact]
    public void Divergences_KindMismatch_ReportsKind()
    {
        Assert.Equal(
            ["$.b is number; recorded string"],
            JsonShape.Divergences(Recorded, "{\"a\":[1,2],\"b\":1,\"c\":[]}", []));
    }

    /// <summary>
    /// Verifies a path listed as ignored is skipped even when the actual JSON lacks it.
    /// </summary>
    [Fact]
    public void Divergences_IgnoredPathAbsentFromActual_IsSkipped()
    {
        Assert.Empty(JsonShape.Divergences(Recorded, "{\"a\":[1,2],\"c\":[]}", ["$.b"]));
    }

    /// <summary>
    /// Verifies an ignored path the recording does not have is reported as stale.
    /// </summary>
    [Fact]
    public void Divergences_IgnoredPathNotInRecording_ReportsStaleIgnore()
    {
        Assert.Equal(
            ["$.zzz is ignored but the recording has no such path"],
            JsonShape.Divergences(Recorded, Recorded, ["$.zzz"]));
    }
}
