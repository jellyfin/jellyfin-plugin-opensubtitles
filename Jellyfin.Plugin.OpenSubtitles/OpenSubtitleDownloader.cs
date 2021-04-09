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
using OpenSubtitlesHandler;

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
        private DateTime _lastRateLimitException;
        private DateTime _lastLogin;
        private int _rateLimitLeft = 40;

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

            var subLanguageId = NormalizeLanguage(request.Language);

            string hash;
            using (var fileStream = File.OpenRead(request.MediaPath))
            {
                hash = Utilities.ComputeHash(fileStream);
            }

            var fileInfo = _fileSystem.GetFileInfo(request.MediaPath);
            var movieByteSize = fileInfo.Length;
            var searchImdbId = request.ContentType == VideoContentType.Movie ? imdbId.ToString(UsCulture) : string.Empty;
            var subtitleSearchParameters = request.ContentType == VideoContentType.Episode
                ? new List<SubtitleSearchParameters>
                {
                    new SubtitleSearchParameters(
                        subLanguageId,
                        query: request.SeriesName,
                        season: request.ParentIndexNumber!.Value.ToString(UsCulture),
                        episode: request.IndexNumber!.Value.ToString(UsCulture))
                }
                : new List<SubtitleSearchParameters>
                {
                    new SubtitleSearchParameters(subLanguageId, imdbid: searchImdbId),
                    new SubtitleSearchParameters(subLanguageId, query: request.Name, imdbid: searchImdbId)
                };
            var parms = new List<SubtitleSearchParameters>
            {
                new SubtitleSearchParameters(
                    subLanguageId,
                    movieHash: hash,
                    movieByteSize: movieByteSize,
                    imdbid: searchImdbId),
            };
            parms.AddRange(subtitleSearchParameters);

            if (_rateLimitLeft == 0)
            {
                await Task.Delay(1000, cancellationToken)
                    .ConfigureAwait(false);
            }

            var searchResponse = await OpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(parms.ToArray(), cancellationToken).ConfigureAwait(false);

            var searchResult = searchResponse.Item1;
            _rateLimitLeft = searchResponse.Item2 ?? _rateLimitLeft;

            if (searchResponse.Item2 != null)
            {
                if (_rateLimitLeft <= 4)
                {
                    await Task.Delay(250, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (searchResult is not MethodResponseSubtitleSearch subtitleSearchResult)
            {
                _logger.LogError(
                    "Invalid response type. Name: {Name}, Message: {Message}, Status: {Status}",
                    searchResult.Name,
                    searchResult.Message,
                    searchResult.Status);
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            Predicate<SubtitleSearchResult> mediaFilter =
                x =>
                    request.ContentType == VideoContentType.Episode
                        ? !string.IsNullOrEmpty(x.SeriesSeason) && !string.IsNullOrEmpty(x.SeriesEpisode) &&
                          int.Parse(x.SeriesSeason, UsCulture) == request.ParentIndexNumber &&
                          int.Parse(x.SeriesEpisode, UsCulture) == request.IndexNumber
                        : !string.IsNullOrEmpty(x.IDMovieImdb) && long.Parse(x.IDMovieImdb, UsCulture) == imdbId;

            var results = subtitleSearchResult.Results;

            // Avoid implicitly captured closure
            var hasCopy = hash;

            return results.Where(x => x.SubBad == "0" && mediaFilter(x) && (!request.IsPerfectMatch || string.Equals(x.MovieHash, hash, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => (string.Equals(x.MovieHash, hash, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                .ThenBy(x => Math.Abs(long.Parse(x.MovieByteSize, UsCulture) - movieByteSize))
                .ThenByDescending(x => int.Parse(x.SubDownloadsCnt, UsCulture))
                .ThenByDescending(x => double.Parse(x.SubRating, UsCulture))
                .Select(i => new RemoteSubtitleInfo
                {
                    Author = i.UserNickName,
                    Comment = i.SubAuthorComment,
                    CommunityRating = float.Parse(i.SubRating, UsCulture),
                    DownloadCount = int.Parse(i.SubDownloadsCnt, UsCulture),
                    Format = i.SubFormat,
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = i.SubLanguageID,

                    Id = i.SubFormat + "-" + i.SubLanguageID + "-" + i.IDSubtitleFile,

                    Name = i.SubFileName,
                    DateCreated = DateTime.Parse(i.SubAddDate, UsCulture),
                    IsHashMatch = i.MovieHash == hasCopy
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

            var idParts = id.Split('-', 3);
            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var downloadsList = new[] { int.Parse(ossId, UsCulture) };

            await Login(cancellationToken).ConfigureAwait(false);

            if ((DateTime.UtcNow - _lastRateLimitException).TotalHours < 1)
            {
                throw new RateLimitExceededException("OpenSubtitles rate limit reached");
            }

            if (_rateLimitLeft == 0)
            {
                await Task.Delay(1000, cancellationToken)
                    .ConfigureAwait(false);
            }

            var downloadResponse = await OpenSubtitlesHandler.OpenSubtitles.DownloadSubtitlesAsync(downloadsList, cancellationToken).ConfigureAwait(false);

            var downloadResult = downloadResponse.Item1;
            _rateLimitLeft = downloadResponse.Item2 ?? _rateLimitLeft;

            if (downloadResponse.Item2 != null)
            {
                if (_rateLimitLeft <= 4)
                {
                    await Task.Delay(250, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if ((downloadResult.Status ?? string.Empty).IndexOf("407", StringComparison.OrdinalIgnoreCase) != -1)
            {
                _lastRateLimitException = DateTime.UtcNow;
                throw new RateLimitExceededException("OpenSubtitles daily limit reached");
            }

            if (downloadResult is not MethodResponseSubtitleDownload subtitleDownloadResult)
            {
                throw new OpenApiException($"Invalid response type. Name: {downloadResult.Name}, Message: {downloadResult.Message}, Status: {downloadResult.Status}");
            }

            _lastRateLimitException = DateTime.MinValue;

            if (subtitleDownloadResult.Results.Count == 0)
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "Subtitle with Id {0} was not found. Name: {1}. Status: {2}. Message: {3}",
                    ossId,
                    downloadResult.Name ?? string.Empty,
                    downloadResult.Status ?? string.Empty,
                    downloadResult.Message ?? string.Empty);

                throw new ResourceNotFoundException(msg);
            }

            var data = Convert.FromBase64String(subtitleDownloadResult.Results[0].Data);

            return new SubtitleResponse
            {
                Format = format,
                Language = language,

                Stream = new MemoryStream(Utilities.Decompress(new MemoryStream(data)))
            };
        }

        private async Task Login(CancellationToken cancellationToken)
        {
            if ((DateTime.UtcNow - _lastLogin).TotalSeconds < 60)
            {
                return;
            }

            var options = GetOptions();
            if (options.Username.Length == 0 || options.Password.Length == 0)
            {
                _logger.LogWarning("The username or password has no value. Attempting to access the service without an account");
                return;
            }

            var loginResponse = await OpenSubtitlesHandler.OpenSubtitles.LogInAsync(
                options.Username,
                options.Password,
                "en",
                cancellationToken).ConfigureAwait(false);

            if (loginResponse.Item2 == 1)
            {
                await Task.Delay(1000, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!(loginResponse.Item1 is MethodResponseLogIn))
            {
                throw new AuthenticationException("Authentication to OpenSubtitles failed.");
            }

            _rateLimitLeft = loginResponse.Item2 ?? _rateLimitLeft;
            _lastLogin = DateTime.UtcNow;
        }

        private static string NormalizeLanguage(string language)
        {
            // Problem with Greek subtitle download #1349
            if (string.Equals(language, "gre", StringComparison.OrdinalIgnoreCase))
            {
                return "ell";
            }

            return language;
        }

        private PluginConfiguration GetOptions()
            => OpenSubtitlesPlugin.Instance!.Configuration;
    }
}
