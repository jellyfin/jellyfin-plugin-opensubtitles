using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSubtitleDownloader"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{OpenSubtitleDownloader}"/> interface.</param>
        public OpenSubtitleDownloader(
            ILogger<OpenSubtitleDownloader> logger)
        {
            _logger = logger;

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();

            OpenSubtitlesHandler.OpenSubtitles.SetVersion(version);

            Util.OnHttpUpdate += str => _logger.LogDebug($"[HTTP] {str}");
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
            await using (var fileStream = File.OpenRead(request.MediaPath))
            {
                hash = Util.ComputeHash(fileStream);
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

            _logger.LogDebug($"Options: {Util.Serialize(options)}");

            var searchResponse = await OpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(options, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.Ok)
            {
                _logger.LogError(
                    "Invalid response: {code} - {body}",
                    searchResponse.Code,
                    searchResponse.Body);

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

        private async Task<SubtitleResponse> GetSubtitlesInternal(
            string id,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Missing param", nameof(id));
            }

            if (_login?.User?.RemainingDownloads <= 0)
            {
                if (_limitReset < DateTime.UtcNow)
                {
                    _logger.LogDebug("Reset time passed, forcing a new login");
                    // force login because the limit resets at midnight
                    _login = null;
                }
                else
                {
                    throw new OpenApiException("OpenSubtitles download limit reached");
                }
            }

            await Login(cancellationToken).ConfigureAwait(false);

            var idParts = id.Split('-', 3);
            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var fid = int.Parse(ossId, _usCulture);

            var info = await OpenSubtitlesHandler.OpenSubtitles.GetSubtitleLinkAsync(fid, _login, cancellationToken).ConfigureAwait(false);

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

                        throw new OpenApiException("OpenSubtitles download limit reached");
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

                throw new OpenApiException(msg);
            }

            if (_login?.User != null)
            {
                _login.User.RemainingDownloads = info.Data.Remaining;
                _logger.LogInformation($"Remaining downloads: {_login.User.RemainingDownloads}");
            }

            if (string.IsNullOrWhiteSpace(info.Data.Link))
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Failed to obtain download link for file {0}: {1}",
                    fid,
                    info.Code);

                throw new OpenApiException(msg);
            }

            var res = await OpenSubtitlesHandler.OpenSubtitles.DownloadSubtitleAsync(info.Data.Link, cancellationToken).ConfigureAwait(false);

            if (!res.Ok)
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} could not be downloaded: {1}",
                    ossId,
                    res.Code);

                throw new OpenApiException(msg);
            }

            return new SubtitleResponse { Format = format, Language = language, Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.Data)) };
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            if (_login != null && DateTime.UtcNow < _login.ExpirationDate)
            {
                return;
            }

            var key = GetOptions().ApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                OpenSubtitlesHandler.OpenSubtitles.SetToken(key);
            }

            var options = GetOptions();
            if (options.Username.Length == 0 || options.Password.Length == 0 || options.ApiKey.Length == 0)
            {
                _logger.LogWarning("The username, password or API key has no value.");
                return;
            }

            var loginResponse = await OpenSubtitlesHandler.OpenSubtitles.LogInAsync(
                options.Username,
                options.Password,
                null,
                cancellationToken).ConfigureAwait(false);

            if (!loginResponse.Ok)
            {
                _logger.LogDebug($"Login failed: {loginResponse.Code} - ${loginResponse.Body}");
                throw new AuthenticationException("Authentication to OpenSubtitles failed.");
            }

            _login = loginResponse.Data;

            var infoResponse = await OpenSubtitlesHandler.OpenSubtitles.GetUserInfo(_login, cancellationToken).ConfigureAwait(false);
            if (infoResponse.Ok)
            {
                _login.User = infoResponse.Data.Data;
            }

            _limitReset = Util.NextReset;
            _logger.LogDebug($"Logged in, download limit reset at {_limitReset}, token expiration at {_login.ExpirationDate}");
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
