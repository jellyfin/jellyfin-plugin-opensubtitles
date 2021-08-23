using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class FeatureDetails
    {
        [JsonPropertyName("feature_type")]
        public string FeatureType { get; set; }
        [JsonPropertyName("imdb_id")]
        public int ImdbId { get; set; }
        [JsonPropertyName("season_number")]
        public int? SeasonNumber { get; set; }
        [JsonPropertyName("episode_number")]
        public int? EpisodeNumber { get; set; }
    }
}
