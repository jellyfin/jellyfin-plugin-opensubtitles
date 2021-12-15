using System.Collections.Generic;
using Xunit;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Tests;

public static class RequestHandlerTests
{
    public static TheoryData<string, Dictionary<string, string>, string> ComputeHash_Success_TestData()
        => new TheoryData<string, Dictionary<string, string>, string>
        {
            {
                "/subtitles",
                new Dictionary<string, string>()
                {
                    { "b", "c and d" },
                    { "a", "1" }
                },
                "/subtitles?a=1&b=c+and+d"
            }
        };

    [Theory]
    [MemberData(nameof(ComputeHash_Success_TestData))]
    public static void ComputeHash_Success(string path, Dictionary<string, string> param, string expected)
        => Assert.Equal(expected, RequestHandler.AddQueryString(path, param));
}
