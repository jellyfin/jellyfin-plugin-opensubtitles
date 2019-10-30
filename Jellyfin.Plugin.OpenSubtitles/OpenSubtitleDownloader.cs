using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using OpenSubtitlesHandler;

namespace Jellyfin.Plugin.OpenSubtitles
{
    public class OpenSubtitleDownloader : ISubtitleProvider
    {
        private static readonly CultureInfo _usCulture = CultureInfo.ReadOnly(new CultureInfo("en-US"));
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private DateTime _lastRateLimitException;
        private DateTime _lastLogin;
        private int _rateLimitLeft = 40;

        public OpenSubtitleDownloader(ILogger<OpenSubtitleDownloader> logger, IFileSystem fileSystem, IHttpClient httpClient)
        {
            _logger = logger;
            _fileSystem = fileSystem;

            Utilities.HttpClient = httpClient;
            OpenSubtitlesHandler.OpenSubtitles.SetUserAgent("jellyfin");
        }

        public string Name => "Open Subtitles";

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public IEnumerable<VideoContentType> SupportedMediaTypes
            => new[] { VideoContentType.Episode, VideoContentType.Movie };

        public Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
            => GetSubtitlesInternal(id, GetOptions(), cancellationToken);

        private async Task<SubtitleResponse> GetSubtitlesInternal(
            string id,
            PluginConfiguration options,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            var idParts = id.Split(new[] { '-' }, 3);

            var format = idParts[0];
            var language = idParts[1];
            var ossId = idParts[2];

            var downloadsList = new[] { int.Parse(ossId, _usCulture) };

            await Login(cancellationToken).ConfigureAwait(false);

            if ((DateTime.UtcNow - _lastRateLimitException).TotalHours < 1)
            {
                throw new RateLimitExceededException("OpenSubtitles rate limit reached");
            }

            if (_rateLimitLeft == 0)
            {
                await Task.Delay(1000);
            }
            var downloadResponse = await OpenSubtitlesHandler.OpenSubtitles.DownloadSubtitlesAsync(downloadsList, cancellationToken).ConfigureAwait(false);

            var downloadResult = downloadResponse.Item1;
            _rateLimitLeft = downloadResponse.Item2 ?? _rateLimitLeft;

            if (downloadResponse.Item2 != null)
            {
                if (_rateLimitLeft <= 4)
                {
                    await Task.Delay(250);
                }
            }

            if ((downloadResult.Status ?? string.Empty).IndexOf("407", StringComparison.OrdinalIgnoreCase) != -1)
            {
                _lastRateLimitException = DateTime.UtcNow;
                throw new RateLimitExceededException("OpenSubtitles daily limit reached");
            }
            else if (!(downloadResult is MethodResponseSubtitleDownload))
            {
                throw new Exception("Invalid response type");
            }

            var results = ((MethodResponseSubtitleDownload)downloadResult).Results;

            _lastRateLimitException = DateTime.MinValue;

            if (results.Count == 0)
            {
                var msg = string.Format("Subtitle with Id {0} was not found. Name: {1}. Status: {2}. Message: {3}",
                    ossId,
                    downloadResult.Name ?? string.Empty,
                    downloadResult.Status ?? string.Empty,
                    downloadResult.Message ?? string.Empty);

                throw new ResourceNotFoundException(msg);
            }

            var data = Convert.FromBase64String(results[0].Data);

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
                _logger.LogWarning("The username or password has no value. Attempting to access the service without an account.");
                return;
            }

            var loginResponse = await OpenSubtitlesHandler.OpenSubtitles.LogInAsync(
                options.Username,
                options.Password,
                "en",
                cancellationToken).ConfigureAwait(false);

            if (loginResponse.Item2 == 1)
            {
                await Task.Delay(1000);
            }

            if (!(loginResponse.Item1 is MethodResponseLogIn))
            {
                throw new Exception("Authentication to OpenSubtitles failed.");
            }
            _rateLimitLeft = loginResponse.Item2 == null ? _rateLimitLeft : (int)loginResponse.Item2;
            _lastLogin = DateTime.UtcNow;
        }

