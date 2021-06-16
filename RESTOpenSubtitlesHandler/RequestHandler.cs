using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;

namespace RESTOpenSubtitlesHandler {
    public static class RequestHandler {
        private static readonly string BASE_API_URL = "https://api.opensubtitles.com/api/v1";
        //header rate limits (5/1s & 240/1 min)
        private static int HRemaining = -1;
        private static int HReset = -1;
        
        //40/10s limits
        private static DateTime WindowStart = DateTime.MinValue;
        private static int RequestCount = 0;
        private static string ApiKey = string.Empty;

        public static void SetApiKey(string key)
        {
            if (ApiKey == string.Empty)
            {
                ApiKey = key;
            }
        }

        public static async Task<(string, (int, int), Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(string endpoint, HttpMethod method, string body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new Exception("API key has not been set up");
            }

            if (headers == null)
            {
                headers = new Dictionary<string, string>();
            }

            if (!headers.ContainsKey("Api-Key"))
            {
                headers.Add("Api-Key", ApiKey);
            }

            var url = endpoint.StartsWith("/") ? BASE_API_URL + endpoint : endpoint;
            var api = url.StartsWith(BASE_API_URL);

            if (api)
            {
                if (HRemaining == 0)
                {
                    await Task.Delay(1000 * HReset, cancellationToken).ConfigureAwait(false);
                    HRemaining = -1;
                    HReset = -1;
                }

                if (RequestCount == 40)
                {
                    var diff = DateTime.UtcNow.Subtract(WindowStart).TotalSeconds;
                    if (diff <= 10)
                    {
                        Util.OnHTTPUpdate("Did 40 requests in < 10s, throttling");
                        
                        await Task.Delay(1000 * (int)Math.Ceiling(10 - diff), cancellationToken).ConfigureAwait(false);
                    }
                }

                if (DateTime.UtcNow.Subtract(WindowStart).TotalSeconds >= 10)
                {
                    WindowStart = DateTime.UtcNow;
                    RequestCount = 0;
                }
            }

            var result = await Util.SendRequestAsync(url, method, body, headers, cancellationToken).ConfigureAwait(false);

            if (api)
            {
                RequestCount++;

                var value = "";
                if (result.Item2.TryGetValue("x-ratelimit-remaining-second", out value))
                {
                    int.TryParse(value, out HRemaining);
                }

                if (result.Item2.TryGetValue("ratelimit-reset", out value))
                {
                    int.TryParse(value, out HReset);
                }

                if (result.Item3 == HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(1000 * (HReset == -1 ? 5 : HReset), cancellationToken).ConfigureAwait(false);

                    return await SendRequestAsync(endpoint, method, body, headers, cancellationToken);
                }
            }

            return (result.Item1, (HRemaining, HReset), result.Item2, result.Item3);
        }
    }
}