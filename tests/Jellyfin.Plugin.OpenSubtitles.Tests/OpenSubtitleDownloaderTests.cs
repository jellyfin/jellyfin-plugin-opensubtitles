using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.OpenSubtitles.Tests;

public class OpenSubtitleDownloaderTests
{
    private readonly Mock<ILogger<OpenSubtitleDownloader>> _loggerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly OpenSubtitleDownloader _downloader;

    public OpenSubtitleDownloaderTests()
    {
        _loggerMock = new Mock<ILogger<OpenSubtitleDownloader>>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        var httpMessageHandler = new HttpMessageHandlerMock();
        var httpClient = new HttpClient(httpMessageHandler);
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        _downloader = new OpenSubtitleDownloader(_loggerMock.Object, _httpClientFactoryMock.Object);
    }

    private class HttpMessageHandlerMock : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });
        }
    }

    [Fact]
    public async Task Search_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _downloader.Search(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Search_ReturnsEmpty_WhenEpisodeInfoMissing()
    {
        var request = new SubtitleSearchRequest
        {
            // ContentType = MediaBrowser.Model.Entities.VideoContentType.Episode,
            IndexNumber = 1,
            ParentIndexNumber = null,
            SeriesName = null
        };

        var result = await _downloader.Search(request, CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Search_ReturnsEmpty_WhenMediaPathMissing()
    {
        var request = new SubtitleSearchRequest
        {
            // ContentType = MediaBrowser.Model.Entities.VideoContentType.Movie,
            MediaPath = null
        };

        var result = await _downloader.Search(request, CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
