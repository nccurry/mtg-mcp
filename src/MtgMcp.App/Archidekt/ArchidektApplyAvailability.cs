using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Tells a preview's reader whether the tool that applies the previewed change is available in this session, and what
/// to ask the user to change when it is not. The apply tools are registered by operation mode, so a mode override
/// hides them from the tool list.
/// </summary>
internal static class ArchidektApplyAvailability
{
    /// <summary>
    /// Explains why <c>archidekt_push_apply</c> is not available, or returns null when it is. A push needs remote mode
    /// and Archidekt credentials.
    /// </summary>
    internal static string? PushNotice(OperationMode mode, ArchidektAuthStatus auth)
    {
        string? modeReason = OperationModeGuard.Allows(mode, OperationRequirement.RemoteWrite)
            ? null
            : $"the server runs in {OperationModeParser.Format(mode)} mode";
        string? credentialReason = auth.CredentialsConfigured ? null : "Archidekt credentials are not configured";
        if (modeReason is null && credentialReason is null)
        {
            return null;
        }

        string reasons = modeReason is not null && credentialReason is not null
            ? $"{modeReason}, and {credentialReason}"
            : modeReason ?? credentialReason!;
        string ask = modeReason is not null
            ? "Ask the user to start the server in remote mode (the default)"
            : "Ask the user to set up Archidekt credentials (see archidekt_auth_status)";
        return $"archidekt_push_apply is not available because {reasons}. {ask}.";
    }

    /// <summary>
    /// Explains why <c>archidekt_pull_apply</c> is not available, or returns null when it is. A pull needs local or
    /// remote mode. It reads a public deck without credentials, so credentials do not decide the answer.
    /// </summary>
    internal static string? PullNotice(OperationMode mode)
    {
        return OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite)
            ? null
            : $"archidekt_pull_apply is not available because the server runs in {OperationModeParser.Format(mode)} " +
                "mode. Ask the user to start the server in local or remote mode (remote is the default). A private " +
                "deck also needs Archidekt credentials.";
    }
}
