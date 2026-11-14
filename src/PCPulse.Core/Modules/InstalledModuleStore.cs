using System.Text.Json;
using PCPulse.Core.Platform;

namespace PCPulse.Core.Modules;

/// <summary>
/// Хранит список установленных модулей и очередь отложенного удаления заблокированных каталогов.
/// </summary>
public sealed class InstalledModuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly PulsePaths _paths;

    public InstalledModuleStore(PulsePaths paths)
    {
        _paths = paths;
    }

    public async Task<List<InstalledModuleRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.InstalledModulesFile))
            return [];

        await using var stream = new FileStream(_paths.InstalledModulesFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await JsonSerializer.DeserializeAsync<List<InstalledModuleRecord>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<InstalledModuleRecord> records, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DataRoot);
        var temp = _paths.InstalledModulesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, _paths.InstalledModulesFile, overwrite: true);
    }

    public async Task AddPendingDeleteAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(directoryPath);
        if (!IsInsideModulesDirectory(full))
            return;

        var pending = await LoadPendingDeletesAsync(cancellationToken).ConfigureAwait(false);
        if (!pending.Contains(full, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            pending.Add(full);
            await SavePendingDeletesAsync(pending, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ProcessPendingDeletesAsync(CancellationToken cancellationToken = default)
    {
        var pending = await LoadPendingDeletesAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
            return;

        var remaining = new List<string>();
        foreach (var dir in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsInsideModulesDirectory(dir) || !Directory.Exists(dir))
                continue;

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                remaining.Add(dir);
            }
        }

        await SavePendingDeletesAsync(remaining, cancellationToken).ConfigureAwait(false);
    }

    private bool IsInsideModulesDirectory(string candidatePath)
    {
        var modulesRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_paths.ModulesDirectory))
                          + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidatePath).StartsWith(modulesRoot, PathComparison);
    }

    private async Task<List<string>> LoadPendingDeletesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.PendingDeletesFile))
            return [];

        await using var stream = new FileStream(_paths.PendingDeletesFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? [];
    }

    private async Task SavePendingDeletesAsync(List<string> paths, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.DataRoot);
        if (paths.Count == 0)
        {
            if (File.Exists(_paths.PendingDeletesFile))
                File.Delete(_paths.PendingDeletesFile);
            return;
        }

        var temp = _paths.PendingDeletesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, paths, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, _paths.PendingDeletesFile, overwrite: true);
    }
}
