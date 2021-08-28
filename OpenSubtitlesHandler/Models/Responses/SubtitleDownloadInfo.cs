using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The subtitle download info.
    /// </summary>
    public class SubtitleDownloadInfo
    {
        /// <summary>
        /// Gets or sets the subtitle download link.
        /// </summary>
        [JsonPropertyName("link")]
        public string? Link { get; set; }

        /// <summary>
        /// Gets or sets the remaining download count.
        /// </summary>
        [JsonPropertyName("remaining")]
        public int Remaining { get; set; }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
