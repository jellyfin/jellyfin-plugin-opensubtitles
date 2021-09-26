using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenSubtitlesHandler.Models;

namespace OpenSubtitlesHandler
{
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
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentException">API Key is empty.</exception>
        public static async Task<HttpResponse> SendRequestAsync(string endpoint, HttpMethod method, object? body, Dictionary<string, string>? headers, string? apiKey, CancellationToken cancellationToken)
        {
            var url = endpoint.StartsWith('/') ? BaseApiUrl + endpoint : endpoint;
            var isFullUrl = url.StartsWith(BaseApiUrl, StringComparison.OrdinalIgnoreCase);

            headers ??= new Dictionary<string, string>();

            if (isFullUrl)
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new ArgumentException("Provided API key is blank", nameof(apiKey));
                }

                if (!headers.ContainsKey("Api-Key"))
                {
                    headers.Add("Api-Key", apiKey);
                }

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
            }

            var (response, responseHeaders, httpStatusCode) = await OpenSubtitlesRequestHelper.Instance!.SendRequestAsync(url, method, body, headers, cancellationToken).ConfigureAwait(false);

            if (!isFullUrl)
            {
                return new HttpResponse
                {
                    Body = response,
                    Code = httpStatusCode
                };
            }

            _requestCount++;

            if (responseHeaders.TryGetValue("x-ratelimit-remaining-second", out var value))
            {
                _ = int.TryParse(value, out _hRemaining);
            }

            if (responseHeaders.TryGetValue("ratelimit-reset", out value))
            {
                _ = int.TryParse(value, out _hReset);
            }

            if (httpStatusCode != HttpStatusCode.TooManyRequests)
            {
                if (!responseHeaders.TryGetValue("x-reason", out value))
                {
                    value = string.Empty;
                }

                return new HttpResponse
                {
                    Body = response,
                    Code = httpStatusCode,
                    Reason = value
                };
            }

            var time = _hReset == -1 ? 5 : _hReset;

            await Task.Delay(time * 1000, cancellationToken).ConfigureAwait(false);

            return await SendRequestAsync(endpoint, method, body, headers, apiKey, cancellationToken).ConfigureAwait(false);
        }
    }
}
