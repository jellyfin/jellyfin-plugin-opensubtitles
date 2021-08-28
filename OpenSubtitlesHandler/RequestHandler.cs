using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSubtitlesHandler
{
    public static class RequestHandler
    {
        private const string BaseApiUrl = "https://api.opensubtitles.com/api/v1";

        // header rate limits (5/1s & 240/1 min)
        private static int _hRemaining = -1;
        private static int _hReset = -1;
        // 40/10s limits
        private static DateTime _windowStart = DateTime.MinValue;
        private static int _requestCount;

        public static async Task<(string response, HttpStatusCode statusCode)> SendRequestAsync(string endpoint, HttpMethod method, object body, Dictionary<string, string> headers, string apiKey, CancellationToken cancellationToken)
        {
            var url = endpoint.StartsWith("/") ? BaseApiUrl + endpoint : endpoint;
            var api = url.StartsWith(BaseApiUrl);

            headers ??= new Dictionary<string, string>();

            if (api)
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

            var (response, responseHeaders, httpStatusCode) = await Util.Instance.SendRequestAsync(url, method, body, headers, cancellationToken).ConfigureAwait(false);

            if (!api)
            {
                return (response, httpStatusCode);
            }

            _requestCount++;

            if (responseHeaders.TryGetValue("x-ratelimit-remaining-second", out var value))
            {
                int.TryParse(value, out _hRemaining);
            }

            if (responseHeaders.TryGetValue("ratelimit-reset", out value))
            {
                int.TryParse(value, out _hReset);
            }

            if (httpStatusCode != HttpStatusCode.TooManyRequests)
            {
                return (response, httpStatusCode);
            }

            var time = _hReset == -1 ? 5 : _hReset;

            await Task.Delay(time * 1000, cancellationToken).ConfigureAwait(false);

            return await SendRequestAsync(endpoint, method, body, headers, apiKey, cancellationToken).ConfigureAwait(false);
        }
    }
}
