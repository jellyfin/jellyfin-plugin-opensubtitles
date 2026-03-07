using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Tests;

public class OpenSubtitlesRequestHelperTests
{
    [Theory]
    [InlineData("breakdance.avi", "8e245d9679d31e12")]
    public void ComputeHash_Success(string filename, string hash)
    {
        using var str = File.OpenRead(Path.Join("Test Data", filename));
        Assert.Equal(hash, OpenSubtitlesRequestHelper.ComputeHash(str));
    }

    [Fact]
    public async Task SearchSubtitleFromHtmlAsync_ReturnsResult_WhenSubtitleFound()
    {
        var html = @"<a itemprop=""url"" title=""Download"" href=""https://dl.opensubtitles.org/en/download/sub/13235718""><span itemprop=""name"">Eenie Meanie subtitles Bulgarian</span></a>";

        var httpMessageHandler = new MockHttpMessageHandler(html);
        var httpClient = new HttpClient(httpMessageHandler);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var helper = new OpenSubtitlesRequestHelper(httpClientFactoryMock.Object);

        var request = new SubtitleSearchRequest
        {
            Name = "Eenie Meanie",
            ProductionYear = 2025
        };
        var options = new Dictionary<string, string> { { "languages", "bg" } };

        var result = await helper.SearchSubtitleFromHtmlAsync(request, options, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Ok);
        Assert.Single(result.Data);
        Assert.Equal(13235718, result.Data[0].Attributes.Files[0].FileId);
        Assert.Equal("Eenie Meanie subtitles Bulgarian", result.Data[0].Attributes.Release);
        Assert.Equal(15514498, result.Data[0].Attributes.FeatureDetails.ImdbId);
    }

    [Fact]
    public async Task SearchSubtitleFromHtmlAsync_ReturnsRealResult_WhenCalledWithRealApi()
    {
        var httpClientFactory = new RealHttpClientFactory();
        var helper = new OpenSubtitlesRequestHelper(httpClientFactory);

        var request = new SubtitleSearchRequest
        {
            Name = "Eenie Meanie",
            ProductionYear = 2025
        };
        var options = new Dictionary<string, string> { { "languages", "bg" } };

        var result = await helper.SearchSubtitleFromHtmlAsync(request, options, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Ok);
        Assert.NotEmpty(result.Data);
        Assert.NotNull(result);
        Assert.True(result.Ok);
        Assert.Single(result.Data);
        Assert.Equal(13235718, result.Data[0].Attributes.Files[0].FileId);
        Assert.Equal("Eenie Meanie subtitles Bulgarian", result.Data[0].Attributes.Release);
        Assert.Equal(15514498, result.Data[0].Attributes.FeatureDetails.ImdbId);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseHtml;

        public MockHttpMessageHandler(string responseHtml)
        {
            _responseHtml = responseHtml;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(_responseHtml)
            });
        }
    }

    private class RealHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };

            var httpClient = new HttpClient(handler)
            {
                DefaultRequestHeaders =
                {
                    UserAgent =
                    {
                        new ProductInfoHeaderValue("Jellyfin-Plugin-OpenSubtitles", "1.0.0")
                    }
                }
            };

            return httpClient;
        }
    }
}
