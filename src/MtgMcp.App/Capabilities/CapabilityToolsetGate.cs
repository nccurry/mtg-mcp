namespace MtgMcp.App.Capabilities;

/// <summary>
/// Records one toolset that startup turned off because its provider credentials are missing or unusable.
/// </summary>
internal sealed record CapabilityToolsetGate(CapabilityToolset Toolset, string Reason)
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
    /// Builds the one-line startup notice that names the toolset, the reason, and the settings to add.
    /// </summary>
    internal string DescribeNotice()
    {
        string name = CapabilityToolsetPolicy.Format(Toolset);
        string state = Reason == CredentialsInvalid ? "its configuration is invalid" : "no credentials are configured";
        return $"Toolset '{name}' is off ({Reason}): {state}. {DescribeFix()} " +
            "In an environment variable, write the setting with MTGMCP__ and double underscores, " +
            "for example MTGMCP__ARCHIDEKT__USERNAME.";
    }
}
