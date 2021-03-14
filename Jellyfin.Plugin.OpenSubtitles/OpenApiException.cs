using System;

namespace Jellyfin.Plugin.OpenSubtitles
{
    /// <summary>
    /// Open api exception.
    /// </summary>
    public class OpenApiException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenApiException"/> class.
        /// </summary>
        /// <param name="msg">The exception message.</param>
        public OpenApiException(string msg)
            : base(msg)
        {
        }
    }
}