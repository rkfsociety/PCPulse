using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using PCPulse.Core.GitHub;
using PCPulse.Core.Security;

namespace PCPulse.Core.Updates;

public sealed record AppUpdateAsset(
    string FileName,
    string DownloadUrl,
    string ChecksumsUrl);

public sealed record AppUpdateCheckResult(
    bool IsUpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    string? ReleaseUrl,
    AppUpdateAsset? Asset,
    string Message);

/// <summary>
/// Проверка и безопасная загрузка обновлений самой оболочки PCPulse из GitHub Releases.
/// </summary>
public sealed class AppUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string? _githubToken;

    public AppUpdateService(HttpClient httpClient, string? githubToken = null)
    {
        _http = httpClient;
        _githubToken = githubToken;
    }

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(
        Version currentVersion,
        bool preferInstallerOnWindows = true,
        string repository = "rkfsociety/PCPulse",
        CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{repository}/releases/latest";
        using var request = GitHubCliAuth.CreateApiRequest(url, _githubToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new AppUpdateCheckResult(
                false,
                currentVersion,
                null,
                null,
                null,
                "Опубликованные релизы PCPulse пока не найдены.");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize<GitHubReleaseClient.GitHubReleaseDto>(json, JsonOptions);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return new AppUpdateCheckResult(false, currentVersion, null, null, null, "Не удалось прочитать тег последнего релиза.");
        }

        var rawTag = release.TagName.Trim();
        if (rawTag.StartsWith('v') || rawTag.StartsWith('V'))
            rawTag = rawTag[1..];

        if (!Version.TryParse(rawTag, out var latestVersion))
        {
            return new AppUpdateCheckResult(false, currentVersion, null, release.HtmlUrl, null, $"Некорректная версия релиза: {release.TagName}");
        }

        if (latestVersion <= currentVersion)
        {
            return new AppUpdateCheckResult(
                false,
                currentVersion,
                latestVersion,
                release.HtmlUrl,
                null,
                $"Установлена актуальная версия PCPulse ({currentVersion}).");
        }

        var asset = SelectPlatformAsset(release.Assets, preferInstallerOnWindows);
        if (asset is null)
        {
            return new AppUpdateCheckResult(
                true,
                currentVersion,
                latestVersion,
                release.HtmlUrl,
                null,
                $"Доступна версия {latestVersion}, но пакет обновления для текущей платформы не найден в релизе.");
        }

        return new AppUpdateCheckResult(
            true,
            currentVersion,
            latestVersion,
            release.HtmlUrl,
            asset,
            $"Доступна новая версия PCPulse {latestVersion} (установлена {currentVersion}).");
    }

    public async Task<string> DownloadAndVerifyAssetAsync(
        AppUpdateAsset asset,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var safeName = Path.GetFileName(asset.FileName);
        var targetPath = Path.Combine(destinationDirectory, safeName);

        var checksumsText = await _http.GetStringAsync(asset.ChecksumsUrl, cancellationToken).ConfigureAwait(false);
        var expectedLine = ExtractChecksumLineForFile(checksumsText, safeName)
            ?? throw new InvalidDataException($"В файле контрольных сумм не найдена запись для «{safeName}».");

        await GitHubReleaseClient.DownloadFileAsync(_http, asset.DownloadUrl, targetPath, cancellationToken).ConfigureAwait(false);
        if (!await Sha256Verifier.VerifyFileAsync(targetPath, expectedLine, cancellationToken).ConfigureAwait(false))
        {
            try { File.Delete(targetPath); } catch { }
            throw new InvalidDataException($"Контрольная сумма SHA-256 файла «{safeName}» не совпала.");
        }

        return targetPath;
    }

    private static AppUpdateAsset? SelectPlatformAsset(
        List<GitHubReleaseClient.GitHubAssetDto>? assets,
        bool preferInstallerOnWindows)
    {
        if (assets is null || assets.Count == 0)
            return null;

        string[] preferredNames;
        if (OperatingSystem.IsWindows())
        {
            preferredNames = preferInstallerOnWindows
                ? ["PCPulse-Setup-x64.exe", "PCPulse-win-x64.exe"]
                : ["PCPulse-win-x64.exe", "PCPulse-Setup-x64.exe"];
        }
        else if (OperatingSystem.IsLinux())
        {
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            preferredNames = [$"PCPulse-linux-{arch}.tar.gz"];
        }
        else
        {
            return null;
        }

        GitHubReleaseClient.GitHubAssetDto? chosen = null;
        foreach (var candidate in preferredNames)
        {
            chosen = assets.FirstOrDefault(a => string.Equals(a.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (chosen is not null)
                break;
        }

        if (chosen?.Name is null || chosen.BrowserDownloadUrl is null)
            return null;

        var checksumAsset = assets.FirstOrDefault(a => string.Equals(a.Name, chosen.Name + ".sha256", StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(a => string.Equals(a.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

        if (checksumAsset?.BrowserDownloadUrl is null)
            return null;

        return new AppUpdateAsset(chosen.Name, chosen.BrowserDownloadUrl, checksumAsset.BrowserDownloadUrl);
    }

    private static string? ExtractChecksumLineForFile(string checksumsText, string fileName)
    {
        var lines = checksumsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 1 && !lines[0].Contains(' ') && !lines[0].Contains('\t'))
            return lines[0];

        foreach (var line in lines)
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                string.Equals(Path.GetFileName(parts[^1].TrimStart('*')), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0];
            }
        }

        return lines.Length == 1 ? lines[0] : null;
    }
}
