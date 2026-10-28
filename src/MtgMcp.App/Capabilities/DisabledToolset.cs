namespace MtgMcp.App.Capabilities;

/// <summary>
/// Records one toolset that startup turned off because its provider credentials are missing or unusable.
/// </summary>
internal sealed record DisabledToolset(CapabilityToolset Toolset, string Reason)
{
    /// <summary>
    /// Names the reason used when no credential setting is present.
    /// </summary>
    internal const string CredentialsMissing = "credentials-missing";

    /// <summary>
    /// Names the reason used when a credential setting is present but the configuration is invalid.
    /// </summary>
    internal const string CredentialsInvalid = "credentials-invalid";

    /// <summary>
    /// Describes the settings that turn the toolset on, without any setting value.
    /// </summary>
    internal string DescribeFix()
    {
        return Toolset switch
        {
            CapabilityToolset.Archidekt =>
                "Set ARCHIDEKT:USERNAME and ARCHIDEKT:PASSWORD, or ARCHIDEKT:CREDENTIALS_FILE, " +
                "or create ~/.mtg-mcp/archidekt.json.",
            CapabilityToolset.Playgroup =>
                "Set PLAYGROUP:API_KEY or PLAYGROUP:CREDENTIALS_FILE, or create ~/.mtg-mcp/playgroup.json.",
            _ => "No credential setting applies.",
        };
    }

    /// <summary>
    /// Builds the one-line startup notice: the toolset, why it is off, the settings to add, and how to write a
    /// setting as an environment variable.
    /// </summary>
    internal string DescribeNotice()
    {
        string name = CapabilityToolsetPolicy.Format(Toolset);
        string state = Reason == CredentialsInvalid
            ? "its credential configuration is invalid"
            : "no credentials are configured";
        string example = Toolset == CapabilityToolset.Playgroup
            ? "MTGMCP__PLAYGROUP__API_KEY"
            : "MTGMCP__ARCHIDEKT__USERNAME";
        return $"Toolset '{name}' is off because {state} ({Reason}). {DescribeFix()} " +
            $"As an environment variable, add the MTGMCP__ prefix and use double underscores, for example {example}.";
    }
}
