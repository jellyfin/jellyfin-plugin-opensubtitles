using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OpenSubtitles.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
