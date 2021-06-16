using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static HttpClient HttpClient = new HttpClient();
        public static Action<string> OnHTTPUpdate = _ => {};
        private static string version = string.Empty;
        public static readonly CultureInfo[] CultureInfos = CultureInfo.GetCultures(CultureTypes.NeutralCultures);

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
            Util.version = version;
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
        /// Convert ISO 639-1 to ISO 639-2
        /// </summary>
        /// <returns>ISO 639-2 string of specified language</returns>
        public static string TwoLetterToThreeLetterISO(string TwoLetterISOLanguageName)
        {
            if (string.IsNullOrWhiteSpace(TwoLetterISOLanguageName))
            {
                return null;
            }

            var ci = CultureInfos.Where(ci => string.Equals(
                ci.TwoLetterISOLanguageName,
                TwoLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase)
            ).FirstOrDefault();

            if (ci == null)
            {
                return null;
            }

            return ci.ThreeLetterISOLanguageName;
        }

        /// <summary>
        /// Convert ISO 639-2 to ISO 639-1
        /// </summary>
        /// <returns>ISO 639-1 string of specified language</returns>
        public static string ThreeLetterToTwoLetterISO(string ThreeLetterISOLanguageName)
        {
            if (string.IsNullOrWhiteSpace(ThreeLetterISOLanguageName))
            {
                return null;
            }

            var ci = CultureInfos.Where(ci => string.Equals(
                ci.ThreeLetterISOLanguageName,
                ThreeLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase)
            ).FirstOrDefault();

            if (ci == null)
            {
                return null;
            }

            return ci.TwoLetterISOLanguageName;
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
            return JsonSerializer.Deserialize<T>(str, new JsonSerializerOptions { IncludeFields = true });
        }

        /// <summary>
        /// Compute hash of specified movie stream
        /// </summary>
        /// <returns>Hash of the movie</returns>
        public static byte[] ComputeMovieHash(Stream input)
        {
            using (input)
            {
                long lhash, streamsize;
                streamsize = input.Length;
                lhash = streamsize;

                long i = 0;
                byte[] buffer = new byte[sizeof(long)];

                while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
                {
                    i++;
                    lhash += BitConverter.ToInt64(buffer, 0);
                }

                input.Position = Math.Max(0, streamsize - 65536);
                i = 0;

                while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
                {
                    i++;
                    lhash += BitConverter.ToInt64(buffer, 0);
                }

                byte[] result = BitConverter.GetBytes(lhash);
                Array.Reverse(result);

                return result;
            }
        }

        internal static async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(string url, HttpMethod method, string body, Dictionary<string, string> headers, CancellationToken cancellationToken)
        {
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                if (string.IsNullOrWhiteSpace(version))
                {
                    throw new Exception("Missing plugin version");
                }

                var UA = "Jellyfin-Plugin-OpenSubtitles/" + version;

                HttpClient.DefaultRequestHeaders.Add("User-Agent", UA);
            }

            HttpContent content = null;
            if (method != HttpMethod.Get && !string.IsNullOrWhiteSpace(body))
            {
                content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var request = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content
            };

            // docs say alphabetical order improves speed
            foreach (var item in headers.OrderBy(x => x.Key))
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

            if (!request.Headers.Contains("accept"))
            {
                request.Headers.Add("Accept", "*/*");
            }

            var result = await HttpClient.SendAsync(request, cancellationToken);
            var resHeaders = result.Headers.ToDictionary(a => a.Key.ToLower(), a => a.Value.First());
            var res = await result.Content.ReadAsStringAsync();

            return (res, resHeaders, result.StatusCode);
        }
    }
}