        public async Task<IEnumerable<NameIdPair>> GetSupportedLanguages(CancellationToken cancellationToken)
        {
            await Login(cancellationToken).ConfigureAwait(false);

            var result = OpenSubtitlesHandler.OpenSubtitles.GetSubLanguages("en");
            if (!(result is MethodResponseGetSubLanguages))
            {
                _logger.LogError("Invalid response type");
                return Enumerable.Empty<NameIdPair>();
            }

            var results = ((MethodResponseGetSubLanguages)result).Languages;

            return results.Select(i => new NameIdPair
            {
                Name = i.LanguageName,
                Id = i.SubLanguageID
            });
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

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            var imdbIdText = request.GetProviderId(MetadataProviders.Imdb);
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

            var subLanguageId = NormalizeLanguage(request.Language);

            string hash;
            using (var fileStream = File.OpenRead(request.MediaPath))
            {
                hash = Utilities.ComputeHash(fileStream);
            }

            var fileInfo = _fileSystem.GetFileInfo(request.MediaPath);
            var movieByteSize = fileInfo.Length;
            var searchImdbId = request.ContentType == VideoContentType.Movie ? imdbId.ToString(_usCulture) : "";
            var subtitleSearchParameters = request.ContentType == VideoContentType.Episode
                ? new List<SubtitleSearchParameters> {
                                                         new SubtitleSearchParameters(subLanguageId,
                                                             query: request.SeriesName,
                                                             season: request.ParentIndexNumber.Value.ToString(_usCulture),
                                                             episode: request.IndexNumber.Value.ToString(_usCulture))
                                                     }
                : new List<SubtitleSearchParameters> {
                                                         new SubtitleSearchParameters(subLanguageId, imdbid: searchImdbId),
                                                         new SubtitleSearchParameters(subLanguageId, query: request.Name, imdbid: searchImdbId)
                                                     };
            var parms = new List<SubtitleSearchParameters> {
                                                               new SubtitleSearchParameters( subLanguageId,
                                                                   movieHash: hash,
                                                                   movieByteSize: movieByteSize,
                                                                   imdbid: searchImdbId ),
                                                           };
            parms.AddRange(subtitleSearchParameters);

            if (_rateLimitLeft == 0)
            {
                await Task.Delay(1000);
            }
            var searchResponse = await OpenSubtitlesHandler.OpenSubtitles.SearchSubtitlesAsync(parms.ToArray(), cancellationToken).ConfigureAwait(false);

            var searchResult = searchResponse.Item1;
            _rateLimitLeft = searchResponse.Item2 ?? _rateLimitLeft;

            if (searchResponse.Item2 != null)
            {
                if (_rateLimitLeft <= 4)
                {
                    await Task.Delay(250);
                }
            }

            if (!(searchResult is MethodResponseSubtitleSearch))
            {
                _logger.LogError("Invalid response type");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }

            Predicate<SubtitleSearchResult> mediaFilter =
                x =>
                    request.ContentType == VideoContentType.Episode
                        ? !string.IsNullOrEmpty(x.SeriesSeason) && !string.IsNullOrEmpty(x.SeriesEpisode) &&
                          int.Parse(x.SeriesSeason, _usCulture) == request.ParentIndexNumber &&
                          int.Parse(x.SeriesEpisode, _usCulture) == request.IndexNumber
                        : !string.IsNullOrEmpty(x.IDMovieImdb) && long.Parse(x.IDMovieImdb, _usCulture) == imdbId;

            var results = ((MethodResponseSubtitleSearch)searchResult).Results;

            // Avoid implicitly captured closure
            var hasCopy = hash;

            return results.Where(x => x.SubBad == "0" && mediaFilter(x) && (!request.IsPerfectMatch || string.Equals(x.MovieHash, hash, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(x => (string.Equals(x.MovieHash, hash, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                    .ThenBy(x => Math.Abs(long.Parse(x.MovieByteSize, _usCulture) - movieByteSize))
                    .ThenByDescending(x => int.Parse(x.SubDownloadsCnt, _usCulture))
                    .ThenByDescending(x => double.Parse(x.SubRating, _usCulture))
                    .Select(i => new RemoteSubtitleInfo
                    {
                        Author = i.UserNickName,
                        Comment = i.SubAuthorComment,
                        CommunityRating = float.Parse(i.SubRating, _usCulture),
                        DownloadCount = int.Parse(i.SubDownloadsCnt, _usCulture),
                        Format = i.SubFormat,
                        ProviderName = Name,
                        ThreeLetterISOLanguageName = i.SubLanguageID,

                        Id = i.SubFormat + "-" + i.SubLanguageID + "-" + i.IDSubtitleFile,

                        Name = i.SubFileName,
                        DateCreated = DateTime.Parse(i.SubAddDate, _usCulture),
                        IsHashMatch = i.MovieHash == hasCopy

                    }).Where(i => !string.Equals(i.Format, "sub", StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Format, "idx", StringComparison.OrdinalIgnoreCase));
        }
    }
}
