using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class Uploader
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
