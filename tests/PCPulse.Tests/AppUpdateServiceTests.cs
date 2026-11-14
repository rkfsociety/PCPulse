using System.Net;
using System.Security.Cryptography;
using System.Text;
using PCPulse.Core.Updates;
using Xunit;

namespace PCPulse.Tests;

public sealed class AppUpdateServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcpulse-upd-tests-" + Guid.NewGuid().ToString("N"));

    public AppUpdateServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DetectsNewerVersionAndSelectsMatchingPlatformAsset()
    {
        const string releaseJson = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/rkfsociety/PCPulse/releases/tag/v0.2.0",
          "assets": [
            { "name": "PCPulse-Setup-x64.exe", "browser_download_url": "https://dl.test/PCPulse-Setup-x64.exe" },
            { "name": "PCPulse-win-x64.exe", "browser_download_url": "https://dl.test/PCPulse-win-x64.exe" },
            { "name": "PCPulse-linux-x64.tar.gz", "browser_download_url": "https://dl.test/PCPulse-linux-x64.tar.gz" },
            { "name": "PCPulse-linux-arm64.tar.gz", "browser_download_url": "https://dl.test/PCPulse-linux-arm64.tar.gz" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://dl.test/SHA256SUMS.txt" }
          ]
        }
        """;

        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releaseJson, Encoding.UTF8, "application/json") }));
        var service = new AppUpdateService(http);

        var result = await service.CheckForUpdateAsync(new Version(0, 1, 0), preferInstallerOnWindows: true);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(0, 2, 0), result.LatestVersion);
        Assert.NotNull(result.Asset);
        Assert.Equal("https://dl.test/SHA256SUMS.txt", result.Asset!.ChecksumsUrl);
    }

    [Fact]
    public async Task DownloadAndVerifyAssetAsync_ParsesSha256SumsAndVerifiesFile()
    {
        var bytes = Encoding.UTF8.GetBytes("installer-binary");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var sums = $"{new string('b', 64)}  other.tar.gz\n{hash}  PCPulse-Setup-x64.exe\n";

        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.ToString() == "https://dl.test/PCPulse-Setup-x64.exe")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (req.RequestUri!.ToString() == "https://dl.test/SHA256SUMS.txt")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var service = new AppUpdateService(http);
        var asset = new AppUpdateAsset("PCPulse-Setup-x64.exe", "https://dl.test/PCPulse-Setup-x64.exe", "https://dl.test/SHA256SUMS.txt");

        var downloaded = await service.DownloadAndVerifyAssetAsync(asset, _tempDir);
        Assert.True(File.Exists(downloaded));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(fn(request));
    }
}
