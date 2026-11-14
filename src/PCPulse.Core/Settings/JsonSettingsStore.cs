using System.Text.Json;
using System.Text.RegularExpressions;
using PCPulse.Contracts;

namespace PCPulse.Core.Settings;

/// <summary>
/// Изолированное JSON-хранилище настроек внутри выделенного каталога данных.
/// </summary>
public sealed partial class JsonSettingsStore : IPulseSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _directory;

    public JsonSettingsStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Каталог настроек не задан.", nameof(directory));
        _directory = Path.GetFullPath(directory);
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var path = GetSafeKeyPath(key);
        if (!File.Exists(path))
            return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var path = GetSafeKeyPath(key);
        Directory.CreateDirectory(_directory);

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafeKeyPath(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetSafeKeyPath(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !SafeKeyRegex().IsMatch(key))
            throw new ArgumentException($"Недопустимый ключ настроек «{key}». Разрешены только [a-zA-Z0-9._-].", nameof(key));

        return Path.Combine(_directory, key + ".json");
    }

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$")]
    private static partial Regex SafeKeyRegex();
}
