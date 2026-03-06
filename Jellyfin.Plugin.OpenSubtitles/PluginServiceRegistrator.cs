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
        })
        .ConfigurePrimaryHttpMessageHandler(c =>
        {
            // Base handler with automatic GZIP/Deflate decompression
            var baseHandler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            // Wrap your existing rate-limited handler around the base handler
            return new ClientSideRateLimitedHandler(c.GetRequiredService<ILogger<ClientSideRateLimitedHandler>>(), baseHandler);
        });
        serviceCollection.AddSingleton<ISubtitleProvider, OpenSubtitleDownloader>();
    }
}
