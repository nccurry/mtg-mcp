using MtgMcp.Core.Results;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Defines the mutation authority granted to the running server.
/// </summary>
internal enum OperationMode
{
    /// <summary>
    /// Allows calculations, local reads, and explicit provider reads without any mutation.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Adds local persistence while continuing to forbid remote mutation.
    /// </summary>
    Local,

    /// <summary>
    /// Allows explicit local and remote mutation operations.
    /// </summary>
    Remote,
}

/// <summary>
/// Classifies the authority an operation requires independently of its capability area.
/// </summary>
internal enum OperationRequirement
{
    /// <summary>
    /// Reads local state or performs a pure calculation.
    /// </summary>
    Read,

    /// <summary>
    /// Reads from a remote provider without mutating provider state.
    /// </summary>
    ProviderRead,

    /// <summary>
    /// Mutates local files or databases.
    /// </summary>
    LocalWrite,

    /// <summary>
    /// Mutates state owned by a remote provider.
    /// </summary>
    RemoteWrite,
}

/// <summary>
/// Parses and formats the stable configuration vocabulary for operation modes.
/// </summary>
internal static class OperationModeParser
{
    /// <summary>
    /// Parses a configured mode, using local authority when the value is omitted.
    /// </summary>
    internal static OperationResult<OperationMode> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new OperationSuccess<OperationMode>(OperationMode.Local);
        }

        string normalized = value.Trim();
        if (normalized.Equals("read-only", StringComparison.OrdinalIgnoreCase))
        {
            return new OperationSuccess<OperationMode>(OperationMode.ReadOnly);
        }

        if (normalized.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return new OperationSuccess<OperationMode>(OperationMode.Local);
        }

        if (normalized.Equals("remote", StringComparison.OrdinalIgnoreCase))
        {
            return new OperationSuccess<OperationMode>(OperationMode.Remote);
        }

        return new OperationInvalidInput(
            "invalid-operation-mode",
            "Operation mode must be read-only, local, or remote.");
    }

    /// <summary>
    /// Formats a validated mode using its stable configuration value.
    /// </summary>
    internal static string Format(OperationMode mode)
    {
        return mode switch
        {
            OperationMode.ReadOnly => "read-only",
            OperationMode.Local => "local",
            OperationMode.Remote => "remote",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown operation mode."),
        };
    }
}

/// <summary>
/// Enforces operation requirements against the effective mutation authority.
/// </summary>
internal static class OperationModeGuard
{
    /// <summary>
    /// Reports whether a mode grants the requested class of operation.
    /// </summary>
    internal static bool Allows(OperationMode mode, OperationRequirement requirement)
    {
        return mode switch
        {
            OperationMode.ReadOnly => requirement is
                OperationRequirement.Read or OperationRequirement.ProviderRead,
            OperationMode.Local => requirement is
                OperationRequirement.Read or
                OperationRequirement.ProviderRead or
                OperationRequirement.LocalWrite,
            OperationMode.Remote => requirement is
                OperationRequirement.Read or
                OperationRequirement.ProviderRead or
                OperationRequirement.LocalWrite or
                OperationRequirement.RemoteWrite,
            _ => false,
        };
    }
}
