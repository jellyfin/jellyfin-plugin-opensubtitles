using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class SubtitleDownloadInfo
    {
        [JsonPropertyName("link")]
        public string Link { get; set; }
        [JsonPropertyName("remaining")]
        public int Remaining { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
