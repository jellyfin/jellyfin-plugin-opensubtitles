using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSubtitlesHandler
{
    public static class Util
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly JsonSerializerOptions SerializerOpts = new JsonSerializerOptions { IncludeFields = true, PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance };
        public static Action<string> OnHttpUpdate = _ => {};
        private static string _version = string.Empty;

        public static DateTime NextReset
        {
            get
            {
                // download limits get reset every day at midnight (UTC)
                var now = DateTime.UtcNow;

                return new DateTime(now.Year, now.Month, now.Day).AddDays(1).AddMinutes(1);
            }
        }

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
            return JsonSerializer.Deserialize<T>(str, SerializerOpts);
        }

        /// <summary>
        /// Compute hash of specified movie stream
        /// </summary>
        /// <returns>Hash of the movie</returns>
        private static byte[] ComputeMovieHash(Stream input)
        {
            using (input)
            {
                var streamSize = input.Length;
                var lHash = streamSize;

                long i = 0;
                byte[] buffer = new byte[sizeof(long)];

                while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
                {
                    i++;
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                input.Position = Math.Max(0, streamSize - 65536);
                i = 0;

                while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
                {
                    i++;
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                byte[] result = BitConverter.GetBytes(lHash);
                Array.Reverse(result);

                return result;
            }
        }

        internal static async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(string url, HttpMethod method, object body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                if (string.IsNullOrWhiteSpace(_version))
                {
                    throw new Exception("Missing plugin version");
                }

                var ua = $"Jellyfin-Plugin-OpenSubtitles/{_version}";

                HttpClient.DefaultRequestHeaders.Add("User-Agent", ua);
            }

            HttpContent content = null;
            if (method != HttpMethod.Get && body != null)
            {
                content = new StringContent(Util.Serialize(body), Encoding.UTF8, "application/json");
            }

            var request = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content
            };

            foreach (var (key, value) in headers)
            {
                if (key.ToLower() == "authorization")
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
