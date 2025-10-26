using System;
using System.Collections.Concurrent;
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

namespace Jellyfin.Plugin.OpenSubtitles;

/// <summary>
/// The open subtitle downloader.
/// </summary>
public class OpenSubtitleDownloader : ISubtitleProvider
{
    private readonly ILogger<OpenSubtitleDownloader> _logger;
    private readonly ConcurrentBag<int> _badSubtitleIds = new ();
    private LoginInfo? _login;
    private DateTime? _limitReset;
    private DateTime? _lastRatelimitLog;
    private List<string>? _languages;
    private PluginConfiguration? _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSubtitleDownloader"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{OpenSubtitleDownloader}"/> interface.</param>
    /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> for creating Http Clients.</param>
    public OpenSubtitleDownloader(ILogger<OpenSubtitleDownloader> logger, IHttpClientFactory httpClientFactory)
    {
        Instance = this;
        _logger = logger;
        OpenSubtitlesRequestHelper.Instance = new OpenSubtitlesRequestHelper(httpClientFactory);
    }

    /// <summary>
    /// Gets the downloader instance.
    /// </summary>
    public static OpenSubtitleDownloader? Instance { get; private set; }

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
        ArgumentNullException.ThrowIfNull(request);

        await Login(cancellationToken).ConfigureAwait(false);

