using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;

/// <summary>
/// The search result.
/// </summary>
public class SearchResult
{
    /// <summary>
    /// Gets or sets the total page count.
    /// </summary>
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    /// <summary>
    /// Gets or sets the current page.
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets the list of response data.
    /// </summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<ResponseData> Data { get; set; } = Array.Empty<ResponseData>();
}
