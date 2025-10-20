using System.Net.Http.Headers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenSubtitles;

/// <summary>
/// Register subtitle provider.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient(nameof(OpenSubtitles), c =>
        {
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                applicationHost.Name.Replace(' ', '_'),
                applicationHost.ApplicationVersionString));
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                "Jellyfin-Plugin-OpenSubtitles",
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString()));
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }).ConfigurePrimaryHttpMessageHandler(c => new ClientSideRateLimitedHandler(c.GetRequiredService<ILogger<ClientSideRateLimitedHandler>>()));

        serviceCollection.AddSingleton<ISubtitleProvider, OpenSubtitleDownloader>();
    }
}
