using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The feature details.
    /// </summary>
    public class FeatureDetails
    {
        /// <summary>
        /// Gets or sets the feature type.
        /// </summary>
        [JsonPropertyName("feature_type")]
        public string? FeatureType { get; set; }

        /// <summary>
        /// Gets or sets the imdb id.
        /// </summary>
        [JsonPropertyName("imdb_id")]
        public int ImdbId { get; set; }

        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("season_number")]
        public int? SeasonNumber { get; set; }

        /// <summary>
        /// Gets or sets the episode number.
        /// </summary>
        [JsonPropertyName("episode_number")]
        public int? EpisodeNumber { get; set; }
    }
}
