using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The sub file.
    /// </summary>
    public class SubFile
    {
        /// <summary>
        /// Gets or sets the file id.
        /// </summary>
        [JsonPropertyName("file_id")]
        public int FileId { get; set; }
    }
}
