using PCPulse.Contracts;
using PCPulse.Core.Elevation;
using PCPulse.Core.Logging;
using PCPulse.Core.Platform;
using PCPulse.Core.Security;
using PCPulse.Core.Settings;

namespace PCPulse.Core.Modules;

/// <summary>
/// Управляет установкой, загрузкой, изоляцией и жизненным циклом модулей PCPulse.
/// </summary>
public sealed class ModuleManager : IAsyncDisposable
{
    private readonly PulsePaths _paths;
    private readonly Version _hostVersion;
    private readonly IPulseDialogService _dialogService;
    private readonly PulseFileLogger _logger;
    private readonly InstalledModuleStore _store;
    private readonly List<LoadedModuleHandle> _handles = [];

    public ModuleManager(
        PulsePaths paths,
        Version hostVersion,
        IPulseDialogService dialogService,
        PulseFileLogger logger)
    {
        _paths = paths;
        _hostVersion = hostVersion;
        _dialogService = dialogService;
        _logger = logger;
        _store = new InstalledModuleStore(paths);
    }

    public event Action<string>? ModuleSummaryInvalidated;
    public event Action? ModulesChanged;

    public IReadOnlyList<LoadedModuleHandle> Modules => _handles;

    public LoadedModuleHandle? GetLoadedModule(string moduleId) =>
        _handles.FirstOrDefault(m => m.Record.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

    public async Task InitializeAndLoadAllAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await _store.ProcessPendingDeletesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var existing in _handles)
            await existing.UnloadAsync(_logger).ConfigureAwait(false);
        _handles.Clear();

