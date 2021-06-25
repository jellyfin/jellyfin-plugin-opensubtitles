using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using RESTOpenSubtitlesHandler;
using RESTOpenSubtitlesHandler.Models.Responses;

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
        private DateTime _lastLogin;
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

            RESTOpenSubtitlesHandler.OpenSubtitles.SetVersion(version);

            Util.OnHttpUpdate += str => _logger.LogInformation("[HTTP] " + str.Trim());
        }

        /// <inheritdoc />
        public string Name
            => "Open Subtitles";

        /// <inheritdoc />
        public IEnumerable<VideoContentType> SupportedMediaTypes
            => new[]
            {
                VideoContentType.Episode,
                VideoContentType.Movie
            };

        /// <inheritdoc />
        public Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
            => GetSubtitlesInternal(id, cancellationToken);

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            var imdbIdText = request.GetProviderId(MetadataProvider.Imdb);
            long imdbId = 0;

            switch (request.ContentType)
            {
                case VideoContentType.Episode:
                    if (!request.IndexNumber.HasValue || !request.ParentIndexNumber.HasValue || string.IsNullOrEmpty(request.SeriesName))
                    {
                        _logger.LogDebug("Episode information missing");
                        return Enumerable.Empty<RemoteSubtitleInfo>();
                    }

                    break;
                case VideoContentType.Movie:
                    if (string.IsNullOrEmpty(request.Name))
                    {
                        _logger.LogDebug("Movie name missing");
                        return Enumerable.Empty<RemoteSubtitleInfo>();
                    }

                    if (string.IsNullOrWhiteSpace(imdbIdText) || !long.TryParse(imdbIdText.TrimStart('t'), NumberStyles.Any, _usCulture, out imdbId))
                    {
                        _logger.LogDebug("Imdb id missing");
                        return Enumerable.Empty<RemoteSubtitleInfo>();
                    }

                    break;
            }

            if (string.IsNullOrEmpty(request.MediaPath))
            {
                _logger.LogDebug("Path Missing");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            await Login(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(JsonSerializer.Serialize(request));

            string hash;
            await using (var fileStream = File.OpenRead(request.MediaPath))
            {
                hash = Util.ComputeHash(fileStream);
            }

            var searchImdbId = request.ContentType == VideoContentType.Movie ? imdbId.ToString(_usCulture) : string.Empty;

            var p = new Dictionary<string, string>
            {
                { "languages", request.TwoLetterISOLanguageName },
                { "moviehash", hash },
                { "type", request.ContentType == VideoContentType.Episode ? "episode" : "movie" }
            };

            if (request.ContentType == VideoContentType.Episode)
            {
                p.Add("season_number", request.ParentIndexNumber!.Value.ToString(_usCulture));
                p.Add("episode_number", request.IndexNumber!.Value.ToString(_usCulture));
            }
            else
            {
                p.Add("imdb_id", searchImdbId);
            }

            if (request.IsPerfectMatch)
            {
                var name = request.ContentType == VideoContentType.Episode ? request.SeriesName : Path.GetFileNameWithoutExtension(request.MediaPath);
                p.Add("query", name.Length <= 2 ? string.Format(CultureInfo.InvariantCulture, "{0} - {1}", request.ProductionYear, name) : name);
                p.Add("moviehash_match", "only");
            }

            var searchResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(p, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.OK)
            {
                _logger.LogError(
                    "Invalid response: {code} - {body}",
                    searchResponse.Code,
                    searchResponse.Body);

                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            bool MediaFilter(RESTOpenSubtitlesHandler.Models.Responses.Data x) =>
                x.Attributes.FeatureDetails.FeatureType == (request.ContentType == VideoContentType.Episode ? "Episode" : "Movie") && request.ContentType == VideoContentType.Episode
                    ? x.Attributes.FeatureDetails.SeasonNumber == request.ParentIndexNumber && x.Attributes.FeatureDetails.EpisodeNumber == request.IndexNumber
                    : x.Attributes.FeatureDetails.ImdbId == imdbId;

            var results = searchResponse.Data;

            var temp = results.Where(x => MediaFilter(x) && (!request.IsPerfectMatch || (x.Attributes.MoviehashMatch ?? false)))
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
                    Id = (i.Attributes.Format ?? "srt") + "-" + request.Language + "-" + i.Attributes.Files[0].FileId,

                    Name = i.Attributes.Release,
                    DateCreated = i.Attributes.UploadDate,
                    IsHashMatch = i.Attributes.MoviehashMatch
                })
                .Where(i => !string.Equals(i.Format, "sub", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Format, "idx", StringComparison.OrdinalIgnoreCase));

            return temp;
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
                    // force login because the limit resets at midnight
                    _login = null;
                }
                else
                {
                    throw new RateLimitExceededException("OpenSubtitles download limit reached");
                }
            }

            await Login(cancellationToken).ConfigureAwait(false);

            var idParts = id.Split('-', 3);
            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var fid = int.Parse(ossId, _usCulture);

            var info = await RESTOpenSubtitlesHandler.OpenSubtitles.GetubtitleLinkAsync(fid, _login, cancellationToken).ConfigureAwait(false);

            if (!info.OK)
            {
                switch (info.Code)
                {
                    case HttpStatusCode.NotAcceptable when info.Data.Remaining <= 0:
                    {
                        if (_login?.User != null)
                        {
                            _login.User.RemainingDownloads = 0;
                        }

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

                throw new OpenApiException(msg);
            }

            if (_login?.User != null)
            {
                _login.User.RemainingDownloads = info.Data.Remaining;
                _logger.LogInformation("Remaining downloads: " + _login.User.RemainingDownloads);
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

            var res = await RESTOpenSubtitlesHandler.OpenSubtitles.DownloadSubtitleAsync(info.Data.Link, cancellationToken).ConfigureAwait(false);

            if (!res.OK)
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} could not be downloaded: {1}",
                    ossId,
                    res.Code);

                throw new OpenApiException(msg);
            }

            return new SubtitleResponse
            {
                Format = format,
                Language = language,

                Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.Data))
            };
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            var key = GetOptions().ApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                RESTOpenSubtitlesHandler.OpenSubtitles.SetToken(key);
            }

            if (_login != null)
            {
                // token expires every ~24h
                if (DateTime.UtcNow.Subtract(_lastLogin).TotalHours < 23.5)
                {
                    return;
                }
            }

            var options = GetOptions();
            if (options.Username.Length == 0 || options.Password.Length == 0 || options.ApiKey.Length == 0)
            {
                _logger.LogWarning("The username, password or API key has no value.");
                return;
            }

            var loginResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.LogInAsync(
                options.Username,
                options.Password,
                cancellationToken).ConfigureAwait(false);

            if (!loginResponse.OK)
            {
                throw new AuthenticationException("Authentication to OpenSubtitles failed.");
            }

            _login = loginResponse.Data;

            var infoResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.GetUserInfo(_login, cancellationToken).ConfigureAwait(false);
            if (infoResponse.OK)
            {
                _login.User = infoResponse.Data.Data;
            }

            _lastLogin = DateTime.UtcNow;
            _limitReset = Util.NextReset;
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
