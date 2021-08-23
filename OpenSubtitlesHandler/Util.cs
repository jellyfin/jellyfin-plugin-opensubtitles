using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;

namespace OpenSubtitlesHandler
{
    public static class Util
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static string _version = string.Empty;

        internal static void SetVersion(string version)
        {
            Util._version = version;
        }

        /// <summary>
        /// Compute movie hash
        /// </summary>
        /// <returns>The hash as Hexadecimal string</returns>
        public static string ComputeHash(Stream stream)
        {
            var hash = ComputeMovieHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Serialize object into JSON
        /// </summary>
        /// <returns>JSON string of the object</returns>
        public static string Serialize(object o)
        {
            return JsonSerializer.Serialize(o);
        }

        /// <summary>
        /// Deserialize object from JSON
        /// </summary>
        /// <returns>Deserialized object</returns>
        public static T Deserialize<T>(string str)
        {
            return JsonSerializer.Deserialize<T>(str);
        }

        /// <summary>
        /// Compute hash of specified movie stream
        /// </summary>
        /// <returns>Hash of the movie</returns>
        private static byte[] ComputeMovieHash(Stream input)
        {
            using (input)
            {
                long streamSize = input.Length, lHash = streamSize;
                int size = sizeof(long), count = 65536 / size;
                var buffer = new byte[size];

                for (int i = 0; i < count && input.Read(buffer, 0, size) > 0; i++)
                {
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                input.Position = Math.Max(0, streamSize - 65536);

                for (int i = 0; i < count && input.Read(buffer, 0, size) > 0; i++)
                {
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                var result = BitConverter.GetBytes(lHash);
                Array.Reverse(result);

                return result;
            }
        }

        internal static async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(string url, HttpMethod method, object body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            if (!HttpClient.DefaultRequestHeaders.Contains(HeaderNames.UserAgent))
            {
                if (string.IsNullOrWhiteSpace(_version))
                {
                    throw new Exception("Missing plugin version");
                }

                var ua = $"Jellyfin-Plugin-OpenSubtitles/{_version}";

                HttpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, ua);
            }

            HttpContent content = null;
            if (method != HttpMethod.Get && body != null)
            {
                content = new StringContent(Util.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            var request = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content
            };

            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, "authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", value);
                }
                else
                {
                    request.Headers.Add(key, value);
                }
            }

            if (!request.Headers.Contains("accept"))
            {
                request.Headers.Add("Accept", "*/*");
            }

            var result = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var resHeaders = result.Headers.ToDictionary(x => x.Key.ToLower(), x => x.Value.First());
            var resBody = await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return (resBody, resHeaders, result.StatusCode);
        }
    }
}
