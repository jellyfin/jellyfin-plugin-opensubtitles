using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;

/// <summary>
/// The uploader.
/// </summary>
public class Uploader
{
    /// <summary>
    /// Gets or sets the uploader name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
