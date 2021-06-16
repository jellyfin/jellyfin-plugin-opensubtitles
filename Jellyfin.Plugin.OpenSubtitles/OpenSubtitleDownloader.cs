using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using RESTOpenSubtitlesHandler;

namespace Jellyfin.Plugin.OpenSubtitles
{
    /// <summary>
    /// The open subtitle downloader.
    /// </summary>
    public class OpenSubtitleDownloader : ISubtitleProvider
    {
        private static readonly CultureInfo UsCulture = CultureInfo.ReadOnly(new CultureInfo("en-US"));
        private readonly ILogger<OpenSubtitleDownloader> _logger;
        private readonly IFileSystem _fileSystem;
        private ResponseObjects.LoginInfo? _login;
        private DateTime _lastLogin;
        private DateTime _limitReset;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSubtitleDownloader"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{OpenSubtitleDownloader}"/> interface.</param>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
        public OpenSubtitleDownloader(
            ILogger<OpenSubtitleDownloader> logger,
            IFileSystem fileSystem)
        {
            _logger = logger;
            _fileSystem = fileSystem;

            var version = System.Reflection.Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString();

            RESTOpenSubtitlesHandler.OpenSubtitles.SetVersion(version);

            Util.OnHTTPUpdate += str => _logger.LogDebug("[HTTP] " + str.Trim());
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

                    if (string.IsNullOrWhiteSpace(imdbIdText) || !long.TryParse(imdbIdText.TrimStart('t'), NumberStyles.Any, UsCulture, out imdbId))
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

            var subLanguageId = Util.ThreeLetterToTwoLetterISO(request.Language);

            string hash;
            using (var fileStream = File.OpenRead(request.MediaPath))
            {
                hash = Util.ComputeHash(fileStream);
            }

            var searchImdbId = request.ContentType == VideoContentType.Movie ? imdbId.ToString(UsCulture) : string.Empty;
            var name = request.ContentType == VideoContentType.Episode ? request.SeriesName : request.Name;

            var p = new Dictionary<string, string>
            {
                { "languages", subLanguageId },
                { "moviehash", hash },
                { "query", name.Length <= 2 ? string.Format(CultureInfo.InvariantCulture, "{0} - {1}", request.ProductionYear, name) : name },
                { "type", request.ContentType == VideoContentType.Episode ? "episode" : "movie" }
            };

            if (request.ContentType == VideoContentType.Episode)
            {
                p.Add("season_number", request.ParentIndexNumber!.Value.ToString(UsCulture));
                p.Add("episode_number", request.IndexNumber!.Value.ToString(UsCulture));
            }
            else
            {
                p.Add("imdb_id", searchImdbId);
            }

            var searchResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(p, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.OK)
            {
                _logger.LogError(
                    "Invalid response: {code} - {body}",
                    searchResponse.code,
                    searchResponse.body);

                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            Predicate<RESTOpenSubtitlesHandler.ResponseObjects.Data> mediaFilter =
                x => x.attributes.feature_details.feature_type == (request.ContentType == VideoContentType.Episode ? "Episode" : "Movie") &&
                    request.ContentType == VideoContentType.Episode
                        ? x.attributes.feature_details.season_number == request.ParentIndexNumber &&
                          x.attributes.feature_details.episode_number == request.IndexNumber
                        : x.attributes.feature_details.imdb_id == imdbId;

            var results = searchResponse.data;

            var temp = results.Where(x => mediaFilter(x) && (!request.IsPerfectMatch || (x.attributes.moviehash_match ?? false)))
                .OrderBy(x => x.attributes.moviehash_match ?? false)
                .ThenByDescending(x => x.attributes.download_count)
                .ThenByDescending(x => x.attributes.ratings)
                .Select(i => new RemoteSubtitleInfo
                {
                    Author = i.attributes.uploader.name,
                    Comment = i.attributes.comments,
                    CommunityRating = (float)i.attributes.ratings,
                    DownloadCount = i.attributes.download_count,
                    Format = i.attributes.format,
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = Util.TwoLetterToThreeLetterISO(i.attributes.language),

                    // new API (currently) does not return the format
                    Id = (i.attributes.format ?? "srt") + "-" + Util.TwoLetterToThreeLetterISO(i.attributes.language) + "-" + i.attributes.files[0].file_id,

                    Name = i.attributes.release,
                    DateCreated = i.attributes.upload_date,
                    IsHashMatch = i.attributes.moviehash_match
                })
                .Where(i => !string.Equals(i.Format, "sub", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Format, "idx", StringComparison.OrdinalIgnoreCase));

            /*if (temp.Any())
            {
                _logger.LogDebug("returning " + Util.Serialize(temp));
            }*/

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

            if (_login?.user?.remaining_downloads <= 0)
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

            var fid = int.Parse(ossId, UsCulture);

            var info = await RESTOpenSubtitlesHandler.OpenSubtitles.GetubtitleLinkAsync(fid, _login, cancellationToken).ConfigureAwait(false);

            if (!info.OK)
            {
                if (info.code == 406 && info.data.remaining <= 0)
                {
                    if (_login?.user != null)
                    {
                        _login.user.remaining_downloads = 0;
                    }

                    throw new RateLimitExceededException("OpenSubtitles download limit reached");
                }

                if (info.code == 401)
                {
                    // JWT token expired, obtain a new one and try again?
                    _login = null;
                    return await GetSubtitlesInternal(id, cancellationToken).ConfigureAwait(false);
                }

                var msg = info.body.Contains("<html", StringComparison.OrdinalIgnoreCase) ? "[html]" : info.body;

                msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Invalid response for file {0}: {1}\n\n{2}",
                    fid,
                    info.code,
                    msg);

                throw new OpenApiException(msg);
            }

            if (_login?.user != null)
            {
                _login.user.remaining_downloads = info.data.remaining;
                _logger.LogInformation("Remaining downloads: " + _login.user.remaining_downloads);
            }

            var res = await RESTOpenSubtitlesHandler.OpenSubtitles.DownloadSubtitleAsync(info.data.link, cancellationToken).ConfigureAwait(false);

            if (!res.OK)
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} could not be downloaded: {1}",
                    ossId,
                    res.code);

                throw new OpenApiException(msg);
            }

            return new SubtitleResponse
            {
                Format = format,
                Language = language,

                Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.data))
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

            _login = loginResponse.data;

            var infoResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.GetUserInfo(_login, cancellationToken).ConfigureAwait(false);
            if (infoResponse.OK)
            {
                _login.user = infoResponse.data.data;
            }

            _lastLogin = DateTime.UtcNow;
            _limitReset = Util.NextReset;
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
