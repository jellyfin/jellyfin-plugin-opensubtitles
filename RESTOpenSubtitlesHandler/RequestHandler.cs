using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;

namespace RESTOpenSubtitlesHandler {
    public static class RequestHandler {
        private static readonly string BASE_API_URL = "https://api.opensubtitles.com/api/v1";
        private static int Remaining = -1;
        private static string ApiKey = string.Empty;

        public static void SetApiKey(string key)
        {
            ApiKey = key;
        }

        public static async Task<(string, HttpStatusCode)> SendRequestAsync(string endpoint, HttpMethod method, string body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new Exception("API key has not been set up");
            }

            if (headers == null)
            {
                headers = new Dictionary<string, string>();
            }

            headers.Add("Api-Key", ApiKey);

            if (method != HttpMethod.Get && !string.IsNullOrWhiteSpace(body)) {
                headers.Add("content-type", "application/json");
            }

            if (Remaining == 0)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                Remaining = -1;
            } 
            
            var result = await Util.SendRequestAsync(BASE_API_URL + endpoint, method, body, headers, cancellationToken).ConfigureAwait(false);

            if (result.Item2 != -1)
            {
                Remaining = result.Item2;
            }

            return (result.Item1, result.Item3);
        }
    }
}