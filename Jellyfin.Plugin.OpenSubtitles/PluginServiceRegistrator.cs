using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.OpenSubtitles;

/// <summary>
/// Register subtitle provider.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<ISubtitleProvider, OpenSubtitleDownloader>();
    }
}
