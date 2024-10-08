﻿using System;
using System.Net;
using System.Text.Json;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models;

/// <summary>
/// The api response.
/// </summary>
/// <typeparam name="T">The type of response.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResponse{T}"/> class.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="response">The http response.</param>
    public ApiResponse(T data, HttpResponse response)
    {
        Data = data;
        Code = response.Code;
        Body = response.Body;

        if (!Ok && string.IsNullOrWhiteSpace(Body) && !string.IsNullOrWhiteSpace(response.Reason))
        {
            Body = response.Reason;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiResponse{T}"/> class.
    /// </summary>
    /// <param name="response">The http response.</param>
    /// <param name="context">The request context.</param>
    public ApiResponse(HttpResponse response, params string[] context)
    {
        Code = response.Code;
        Body = response.Body;

        if (!Ok && string.IsNullOrWhiteSpace(Body) && !string.IsNullOrWhiteSpace(response.Reason))
        {
            Body = response.Reason;
        }

        if (!Ok)
        {
            // don't bother parsing json if HTTP status code is bad
            return;
        }

        try
        {
            Data = JsonSerializer.Deserialize<T>(Body) ?? default;
        }
        catch (Exception ex)
        {
            throw new JsonException($"Failed to parse response, code: {Code}, context: {string.Join(", ", context)}, body: \n{(string.IsNullOrWhiteSpace(Body) ? "\"\"" : Body)}", ex);
        }
    }

    /// <summary>
    /// Gets the status code.
    /// </summary>
    public HttpStatusCode Code { get; }

    /// <summary>
    /// Gets the response body.
    /// </summary>
    public string Body { get; } = string.Empty;

    /// <summary>
    /// Gets the deserialized data.
    /// </summary>
    public T? Data { get; }

    /// <summary>
    /// Gets a value indicating whether the request was successful.
    /// </summary>
    public bool Ok => (int)Code >= 200 && (int)Code <= 299;
}
