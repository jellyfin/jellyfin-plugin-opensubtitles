using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class UserInfo
    {
        [JsonPropertyName("allowed_downloads")]
        public int AllowedDownloads { get; set; }
        [JsonPropertyName("remaining_downloads")]
        public int? RemainingDownloads { get; set; }
    }
}
