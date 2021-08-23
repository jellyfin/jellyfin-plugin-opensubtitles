using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class Data
    {
        [JsonPropertyName("attributes")]
        public Attributes Attributes { get; set; }
    }
}