        if (request.IsAutomated && _login is null)
        {
            // Login attempt failed, since this is a task to download subtitles there's no point in continuing
            _logger.LogDebug("Returning empty results because login failed");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        if (request.IsAutomated && _login?.User?.RemainingDownloads <= 0)
        {
            if (_lastRatelimitLog is null || DateTime.UtcNow.Subtract(_lastRatelimitLog.Value).TotalSeconds > 60)
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

        var language = await GetLanguage(request.TwoLetterISOLanguageName, request.MediaPath, cancellationToken).ConfigureAwait(false);

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

        var options = new Dictionary<string, string>();
        if (request.IsPerfectMatch && !string.IsNullOrEmpty(hash))
        {
            options.Add("languages", language);
            options.Add("moviehash", hash);
        }
        else
        {
            options.Add("languages", language);
            options.Add("type", request.ContentType == VideoContentType.Episode ? "episode" : "movie");

            if (!string.IsNullOrEmpty(hash))
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
        }

        _logger.LogDebug("Search query: {Query}", options);

        var searchResponse = await OpenSubtitlesApi.SearchSubtitlesAsync(options, cancellationToken).ConfigureAwait(false);

        if (!searchResponse.Ok)
        {
            _logger.LogError("Invalid response: {Code} - {Body}", searchResponse.Code, searchResponse.Body);
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        if (searchResponse.Data is null)
        {
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        bool BadSubtitleFilter(ResponseData x) =>
            x.Attributes?.Files?.Count > 0 &&
            x.Attributes.Files[0].FileId.HasValue
            && (!request.IsAutomated || !_badSubtitleIds.Contains(x.Attributes.Files[0].FileId!.Value));

        bool MediaFilter(ResponseData x) =>
            x.Attributes!.FeatureDetails?.FeatureType == (request.ContentType == VideoContentType.Episode ? "Episode" : "Movie")
            && (request.ContentType == VideoContentType.Episode
                ? x.Attributes.FeatureDetails.SeasonNumber == request.ParentIndexNumber
                  && x.Attributes.FeatureDetails.EpisodeNumber == request.IndexNumber
                : x.Attributes.FeatureDetails?.ImdbId == imdbId);

        bool MatchFilter(ResponseData x) => !request.IsPerfectMatch || (x.Attributes?.MovieHashMatch ?? false);

        return searchResponse.Data
            .Where(x => BadSubtitleFilter(x) && MediaFilter(x) && MatchFilter(x))
            .OrderByDescending(x => x.Attributes!.MovieHashMatch ?? false)
            .ThenByDescending(x => x.Attributes!.DownloadCount)
            .ThenByDescending(x => x.Attributes!.Ratings)
            .ThenByDescending(x => x.Attributes!.FromTrusted)
            .Select(i => new RemoteSubtitleInfo
            {
                Author = i.Attributes!.Uploader?.Name,
                Comment = i.Attributes.Comments,
                CommunityRating = i.Attributes.Ratings,
                DownloadCount = i.Attributes.DownloadCount,
                Format = "srt",
                ProviderName = Name,
                ThreeLetterISOLanguageName = request.Language,
                Id = BuildSubtitleId(request.Language, i),
                Name = i.Attributes.Release,
                DateCreated = i.Attributes.UploadDate,
                IsHashMatch = i.Attributes.MovieHashMatch,
                HearingImpaired = i.Attributes.HearingImpaired,
                MachineTranslated = i.Attributes.MachineTranslated,
                AiTranslated = i.Attributes.AiTranslated,
                FrameRate = i.Attributes.Fps,
                Forced = i.Attributes.ForeignPartsOnly
            });
    }

    private string BuildSubtitleId(string language, ResponseData res)
    {
        var id = $"srt-{language}-{res.Attributes!.Files[0].FileId}";
        if (res.Attributes.HearingImpaired ?? false)
        {
            id += "-sdh";
        }

        if (res.Attributes.ForeignPartsOnly ?? false)
        {
            id += "-forced";
        }

        return id;
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
        if (_login is null)
        {
            throw new AuthenticationException("Unable to login");
        }

        var idParts = id.Split('-');
        if (idParts.Length < 3)
        {
            throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Invalid subtitle id format: {0}", id));
        }

        var format = idParts[0];
        var language = idParts[1];
        var fileId = int.Parse(idParts[2], CultureInfo.InvariantCulture);
        var isHearingImpaired = id.Contains("-sdh", StringComparison.OrdinalIgnoreCase);
        var isForced = id.Contains("-forced", StringComparison.OrdinalIgnoreCase);

        var info = await OpenSubtitlesApi
            .GetSubtitleLinkAsync(fileId, format, _login, cancellationToken)
            .ConfigureAwait(false);

        if (info.Data?.ResetTime is not null)
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
                    if (_login.User is not null)
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
                fileId,
                info.Code,
                msg);

            throw new HttpRequestException(msg);
        }

        if (_login.User is not null)
        {
            _login.User.RemainingDownloads = info.Data?.Remaining;
            _logger.LogInformation("Remaining subtitle downloads: {RemainingDownloads}", _login.User.RemainingDownloads);
        }

        if (string.IsNullOrWhiteSpace(info.Data?.Link))
        {
            var msg = string.Format(
                CultureInfo.InvariantCulture,
                "Failed to obtain download link for file {0}: {1} (empty response)",
                fileId,
                info.Code);

            throw new HttpRequestException(msg);
        }

        var res = await OpenSubtitlesApi.DownloadSubtitleAsync(info.Data.Link, cancellationToken).ConfigureAwait(false);

        if (res.Code != HttpStatusCode.OK || string.IsNullOrWhiteSpace(res.Body))
        {
            var additionalMsg = string.Empty;
            if (res.Code == HttpStatusCode.OK && string.IsNullOrWhiteSpace(res.Body))
            {
                additionalMsg = " - this is most likely a broken subtitle, report at opensubtitles.com/contact and make sure to include the id";
                if (!_badSubtitleIds.Contains(fileId))
                {
                    _badSubtitleIds.Add(fileId);
                }
            }

            var msg = string.Format(
                CultureInfo.InvariantCulture,
                "Subtitle with Id {0} could not be downloaded: {1}{2}",
                fileId,
                res.Code,
                additionalMsg);

            throw new HttpRequestException(msg);
        }

        return new SubtitleResponse
        {
            Format = format,
            Language = language,
            Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.Body)),
            IsForced = isForced,
            IsHearingImpaired = isHearingImpaired
        };
    }

    private async Task Login(CancellationToken cancellationToken)
    {
        if (_configuration is null || (_login is not null && DateTime.UtcNow < _login.ExpirationDate))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuration.Username) || string.IsNullOrWhiteSpace(_configuration.Password))
        {
            throw new AuthenticationException("Account username and/or password are not set up");
        }

        if (_configuration.CredentialsInvalid)
        {
            _logger.LogDebug("Skipping login due to credentials being invalid");
            return;
        }

        var loginResponse = await OpenSubtitlesApi.LogInAsync(
            _configuration.Username,
            _configuration.Password,
            cancellationToken).ConfigureAwait(false);

        if (!loginResponse.Ok)
        {
            // 400 = Using email, 401 = invalid credentials
            if ((loginResponse.Code == HttpStatusCode.BadRequest && _configuration.Username.Contains('@', StringComparison.OrdinalIgnoreCase))
                || loginResponse.Code == HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Login failed due to invalid credentials, invalidating them ({Code} - {Body})", loginResponse.Code, loginResponse.Body);
                _configuration.CredentialsInvalid = true;
                OpenSubtitlesPlugin.Instance!.SaveConfiguration(_configuration);
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
        if (_login is null)
        {
            return;
        }

        var infoResponse = await OpenSubtitlesApi.GetUserInfo(_login, cancellationToken).ConfigureAwait(false);
        if (infoResponse.Ok)
        {
            _login.User = infoResponse.Data?.Data;
            _limitReset = _login.User?.ResetTime;
        }
    }

    private async Task<string> GetLanguage(string language, string mediaPath, CancellationToken cancellationToken)
    {
        if (language == "zh")
        {
            language = "zh-CN";
        }
        else if (language == "pt")
        {
            language = "pt-PT";
        }

        if (_languages is null || _languages.Count == 0)
        {
            var res = await OpenSubtitlesApi.GetLanguageList(cancellationToken).ConfigureAwait(false);

            if (!res.Ok || res.Data?.Data is null)
            {
                throw new HttpRequestException(string.Format(CultureInfo.InvariantCulture, "Failed to get language list: {0}", res.Code));
            }

            _languages = res.Data.Data.Where(x => !string.IsNullOrWhiteSpace(x.Code)).Select(x => x.Code!).ToList();
        }

        var found = _languages.FirstOrDefault(x => string.Equals(x, language, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            return found;
        }

        if (language.Contains('-', StringComparison.OrdinalIgnoreCase))
        {
            return await GetLanguage(language.Split('-')[0], mediaPath, cancellationToken).ConfigureAwait(false);
        }

        throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "Language '{0}' is not supported ({1})", language, mediaPath));
    }

    internal void ConfigurationChanged(PluginConfiguration e)
    {
        _configuration = e;
        // force a login next time a request is made
        _login = null;
    }
}
