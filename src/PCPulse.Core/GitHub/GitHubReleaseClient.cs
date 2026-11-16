using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCPulse.Contracts;
using PCPulse.Core.Catalog;
using PCPulse.Core.Modules;
using PCPulse.Core.Security;

namespace PCPulse.Core.GitHub;

public enum ModuleAvailabilityStatus
{
    CompatibleAvailable = 0,
    NoModuleReleases = 1,
    IncompatiblePlatform = 2,
    IncompatibleContract = 3
}

public sealed record ModuleReleaseResolution(
    ModuleAvailabilityStatus Status,
    ModuleManifest? Manifest,
    string? ZipFileName,
    string? ZipUrl,
    string? Sha256Url,
    string? Reason,
    string? LatestSkippedReason);

/// <summary>
/// Клиент GitHub Releases для поиска совместимых модулей по <c>.manifest.json</c> и безопасной загрузки с проверкой SHA-256.
/// </summary>
public sealed class GitHubReleaseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string? _githubToken;

    public GitHubReleaseClient(HttpClient httpClient, string? githubToken = null)
    {
        _http = httpClient;
        _githubToken = githubToken;
    }

    public static HttpClient CreateDefaultHttpClient(Version hostVersion)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PCPulse", hostVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string GetCurrentRid()
    {
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        if (OperatingSystem.IsWindows())
            return $"win-{arch}";
        if (OperatingSystem.IsLinux())
            return $"linux-{arch}";
        return $"unknown-{arch}";
    }

    public async Task<ModuleReleaseResolution> FindLatestCompatibleModuleAsync(
        CatalogModuleEntry entry,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{entry.Repository}/releases?per_page=10";
        using var request = GitHubCliAuth.CreateApiRequest(url, _githubToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ModuleReleaseResolution(
                ModuleAvailabilityStatus.NoModuleReleases,
                null,
                null,
                null,
                null,
                "У репозитория ещё нет опубликованных релизов.",
                null);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var releases = JsonSerializer.Deserialize<List<GitHubReleaseDto>>(json, JsonOptions) ?? [];

        var currentPlatform = PulseContractVersion.GetCurrentPlatform();
        var currentRid = GetCurrentRid();
        string? latestSkippedReason = null;
        ModuleAvailabilityStatus? fallbackStatus = null;

        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease || release.Assets is null || release.Assets.Count == 0)
                continue;

            var manifestAsset = release.Assets.FirstOrDefault(a =>
                a.Name is not null &&
                a.Name.StartsWith($"{entry.Id}-module-", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase));

            if (manifestAsset?.BrowserDownloadUrl is null)
                continue;

            var manifestJson = await _http.GetStringAsync(manifestAsset.BrowserDownloadUrl, cancellationToken).ConfigureAwait(false);
            var manifest = ModuleManifest.Parse(manifestJson);
            if (!manifest.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            if ((manifest.SupportedPlatforms & currentPlatform) == 0)
            {
                latestSkippedReason ??= $"Версия {manifest.Version} не поддерживает текущую ОС ({currentPlatform}).";
                fallbackStatus ??= ModuleAvailabilityStatus.IncompatiblePlatform;
                continue;
            }

            if (!PulseContractVersion.IsCompatible(manifest.ContractVersion, out var contractReason))
            {
                latestSkippedReason ??= $"Версия {manifest.Version}: {contractReason}";
                fallbackStatus ??= ModuleAvailabilityStatus.IncompatibleContract;
                continue;
            }

            var exactZipName = $"{entry.Id}-module-{manifest.Version}-{currentRid}.zip";
            var anyZipName = $"{entry.Id}-module-{manifest.Version}-any.zip";
            var zipAsset = release.Assets.FirstOrDefault(a =>
                               string.Equals(a.Name, exactZipName, StringComparison.OrdinalIgnoreCase))
                           ?? release.Assets.FirstOrDefault(a =>
                               string.Equals(a.Name, anyZipName, StringComparison.OrdinalIgnoreCase));

            if (zipAsset?.Name is null || zipAsset.BrowserDownloadUrl is null)
            {
                latestSkippedReason ??= $"В релизе {manifest.Version} нет пакета для {currentRid}.";
                fallbackStatus ??= ModuleAvailabilityStatus.IncompatiblePlatform;
                continue;
            }

            var shaAsset = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, zipAsset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

            if (shaAsset?.BrowserDownloadUrl is null)
            {
                latestSkippedReason ??= $"В релизе {manifest.Version} отсутствует файл {zipAsset.Name}.sha256.";
                continue;
            }

            return new ModuleReleaseResolution(
                ModuleAvailabilityStatus.CompatibleAvailable,
                manifest,
                zipAsset.Name,
                zipAsset.BrowserDownloadUrl,
                shaAsset.BrowserDownloadUrl,
                null,
                latestSkippedReason);
        }

        return new ModuleReleaseResolution(
            fallbackStatus ?? ModuleAvailabilityStatus.NoModuleReleases,
            null,
            null,
            null,
            null,
            latestSkippedReason ?? "Совместимые модульные пакеты PCPulse в релизах этого репозитория пока не найдены.",
            latestSkippedReason);
    }

    public async Task<string> DownloadAndVerifyModuleZipAsync(
        ModuleReleaseResolution resolution,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resolution.ZipUrl) ||
            string.IsNullOrWhiteSpace(resolution.Sha256Url) ||
            string.IsNullOrWhiteSpace(resolution.ZipFileName))
        {
            throw new InvalidOperationException("В описании релиза модуля отсутствуют ссылки на архив или SHA-256.");
        }

        Directory.CreateDirectory(destinationDirectory);
        var safeFileName = Path.GetFileName(resolution.ZipFileName);
        var targetZipPath = Path.Combine(destinationDirectory, safeFileName);

        var shaContent = await _http.GetStringAsync(resolution.Sha256Url, cancellationToken).ConfigureAwait(false);
        await DownloadFileAsync(_http, resolution.ZipUrl, targetZipPath, cancellationToken).ConfigureAwait(false);

        if (!await Sha256Verifier.VerifyFileAsync(targetZipPath, shaContent, cancellationToken).ConfigureAwait(false))
        {
            try { File.Delete(targetZipPath); } catch { }
            throw new InvalidDataException($"Контрольная сумма SHA-256 архива «{safeFileName}» не совпала.");
        }

        return targetZipPath;
    }

    internal static async Task DownloadFileAsync(
        HttpClient http,
        string url,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    internal sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAssetDto>? Assets { get; set; }
    }

    internal sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
