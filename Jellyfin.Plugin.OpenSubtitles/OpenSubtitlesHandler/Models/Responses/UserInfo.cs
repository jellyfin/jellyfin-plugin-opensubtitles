using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;

/// <summary>
/// The user info.
/// </summary>
public class UserInfo
{
    /// <summary>
    /// Gets or sets the allowed downloads count.
    /// </summary>
    [JsonPropertyName("allowed_downloads")]
    public int AllowedDownloads { get; set; }

    /// <summary>
    /// Gets or sets the remaining download count.
    /// </summary>
    [JsonPropertyName("remaining_downloads")]
    public int? RemainingDownloads { get; set; }

    /// <summary>
    /// Gets or sets the timestamp in which the download count resets.
    /// </summary>
    [JsonPropertyName("reset_time_utc")]
    public DateTime ResetTime { get; set; }
}
