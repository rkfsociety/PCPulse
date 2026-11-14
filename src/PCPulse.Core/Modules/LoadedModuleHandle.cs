using Avalonia.Controls;
using PCPulse.Contracts;

namespace PCPulse.Core.Modules;

public enum ModuleRuntimeState
{
    Disabled = 0,
    Loaded = 1,
    Faulted = 2,
    IncompatiblePlatform = 3,
    IncompatibleContract = 4
}

/// <summary>
/// Состояние загруженного в текущей сессии модуля.
/// </summary>
public sealed class LoadedModuleHandle
{
    internal LoadedModuleHandle(InstalledModuleRecord record)
    {
        Record = record;
        State = record.Enabled ? ModuleRuntimeState.Disabled : ModuleRuntimeState.Disabled;
    }

    public InstalledModuleRecord Record { get; internal set; }
    public ModuleManifest? Manifest { get; internal set; }
    public ModuleRuntimeState State { get; internal set; }
    public string? ErrorMessage { get; internal set; }
    public IPulseModule? Module { get; internal set; }
    public Control? CachedView { get; internal set; }
    public PulseStatusSummary? LastSummary { get; internal set; }
    internal ModuleLoadContext? LoadContext { get; set; }

    internal async ValueTask UnloadAsync(IPulseLogger logger)
    {
        if (Module is not null)
        {
            try
            {
                await Module.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"Ошибка при выгрузке модуля «{Record.Id}».", ex);
            }
        }

        Module = null;
        CachedView = null;
        LastSummary = null;

        if (LoadContext is not null)
        {
            try
            {
                LoadContext.Unload();
            }
            catch (Exception ex)
            {
                logger.Warn($"Не удалось выгрузить AssemblyLoadContext модуля «{Record.Id}»: {ex.Message}");
            }
            LoadContext = null;
        }
    }
}
