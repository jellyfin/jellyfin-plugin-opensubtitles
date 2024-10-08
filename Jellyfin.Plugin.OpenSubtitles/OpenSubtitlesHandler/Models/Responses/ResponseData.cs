﻿using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;

/// <summary>
/// The response data.
/// </summary>
public class ResponseData
{
    /// <summary>
    /// Gets or sets the response attributes.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Attributes? Attributes { get; set; }
}
