using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;

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
    /// Calculates: size + 64bit chksum of the first and last 64k (even if they overlap because the file is smaller than 128k).
    /// </summary>
    /// <param name="input">The input stream.</param>
    /// <returns>The hash as Hexadecimal string.</returns>
    public static string ComputeHash(Stream input)
    {
        const int HashLength = 8; // 64 bit hash
        const long HashPos = 64 * 1024; // 64k

        long streamsize = input.Length;
        ulong hash = (ulong)streamsize;

        Span<byte> buffer = stackalloc byte[HashLength];
        while (input.Position < HashPos && input.Read(buffer) > 0)
        {
            hash += BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        input.Seek(-HashPos, SeekOrigin.End);
        while (input.Read(buffer) > 0)
        {
            hash += BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        BinaryPrimitives.WriteUInt64BigEndian(buffer, hash);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }

    internal async Task<(string body, Dictionary<string, string> headers, HttpStatusCode statusCode)> SendRequestAsync(
        string url,
        HttpMethod method,
        object? body,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient(NamedClient.Default);

        HttpContent? content = null;
        if (method != HttpMethod.Get && body is not null)
        {
            content = JsonContent.Create(body);
        }

        using var request = new HttpRequestMessage
        {
            Method = method,
            RequestUri = new Uri(url),
            Content = content,
            Headers =
            {
                UserAgent = { new ProductInfoHeaderValue("Jellyfin-Plugin-OpenSubtitles", _version) },
                Accept = { new MediaTypeWithQualityHeaderValue("*/*") },
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

        using var result = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var resHeaders = result.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value.First());
        var resBody = await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return (resBody, resHeaders, result.StatusCode);
    }
}
