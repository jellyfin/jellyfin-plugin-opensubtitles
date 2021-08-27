using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using OpenSubtitlesHandler;
using OpenSubtitlesHandler.Models.Responses;

namespace Jellyfin.Plugin.OpenSubtitles
{
    /// <summary>
    /// The open subtitle downloader.
    /// </summary>
    public class OpenSubtitleDownloader : ISubtitleProvider
    {
        private static readonly CultureInfo _usCulture = CultureInfo.ReadOnly(new CultureInfo("en-US"));
        private readonly ILogger<OpenSubtitleDownloader> _logger;
        private LoginInfo? _login;
        private DateTime _limitReset;
        private string _apiKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSubtitleDownloader"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{OpenSubtitleDownloader}"/> interface.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> for creating Http Clients.</param>
        public OpenSubtitleDownloader(ILogger<OpenSubtitleDownloader> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString();

            OpenSubtitlesHandler.Util.Instance = new Util(httpClientFactory, version);

            OpenSubtitlesPlugin.Instance!.ConfigurationChanged += (_, _) =>
            {
                _apiKey = GetOptions().ApiKey;
                // force a login next time a request is made
                _login = null;
            };

            _apiKey = GetOptions().ApiKey;
        }

        /// <inheritdoc />
        public string Name
            => "Open Subtitles";

        /// <inheritdoc />
        public IEnumerable<VideoContentType> SupportedMediaTypes
            => new[] { VideoContentType.Episode, VideoContentType.Movie };

        /// <inheritdoc />
        public Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
            => GetSubtitlesInternal(id, cancellationToken);

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new AuthenticationException("API key not set up");
            }

            long.TryParse(request.GetProviderId(MetadataProvider.Imdb)?.TrimStart('t') ?? string.Empty, NumberStyles.Any, _usCulture, out var imdbId);

