using MtgMcp.Archidekt;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Configuration;

/// <summary>
/// Records, once at startup, whether each provider has credentials configured. Only the presence of settings or
/// files is checked: no provider is called and no secret is read.
/// </summary>
internal sealed record ProviderCredentialPresence(bool Archidekt, bool Playgroup)
{
    /// <summary>
    /// Works out presence from the resolved provider options. Archidekt needs a username and a password, or a
    /// credential file that exists. Playgroup needs an API key, or a credential file that exists.
    /// </summary>
    internal static ProviderCredentialPresence From(ArchidektOptions archidekt, PlaygroupOptions playgroup)
    {
        ArgumentNullException.ThrowIfNull(archidekt);
        ArgumentNullException.ThrowIfNull(playgroup);
        bool pair = !string.IsNullOrWhiteSpace(archidekt.Username) && !string.IsNullOrWhiteSpace(archidekt.Password);
        return new ProviderCredentialPresence(
            pair || FileExists(archidekt.CredentialsFile),
            !string.IsNullOrWhiteSpace(playgroup.ApiKey) || FileExists(playgroup.CredentialsFile));
    }

    /// <summary>
    /// Reports whether a named credential file exists, without opening it.
    /// </summary>
    private static bool FileExists(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }
}
