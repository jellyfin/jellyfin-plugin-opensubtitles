using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The encapsulated user info.
    /// </summary>
    public class EncapsulatedUserInfo
    {
        /// <summary>
        /// Gets or sets the user info data.
        /// </summary>
        [JsonPropertyName("data")]
        public UserInfo? Data { get; set; }
    }
}
