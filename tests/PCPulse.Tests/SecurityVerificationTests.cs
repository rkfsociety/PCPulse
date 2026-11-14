using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PCPulse.Core.Logging;
using PCPulse.Core.Security;
using PCPulse.Core.Settings;
using Xunit;

namespace PCPulse.Tests;

public sealed class SecurityVerificationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pcpulse-sec-tests-" + Guid.NewGuid().ToString("N"));

    public SecurityVerificationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ResolveSafeChildPath_AllowsSubdirectoryAndRejectsEscape()
    {
        var safe = PathGuard.ResolveSafeChildPath(_tempDir, "sub/file.dll");
        Assert.StartsWith(Path.GetFullPath(_tempDir), safe, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<UnauthorizedAccessException>(() => PathGuard.ResolveSafeChildPath(_tempDir, "../escape.dll"));
        Assert.Throws<UnauthorizedAccessException>(() => PathGuard.ResolveSafeChildPath(_tempDir, Path.GetFullPath(_tempDir)));
    }

    [Fact]
    public async Task Sha256Verifier_AcceptsMatchingHashAndRejectsMismatch()
    {
        var file = Path.Combine(_tempDir, "payload.zip");
        var bytes = Encoding.UTF8.GetBytes("pcpulse-payload");
        await File.WriteAllBytesAsync(file, bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        Assert.True(await Sha256Verifier.VerifyFileAsync(file, $"{hash}  payload.zip"));
        Assert.True(await Sha256Verifier.VerifyFileAsync(file, hash.ToUpperInvariant()));
        Assert.False(await Sha256Verifier.VerifyFileAsync(file, new string('0', 64)));
        Assert.False(await Sha256Verifier.VerifyFileAsync(file, "not-a-hash"));
    }

    [Fact]
    public async Task ZipSafeExtractor_RejectsZipSlipTraversalEntry()
    {
        var zipPath = Path.Combine(_tempDir, "evil.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("pwn");
        }

        var extractDir = Path.Combine(_tempDir, "out");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ZipSafeExtractor.ExtractToDirectoryAsync(zipPath, extractDir));
        Assert.False(File.Exists(Path.Combine(_tempDir, "outside.txt")));
    }

    [Fact]
    public async Task JsonSettingsStore_RejectsTraversalKeyAndRoundTripsObject()
    {
        var store = new JsonSettingsStore(_tempDir);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("../bad", new SampleConfig("x")));

        await store.SaveAsync("prefs", new SampleConfig("dark"));
        var loaded = await store.LoadAsync<SampleConfig>("prefs");
        Assert.NotNull(loaded);
        Assert.Equal("dark", loaded.Theme);
    }

    [Fact]
    public void PulseFileLogger_WritesAndRotatesLogFile()
    {
        var logger = new PulseFileLogger(_tempDir, "test", maxBytes: 128);
        for (var i = 0; i < 10; i++)
        {
            logger.Info("Сообщение журнала " + new string('A', 30));
        }

        Assert.True(File.Exists(Path.Combine(_tempDir, "app.log")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "app.log.1")));
    }

    private sealed record SampleConfig(string Theme);
}
