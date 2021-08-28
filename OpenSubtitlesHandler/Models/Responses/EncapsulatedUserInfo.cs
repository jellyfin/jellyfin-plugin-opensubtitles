using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class EncapsulatedUserInfo
    {
        [JsonPropertyName("data")]
        public UserInfo Data { get; set; }
    }
}
