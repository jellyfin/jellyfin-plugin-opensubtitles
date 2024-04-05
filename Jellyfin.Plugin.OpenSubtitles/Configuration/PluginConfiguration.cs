using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OpenSubtitles.Configuration;

/// <summary>
/// The plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the credentials are invalid.
    /// </summary>
    public bool CredentialsInvalid { get; set; } = false;
}
