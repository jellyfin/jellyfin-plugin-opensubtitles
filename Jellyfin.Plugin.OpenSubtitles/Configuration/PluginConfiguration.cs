using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OpenSubtitles.Configuration
{
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
        /// Gets or sets the API Key.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
    }
}
