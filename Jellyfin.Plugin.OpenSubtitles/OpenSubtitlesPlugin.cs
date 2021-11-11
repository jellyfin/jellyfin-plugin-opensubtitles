using System;
using System.Collections.Generic;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles
{
    /// <summary>
    /// The open subtitles plugin.
    /// </summary>
    public class OpenSubtitlesPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Default API key to use when performing an API call.
        /// </summary>
        public const string ApiKey = "gUCLWGoAg2PmyseoTM0INFFVPcDCeDlT";

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSubtitlesPlugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public OpenSubtitlesPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <inheritdoc />
        public override string Name
            => "Open Subtitles";

        /// <inheritdoc />
        public override Guid Id
            => Guid.Parse("4b9ed42f-5185-48b5-9803-6ff2989014c4");

        /// <summary>
        /// Gets the plugin instance.
        /// </summary>
        public static OpenSubtitlesPlugin? Instance { get; private set; }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "opensubtitles",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.opensubtitles.html",
                },
                new PluginPageInfo
                {
                    Name = "opensubtitlesjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.opensubtitles.js"
                }
            };
        }
    }
}
