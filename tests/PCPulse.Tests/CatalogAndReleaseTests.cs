using System.Net;
using System.Security.Cryptography;
using System.Text;
using PCPulse.Contracts;
using PCPulse.Core.Catalog;
using PCPulse.Core.GitHub;
using Xunit;

namespace PCPulse.Tests;

public sealed class CatalogAndReleaseTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcpulse-cat-tests-" + Guid.NewGuid().ToString("N"));

    public CatalogAndReleaseTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ModuleCatalog_LoadBuiltIn_ContainsMemPulseAndDiskPulse()
    {
        var entries = ModuleCatalog.LoadBuiltIn();

        Assert.Contains(entries, e => e.Id == "mempulse" && e.Repository == "rkfsociety/MemPulse" && e.Platforms == PulsePlatform.All);
        Assert.Contains(entries, e => e.Id == "diskpulse" && e.Repository == "rkfsociety/DiskPulse" && e.Platforms == PulsePlatform.All);
    }

    [Fact]
    public async Task FindLatestCompatibleModuleAsync_SkipsFutureContractVersionWithoutDownloadingZip_AndSelectsOlderCompatible()
    {
        var rid = GitHubReleaseClient.GetCurrentRid();
        var releasesJson = $$"""
        [
          {
            "tag_name": "v2.0.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              { "name": "mempulse-module-2.0.0.manifest.json", "browser_download_url": "https://dl.test/v2.manifest.json" },
              { "name": "mempulse-module-2.0.0-{{rid}}.zip", "browser_download_url": "https://dl.test/v2.zip" },
              { "name": "mempulse-module-2.0.0-{{rid}}.zip.sha256", "browser_download_url": "https://dl.test/v2.zip.sha256" }
            ]
          },
          {
            "tag_name": "v1.2.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              { "name": "mempulse-module-1.2.0.manifest.json", "browser_download_url": "https://dl.test/v1.manifest.json" },
              { "name": "mempulse-module-1.2.0-any.zip", "browser_download_url": "https://dl.test/v1-any.zip" },
              { "name": "mempulse-module-1.2.0-any.zip.sha256", "browser_download_url": "https://dl.test/v1-any.zip.sha256" }
            ]
          }
        ]
        """;

        var v2Manifest = """
        {
          "id": "mempulse",
          "name": "MemPulse",
          "description": "Future",
          "version": "2.0.0",
          "contractVersion": "2.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "MemPulse.PulseModule.dll",
          "entryType": "MemPulse.PulseModule.MemPulseModule"
        }
        """;

        var v1Manifest = """
        {
          "id": "mempulse",
          "name": "MemPulse",
          "description": "Compatible",
          "version": "1.2.0",
          "contractVersion": "1.0",
          "platforms": ["windows", "linux"],
          "entryAssembly": "MemPulse.PulseModule.dll",
          "entryType": "MemPulse.PulseModule.MemPulseModule"
        }
        """;

        var handler = new StubHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.EndsWith("/releases?per_page=10", StringComparison.Ordinal))
                return JsonResponse(releasesJson);
            if (url == "https://dl.test/v2.manifest.json")
                return JsonResponse(v2Manifest);
            if (url == "https://dl.test/v1.manifest.json")
                return JsonResponse(v1Manifest);
            if (url == "https://dl.test/v2.zip")
                throw new InvalidOperationException("Не должен скачивать ZIP несовместимого релиза!");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http);
        var entry = new CatalogModuleEntry("mempulse", "MemPulse", "Desc", "rkfsociety/MemPulse", PulsePlatform.All);

        var result = await client.FindLatestCompatibleModuleAsync(entry);

        Assert.Equal(ModuleAvailabilityStatus.CompatibleAvailable, result.Status);
        Assert.NotNull(result.Manifest);
        Assert.Equal(new Version(1, 2, 0), result.Manifest!.Version);
        Assert.Equal("https://dl.test/v1-any.zip", result.ZipUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.LatestSkippedReason));
    }

    [Fact]
    public async Task DownloadAndVerifyModuleZipAsync_RejectsSha256MismatchAndDeletesPartialZip()
    {
        var payload = Encoding.UTF8.GetBytes("tampered-zip-bytes");
        var wrongHash = new string('a', 64);

        var handler = new StubHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url == "https://dl.test/mod.zip")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            if (url == "https://dl.test/mod.zip.sha256")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{wrongHash}  mod.zip") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http);
        var resolution = new ModuleReleaseResolution(
            ModuleAvailabilityStatus.CompatibleAvailable,
            Manifest: null,
            ZipFileName: "mod.zip",
            ZipUrl: "https://dl.test/mod.zip",
            Sha256Url: "https://dl.test/mod.zip.sha256",
            Reason: null,
            LatestSkippedReason: null);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.DownloadAndVerifyModuleZipAsync(resolution, _tempDir));

        Assert.False(File.Exists(Path.Combine(_tempDir, "mod.zip")));
    }

    [Fact]
    public async Task DownloadAndVerifyModuleZipAsync_AcceptsValidSha256()
    {
        var payload = Encoding.UTF8.GetBytes("valid-zip-bytes");
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));

        var handler = new StubHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url == "https://dl.test/mod.zip")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            if (url == "https://dl.test/mod.zip.sha256")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{hash}  mod.zip") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        var client = new GitHubReleaseClient(http);
        var resolution = new ModuleReleaseResolution(
            ModuleAvailabilityStatus.CompatibleAvailable,
            Manifest: null,
            ZipFileName: "mod.zip",
            ZipUrl: "https://dl.test/mod.zip",
            Sha256Url: "https://dl.test/mod.zip.sha256",
            Reason: null,
            LatestSkippedReason: null);

        var path = await client.DownloadAndVerifyModuleZipAsync(resolution, _tempDir);
        Assert.True(File.Exists(path));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