            if (request.ContentType == VideoContentType.Episode && (!request.IndexNumber.HasValue || !request.ParentIndexNumber.HasValue || string.IsNullOrEmpty(request.SeriesName)))
            {
                _logger.LogDebug("Episode information missing");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            if (string.IsNullOrEmpty(request.MediaPath))
            {
                _logger.LogDebug("Path Missing");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            await Login(cancellationToken).ConfigureAwait(false);

            string hash;
            try
            {
                await using (var fileStream = File.OpenRead(request.MediaPath))
                {
                    hash = Util.ComputeHash(fileStream);
                }
            }
            catch (IOException e)
            {
                throw new IOException(string.Format(CultureInfo.InvariantCulture, "IOException while computing hash for {MediaPath}", request.MediaPath), e);
            }

            var options = new Dictionary<string, string>
            {
                { "languages", request.TwoLetterISOLanguageName },
                { "moviehash", hash },
                { "type", request.ContentType == VideoContentType.Episode ? "episode" : "movie" }
            };

            // If we have the IMDb ID we use that, otherwise query with the details
            if (imdbId != 0)
            {
                options.Add("imdb_id", imdbId.ToString(_usCulture));
            }
            else
            {
                if (request.ContentType == VideoContentType.Episode)
                {
                    options.Add("query", request.SeriesName.Length <= 2 ? $"{request.SeriesName} {request.ProductionYear}" : request.SeriesName);
                    options.Add("season_number", request.ParentIndexNumber?.ToString(_usCulture) ?? string.Empty);
                    options.Add("episode_number", request.IndexNumber?.ToString(_usCulture) ?? string.Empty);
                }
                else
                {
                    options.Add("query", request.Name.Length <= 2 ? $"{request.Name} {request.ProductionYear}" : request.Name);
                }
            }

            if (request.IsPerfectMatch)
            {
                options.Add("moviehash_match", "only");
            }

            _logger.LogDebug("Search query: {Query}", string.Join(", ", options.Keys.Select(x => $"{x}: {options[x]}")));

            var searchResponse = await OpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(options, _apiKey, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.Ok)
            {
                _logger.LogError("Invalid response: {Code} - {Body}", searchResponse.Code, searchResponse.Body);
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            bool MediaFilter(OpenSubtitlesHandler.Models.Responses.Data x) =>
                x.Attributes.FeatureDetails.FeatureType == (request.ContentType == VideoContentType.Episode ? "Episode" : "Movie") && request.ContentType == VideoContentType.Episode
                    ? x.Attributes.FeatureDetails.SeasonNumber == request.ParentIndexNumber && x.Attributes.FeatureDetails.EpisodeNumber == request.IndexNumber
                    : x.Attributes.FeatureDetails.ImdbId == imdbId;

            return searchResponse.Data.Where(x => MediaFilter(x) && (!request.IsPerfectMatch || (x.Attributes.MoviehashMatch ?? false)))
                .OrderByDescending(x => x.Attributes.MoviehashMatch ?? false)
                .ThenByDescending(x => x.Attributes.DownloadCount)
                .ThenByDescending(x => x.Attributes.Ratings)
                .ThenByDescending(x => x.Attributes.FromTrusted)
                .Select(i => new RemoteSubtitleInfo
                {
                    Author = i.Attributes.Uploader.Name,
                    Comment = i.Attributes.Comments,
                    CommunityRating = i.Attributes.Ratings,
                    DownloadCount = i.Attributes.DownloadCount,
                    Format = i.Attributes.Format,
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = request.Language,

                    // new API (currently) does not return the format
                    Id = $"{i.Attributes.Format ?? "srt"}-{request.Language}-{i.Attributes.Files[0].FileId}",
                    Name = i.Attributes.Release,
                    DateCreated = i.Attributes.UploadDate,
                    IsHashMatch = i.Attributes.MoviehashMatch
                })
                .Where(i => !string.Equals(i.Format, "sub", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Format, "idx", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<SubtitleResponse> GetSubtitlesInternal(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Missing param", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new AuthenticationException("API key not set up");
            }

            if (_login?.User?.RemainingDownloads <= 0)
            {
                if (_limitReset < DateTime.UtcNow)
                {
                    _logger.LogDebug("Reset time passed, updating user info");

                    await UpdateUserInfo(cancellationToken).ConfigureAwait(false);

                    // this shouldn't happen?
                    if (_login.User.RemainingDownloads <= 0)
                    {
                        _logger.LogError("OpenSubtitles download limit reached");
                        throw new RateLimitExceededException("OpenSubtitles download limit reached");
                    }
                }
                else
                {
                    _logger.LogError("OpenSubtitles download limit reached");
                    throw new RateLimitExceededException("OpenSubtitles download limit reached");
                }
            }

            await Login(cancellationToken).ConfigureAwait(false);

            var idParts = id.Split('-', 3);
            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var fid = int.Parse(ossId, _usCulture);

            var info = await OpenSubtitlesHandler.OpenSubtitles.GetSubtitleLinkAsync(fid, _login, _apiKey, cancellationToken).ConfigureAwait(false);

            if (info.Data.Message != null && info.Data.Message.Contains("UTC", StringComparison.Ordinal))
            {
                // "Your quota will be renewed in 20 hours and 52 minutes (2021-08-24 12:02:10 UTC) "
                var str = info.Data.Message.Split('(')[1].Trim().Replace(" UTC)", "Z", StringComparison.Ordinal);
                _limitReset = DateTime.Parse(str, _usCulture, DateTimeStyles.AdjustToUniversal);

                _logger.LogDebug("Updated expiration time to {ResetTime}", _limitReset);
            }

            if (!info.Ok)
            {
                switch (info.Code)
                {
                    case HttpStatusCode.NotAcceptable when info.Data.Remaining <= 0:
                    {
                        if (_login?.User != null)
                        {
                            _login.User.RemainingDownloads = 0;
                        }

                        _logger.LogError("OpenSubtitles download limit reached");
                        throw new RateLimitExceededException("OpenSubtitles download limit reached");
                    }

                    case HttpStatusCode.Unauthorized:
                        // JWT token expired, obtain a new one and try again?
                        _login = null;
                        return await GetSubtitlesInternal(id, cancellationToken).ConfigureAwait(false);
                }

                var msg = info.Body.Contains("<html", StringComparison.OrdinalIgnoreCase) ? "[html]" : info.Body;

                msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Invalid response for file {0}: {1}\n\n{2}",
                    fid,
                    info.Code,
                    msg);

                throw new HttpRequestException(msg);
            }

            if (_login?.User != null)
            {
                _login.User.RemainingDownloads = info.Data.Remaining;
                _logger.LogInformation("Remaining downloads: {RemainingDownloads}", _login.User.RemainingDownloads);
            }

            if (string.IsNullOrWhiteSpace(info.Data.Link))
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Failed to obtain download link for file {0}: {1}",
                    fid,
                    info.Code);

                throw new HttpRequestException(msg);
            }

            var res = await OpenSubtitlesHandler.OpenSubtitles.DownloadSubtitleAsync(info.Data.Link, cancellationToken).ConfigureAwait(false);

            if (!res.Ok)
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} could not be downloaded: {1}",
                    ossId,
                    res.Code);

                throw new HttpRequestException(msg);
            }

            return new SubtitleResponse { Format = format, Language = language, Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.Data)) };
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            if (_login != null && DateTime.UtcNow < _login.ExpirationDate)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new AuthenticationException("API key is not set up");
            }

            var options = GetOptions();
            if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            {
                throw new AuthenticationException("Account username and/or password are not set up");
            }

            var loginResponse = await OpenSubtitlesHandler.OpenSubtitles.LogInAsync(
                options.Username,
                options.Password,
                _apiKey,
                cancellationToken).ConfigureAwait(false);

            if (!loginResponse.Ok)
            {
                _logger.LogError("Login failed: {Code} - {Body}", loginResponse.Code, loginResponse.Body);
                throw new AuthenticationException("Authentication to OpenSubtitles failed.");
            }

            _login = loginResponse.Data;

            await UpdateUserInfo(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Logged in, download limit reset at {ResetTime}, token expiration at {ExpirationDate}", _limitReset, _login.ExpirationDate);
        }

        private async Task UpdateUserInfo(CancellationToken cancellationToken)
        {
            if (_login == null)
            {
                return;
            }

            var infoResponse = await OpenSubtitlesHandler.OpenSubtitles.GetUserInfo(_login, _apiKey, cancellationToken).ConfigureAwait(false);
            if (infoResponse.Ok)
            {
                _login.User = infoResponse.Data.Data;
                _limitReset = _login.User.ResetTime;
            }
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
