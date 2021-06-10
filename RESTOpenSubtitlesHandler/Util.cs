using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RESTOpenSubtitlesHandler {
    public static class Util {
        private static HttpClient HttpClient { get; set; } = new HttpClient();

        /// <summary>
        /// Compute movie hash
        /// </summary>
        /// <returns>The hash as Hexadecimal string</returns>
        public static string ComputeHash(Stream stream)
        {
            var hash = MovieHasher.ComputeMovieHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Decompress data using gzip.
        /// </summary>
        /// <param name="inputStream">The stream that hold the data</param>
        /// <returns>Bytes array of decompressed data</returns>
        public static byte[] Decompress(Stream inputStream)
        {
            return RunGzip(inputStream, CompressionMode.Decompress);
        }

        /// <summary>
        /// Compress data using gzip. Returned buffer does not have the standard gzip header.
        /// </summary>
        /// <param name="inputStream">The stream that holds the data.</param>
        /// <returns>Bytes array of compressed data without header bytes.</returns>
        public static byte[] Compress(Stream inputStream)
        {
            return RunGzip(inputStream, CompressionMode.Compress);
        }

        private static byte[] RunGzip(Stream inputStream, CompressionMode mode)
        {
            using var outputStream = new MemoryStream();
            using var decompressionStream = new GZipStream(inputStream, mode);

            decompressionStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        public static string Serialize(object o)
        {
            return JsonSerializer.Serialize(o);
        }

        public static T Deserialize<T>(string str)
        {
            return JsonSerializer.Deserialize<T>(str);
        }

        public static bool IsOKCode(HttpStatusCode code) => (int)code < 400;

        internal static async Task<(string, int, HttpStatusCode)> SendRequestAsync(string url, HttpMethod method, string body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                System.Diagnostics.Debug.WriteLine("set ua");
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "test");
            }

            HttpContent content = null;
            if (method != HttpMethod.Get && !string.IsNullOrWhiteSpace(body))
            {
                content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var request = new HttpRequestMessage {
                Method = method,
                RequestUri = new Uri(url),
                Content = content
            };
            
            foreach (var item in headers)
            {
                if (item.Key.ToLower() == "authorization")
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", item.Value);
                }
                else
                {
                    request.Headers.Add(item.Key, item.Value);
                }
            }

            var result = await HttpClient.SendAsync(request, cancellationToken);
            var res = await result.Content.ReadAsStringAsync();

            IEnumerable<string> values;
            int remaining = -1;

            if (result.Headers.TryGetValues("X-RateLimit-Remaining-Second", out values) || result.Headers.TryGetValues("RateLimit-Remaining", out values))
            {
                var temp = values.ToList();
                if (temp.Count > 0) {
                    System.Diagnostics.Debug.WriteLine("Got remaining: " + temp[0]);

                    if (!int.TryParse(temp[0], out remaining)) {
                        System.Diagnostics.Debug.WriteLine("remaining was NOT an int");
                    }
                }
            }

            return (res, remaining, result.StatusCode);
        }
    }
}