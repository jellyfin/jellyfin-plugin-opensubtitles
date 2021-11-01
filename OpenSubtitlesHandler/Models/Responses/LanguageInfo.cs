using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The language info.
    /// </summary>
    public class LanguageInfo
    {
        /// <summary>
        /// Gets or sets the language code.
        /// </summary>
        [JsonPropertyName("language_code")]
        public string? Code { get; set; }
    }
}
