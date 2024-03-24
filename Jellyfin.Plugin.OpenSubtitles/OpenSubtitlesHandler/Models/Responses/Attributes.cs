using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;

/// <summary>
/// The attributes response.
/// </summary>
public class Attributes
{
    /// <summary>
    /// Gets or sets the download count.
    /// </summary>
    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets the subtitle rating.
    /// </summary>
    [JsonPropertyName("ratings")]
    public float Ratings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this subtitle was from a trusted uploader.
    /// </summary>
    [JsonPropertyName("from_trusted")]
    public bool? FromTrusted { get; set; }

    /// <summary>
    /// Gets or sets the subtitle upload date.
    /// </summary>
    [JsonPropertyName("upload_date")]
    public DateTime UploadDate { get; set; }

    /// <summary>
    /// Gets or sets the release this subtitle is for.
    /// </summary>
    [JsonPropertyName("release")]
    public string? Release { get; set; }

    /// <summary>
    /// Gets or sets the comments for the subtitle.
    /// </summary>
    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    /// <summary>
    /// Gets or sets the uploader.
    /// </summary>
    [JsonPropertyName("uploader")]
    public Uploader? Uploader { get; set; }

    /// <summary>
    /// Gets or sets the feature details.
    /// </summary>
    [JsonPropertyName("feature_details")]
    public FeatureDetails? FeatureDetails { get; set; }

    /// <summary>
    /// Gets or sets the list of files.
    /// </summary>
    [JsonPropertyName("files")]
    public IReadOnlyList<SubFile> Files { get; set; } = Array.Empty<SubFile>();

    /// <summary>
    /// Gets or sets a value indicating whether this was a hash match.
    /// </summary>
    [JsonPropertyName("moviehash_match")]
    public bool? MovieHashMatch { get; set; }
}
