namespace MtgMcp.E2E.Tests;

/// <summary>
/// Says which provider credentials an isolated test server starts with. Tests state this so a developer's own
/// credential files never change which toolsets are on.
/// </summary>
public enum McpCredentials
{
    /// <summary>
    /// Neither provider has credentials, so the Archidekt and Playgroup toolsets are off.
    /// </summary>
    None,

    /// <summary>
    /// Only Archidekt has credentials; the values are fake and no test calls the provider.
    /// </summary>
    ArchidektOnly,

    /// <summary>
    /// Both providers have credentials, so every toolset can be on. The Archidekt values are fake and the
    /// Playgroup credential file is empty, so provider calls report that the service is unavailable.
    /// </summary>
    Both,
}
