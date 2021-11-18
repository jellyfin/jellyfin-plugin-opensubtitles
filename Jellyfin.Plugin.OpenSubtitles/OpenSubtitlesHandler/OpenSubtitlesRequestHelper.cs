using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler
{
    /// <summary>
    /// Http util helper.
    /// </summary>
    public class OpenSubtitlesRequestHelper
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly string _version;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSubtitlesRequestHelper"/> class.
        /// </summary>
        /// <param name="factory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="version">The plugin version.</param>
        public OpenSubtitlesRequestHelper(IHttpClientFactory factory, string version)
        {
            _clientFactory = factory;
            _version = version;
        }

        /// <summary>
        /// Gets or sets the current instance.
        /// </summary>
        public static OpenSubtitlesRequestHelper? Instance { get; set; }

        /// <summary>
        /// Compute movie hash.
        /// </summary>
        /// <param name="stream">The input stream.</param>
        /// <returns>The hash as Hexadecimal string.</returns>
        public static string ComputeHash(Stream stream)
        {
            var hash = ComputeMovieHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Compute hash of specified movie stream.
        /// </summary>
        /// <returns>Hash of the movie.</returns>
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

        internal async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(string url, HttpMethod method, object? body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            var client = _clientFactory.CreateClient("Default");

            HttpContent? content = null;
            if (method != HttpMethod.Get && body != null)
            {
                content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            using var request = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content,
                Headers =
                {
                    UserAgent = { new ProductInfoHeaderValue("Jellyfin-Plugin-OpenSubtitles", _version) },
                    Accept = { new MediaTypeWithQualityHeaderValue("*/*") }
                }
            };

            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, "authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
                }
                else
                {
                    request.Headers.Add(key, value);
                }
            }

            var result = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var resHeaders = result.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value.First());
            var resBody = await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return (resBody, resHeaders, result.StatusCode);
        }
    }
}
