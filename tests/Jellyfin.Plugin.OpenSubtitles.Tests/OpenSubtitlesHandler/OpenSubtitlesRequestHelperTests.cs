using System.IO;
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
}
