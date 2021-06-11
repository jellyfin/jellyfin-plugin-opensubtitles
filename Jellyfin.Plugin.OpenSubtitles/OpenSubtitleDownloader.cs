using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Authentication;
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

            _logger.LogInformation("version: " + version);

            RESTOpenSubtitlesHandler.OpenSubtitles.SetVersion(version);
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

            // _logger.LogInformation("search params: " + Util.Serialize(p));

            var searchResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(p, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.IsOK())
            {
                _logger.LogError(
                    "Invalid response: {code}\n{body}\n{query}\n{remaining} {reset}",
                    searchResponse.code,
                    searchResponse.remaining,
                    searchResponse.reset,
                    searchResponse.body,
                    Util.Serialize(p));

                if (searchResponse.code == 401)
                {
                    _login = null;
                }

                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            Predicate<RESTOpenSubtitlesHandler.ResponseObjects.Data> mediaFilter =
                x => x.attributes.feature_details.feature_type == (request.ContentType == VideoContentType.Episode ? "Episode" : "Movie") &&
                    request.ContentType == VideoContentType.Episode
                        ? x.attributes.feature_details.season_number == request.ParentIndexNumber &&
                          x.attributes.feature_details.episode_number == request.IndexNumber
                        : x.attributes.feature_details.imdb_id == imdbId;

            var results = searchResponse.data;

            // _logger.LogInformation("got here #2 " + searchResponse.code + "\n" + searchResponse.body.Contains("moviehash_match\":false}}", StringComparison.Ordinal));

            return results.data.Where(x => mediaFilter(x) && (!request.IsPerfectMatch || (x.attributes.moviehash_match ?? false)))
                .OrderBy(x => x.attributes.moviehash_match ?? false)
                // .ThenBy(x => Math.Abs(long.Parse(x.MovieByteSize, UsCulture) - movieByteSize)) new api does not send moviebytesize (nor hash)
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

                    // new api (currently?) does not return the format
                    Id = (i.attributes.format ?? "srt") + "-" + Util.TwoLetterToThreeLetterISO(i.attributes.language) + "-" + i.attributes.files[0].file_id,

                    Name = i.attributes.release,
                    DateCreated = i.attributes.upload_date,
                    IsHashMatch = i.attributes.moviehash_match
                })
                .Where(i => !string.Equals(i.Format, "sub", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Format, "idx", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<SubtitleResponse> GetSubtitlesInternal(
            string id,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (_login?.user?.remaining_downloads <= 0)
            {
                if (Util.NextReset < DateTime.UtcNow)
                {
                    // force info refresh
                    _login = null;
                }
                else
                {
                    throw new RateLimitExceededException("OpenSubtitles download count limit reached");
                }
            }

            await Login(cancellationToken).ConfigureAwait(false);

            var idParts = id.Split('-', 3);
            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var file_id = int.Parse(ossId, UsCulture);

            var downloadResult = await RESTOpenSubtitlesHandler.OpenSubtitles.DownloadSubtitleAsync(file_id, _login, cancellationToken)
                .ConfigureAwait(false);

            if (!downloadResult.IsOK())
            {
                if (downloadResult.code == 406)
                {
                    if (_login?.user != null)
                    {
                        _login.user.remaining_downloads = 0;
                    }

                    throw new RateLimitExceededException("OpenSubtitles download count limit hit");
                }

                if (downloadResult.code == 401)
                {
                    _login = null;
                }

                var msg = downloadResult.body.Contains("<html", StringComparison.OrdinalIgnoreCase) ? "[html]" : downloadResult.body;

                throw new OpenApiException("Invalid response for file " + file_id + ": " + downloadResult.code + "\n\n" + msg);
            }

            if (string.IsNullOrWhiteSpace(downloadResult.data))
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} was not found.",
                    ossId);

                throw new ResourceNotFoundException(msg);
            }

            var data = Convert.FromBase64String(downloadResult.data);

            if (_login?.user != null)
            {
                _login.user.allowed_downloads--;
                _logger.LogInformation("Remaining downloads: " + _login.user.allowed_downloads);
            }

            _logger.LogInformation("successfully downloaded " + id);

            return new SubtitleResponse
            {
                Format = format,
                Language = language,

                Stream = new MemoryStream(Util.Decompress(new MemoryStream(data)))
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
                // token expires (apparently ?) every ~3h
                if ((DateTime.UtcNow - _lastLogin).TotalHours < 3)
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

            if (loginResponse == null)
            {
                throw new AuthenticationException("Authentication to OpenSubtitles failed.");
            }

            _login = loginResponse.data;

            var infoResponse = await RESTOpenSubtitlesHandler.OpenSubtitles.GetUserInfo(_login, cancellationToken).ConfigureAwait(false);
            if (infoResponse.IsOK())
            {
                _login.user = infoResponse.data.data;
            }

            _lastLogin = DateTime.UtcNow;
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
