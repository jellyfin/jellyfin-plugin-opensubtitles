using System.Net;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models
{
    /// <summary>
    /// The http response.
    /// </summary>
    public class HttpResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpResponse"/> class.
        /// </summary>
        public HttpResponse()
        {
        }

        /// <summary>
        /// Gets the status code.
        /// </summary>
        public HttpStatusCode Code { get; init; }

        /// <summary>
        /// Gets the response body.
        /// </summary>
        public string Body { get; init; } = string.Empty;

        /// <summary>
        /// Gets the response fail reason.
        /// </summary>
        public string Reason { get; init; } = string.Empty;
    }
}