        var records = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in records)
        {
            var handle = new LoadedModuleHandle(record);
            _handles.Add(handle);
            if (record.Enabled)
            {
                await LoadHandleAsync(handle, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                handle.State = ModuleRuntimeState.Disabled;
            }
        }

        ModulesChanged?.Invoke();
    }

    public async Task<InstalledModuleRecord> InstallFromZipAsync(
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var stagingDir = Path.Combine(_paths.TempDirectory, "stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ZipSafeExtractor.ExtractToDirectoryAsync(zipPath, stagingDir, cancellationToken).ConfigureAwait(false);
            var manifestPath = Path.Combine(stagingDir, "module.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("В корне архива модуля отсутствует обязательный файл module.json.");

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = ModuleManifest.Parse(manifestJson);

            var currentPlatform = PulseContractVersion.GetCurrentPlatform();
            if ((manifest.SupportedPlatforms & currentPlatform) == 0)
                throw new PlatformNotSupportedException($"Модуль «{manifest.Name}» не поддерживает текущую ОС ({currentPlatform}).");

            if (!PulseContractVersion.IsCompatible(manifest.ContractVersion, out var contractError))
                throw new InvalidOperationException(contractError);

            var targetDir = _paths.GetModuleVersionDirectory(manifest.Id, manifest.Version);
            if (Directory.Exists(targetDir))
            {
                var existingHandle = GetLoadedModule(manifest.Id);
                if (existingHandle is not null)
                    await existingHandle.UnloadAsync(_logger).ConfigureAwait(false);

                await TryDeleteOrScheduleAsync(targetDir, cancellationToken).ConfigureAwait(false);
                if (Directory.Exists(targetDir))
                {
                    targetDir = targetDir + "-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(stagingDir, targetDir);

            var records = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var previous = records.FirstOrDefault(r => r.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase));
            if (previous is not null)
            {
                var prevHandle = GetLoadedModule(manifest.Id);
                if (prevHandle is not null)
                {
                    await prevHandle.UnloadAsync(_logger).ConfigureAwait(false);
                    _handles.Remove(prevHandle);
                }

                if (!string.Equals(previous.InstallDirectory, targetDir, StringComparison.OrdinalIgnoreCase))
                    await TryDeleteOrScheduleAsync(previous.InstallDirectory, cancellationToken).ConfigureAwait(false);

                records.Remove(previous);
            }

            var newRecord = new InstalledModuleRecord(
                manifest.Id,
                manifest.Name,
                manifest.Description,
                manifest.Version.ToString(),
                manifest.ContractVersion,
                targetDir,
                Enabled: true);

            records.Add(newRecord);
            await _store.SaveAsync(records, cancellationToken).ConfigureAwait(false);

            var handle = new LoadedModuleHandle(newRecord) { Manifest = manifest };
            _handles.Add(handle);
            await LoadHandleAsync(handle, cancellationToken).ConfigureAwait(false);
            ModulesChanged?.Invoke();

            return newRecord;
        }
        finally
        {
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { }
            }
        }
    }

    public async Task SetModuleEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var handle = GetLoadedModule(moduleId)
            ?? throw new InvalidOperationException($"Модуль «{moduleId}» не установлен.");

        var records = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var index = records.FindIndex(r => r.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException($"Запись о модуле «{moduleId}» не найдена.");

        var updated = records[index] with { Enabled = enabled };
        records[index] = updated;
        await _store.SaveAsync(records, cancellationToken).ConfigureAwait(false);

        handle.Record = updated;
        if (!enabled)
        {
            await handle.UnloadAsync(_logger).ConfigureAwait(false);
            handle.State = ModuleRuntimeState.Disabled;
            handle.ErrorMessage = null;
        }
        else
        {
            await LoadHandleAsync(handle, cancellationToken).ConfigureAwait(false);
        }

        ModulesChanged?.Invoke();
    }

    public async Task UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var handle = GetLoadedModule(moduleId);
        if (handle is not null)
        {
            await handle.UnloadAsync(_logger).ConfigureAwait(false);
            _handles.Remove(handle);
        }

        var records = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var existing = records.FirstOrDefault(r => r.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            records.Remove(existing);
            await _store.SaveAsync(records, cancellationToken).ConfigureAwait(false);
            await TryDeleteOrScheduleAsync(existing.InstallDirectory, cancellationToken).ConfigureAwait(false);
        }

        ModulesChanged?.Invoke();
    }

    public Avalonia.Controls.Control? GetOrCreateView(string moduleId)
    {
        var handle = GetLoadedModule(moduleId);
        if (handle is null || handle.State != ModuleRuntimeState.Loaded || handle.Module is null)
            return null;

        if (handle.CachedView is not null)
            return handle.CachedView;

        try
        {
            handle.CachedView = handle.Module.CreateView();
            return handle.CachedView;
        }
        catch (Exception ex)
        {
            handle.State = ModuleRuntimeState.Faulted;
            handle.ErrorMessage = $"Ошибка создания интерфейса модуля: {ex.Message}";
            _logger.Error($"Сбой CreateView для модуля «{moduleId}».", ex);
            return null;
        }
    }

    public async Task RefreshAllSummariesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var handle in _handles)
        {
            if (handle.State != ModuleRuntimeState.Loaded || handle.Module is null)
                continue;

            try
            {
                handle.LastSummary = await handle.Module.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Не удалось получить сводку модуля «{handle.Record.Id}»: {ex.Message}");
            }
        }
    }

    private async Task LoadHandleAsync(LoadedModuleHandle handle, CancellationToken cancellationToken)
    {
        var record = handle.Record;
        try
        {
            if (!Directory.Exists(record.InstallDirectory))
                throw new DirectoryNotFoundException($"Каталог модуля не найден: {record.InstallDirectory}");

            var manifestPath = Path.Combine(record.InstallDirectory, "module.json");
            var manifest = ModuleManifest.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
            handle.Manifest = manifest;

            var currentPlatform = PulseContractVersion.GetCurrentPlatform();
            if ((manifest.SupportedPlatforms & currentPlatform) == 0)
            {
                handle.State = ModuleRuntimeState.IncompatiblePlatform;
                handle.ErrorMessage = $"Модуль не поддерживает ОС {currentPlatform}.";
                return;
            }

            if (!PulseContractVersion.IsCompatible(manifest.ContractVersion, out var contractReason))
            {
                handle.State = ModuleRuntimeState.IncompatibleContract;
                handle.ErrorMessage = contractReason;
                return;
            }

            var entryPath = PathGuard.ResolveSafeChildPath(record.InstallDirectory, manifest.EntryAssembly);
            if (!File.Exists(entryPath))
                throw new FileNotFoundException($"Не найдена точка входа модуля «{manifest.EntryAssembly}».", entryPath);

            var loadContext = new ModuleLoadContext(manifest.Id, entryPath);
            handle.LoadContext = loadContext;

            var assembly = loadContext.LoadFromAssemblyPath(entryPath);
            var moduleType = assembly.GetType(manifest.EntryType, throwOnError: true, ignoreCase: false)!;
            if (!typeof(IPulseModule).IsAssignableFrom(moduleType))
                throw new InvalidCastException($"Тип «{manifest.EntryType}» не реализует IPulseModule текущего контракта.");

            var instance = (IPulseModule)(Activator.CreateInstance(moduleType)
                ?? throw new InvalidOperationException($"Не удалось создать экземпляр «{manifest.EntryType}»."));

            var moduleDataDir = _paths.GetModuleDataDirectory(manifest.Id);
            Directory.CreateDirectory(moduleDataDir);

            var hostContext = new HostContextImpl(
                manifest.Id,
                moduleDataDir,
                record.InstallDirectory,
                _hostVersion,
                _dialogService,
                new ProcessElevationService(record.InstallDirectory, manifest),
                _logger.ForSource(manifest.Id),
                new JsonSettingsStore(moduleDataDir),
                () => ModuleSummaryInvalidated?.Invoke(manifest.Id));

            await instance.InitializeAsync(hostContext, cancellationToken).ConfigureAwait(false);
            handle.Module = instance;
            handle.State = ModuleRuntimeState.Loaded;
            handle.ErrorMessage = null;

            try
            {
                handle.LastSummary = await instance.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка начальной сводки модуля «{manifest.Id}»: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки модуля «{record.Id}».", ex);
            await handle.UnloadAsync(_logger).ConfigureAwait(false);
            handle.State = ModuleRuntimeState.Faulted;
            handle.ErrorMessage = ex.Message;
        }
    }

    private async Task TryDeleteOrScheduleAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return;

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            await _store.AddPendingDeleteAsync(directoryPath, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var handle in _handles)
            await handle.UnloadAsync(_logger).ConfigureAwait(false);
        _handles.Clear();
    }

    private sealed class HostContextImpl : IPulseHostContext
    {
        private readonly Action _onSummaryRefresh;

        public HostContextImpl(
            string moduleId,
            string moduleDataDirectory,
            string moduleInstallDirectory,
            Version hostVersion,
            IPulseDialogService dialogs,
            IPulseElevationService elevation,
            IPulseLogger logger,
            IPulseSettingsStore settings,
            Action onSummaryRefresh)
        {
            ModuleId = moduleId;
            ModuleDataDirectory = moduleDataDirectory;
            ModuleInstallDirectory = moduleInstallDirectory;
            HostVersion = hostVersion;
            Dialogs = dialogs;
            Elevation = elevation;
            Logger = logger;
            Settings = settings;
            _onSummaryRefresh = onSummaryRefresh;
        }

        public string ModuleId { get; }
        public string ModuleDataDirectory { get; }
        public string ModuleInstallDirectory { get; }
        public Version HostVersion { get; }
        public string ContractVersion => PulseContractVersion.Current;
        public IPulseDialogService Dialogs { get; }
        public IPulseElevationService Elevation { get; }
        public IPulseLogger Logger { get; }
        public IPulseSettingsStore Settings { get; }
        public void RequestSummaryRefresh() => _onSummaryRefresh();
    }
}
