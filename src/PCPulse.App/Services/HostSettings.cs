using System.Text.Json;
using PCPulse.Core.Platform;

namespace PCPulse.App.Services;

public sealed record HostSettings(
    bool ShowTrayIcon = true,
    bool MinimizeToTrayOnClose = true);

public sealed class HostSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PulsePaths _paths;

    public HostSettingsStore(PulsePaths paths)
    {
        _paths = paths;
    }

    public async Task<HostSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.HostSettingsFile))
            return new HostSettings();

        try
        {
            await using var stream = new FileStream(_paths.HostSettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<HostSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? new HostSettings();
        }
        catch
        {
            return new HostSettings();
        }
    }

    public async Task SaveAsync(HostSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_paths.DataRoot);
        var temp = _paths.HostSettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, _paths.HostSettingsFile, overwrite: true);
    }
}
