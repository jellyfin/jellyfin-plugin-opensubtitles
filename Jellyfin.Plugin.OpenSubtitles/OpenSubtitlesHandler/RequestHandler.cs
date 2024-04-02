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

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;

/// <summary>
/// The request handler.
/// </summary>
public static class RequestHandler
{
    private const string BaseApiUrl = "https://api.opensubtitles.com/api/v1";

    // header rate limits (5/1s & 240/1 min)
    private static int _hRemaining = -1;
    private static int _hReset = -1;
    // 40/10s limits
    private static DateTime _windowStart = DateTime.MinValue;
    private static int _requestCount;

    /// <summary>
    /// Send the request.
    /// </summary>
    /// <param name="endpoint">The endpoint to send request to.</param>
    /// <param name="method">The method.</param>
    /// <param name="body">The request body.</param>
    /// <param name="headers">The headers.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="attempt">The request attempt key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentException">API Key is empty.</exception>
    public static async Task<HttpResponse> SendRequestAsync(
        string endpoint,
        HttpMethod method,
        object? body,
        Dictionary<string, string>? headers,
        string? apiKey,
        int attempt,
        CancellationToken cancellationToken)
    {
        headers ??= new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Provided API key is blank", nameof(apiKey));
        }

        headers.TryAdd("Api-Key", apiKey);
        if (_hRemaining == 0)
        {
            await Task.Delay(1000 * _hReset, cancellationToken).ConfigureAwait(false);
            _hRemaining = -1;
            _hReset = -1;
        }

        if (_requestCount == 40)
        {
            var diff = DateTime.UtcNow.Subtract(_windowStart).TotalSeconds;
            if (diff <= 10)
            {
                await Task.Delay(1000 * (int)Math.Ceiling(10 - diff), cancellationToken).ConfigureAwait(false);
                _hRemaining = -1;
                _hReset = -1;
            }
        }

        if (DateTime.UtcNow.Subtract(_windowStart).TotalSeconds >= 10)
        {
            _windowStart = DateTime.UtcNow;
            _requestCount = 0;
        }

        var response = await OpenSubtitlesRequestHelper.Instance!.SendRequestAsync(BaseApiUrl + endpoint, method, body, headers, cancellationToken).ConfigureAwait(false);

        _requestCount++;

        if (response.headers.TryGetValue("x-ratelimit-remaining-second", out var value))
        {
            _ = int.TryParse(value, out _hRemaining);
        }

        if (response.headers.TryGetValue("ratelimit-reset", out value))
        {
            _ = int.TryParse(value, out _hReset);
        }

        if (response.statusCode == HttpStatusCode.TooManyRequests && attempt <= 4)
        {
            var time = _hReset == -1 ? 5 : _hReset;

            await Task.Delay(time * 1000, cancellationToken).ConfigureAwait(false);

            return await SendRequestAsync(endpoint, method, body, headers, apiKey, attempt + 1, cancellationToken).ConfigureAwait(false);
        }

        if (response.statusCode == HttpStatusCode.BadGateway && attempt <= 3)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            return await SendRequestAsync(endpoint, method, body, headers, apiKey, attempt + 1, cancellationToken).ConfigureAwait(false);
        }

        if (!response.headers.TryGetValue("x-reason", out value))
        {
            value = string.Empty;
        }

        if ((int)response.statusCode >= 400 && (int)response.statusCode <= 499)
        {
            // Wait 1s after a 4xx response
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponse
        {
            Body = response.body,
            Code = response.statusCode,
            Reason = value
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
            url.Append(HttpUtility.UrlEncode(key))
                .Append('=')
                .Append(HttpUtility.UrlEncode(value))
                .Append('&');
        }

        url.Length -= 1; // Remove last &
        return url.ToString();
    }
}
