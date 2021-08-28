using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class SearchResult
    {
        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }
        [JsonPropertyName("page")]
        public int Page { get; set; }
        [JsonPropertyName("data")]
        public List<Data> Data { get; set; }
    }
}
