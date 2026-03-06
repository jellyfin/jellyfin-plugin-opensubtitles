using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;

/// <summary>
/// Http util helper.
/// </summary>
public class OpenSubtitlesRequestHelper
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<OpenSubtitleDownloader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSubtitlesRequestHelper"/> class.
    /// </summary>
    /// <param name="factory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    public OpenSubtitlesRequestHelper(ILogger<OpenSubtitleDownloader> logger, IHttpClientFactory factory)
    {
        _clientFactory = factory;
        _logger = logger;
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
        var client = _clientFactory.CreateClient(nameof(OpenSubtitles));

        HttpContent? content = null;
        if (method != HttpMethod.Get && body is not null)
        {
            content = JsonContent.Create(body);
        }

        using var request = new HttpRequestMessage
        {
            Method = method,
            RequestUri = new Uri(url),
            Content = content
        };

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
            }
            else
            {
                request.Headers.Add(key, value);
            }
        }

        using var result = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var resHeaders = result.Headers.ToDictionary(x => x.Key, x => x.Value.First(), StringComparer.OrdinalIgnoreCase);
        var resBody = await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return (resBody, resHeaders, result.StatusCode);
    }

    /// <summary>
    /// Search subtitle from HTML page.
    /// </summary>
    /// <param name="request">The subtitle search request.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">cancel token.</param>
    /// <returns>The download link and subtitle name, or null if not found.</returns>
    public async Task<ApiResponse<IReadOnlyList<ResponseData>>> SearchSubtitleFromHtmlAsync(SubtitleSearchRequest request, Dictionary<string, string> options, CancellationToken cancellationToken)
    {
        string title = request.Name;
        string year = request.ProductionYear?.ToString(CultureInfo.InvariantCulture) ?? DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
        using var client = _clientFactory.CreateClient(nameof(OpenSubtitles));

        string encodedTitle = Uri.EscapeDataString(title);
        string url = $@"https://www.opensubtitles.org/en/search2?MovieName={encodedTitle}&id=8&action=search&SubLanguageID={options["languages"]}&MovieYear={year}";

        _logger.LogInformation("Search url: {url}", url);

        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var pattern = @"<a itemprop=""url"" title=""Download"" href=""(?<link>https://dl\.opensubtitles\.org/en/download/sub/\d+)""><span itemprop=""name"">(?<name>[^<]+)</span></a>";
        var match = System.Text.RegularExpressions.Regex.Match(html, pattern);

        if (match.Success)
        {
            var downloadLink = match.Groups["link"].Value;
            var subtitleName = match.Groups["name"].Value;
            var responseData = CreateResponseData(downloadLink, title);

            var httpResponse = new HttpResponse { Code = System.Net.HttpStatusCode.OK, Body = string.Empty };
            return new ApiResponse<IReadOnlyList<ResponseData>>(new List<ResponseData> { responseData }, httpResponse);
        }

        return new ApiResponse<IReadOnlyList<ResponseData>>(new List<ResponseData>(), new HttpResponse { Code = System.Net.HttpStatusCode.OK, Body = string.Empty });
    }

    private static ResponseData CreateResponseData(string downloadLink, string subtitleName)
    {
        return new ResponseData
        {
            Attributes = new Attributes
            {
                Files = new List<SubFile>
                {
                    new SubFile
                    {
                        FileId = LoadIdFromLink(downloadLink)
                    }
                },
                Release = subtitleName,
                Uploader = new Uploader { Name = "Unknown" }
            }
        };
    }

    private static int LoadIdFromLink(string link)
    {
        var match = System.Text.RegularExpressions.Regex.Match(link, @"https://dl\.opensubtitles\.org/en/download/sub/(?<id>\d+)");
        if (match.Success && int.TryParse(match.Groups["id"].Value, out var id))
        {
            return id;
        }

        throw new FormatException($"Unable to extract subtitle ID from link: {link}");
    }
}
