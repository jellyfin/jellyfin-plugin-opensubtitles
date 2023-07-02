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
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenSubtitles
{
    /// <summary>
    /// The open subtitle downloader.
    /// </summary>
    public class OpenSubtitleDownloader : ISubtitleProvider
    {
        private readonly ILogger<OpenSubtitleDownloader> _logger;
        private LoginInfo? _login;
        private DateTime? _limitReset;
        private DateTime? _lastRatelimitLog;
        private IReadOnlyList<string>? _languages;
        private string _customApiKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSubtitleDownloader"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{OpenSubtitleDownloader}"/> interface.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> for creating Http Clients.</param>
        public OpenSubtitleDownloader(ILogger<OpenSubtitleDownloader> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString();

            OpenSubtitlesRequestHelper.Instance = new OpenSubtitlesRequestHelper(httpClientFactory, version);

            OpenSubtitlesPlugin.Instance!.ConfigurationChanged += (_, _) =>
            {
                _customApiKey = GetOptions().CustomApiKey;
                // force a login next time a request is made
                _login = null;
            };

            _customApiKey = GetOptions().CustomApiKey;
        }

        /// <summary>
        /// Gets the API key that will be used for requests.
        /// </summary>
        public string ApiKey
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_customApiKey) ? _customApiKey : OpenSubtitlesPlugin.ApiKey;
            }
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

            await Login(cancellationToken).ConfigureAwait(false);

            if (request.IsAutomated && _login is null)
            {
                // Login attempt failed, since this is a task to download subtitles there's no point in continuing
                _logger.LogDebug("Returning empty results because login failed");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            if (request.IsAutomated && _login?.User?.RemainingDownloads <= 0)
            {
                if (_lastRatelimitLog == null || DateTime.UtcNow.Subtract(_lastRatelimitLog.Value).TotalSeconds > 60)
                {
                    _logger.LogInformation("Daily download limit reached, returning no results for automated task");
                    _lastRatelimitLog = DateTime.UtcNow;
                }

                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            long.TryParse(request.GetProviderId(MetadataProvider.Imdb)?.TrimStart('t') ?? string.Empty, NumberStyles.Any, CultureInfo.InvariantCulture, out var imdbId);

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

            var language = await GetLanguage(request.TwoLetterISOLanguageName, cancellationToken).ConfigureAwait(false);

            string? hash = null;
            if (!Path.GetExtension(request.MediaPath).Equals(".strm", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    #pragma warning disable CA2007
                    await using var fileStream = File.OpenRead(request.MediaPath);
                    #pragma warning restore CA2007

                    hash = OpenSubtitlesRequestHelper.ComputeHash(fileStream);
                }
                catch (IOException ex)
                {
                    throw new IOException(string.Format(CultureInfo.InvariantCulture, "IOException while computing hash for {0}", request.MediaPath), ex);
                }
            }

            var options = new Dictionary<string, string>
            {
                { "languages", language },
                { "type", request.ContentType == VideoContentType.Episode ? "episode" : "movie" }
            };

            if (hash is not null)
            {
                options.Add("moviehash", hash);
            }

            // If we have the IMDb ID we use that, otherwise query with the details
            if (imdbId != 0)
            {
                options.Add("imdb_id", imdbId.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                options.Add("query", Path.GetFileName(request.MediaPath));

                if (request.ContentType == VideoContentType.Episode)
                {
                    if (request.ParentIndexNumber.HasValue)
                    {
                        options.Add("season_number", request.ParentIndexNumber.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    if (request.IndexNumber.HasValue)
                    {
                        options.Add("episode_number", request.IndexNumber.Value.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            if (request.IsPerfectMatch && hash is not null)
            {
                options.Add("moviehash_match", "only");
            }

            _logger.LogDebug("Search query: {Query}", options);

            var searchResponse = await OpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(options, ApiKey, cancellationToken).ConfigureAwait(false);

            if (!searchResponse.Ok)
            {
                _logger.LogError("Invalid response: {Code} - {Body}", searchResponse.Code, searchResponse.Body);
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            bool MediaFilter(ResponseData x) =>
                x.Attributes?.FeatureDetails?.FeatureType == (request.ContentType == VideoContentType.Episode ? "Episode" : "Movie")
                && request.ContentType == VideoContentType.Episode
                    ? x.Attributes.FeatureDetails.SeasonNumber == request.ParentIndexNumber
                      && x.Attributes.FeatureDetails.EpisodeNumber == request.IndexNumber
                    : x.Attributes?.FeatureDetails?.ImdbId == imdbId;

            if (searchResponse.Data == null)
            {
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            return searchResponse.Data
                .Where(x => MediaFilter(x) && (!request.IsPerfectMatch || (x.Attributes?.MovieHashMatch ?? false)))
                .OrderByDescending(x => x.Attributes?.MovieHashMatch ?? false)
                .ThenByDescending(x => x.Attributes?.DownloadCount)
                .ThenByDescending(x => x.Attributes?.Ratings)
                .ThenByDescending(x => x.Attributes?.FromTrusted)
                .Select(i => new RemoteSubtitleInfo
                {
                    Author = i.Attributes?.Uploader?.Name,
                    Comment = i.Attributes?.Comments,
                    CommunityRating = i.Attributes?.Ratings,
                    DownloadCount = i.Attributes?.DownloadCount,
                    Format = "srt",
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = request.Language,
                    Id = $"srt-{request.Language}-{i.Attributes?.Files[0].FileId}",
                    Name = i.Attributes?.Release,
                    DateCreated = i.Attributes?.UploadDate,
                    IsHashMatch = i.Attributes?.MovieHashMatch
                })
                .Where(i => !string.Equals(i.Format, "sub", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Format, "idx", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<SubtitleResponse> GetSubtitlesInternal(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Missing param", nameof(id));
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
            if (_login == null)
            {
                throw new AuthenticationException("Unable to login");
            }

            var idParts = id.Split('-', 3);
            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var fid = int.Parse(ossId, CultureInfo.InvariantCulture);

            var info = await OpenSubtitlesHandler.OpenSubtitles.GetSubtitleLinkAsync(fid, _login, ApiKey, cancellationToken).ConfigureAwait(false);

            if (info.Data?.ResetTime != null)
            {
                _limitReset = info.Data.ResetTime;
                _logger.LogDebug("Updated expiration time to {ResetTime}", _limitReset);
            }

            if (!info.Ok)
            {
                switch (info.Code)
                {
                    case HttpStatusCode.NotAcceptable when info.Data?.Remaining <= 0:
                    {
                        if (_login.User != null)
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

            if (_login.User != null)
            {
                _login.User.RemainingDownloads = info.Data?.Remaining;
                _logger.LogInformation("Remaining downloads: {RemainingDownloads}", _login.User.RemainingDownloads);
            }

            if (string.IsNullOrWhiteSpace(info.Data?.Link))
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Failed to obtain download link for file {0}: {1} (empty response)",
                    fid,
                    info.Code);

                throw new HttpRequestException(msg);
            }

            var res = await OpenSubtitlesHandler.OpenSubtitles.DownloadSubtitleAsync(info.Data.Link, cancellationToken).ConfigureAwait(false);

            if (res.Code != HttpStatusCode.OK || string.IsNullOrWhiteSpace(res.Body))
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} could not be downloaded: {1}",
                    ossId,
                    res.Code);

                throw new HttpRequestException(msg);
            }

            return new SubtitleResponse { Format = format, Language = language, Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.Body)) };
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            if (_login != null && DateTime.UtcNow < _login.ExpirationDate)
            {
                return;
            }

            var options = GetOptions();
            if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            {
                throw new AuthenticationException("Account username and/or password are not set up");
            }

            if (options.CredentialsInvalid)
            {
                _logger.LogDebug("Skipping login due to credentials being invalid");
                return;
            }

            var loginResponse = await OpenSubtitlesHandler.OpenSubtitles.LogInAsync(
                options.Username,
                options.Password,
                ApiKey,
                cancellationToken).ConfigureAwait(false);

            if (!loginResponse.Ok)
            {
                // 400 = Using email, 401 = invalid credentials, 403 = invalid api key
                if ((loginResponse.Code == HttpStatusCode.BadRequest && options.Username.Contains('@', StringComparison.OrdinalCultureIgnoreCase))
                    || loginResponse.Code == HttpStatusCode.Unauthorized
                    || (loginResponse.Code == HttpStatusCode.Forbidden && ApiKey == options.CustomApiKey))
                {
                    _logger.LogError("Login failed due to invalid credentials/API key, invalidating them ({Code} - {Body})", loginResponse.Code, loginResponse.Body);
                    options.CredentialsInvalid = true;
                    OpenSubtitlesPlugin.Instance!.SaveConfiguration(options);
                }
                else
                {
                    _logger.LogError("Login failed: {Code} - {Body}", loginResponse.Code, loginResponse.Body);
                }

                throw new AuthenticationException("Authentication to OpenSubtitles failed.");
            }

            _login = loginResponse.Data;

            await UpdateUserInfo(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Logged in, download limit reset at {ResetTime}, token expiration at {ExpirationDate}", _limitReset, _login?.ExpirationDate);
        }

        private async Task UpdateUserInfo(CancellationToken cancellationToken)
        {
            if (_login == null)
            {
                return;
            }

            var infoResponse = await OpenSubtitlesHandler.OpenSubtitles.GetUserInfo(_login, ApiKey, cancellationToken).ConfigureAwait(false);
            if (infoResponse.Ok)
            {
                _login.User = infoResponse.Data?.Data;
                _limitReset = _login.User?.ResetTime;
            }
        }

        private async Task<string> GetLanguage(string language, CancellationToken cancellationToken)
        {
            if (language == "zh")
            {
                language = "zh-CN";
            }
            else if (language == "pt")
            {
                language = "pt-PT";
            }

            if (_languages == null || _languages.Count == 0)
            {
                var res = await OpenSubtitlesHandler.OpenSubtitles.GetLanguageList(ApiKey, cancellationToken).ConfigureAwait(false);

                if (!res.Ok || res.Data?.Data == null)
                {
                    throw new HttpRequestException(string.Format(CultureInfo.InvariantCulture, "Failed to get language list: {0}", res.Code));
                }

                _languages = res.Data.Data.Where(x => !string.IsNullOrWhiteSpace(x.Code)).Select(x => x.Code!).ToList();
            }

            var found = _languages.FirstOrDefault(x => string.Equals(x, language, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return found;
            }

            if (language.Contains('-', StringComparison.OrdinalIgnoreCase))
            {
                return await GetLanguage(language.Split('-')[0], cancellationToken).ConfigureAwait(false);
            }

            throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "Language '{0}' is not supported", language));
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
