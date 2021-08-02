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

        private static string _apiKey = string.Empty;
        // header rate limits (5/1s & 240/1 min)
        private static int _hRemaining = -1;
        private static int _hReset = -1;
        // 40/10s limits
        private static DateTime _windowStart = DateTime.MinValue;
        private static int _requestCount;

        public static void SetApiKey(string key)
        {
            if (_apiKey == string.Empty)
            {
                _apiKey = key;
            }
        }

        public static async Task<(string response, (int remaining, int reset) limits, Dictionary<string, string> headers, HttpStatusCode statusCode)> SendRequestAsync(string endpoint, HttpMethod method, object body, Dictionary<string, string> headers, string apiKey, CancellationToken cancellationToken)
        {
            var key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : _apiKey;

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new Exception("API key has not been set up");
            }

            headers ??= new Dictionary<string, string>();

            if (!headers.ContainsKey("Api-Key"))
            {
                headers.Add("Api-Key", key);
            }

            var url = endpoint.StartsWith("/") ? BaseApiUrl + endpoint : endpoint;
            var api = url.StartsWith(BaseApiUrl);

            if (api)
            {
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

            var (response, responseHeaders, httpStatusCode) = await Util.SendRequestAsync(url, method, body, headers, cancellationToken).ConfigureAwait(false);

            if (!api)
            {
                return (response, (_hRemaining, _hReset), responseHeaders, httpStatusCode);
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
                return (response, (_hRemaining, _hReset), responseHeaders, httpStatusCode);
            }

            var time = _hReset == -1 ? 5 : _hReset;

            Util.OnHttpUpdate($"Received TooManyRequests on {method} {endpoint}, trying again in {time}s");

            await Task.Delay(time * 1000, cancellationToken).ConfigureAwait(false);

            return await SendRequestAsync(endpoint, method, body, headers, key, cancellationToken).ConfigureAwait(false);
        }
    }
}
