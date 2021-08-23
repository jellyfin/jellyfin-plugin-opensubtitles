using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class SubFile
    {
        [JsonPropertyName("file_id")]
        public int FileId { get; set; }
    }
}
