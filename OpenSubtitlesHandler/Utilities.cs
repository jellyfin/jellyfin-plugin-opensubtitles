/* This file is part of OpenSubtitles Handler
   A library that handle OpenSubtitles.org XML-RPC methods.

   Copyright © Ala Ibrahim Hadid 2013

   This program is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSubtitlesHandler
{
    /// <summary>
    /// Include helper methods. All member are statics.
    /// </summary>
    public static class Utilities
    {
        public static HttpClient HttpClient { get; set; }
        private const string XML_RPC_SERVER = "https://api.opensubtitles.org/xml-rpc";

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

        /// <summary>
        /// Handle server response stream and decode it as given encoding string.
        /// </summary>
        /// <returns>The string of the stream after decode using given encoding</returns>
        public static string GetStreamString(Stream response)
        {
            using var reader = new StreamReader(response, Encoding.ASCII);
            return reader.ReadToEnd();
        }

        public static byte[] GetASCIIBytes(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        /// <summary>
        /// Send a request to the server
        /// </summary>
        /// <param name="request">The request buffer to send as bytes array.</param>
        /// <param name="userAgent">The user agent value.</param>
        /// <returns>Response of the server or stream of error message as string started with 'ERROR:' keyword.</returns>
        public static Stream SendRequest(byte[] request, string userAgent)
        {
            var result = SendRequestAsync(request, userAgent, CancellationToken.None).Result;
            return result.Item1;
        }

        public static async Task<(Stream, int?, HttpStatusCode)> SendRequestAsync(byte[] request, string userAgent, CancellationToken cancellationToken)
        {
            var clientUserAgent = userAgent ?? "xmlrpc-epi-php/0.2 (PHP)";
            HttpClient.DefaultRequestHeaders.Add("User-Agent", clientUserAgent);

            var content = new StringContent(Encoding.UTF8.GetString(request), Encoding.UTF8, MediaTypeNames.Text.Xml);

            var result = await HttpClient.PostAsync(XML_RPC_SERVER, content);

            IEnumerable<string> values;
            int? limit = null;
            if (result.Headers.TryGetValues("X-RateLimit-Remaining", out values))
            {
                int num;
                if(int.TryParse(values.FirstOrDefault(), out num))
                {
                    limit = num;
                }
            }

            return (result.Content.ReadAsStream(), limit, result.StatusCode);
        }
    }
}
