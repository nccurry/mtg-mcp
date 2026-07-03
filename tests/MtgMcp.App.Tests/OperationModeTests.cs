using MtgMcp.App.Configuration;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies operation-mode normalization and mutation authority.
/// </summary>
public sealed class OperationModeTests
{
    /// <summary>
    /// Verifies the local default and every accepted normalized mode value.
    /// </summary>
    [Fact]
    public void Parse_ValidValues_NormalizesMode()
    {
        (string? ConfiguredValue, OperationMode ExpectedMode, string ExpectedValue)[] cases =
        [
            (null, OperationMode.Local, "local"),
            (" ", OperationMode.Local, "local"),
            ("READ-ONLY", OperationMode.ReadOnly, "read-only"),
            (" local ", OperationMode.Local, "local"),
            ("Remote", OperationMode.Remote, "remote"),
        ];

        foreach ((string? configuredValue, OperationMode expectedMode, string expectedValue) in cases)
        {
            OperationResult<OperationMode> result = OperationModeParser.Parse(configuredValue);
            OperationSuccess<OperationMode> success =
                Assert.IsType<OperationSuccess<OperationMode>>(result.Value);

            Assert.Equal(expectedMode, success.Data);
            Assert.Equal(expectedValue, OperationModeParser.Format(success.Data));
        }
    }

    /// <summary>
    /// Verifies that an unknown mode produces a stable sanitized invalid-input result.
    /// </summary>
    [Fact]
    public void Parse_UnknownValue_ReturnsInvalidInput()
    {
        const string unknownMode = "secret-mode-value";

        OperationResult<OperationMode> result = OperationModeParser.Parse(unknownMode);
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);

        Assert.Equal("invalid-operation-mode", invalid.ReasonCode);
        Assert.DoesNotContain(unknownMode, invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies read-only, local, and remote authority across every operation class.
    /// </summary>
    [Fact]
    public void Guard_ModeAndRequirement_ReturnExpectedAuthority()
    {
        (OperationMode Mode, OperationRequirement Requirement, bool Expected)[] cases =
        [
            (OperationMode.ReadOnly, OperationRequirement.Read, true),
            (OperationMode.ReadOnly, OperationRequirement.ProviderRead, true),
            (OperationMode.ReadOnly, OperationRequirement.LocalWrite, false),
            (OperationMode.ReadOnly, OperationRequirement.RemoteWrite, false),
            (OperationMode.Local, OperationRequirement.Read, true),
            (OperationMode.Local, OperationRequirement.ProviderRead, true),
            (OperationMode.Local, OperationRequirement.LocalWrite, true),
            (OperationMode.Local, OperationRequirement.RemoteWrite, false),
            (OperationMode.Remote, OperationRequirement.Read, true),
            (OperationMode.Remote, OperationRequirement.ProviderRead, true),
            (OperationMode.Remote, OperationRequirement.LocalWrite, true),
            (OperationMode.Remote, OperationRequirement.RemoteWrite, true),
        ];

        foreach ((OperationMode mode, OperationRequirement requirement, bool expected) in cases)
        {
            Assert.Equal(expected, OperationModeGuard.Allows(mode, requirement));
        }
    }

    /// <summary>
    /// Verifies that undefined enum values cannot acquire authority or a public name.
    /// </summary>
    [Fact]
    public void UndefinedMode_IsRejected()
    {
        OperationMode undefined = (OperationMode)999;

        Assert.False(OperationModeGuard.Allows(undefined, OperationRequirement.Read));
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationModeParser.Format(undefined));
    }
}
