using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;

/// <summary>
/// The request handler.
/// </summary>
public static class RequestHandler
{
    private const string BaseApiUrl = "https://api.opensubtitles.com/api/v1";
    private const int RetryLimit = 5;

    /// <summary>
    /// Send the request.
    /// </summary>
    /// <param name="endpoint">The endpoint to send request to.</param>
    /// <param name="method">The method.</param>
    /// <param name="body">The request body.</param>
    /// <param name="headers">The headers.</param>
    /// <param name="attempt">The request attempt key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="isFullUrl">The flag to not append baseUrl.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentException">API Key is empty.</exception>
    public static async Task<HttpResponse> SendRequestAsync(
        string endpoint,
        HttpMethod method,
        object? body,
        Dictionary<string, string>? headers,
        int attempt,
        CancellationToken cancellationToken,
        bool isFullUrl = false)
    {
        headers ??= new Dictionary<string, string>();
        headers.TryAdd("Api-Key", OpenSubtitlesPlugin.ApiKey);

        var url = isFullUrl ? endpoint : BaseApiUrl + endpoint;
        var response = await OpenSubtitlesRequestHelper.Instance!.SendRequestAsync(url, method, body, headers, cancellationToken).ConfigureAwait(false);

        if (response.statusCode == HttpStatusCode.TooManyRequests
            && attempt < RetryLimit
            && response.headers.TryGetValue(HeaderNames.RetryAfter, out var retryAfterStr)
            && int.TryParse(retryAfterStr, out var retryAfter))
        {
            await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken).ConfigureAwait(false);
            return await SendRequestAsync(endpoint, method, body, headers, attempt + 1, cancellationToken, isFullUrl).ConfigureAwait(false);
        }

        if (response.statusCode == HttpStatusCode.BadGateway && attempt < RetryLimit)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            return await SendRequestAsync(endpoint, method, body, headers, attempt + 1, cancellationToken, isFullUrl).ConfigureAwait(false);
        }

        if (!response.headers.TryGetValue("x-reason", out var responseReason))
        {
            responseReason = string.Empty;
        }

        return new HttpResponse
        {
            Body = response.body,
            Code = response.statusCode,
            Reason = responseReason
        };
    }

    /// <summary>
    /// Append the given query keys and values to the URI.
    /// </summary>
    /// <param name="path">The base URI.</param>
    /// <param name="param">A dictionary of query keys and values to append.</param>
    /// <returns>The combined result.</returns>
    public static string AddQueryString(string path, Dictionary<string, string> param)
    {
        if (param.Count == 0)
        {
            return path;
        }

        var url = new StringBuilder(path);
        url.Append('?');
        foreach (var (key, value) in param.OrderBy(x => x.Key))
        {
            url.Append(HttpUtility.UrlEncode(key.ToLowerInvariant()))
                .Append('=')
                .Append(HttpUtility.UrlEncode(value.ToLowerInvariant()))
                .Append('&');
        }

        url.Length -= 1; // Remove last &
        return url.ToString();
    }
}
