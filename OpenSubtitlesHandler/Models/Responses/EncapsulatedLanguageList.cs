using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The encapsulated language list.
    /// </summary>
    public class EncapsulatedLanguageList
    {
        /// <summary>
        /// Gets or sets the language list.
        /// </summary>
        [JsonPropertyName("data")]
        public IReadOnlyList<LanguageInfo>? Data { get; set; }
    }
}
