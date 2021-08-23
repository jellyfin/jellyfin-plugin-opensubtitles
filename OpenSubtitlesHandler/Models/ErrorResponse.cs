using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models
{
    public class ErrorResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
